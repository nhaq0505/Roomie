using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class MonitoringAccuracyAndOutboxRegressionTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("ASSERTION FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n=== RUNNING TASK T4B METRIC ACCURACY & OUTBOX REGRESSION TESTS ===");
        await TestSubscriptionPayment_FailFirst_Replay_Reconcile_Restart_Async();
        await TestInvoiceIssuance_Publish_And_ReadingsRollbackWithProductWrites_Async();
        await TestOutboxAck_VerifiedSuccessOnly_SanitizedError_SystemActor_Async();
        await TestBoundaryCaps_Exceeding50k_FilteredOwnerBeyond50k_Daily_Retention_Funnel_Revenue_CAC_Async();
        await TestRelationalSubquery_ManyMatchingOwners_SqlTranslation_Npgsql_Async();
        await TestPruneExpiredTelemetry_CompletedOutboxPruned_PendingFailedPreserved_Async();
        Console.WriteLine("=== ALL TASK T4B METRIC ACCURACY & OUTBOX REGRESSION TESTS PASSED ===\n");
    }

    private static async Task<(PostgresTestDb Connection, PostgresAppDbContext Db)> CreateTestDbAsync()
    {
        var fixture = await PostgresTestDb.CreateAsync(migrate: true);
        var db = fixture.CreatePostgresDbContext();
        return (fixture, db);
    }

    private static async Task TestSubscriptionPayment_FailFirst_Replay_Reconcile_Restart_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var serviceScopeFactory = new TestScopeFactory(db);
        var realTracker = new OwnerMilestoneTracker(db, clock, serviceScopeFactory);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebhookBaseUrl"] = "https://callbacks.example.com",
            ["APP_PUBLIC_URL"] = "https://callbacks.example.com"
        }).Build();

        var protection = new EphemeralDataProtectionProvider();
        var fakeHttp = new FakePayOsHandler();

        // 1. Fail-first milestone tracker wrapper
        var failFirstTracker = new FailingMilestoneTracker(realTracker, shouldFail: true);
        var webhookService = new PaymentWebhookService(db, protection, fakeHttp, config, failFirstTracker);
        var payOsService = new PayOsService(db, protection, fakeHttp, config, NullLogger<PayOsService>.Instance, webhookService);

        var ownerId = Guid.NewGuid();
        var owner = new AppUser
        {
            Id = ownerId,
            Email = "pro.owner@example.test",
            Name = "Pro Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            SubscriptionTier = SubscriptionTier.Free,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.Users.Add(owner);

        // Pre-create attribution and milestone
        var attribution = new RegistrationAttribution
        {
            OwnerId = ownerId,
            FirstTouchSource = "facebook",
            FirstTouchMedium = "cpc",
            FirstTouchCampaign = "summer2026",
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.RegistrationAttributions.Add(attribution);

        var milestone = new OwnerMilestone
        {
            OwnerId = ownerId,
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-10),
            ActivatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            FirstPaidAt = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.OwnerMilestones.Add(milestone);

        var attempt = new PayOsAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            OrderCode = 8881234567,
            Kind = "Subscription",
            Amount = 299_000,
            SubscriptionDays = 30,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        db.PayOsAttempts.Add(attempt);
        await db.SaveChangesAsync();

        // Step 1: Initial webhook execution where postcommit telemetry fails
        var success = await webhookService.HandleSubscriptionPaymentSuccessAsync(
            attempt, 299_000m, "pay-ref-test-01", default);

        Assert(success, "HandleSubscriptionPaymentSuccessAsync succeeds even when milestone telemetry throws post-commit");

        // Verify product side effects committed once
        var updatedAttempt = await db.PayOsAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert(updatedAttempt.Status == PaymentAttemptStatus.Paid, "Payment attempt is marked Paid");
        Assert(updatedAttempt.PaidAt.HasValue, "Authoritative PaidAt timestamp persisted in database");
        var persistedPaidAt = updatedAttempt.PaidAt!.Value;

        var updatedOwner = await db.Users.SingleAsync(u => u.Id == ownerId);
        Assert(updatedOwner.SubscriptionTier == SubscriptionTier.Pro, "Owner tier updated to Pro");
        Assert(updatedOwner.SubscriptionExpiresAt.HasValue, "Owner SubscriptionExpiresAt populated");
        var initialExpiry = updatedOwner.SubscriptionExpiresAt!.Value;

        var processedWebhook = await db.ProcessedWebhooks.SingleOrDefaultAsync(p => p.Reference == "pay-ref-test-01");
        Assert(processedWebhook != null, "ProcessedWebhook entry persisted");

        // Outbox event is Pending
        var outboxEvent = await db.MilestoneOutboxEvents.SingleOrDefaultAsync(e => e.OwnerId == ownerId && e.EventType == "SubscriptionPaid");
        Assert(outboxEvent != null, "MilestoneOutboxEvent created in transaction");
        Assert(outboxEvent!.Status == "Pending", "MilestoneOutboxEvent status is Pending after telemetry failure");
        Assert(outboxEvent.OccurredAt == persistedPaidAt, "MilestoneOutboxEvent OccurredAt matches persisted PaidAt");

        // Milestone is still null because initial telemetry failed
        var mAfterFail = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterFail.FirstPaidAt == null, "OwnerMilestone.FirstPaidAt is null before replay/repair");

        // Step 2: Webhook Replay with identical reference
        failFirstTracker.ShouldFail = false; // telemetry recovered
        var replaySuccess = await webhookService.HandleSubscriptionPaymentSuccessAsync(
            attempt, 299_000m, "pay-ref-test-01", default);

        Assert(replaySuccess, "Replayed duplicate webhook succeeds idempotently");

        // Verify NO duplicate financial mutations
        var ownerAfterReplay = await db.Users.SingleAsync(u => u.Id == ownerId);
        Assert(ownerAfterReplay.SubscriptionExpiresAt == initialExpiry,
            "Replay preserves original SubscriptionExpiresAt without extending Pro expiry a second time");
        Assert(await db.ProcessedWebhooks.CountAsync(p => p.OwnerId == ownerId) == 1,
            "Replay does not duplicate ProcessedWebhooks row");

        // Verify milestone was authoritatively repaired with persisted PaidAt (never fabricated)
        var mAfterReplay = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterReplay.FirstPaidAt.HasValue && Math.Abs((mAfterReplay.FirstPaidAt.Value - persistedPaidAt).TotalMilliseconds) < 1,
            "OwnerMilestone.FirstPaidAt authoritatively repaired with persisted PaidAt");
        Assert(mAfterReplay.FirstPaidOrderCode == attempt.OrderCode,
            "OwnerMilestone.FirstPaidOrderCode matches payment order");

        // Outbox event marked Completed
        var outboxAfterReplay = await db.MilestoneOutboxEvents.SingleAsync(e => e.Id == outboxEvent.Id);
        Assert(outboxAfterReplay.Status == "Completed", "MilestoneOutboxEvent marked Completed on replay");

        // Step 3: Already-Paid Replay with DIFFERENT reference
        // Clear FirstPaidAt to simulate dropped telemetry on an already-Paid order
        mAfterReplay.FirstPaidAt = null;
        mAfterReplay.FirstPaidOrderCode = null;
        await db.SaveChangesAsync();

        var diffRefSuccess = await webhookService.HandleSubscriptionPaymentSuccessAsync(
            attempt, 299_000m, "pay-ref-different-02", default);

        Assert(diffRefSuccess, "Already-Paid webhook replay with new reference succeeds");
        var mAfterDiffRef = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterDiffRef.FirstPaidAt.HasValue && Math.Abs((mAfterDiffRef.FirstPaidAt.Value - persistedPaidAt).TotalMilliseconds) < 1,
            "Already-Paid replay repairs missing FirstPaidAt milestone using persisted PaidAt");
        var ownerAfterDiff = await db.Users.SingleAsync(u => u.Id == ownerId);
        Assert(ownerAfterDiff.SubscriptionExpiresAt == initialExpiry,
            "Already-Paid replay with new reference does NOT extend Pro subscription expiry");

        // Step 4: Reconciliation of Paid subscription order
        mAfterDiffRef.FirstPaidAt = null;
        mAfterDiffRef.FirstPaidOrderCode = null;
        await db.SaveChangesAsync();

        var reconcileResult = await payOsService.ReconcileSubscriptionOrderAsync(ownerId, attempt.OrderCode, default);
        var recStatus = reconcileResult.GetType().GetProperty("status")?.GetValue(reconcileResult)?.ToString();
        Assert(recStatus == "Paid", "ReconcileSubscriptionOrderAsync returns Paid status");

        var mAfterReconcile = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterReconcile.FirstPaidAt.HasValue && Math.Abs((mAfterReconcile.FirstPaidAt.Value - persistedPaidAt).TotalMilliseconds) < 1,
            "Reconciliation of Paid order idempotently repairs missing FirstPaidAt milestone");

        // Step 5: Application Restart survival
        // Simulate restart by clearing FirstPaidAt, creating a fresh tracker and running RepairMissingMilestonesAsync
        mAfterReconcile.FirstPaidAt = null;
        mAfterReconcile.FirstPaidOrderCode = null;
        outboxAfterReplay.Status = "Pending";
        await db.SaveChangesAsync();

        var freshTracker = new OwnerMilestoneTracker(db, clock, serviceScopeFactory);
        await freshTracker.RepairMissingMilestonesAsync(default);

        var mAfterRestart = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterRestart.FirstPaidAt.HasValue && Math.Abs((mAfterRestart.FirstPaidAt.Value - persistedPaidAt).TotalMilliseconds) < 1,
            "RepairMissingMilestonesAsync restores FirstPaidAt from persisted PaidAt upon restart");
        var outboxAfterRestart = await db.MilestoneOutboxEvents.SingleAsync(e => e.Id == outboxEvent.Id);
        Assert(outboxAfterRestart.Status == "Completed",
            "Pending outbox event processed and marked Completed upon restart");

        // Step 6: Query admin revenue & growth metrics through query service
        var revenueQueryService = new DefaultRevenueMonitoringQueryService(db, clock);
        var growthQueryService = new DefaultGrowthMonitoringQueryService(db, clock);

        var revenueResp = await revenueQueryService.GetRevenueMetricsAsync(new MonitoringFilterRequest
        {
            FromDate = ApiSupport.VietnamDate(persistedPaidAt).AddDays(-20),
            ToDate = ApiSupport.VietnamDate(persistedPaidAt)
        }, default);

        Assert(revenueResp.Summary.PaidSubscriptionCount == 1,
            "Admin revenue response reports exact PaidSubscriptionCount == 1");
        Assert(revenueResp.Summary.TotalSubscriptionRevenueVnd == 299_000m,
            "Admin revenue response reports exact TotalSubscriptionRevenueVnd == 299,000 VND");

        var growthResp = await growthQueryService.GetGrowthMetricsAsync(new MonitoringFilterRequest
        {
            FromDate = ApiSupport.VietnamDate(persistedPaidAt).AddDays(-20),
            ToDate = ApiSupport.VietnamDate(persistedPaidAt)
        }, default);

        Assert(growthResp.Summary.Registrations == 1, "Growth query shows 1 registered owner");
        Assert(growthResp.Summary.ActivatedOwners == 1, "Growth query shows 1 activated owner");
    }

    private static async Task TestInvoiceIssuance_Publish_And_ReadingsRollbackWithProductWrites_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var serviceScopeFactory = new TestScopeFactory(db);
        var realTracker = new OwnerMilestoneTracker(db, clock, serviceScopeFactory);

        var ownerId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = ownerId,
            Email = "draft.owner@example.test",
            Name = "Draft Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
        db.Users.Add(user);

        var building = new Building
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = "Tòa Nhà Draft",
            Address = "123 Phố Test"
        };
        var room = new Room
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            Name = "P201",
            Rent = 3_000_000m,
            ElectricityRate = 4_000m,
            WaterFee = 100_000m,
            WaterPerPerson = false,
            ServiceFee = 50_000m
        };
        var occupancy = new Occupancy
        {
            Id = Guid.NewGuid(),
            RoomId = room.Id,
            TenantName = "Người Thuê B",
            Phone = "0987654321",
            People = 1,
            StartDate = new DateOnly(2026, 8, 1),
            IsActive = true
        };
        var milestone = new OwnerMilestone
        {
            OwnerId = ownerId,
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-30),
            ActivatedAt = null,
            FirstActivatedInvoiceId = null
        };

        db.Buildings.Add(building);
        db.Rooms.Add(room);
        db.Occupancies.Add(occupancy);
        db.OwnerMilestones.Add(milestone);
        await db.SaveChangesAsync();

        // PART A: Draft created 20 days ago, published today
        var draftCreatedAt = DateTimeOffset.UtcNow.AddDays(-20);
        var draftInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            RoomId = room.Id,
            BuildingId = building.Id,
            OccupancyId = occupancy.Id,
            RoomName = room.Name,
            BuildingName = building.Name,
            Period = "2026-09",
            TenantName = occupancy.TenantName,
            Kind = "Monthly",
            LifecycleState = InvoiceLifecycleState.Draft,
            Total = 3_150_000m,
            IssuedAt = draftCreatedAt
        };
        db.Invoices.Add(draftInvoice);
        await db.SaveChangesAsync();

        // Assert milestone is NOT activated by Draft creation
        var mDraft = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mDraft.ActivatedAt == null, "Draft invoice creation does NOT activate owner milestone");

        // Now simulate publish transition with telemetry failure
        var publishTime = DateTimeOffset.UtcNow;
        draftInvoice.LifecycleState = InvoiceLifecycleState.Issued;
        draftInvoice.IssuedAt = publishTime; // updated to true publish time!
        draftInvoice.Version++;

        var publishRevision = new InvoiceRevision
        {
            InvoiceId = draftInvoice.Id,
            Revision = 1,
            RequestId = Guid.NewGuid(),
            RequestFingerprint = "fingerprint-publish",
            Reason = "Owner published the partial-month draft.",
            ActorId = ownerId,
            ActorName = user.Name,
            CreatedAt = publishTime,
            Version = draftInvoice.Version,
            BeforeJson = "{}",
            AfterJson = "{}"
        };
        db.InvoiceRevisions.Add(publishRevision);

        var outboxPublish = new MilestoneOutboxEvent
        {
            OwnerId = ownerId,
            EventType = "InvoiceIssued",
            TargetId = draftInvoice.Id.ToString(),
            OccurredAt = publishTime,
            Status = "Pending"
        };
        db.MilestoneOutboxEvents.Add(outboxPublish);
        await db.SaveChangesAsync();

        // Milestone is still null because telemetry failed
        var mBeforeRepair = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mBeforeRepair.ActivatedAt == null, "Milestone ActivatedAt is null after failed postcommit hook");

        // Run repair from provable actual Issued transition time
        await realTracker.EnsureInvoiceIssuedMilestoneAsync(ownerId, default);

        var mAfterRepair = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mAfterRepair.ActivatedAt.HasValue, "Owner milestone ActivatedAt populated by repair");
        Assert(mAfterRepair.ActivatedAt.HasValue && Math.Abs((mAfterRepair.ActivatedAt.Value - publishTime).TotalMilliseconds) < 1,
            "ActivatedAt matches provable actual publish transition time, NOT draft creation time (20 days ago)!");
        Assert(mAfterRepair.FirstActivatedInvoiceId == draftInvoice.Id,
            "FirstActivatedInvoiceId matches published monthly invoice");

        // Second monthly invoice does NOT overwrite original ActivatedAt
        await realTracker.TrackInvoiceIssuedAsync(ownerId, Guid.NewGuid(), "Monthly", ActorType.Owner, DateTimeOffset.UtcNow, default);
        var mPreserved = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerId);
        Assert(mPreserved.ActivatedAt.HasValue && Math.Abs((mPreserved.ActivatedAt.Value - publishTime).TotalMilliseconds) < 1, "Subsequent monthly invoice preserves original ActivatedAt timestamp");

        // PART B: /readings rollback after meaningful product writes
        var ownerRollbackId = Guid.NewGuid();
        var userRollback = new AppUser
        {
            Id = ownerRollbackId,
            Email = "rollback.owner@example.test",
            Name = "Rollback Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        var milestoneRollback = new OwnerMilestone
        {
            OwnerId = ownerRollbackId,
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-5),
            ActivatedAt = null
        };
        db.Users.Add(userRollback);
        db.OwnerMilestones.Add(milestoneRollback);
        await db.SaveChangesAsync();

        // Perform meaningful writes inside outer transaction, then roll back
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var testReading = new MonthlyReading
            {
                RoomId = room.Id,
                OccupancyId = occupancy.Id,
                Period = "2026-10",
                CurrentReading = 200m,
                RecordedAt = DateTimeOffset.UtcNow
            };
            db.Readings.Add(testReading);

            var testInvoice = new Invoice
            {
                OwnerId = ownerRollbackId,
                RoomId = room.Id,
                BuildingId = building.Id,
                OccupancyId = occupancy.Id,
                RoomName = room.Name,
                BuildingName = building.Name,
                Period = "2026-10",
                TenantName = "Rollback Tenant",
                Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Issued,
                Total = 2_000_000m,
                IssuedAt = DateTimeOffset.UtcNow
            };
            db.Invoices.Add(testInvoice);

            var testOutbox = new MilestoneOutboxEvent
            {
                OwnerId = ownerRollbackId,
                EventType = "InvoiceIssued",
                TargetId = testInvoice.Id.ToString(),
                OccurredAt = testInvoice.IssuedAt,
                Status = "Pending"
            };
            db.MilestoneOutboxEvents.Add(testOutbox);

            await db.SaveChangesAsync();

            // Meaningful product writes occurred! Now roll back:
            await transaction.RollbackAsync();
        }

        // Verify everything was rolled back cleanly
        var rolledBackReading = await db.Readings.SingleOrDefaultAsync(r => r.Period == "2026-10");
        Assert(rolledBackReading == null, "Rolled back reading is NOT in database");

        var rolledBackInvoice = await db.Invoices.SingleOrDefaultAsync(i => i.Period == "2026-10" && i.OwnerId == ownerRollbackId);
        Assert(rolledBackInvoice == null, "Rolled back invoice is NOT in database");

        var rolledBackOutbox = await db.MilestoneOutboxEvents.SingleOrDefaultAsync(e => e.OwnerId == ownerRollbackId);
        Assert(rolledBackOutbox == null, "Durable outbox entry was cleanly rolled back with transaction");

        var mAfterRollback = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerRollbackId);
        Assert(mAfterRollback.ActivatedAt == null,
            "Rollback after meaningful writes leaves ActivatedAt null (NO fake rollback milestone)");
    }

    private static async Task TestOutboxAck_VerifiedSuccessOnly_SanitizedError_SystemActor_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var serviceScopeFactory = new TestScopeFactory(db);
        var realTracker = new OwnerMilestoneTracker(db, clock, serviceScopeFactory);

        // 1. DRAFT EDIT REVISION VS PUBLISH REVISION TEST
        // An owner creates a draft 20 days ago, edits it 15 days ago, and publishes it 2 days ago.
        var ownerDraftId = Guid.NewGuid();
        var userDraft = new AppUser
        {
            Id = ownerDraftId,
            Email = "draft.publish.owner@example.test",
            Name = "Draft Publish Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = now.AddDays(-25)
        };
        var milestoneDraft = new OwnerMilestone
        {
            OwnerId = ownerDraftId,
            RegisteredAt = now.AddDays(-25),
            ActivatedAt = null
        };
        db.Users.Add(userDraft);
        db.OwnerMilestones.Add(milestoneDraft);

        var draftInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerDraftId,
            OccupancyId = Guid.NewGuid(),
            RoomName = "P101",
            BuildingName = "B1",
            TenantName = "Tenant",
            Period = "2026-09",
            Kind = "Monthly",
            LifecycleState = InvoiceLifecycleState.Issued, // currently Issued
            IssuedAt = now.AddDays(-2) // true issuance at publish time
        };
        db.Invoices.Add(draftInvoice);

        // Draft edit revision (created 15 days ago - BEFORE issuance!)
        var draftEditRevision = new InvoiceRevision
        {
            InvoiceId = draftInvoice.Id,
            Revision = 1,
            RequestId = Guid.NewGuid(),
            RequestFingerprint = "fp-edit",
            Reason = "Owner edited meter readings in draft mode.",
            ActorId = ownerDraftId,
            ActorName = userDraft.Name,
            CreatedAt = now.AddDays(-15),
            Version = 1,
            BeforeJson = "{}",
            AfterJson = "{}"
        };
        // True publish revision (created 2 days ago - at publish time!)
        var publishTime = now.AddDays(-2);
        var publishRevision = new InvoiceRevision
        {
            InvoiceId = draftInvoice.Id,
            Revision = 2,
            RequestId = Guid.NewGuid(),
            RequestFingerprint = "fp-publish",
            Reason = "Owner published the partial-month draft.",
            ActorId = ownerDraftId,
            ActorName = userDraft.Name,
            CreatedAt = publishTime,
            Version = 2,
            BeforeJson = "{}",
            AfterJson = "{}"
        };
        db.InvoiceRevisions.Add(draftEditRevision);
        db.InvoiceRevisions.Add(publishRevision);
        await db.SaveChangesAsync();

        // Run repair: must match publishRevision (2 days ago), NOT draftEditRevision (15 days ago) or draft creation (20 days ago)
        await realTracker.EnsureInvoiceIssuedMilestoneAsync(ownerDraftId, default);
        var repairedDraftMilestone = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerDraftId);
        Assert(repairedDraftMilestone.ActivatedAt.HasValue, "EnsureInvoiceIssuedMilestoneAsync sets ActivatedAt");
        Assert(Math.Abs((repairedDraftMilestone.ActivatedAt!.Value - publishTime).TotalMilliseconds) < 1,
            "EnsureInvoiceIssuedMilestoneAsync uses true publish revision time, ignoring draft edit revision!");

        // 2. OUTBOX DURABLE ACK ONLY ON VERIFIED MILESTONE SUCCESS & SANITIZED ERROR
        var ownerOutboxId = Guid.NewGuid();
        var userOutbox = new AppUser
        {
            Id = ownerOutboxId,
            Email = "outbox.fail@example.test",
            Name = "Outbox Fail Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = now.AddDays(-10)
        };
        db.Users.Add(userOutbox);

        var pendingDraftInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerOutboxId,
            OccupancyId = Guid.NewGuid(),
            RoomName = "P101",
            BuildingName = "B1",
            TenantName = "Tenant",
            Period = "2026-10",
            Kind = "Monthly",
            LifecycleState = InvoiceLifecycleState.Draft, // In draft state: cannot be verified as issued milestone
            IssuedAt = now
        };
        db.Invoices.Add(pendingDraftInvoice);

        // Outbox event referencing draft invoice where milestone cannot be recorded yet
        var outboxPending = new MilestoneOutboxEvent
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerOutboxId,
            EventType = "InvoiceIssued",
            TargetId = $"{pendingDraftInvoice.Id}:System",
            OccurredAt = now,
            Status = "Pending"
        };
        db.MilestoneOutboxEvents.Add(outboxPending);
        await db.SaveChangesAsync();

        var customService = new OwnerMilestoneTracker(db, clock, serviceScopeFactory);

        // Processing outbox when milestone does NOT record must NOT mark it Completed!
        await customService.ProcessPendingOutboxEventsAsync(default);
        var outboxAfterAttempt = await db.MilestoneOutboxEvents.SingleAsync(e => e.Id == outboxPending.Id);
        Assert(outboxAfterAttempt.Status == "Pending",
            "Outbox processing keeps Status == 'Pending' when milestone verification fails (never unconditional Completed!)");
        Assert(outboxAfterAttempt.RetryCount >= 1, "RetryCount incremented upon verification failure");
        Assert(outboxAfterAttempt.LastError == "ERR_MILESTONE_NOT_RECORDED", "Outbox LastError set to ERR_MILESTONE_NOT_RECORDED");
        if (outboxAfterAttempt.LastError != null)
        {
            Assert(!outboxAfterAttempt.LastError.Contains("SELECT") && !outboxAfterAttempt.LastError.Contains("password") && !outboxAfterAttempt.LastError.Contains("@"),
                "Outbox LastError contains sanitized error code without PII or raw SQL");
        }

        // Now issue the invoice and re-process: verification succeeds and outbox transitions to Completed!
        pendingDraftInvoice.LifecycleState = InvoiceLifecycleState.Issued;
        pendingDraftInvoice.IssuedAt = now;
        await db.SaveChangesAsync();

        await customService.ProcessPendingOutboxEventsAsync(default);
        var outboxSuccess = await db.MilestoneOutboxEvents.SingleAsync(e => e.Id == outboxPending.Id);
        Assert(outboxSuccess.Status == "Completed", "Outbox transitions to Completed once milestone verification succeeds in DB");
        Assert(outboxSuccess.ProcessedAt.HasValue, "ProcessedAt recorded upon durable verification");

        // 3. SYSTEM ACTOR INTEGRITY (ZERO FALSE WAU/MAU OWNER ACTIONS)
        var ownerSystemId = Guid.NewGuid();
        var userSystem = new AppUser
        {
            Id = ownerSystemId,
            Email = "system.sweep@example.test",
            Name = "System Sweep Owner",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = now.AddDays(-10)
        };
        var milestoneSystem = new OwnerMilestone
        {
            OwnerId = ownerSystemId,
            RegisteredAt = now.AddDays(-10),
            ActivatedAt = null
        };
        db.Users.Add(userSystem);
        db.OwnerMilestones.Add(milestoneSystem);

        var systemInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerSystemId,
            OccupancyId = Guid.NewGuid(),
            RoomName = "P101",
            BuildingName = "B1",
            TenantName = "Tenant",
            Period = "2026-11",
            Kind = "Monthly",
            LifecycleState = InvoiceLifecycleState.Issued,
            IssuedAt = now
        };
        db.Invoices.Add(systemInvoice);

        // Outbox event generated by automated system sweep
        var systemOutbox = new MilestoneOutboxEvent
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerSystemId,
            EventType = "InvoiceIssued",
            TargetId = $"{systemInvoice.Id}:System", // actor is explicitly System!
            OccurredAt = now,
            Status = "Pending"
        };
        db.MilestoneOutboxEvents.Add(systemOutbox);
        await db.SaveChangesAsync();

        // Process the system outbox event
        await customService.ProcessPendingOutboxEventsAsync(default);

        var outboxProcessed = await db.MilestoneOutboxEvents.SingleAsync(e => e.Id == systemOutbox.Id);
        Assert(outboxProcessed.Status == "Completed", "System invoice outbox processed to Completed");

        var milestoneAfterSystem = await db.OwnerMilestones.SingleAsync(m => m.OwnerId == ownerSystemId);
        Assert(milestoneAfterSystem.ActivatedAt.HasValue, "Milestone ActivatedAt is populated by system sweep");

        // Verify OwnerDailyActivity: System action MUST NOT increment OwnerActionCount or set HasOwnerAction!
        var activity = await db.OwnerDailyActivities.SingleOrDefaultAsync(a => a.OwnerId == ownerSystemId && a.ActivityDate == today);
        Assert(activity != null, "OwnerDailyActivity record created for today");
        Assert(activity!.OwnerActionCount == 0,
            "System invoice sweep produces OwnerActionCount == 0 (zero false WAU/MAU owner activity!)");
        Assert(activity.HasOwnerAction == false,
            "System invoice sweep produces HasOwnerAction == false");
        Assert(activity.SystemActionCount == 1,
            "System invoice sweep correctly increments SystemActionCount == 1");
    }

    private static async Task TestBoundaryCaps_Exceeding50k_FilteredOwnerBeyond50k_Daily_Retention_Funnel_Revenue_CAC_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var pgNow = now.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz");

        await using var rawConn = connection.OpenConnection();

        // 1. BOUNDARY CAP TEST: Filtered owner beyond first 50,000 attributions
        // Bulk insert 50,000 direct attributions using raw SQL for maximum performance
        using (var transaction = rawConn.BeginTransaction())
        {
            var insertCmd = rawConn.CreateCommand();
            insertCmd.Transaction = transaction;

            // Insert 50,000 dummy direct users and attributions in batches of 500
            for (var batch = 0; batch < 100; batch++)
            {
                var userValues = new List<string>();
                var attrValues = new List<string>();
                for (var i = 0; i < 500; i++)
                {
                    var id = Guid.NewGuid();
                    userValues.Add($"('{id}', 'Bulk User', 'bulk_{batch}_{i}@example.test', 'pwd', 'Owner', 'Approved', 'Free', '{pgNow}')");
                    attrValues.Add($"('{id}', 'Code', true, '{pgNow}', '(direct)', '(none)', NULL, NULL)");
                }

                insertCmd.CommandText = $"INSERT INTO \"Users\" (\"Id\",\"Name\",\"Email\",\"PasswordHash\",\"Role\",\"Status\",\"SubscriptionTier\",\"CreatedAt\") VALUES {string.Join(",", userValues)}";
                await insertCmd.ExecuteNonQueryAsync();

                insertCmd.CommandText = $"INSERT INTO \"RegistrationAttributions\" (\"OwnerId\",\"VerificationMethod\",\"AutoApproved\",\"RegisteredAt\",\"FirstTouchSource\",\"FirstTouchMedium\",\"FirstTouchCampaign\",\"BrowserId\") VALUES {string.Join(",", attrValues)}";
                await insertCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }

        var totalDirectAttributions = await db.RegistrationAttributions.CountAsync();
        Assert(totalDirectAttributions == 50_000, "Successfully seeded 50,000 direct attributions");

        // Now insert the 50,001st owner with matching UTM filter "special-google"
        var specialOwnerId = Guid.NewGuid();
        var specialUser = new AppUser
        {
            Id = specialOwnerId,
            Email = "special50001@example.test",
            Name = "Special Owner 50001",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = now
        };
        var specialAttr = new RegistrationAttribution
        {
            OwnerId = specialOwnerId,
            FirstTouchSource = "special-google",
            FirstTouchMedium = "special-cpc",
            FirstTouchCampaign = "special-campaign",
            RegisteredAt = now
        };
        db.Users.Add(specialUser);
        db.RegistrationAttributions.Add(specialAttr);
        await db.SaveChangesAsync();

        Assert(await db.RegistrationAttributions.CountAsync() == 50_001,
            "Total attributions in DB is exactly 50,001 (>50k boundary)");

        // Query GetMatchingOwnerIdsAsync with filter
        var matchingFilter = new MonitoringFilterRequest
        {
            Source = "special-google",
            Channel = "special-cpc",
            Campaign = "special-campaign"
        };
        var matchedIds = await DefaultGrowthMonitoringQueryService.GetMatchingOwnerIdsAsync(
            db, matchingFilter, new HashSet<string>(StringComparer.OrdinalIgnoreCase), default);

        Assert(matchedIds != null && matchedIds.Contains(specialOwnerId),
            "Owner at row 50,001 (>50k cap) is correctly matched by database filter join!");
        Assert(matchedIds!.Count == 1,
            "Only the matching 50,001st owner is returned in matchingOwnerIds");

        // 2. BOUNDARY CAP TEST: Registration Funnel Facts > 50,000
        using (var transaction = rawConn.BeginTransaction())
        {
            var insertCmd = rawConn.CreateCommand();
            insertCmd.Transaction = transaction;

            // Insert 25,005 Started facts and 25,005 Verified facts
            for (var batch = 0; batch < 50; batch++)
            {
                var factValues = new List<string>();
                for (var i = 0; i < 500; i++)
                {
                    var fid1 = Guid.NewGuid();
                    var fid2 = Guid.NewGuid();
                    var attemptId = Guid.NewGuid();
                    factValues.Add($"('{fid1}', '{attemptId}', 'Started', 'Code', '{pgNow}', 'bulk-source', 'bulk-med', 'bulk-camp')");
                    factValues.Add($"('{fid2}', '{attemptId}', 'Verified', 'Code', '{pgNow}', NULL, NULL, NULL)");
                }
                insertCmd.CommandText = $"INSERT INTO \"RegistrationFacts\" (\"Id\",\"RegistrationAttemptId\",\"Stage\",\"VerificationMethod\",\"OccurredAt\",\"UtmSource\",\"UtmMedium\",\"UtmCampaign\") VALUES {string.Join(",", factValues)}";
                await insertCmd.ExecuteNonQueryAsync();
            }

            // Plus 5 more pairs to reach 50,010 total facts (>50k)
            var lastValues = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var fid1 = Guid.NewGuid();
                var fid2 = Guid.NewGuid();
                var attemptId = Guid.NewGuid();
                lastValues.Add($"('{fid1}', '{attemptId}', 'Started', 'Code', '{pgNow}', 'bulk-source', 'bulk-med', 'bulk-camp')");
                lastValues.Add($"('{fid2}', '{attemptId}', 'Verified', 'Code', '{pgNow}', NULL, NULL, NULL)");
            }
            insertCmd.CommandText = $"INSERT INTO \"RegistrationFacts\" (\"Id\",\"RegistrationAttemptId\",\"Stage\",\"VerificationMethod\",\"OccurredAt\",\"UtmSource\",\"UtmMedium\",\"UtmCampaign\") VALUES {string.Join(",", lastValues)}";
            await insertCmd.ExecuteNonQueryAsync();

            transaction.Commit();
        }

        var totalFactsInDb = await db.RegistrationFacts.CountAsync();
        Assert(totalFactsInDb == 50_010, $"Seeded exactly 50,010 facts (>50k boundary, got {totalFactsInDb})");

        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);
        var growthMetrics = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest
        {
            FromDate = today.AddDays(-1),
            ToDate = today
        }, default);

        Assert(growthMetrics.Funnel!.StartedCount == 25_005,
            $"Funnel StartedCount is exact without Take(50k) truncation (expected 25,005, got {growthMetrics.Funnel.StartedCount})");
        Assert(growthMetrics.Funnel.VerifiedCount == 25_005,
            $"Funnel VerifiedCount is exact without Take(50k) truncation (expected 25,005, got {growthMetrics.Funnel.VerifiedCount})");

        // 3. BOUNDARY CAP TEST: Revenue orders > 50,000
        using (var transaction = rawConn.BeginTransaction())
        {
            var insertCmd = rawConn.CreateCommand();
            insertCmd.Transaction = transaction;

            // Insert 50,005 paid subscription attempts
            for (var batch = 0; batch < 100; batch++)
            {
                var orderValues = new List<string>();
                for (var i = 0; i < 500; i++)
                {
                    var id = Guid.NewGuid();
                    var code = 1000000000L + (batch * 500) + i;
                    orderValues.Add($"('{id}', '{specialOwnerId}', '{Guid.Empty}', {code}, 'Subscription', 10000, 'Paid', '{pgNow}', '{pgNow}')");
                }
                insertCmd.CommandText = $"INSERT INTO \"PayOsAttempts\" (\"Id\",\"OwnerId\",\"InvoiceId\",\"OrderCode\",\"Kind\",\"Amount\",\"Status\",\"CreatedAt\",\"PaidAt\") VALUES {string.Join(",", orderValues)}";
                await insertCmd.ExecuteNonQueryAsync();
            }

            // 5 more orders to reach 50,005 (>50k)
            var lastOrders = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                var code = 2000000000L + i;
                lastOrders.Add($"('{id}', '{specialOwnerId}', '{Guid.Empty}', {code}, 'Subscription', 10000, 'Paid', '{pgNow}', '{pgNow}')");
            }
            insertCmd.CommandText = $"INSERT INTO \"PayOsAttempts\" (\"Id\",\"OwnerId\",\"InvoiceId\",\"OrderCode\",\"Kind\",\"Amount\",\"Status\",\"CreatedAt\",\"PaidAt\") VALUES {string.Join(",", lastOrders)}";
            await insertCmd.ExecuteNonQueryAsync();

            transaction.Commit();
        }

        var totalOrdersInDb = await db.PayOsAttempts.CountAsync(x => x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid);
        Assert(totalOrdersInDb == 50_005, $"Seeded exactly 50,005 paid orders (>50k boundary, got {totalOrdersInDb})");

        var revenueService = new DefaultRevenueMonitoringQueryService(db, clock);
        var revMetrics = await revenueService.GetRevenueMetricsAsync(new MonitoringFilterRequest
        {
            FromDate = today.AddDays(-1),
            ToDate = today
        }, default);

        Assert(revMetrics.Summary.PaidSubscriptionCount == 50_005,
            $"Revenue Summary.PaidSubscriptionCount is exact without Take(50k) truncation (expected 50,005, got {revMetrics.Summary.PaidSubscriptionCount})");
        Assert(revMetrics.Summary.TotalSubscriptionRevenueVnd == 500_050_000m,
            $"Revenue Summary.TotalSubscriptionRevenueVnd sums ALL 50,005 orders exactly (expected 500,050,000 VND, got {revMetrics.Summary.TotalSubscriptionRevenueVnd})");

        // 4. BOUNDARY CAP TEST: Marketing Costs & CAC30 without Take(10k) cap
        using (var transaction = rawConn.BeginTransaction())
        {
            var insertCmd = rawConn.CreateCommand();
            insertCmd.Transaction = transaction;

            // Insert 10,005 campaign cost entries
            var dateStr = today.AddDays(-35).ToString("yyyy-MM-dd");
            for (var batch = 0; batch < 20; batch++)
            {
                var costValues = new List<string>();
                for (var i = 0; i < 500; i++)
                {
                    var id = Guid.NewGuid();
                    costValues.Add($"('{id}', '{dateStr}', 'BigCampaign', 'cpc', '1000', '0', NULL, '{pgNow}', '{pgNow}', '{Guid.Empty}', 'System')");
                }
                insertCmd.CommandText = $"INSERT INTO \"CampaignCosts\" (\"Id\",\"Date\",\"Campaign\",\"Channel\",\"CashVnd\",\"PersonHours\",\"HourlyRate\",\"CreatedAt\",\"UpdatedAt\",\"UpdatedBy\",\"UpdatedByName\") VALUES {string.Join(",", costValues)}";
                await insertCmd.ExecuteNonQueryAsync();
            }

            // 5 more costs to reach 10,005 (>10k)
            var lastCosts = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var id = Guid.NewGuid();
                lastCosts.Add($"('{id}', '{dateStr}', 'BigCampaign', 'cpc', '1000', '0', NULL, '{pgNow}', '{pgNow}', '{Guid.Empty}', 'System')");
            }
            insertCmd.CommandText = $"INSERT INTO \"CampaignCosts\" (\"Id\",\"Date\",\"Campaign\",\"Channel\",\"CashVnd\",\"PersonHours\",\"HourlyRate\",\"CreatedAt\",\"UpdatedAt\",\"UpdatedBy\",\"UpdatedByName\") VALUES {string.Join(",", lastCosts)}";
            await insertCmd.ExecuteNonQueryAsync();

            transaction.Commit();
        }

        var totalCostsInDb = await db.CampaignCosts.CountAsync();
        Assert(totalCostsInDb == 10_005, $"Seeded exactly 10,005 cost entries (>10k boundary, got {totalCostsInDb})");

        var costMetrics = await revenueService.GetRevenueMetricsAsync(new MonitoringFilterRequest
        {
            FromDate = today.AddDays(-40),
            ToDate = today.AddDays(-31)
        }, default);

        Assert(costMetrics.Summary.TotalMarketingSpendVnd == 10_005_000m,
            $"TotalMarketingSpendVnd sums ALL 10,005 entries exactly without Take(10k) cap (expected 10,005,000 VND, got {costMetrics.Summary.TotalMarketingSpendVnd})");
    }

    private static async Task TestRelationalSubquery_ManyMatchingOwners_SqlTranslation_Npgsql_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);

        // 1. SQL TRANSLATION TEST: PostgreSQL (Npgsql)
        var filter = new MonitoringFilterRequest(Source: "multi-source");
        var campaignChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PostgreSQL Translation Check
        var pgQuery = db.OwnerMilestones.Where(m =>
            DefaultGrowthMonitoringQueryService.GetMatchingOwnerIdsQuery(db, filter, campaignChannels)!.Contains(m.OwnerId));
        var pgSql = pgQuery.ToQueryString();
        Assert(pgSql.Contains("IN (SELECT") || pgSql.Contains("in (select") || (pgSql.Contains("IN (") && pgSql.Contains("RegistrationAttributions")),
            "PostgreSQL (Npgsql) generates relational SQL subquery for matching owner IDs without materialization");
        Assert(!pgSql.Contains("@__matchingOwnerIds_"),
            "Npgsql SQL does not inline or parameterize matching owner IDs");

        // 2. MANY MATCHING OWNERS TEST (EXACT METRIC COUNTS)
        const int matchingCount = 20;
        const int nonMatchingCount = 10;
        var matchingOwnerIds = new List<Guid>();

        for (var i = 0; i < matchingCount; i++)
        {
            var ownerId = Guid.NewGuid();
            matchingOwnerIds.Add(ownerId);
            db.Users.Add(new AppUser
            {
                Id = ownerId,
                Email = $"multi_{i}@example.test",
                Name = $"Multi Owner {i}",
                PasswordHash = "fixture",
                Role = UserRole.Owner,
                Status = OwnerStatus.Approved,
                CreatedAt = now.AddDays(-15)
            });
            db.RegistrationAttributions.Add(new RegistrationAttribution
            {
                OwnerId = ownerId,
                FirstTouchSource = "multi-source",
                FirstTouchMedium = "cpc",
                FirstTouchCampaign = "multi-campaign",
                RegisteredAt = now.AddDays(-15)
            });
            db.OwnerMilestones.Add(new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = now.AddDays(-15),
                ActivatedAt = now.AddDays(-10)
            });
            db.OwnerDailyActivities.Add(new OwnerDailyActivity
            {
                OwnerId = ownerId,
                ActivityDate = today,
                HasOwnerAction = true,
                OwnerActionCount = 1,
                LastActionType = "Login"
            });
            // 5 of these owners paid subscriptions
            if (i < 5)
            {
                db.PayOsAttempts.Add(new PayOsAttempt
                {
                    Id = Guid.NewGuid(),
                    OrderCode = 5000000 + i,
                    OwnerId = ownerId,
                    Amount = 100_000,
                    Kind = "Subscription",
                    Status = PaymentAttemptStatus.Paid,
                    PaidAt = now.AddDays(-2),
                    SubscriptionDays = 30
                });
            }
        }

        // Non-matching owners
        for (var i = 0; i < nonMatchingCount; i++)
        {
            var otherOwnerId = Guid.NewGuid();
            db.Users.Add(new AppUser
            {
                Id = otherOwnerId,
                Email = $"other_{i}@example.test",
                Name = $"Other Owner {i}",
                PasswordHash = "fixture",
                Role = UserRole.Owner,
                Status = OwnerStatus.Approved,
                CreatedAt = now.AddDays(-15)
            });
            db.RegistrationAttributions.Add(new RegistrationAttribution
            {
                OwnerId = otherOwnerId,
                FirstTouchSource = "other-source",
                FirstTouchMedium = "cpc",
                RegisteredAt = now.AddDays(-15)
            });
            db.OwnerMilestones.Add(new OwnerMilestone
            {
                OwnerId = otherOwnerId,
                RegisteredAt = now.AddDays(-15),
                ActivatedAt = now.AddDays(-10)
            });
            db.OwnerDailyActivities.Add(new OwnerDailyActivity
            {
                OwnerId = otherOwnerId,
                ActivityDate = today,
                HasOwnerAction = true,
                OwnerActionCount = 1,
                LastActionType = "Login"
            });
            db.PayOsAttempts.Add(new PayOsAttempt
            {
                Id = Guid.NewGuid(),
                OrderCode = 6000000 + i,
                OwnerId = otherOwnerId,
                Amount = 100_000,
                Kind = "Subscription",
                Status = PaymentAttemptStatus.Paid,
                PaidAt = now.AddDays(-2),
                SubscriptionDays = 30
            });
        }

        await db.SaveChangesAsync();

        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);
        var growthMetrics = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest
        {
            Source = "multi-source",
            FromDate = today.AddDays(-30),
            ToDate = today
        }, default);

        Assert(growthMetrics.Summary.Registrations == matchingCount,
            $"Growth metrics matching registrations count is exactly {matchingCount} (got {growthMetrics.Summary.Registrations})");
        Assert(growthMetrics.Summary.ActivatedOwners == matchingCount,
            $"Growth metrics matching activated owners count is exactly {matchingCount} (got {growthMetrics.Summary.ActivatedOwners})");
        Assert(growthMetrics.Summary.CurrentWau == matchingCount,
            $"Growth metrics matching WAU count is exactly {matchingCount} (got {growthMetrics.Summary.CurrentWau})");
        Assert(growthMetrics.Summary.CurrentMau == matchingCount,
            $"Growth metrics matching MAU count is exactly {matchingCount} (got {growthMetrics.Summary.CurrentMau})");

        var revenueService = new DefaultRevenueMonitoringQueryService(db, clock);
        var revenueMetrics = await revenueService.GetRevenueMetricsAsync(new MonitoringFilterRequest
        {
            Source = "multi-source",
            FromDate = today.AddDays(-30),
            ToDate = today
        }, default);

        Assert(revenueMetrics.Summary.TotalSubscriptionRevenueVnd == 500_000m,
            $"Revenue metrics sums matching subscriptions only: expected 500,000 VND, got {revenueMetrics.Summary.TotalSubscriptionRevenueVnd}");
        Assert(revenueMetrics.Summary.PaidSubscriptionCount == 5,
            $"Revenue metrics counts matching paid subscriptions only: expected 5, got {revenueMetrics.Summary.PaidSubscriptionCount}");
    }

    private static async Task TestPruneExpiredTelemetry_CompletedOutboxPruned_PendingFailedPreserved_Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var clock = TimeProvider.System;
        var now = clock.GetUtcNow();

        var durableOwnerId = Guid.NewGuid();
        var oldCompletedId = Guid.NewGuid();
        var freshCompletedId = Guid.NewGuid();
        var oldPendingId = Guid.NewGuid();
        var oldFailedId = Guid.NewGuid();

        db.Users.Add(new AppUser
        {
            Id = durableOwnerId,
            Email = "durable@example.test",
            Name = "Durable User",
            PasswordHash = "fixture",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved,
            CreatedAt = now.AddDays(-60)
        });

        // 1. Old completed outbox event (35 days old - exceeds 30-day retention)
        db.MilestoneOutboxEvents.Add(new MilestoneOutboxEvent
        {
            Id = oldCompletedId,
            OwnerId = durableOwnerId,
            EventType = "SubscriptionPaid",
            TargetId = "12345:System",
            OccurredAt = now.AddDays(-35),
            ProcessedAt = now.AddDays(-35),
            Status = "Completed"
        });

        // 2. Fresh completed outbox event (5 days old - within 30-day retention)
        db.MilestoneOutboxEvents.Add(new MilestoneOutboxEvent
        {
            Id = freshCompletedId,
            OwnerId = durableOwnerId,
            EventType = "SubscriptionPaid",
            TargetId = "12346:System",
            OccurredAt = now.AddDays(-5),
            ProcessedAt = now.AddDays(-5),
            Status = "Completed"
        });

        // 3. Old pending outbox event (40 days old - MUST BE PRESERVED FOR RETRY!)
        db.MilestoneOutboxEvents.Add(new MilestoneOutboxEvent
        {
            Id = oldPendingId,
            OwnerId = durableOwnerId,
            EventType = "InvoiceIssued",
            TargetId = $"{Guid.NewGuid()}:Owner",
            OccurredAt = now.AddDays(-40),
            Status = "Pending",
            RetryCount = 3,
            LastError = "ERR_TIMEOUT"
        });

        // 4. Old failed outbox event (40 days old - MUST BE PRESERVED FOR AUDIT/RECOVERY!)
        db.MilestoneOutboxEvents.Add(new MilestoneOutboxEvent
        {
            Id = oldFailedId,
            OwnerId = durableOwnerId,
            EventType = "InvoiceIssued",
            TargetId = $"{Guid.NewGuid()}:Owner",
            OccurredAt = now.AddDays(-40),
            Status = "Failed",
            RetryCount = 10,
            LastError = "ERR_DB_UPDATE"
        });

        // 5. Durable milestone (MUST NEVER BE PRUNED!)
        db.OwnerMilestones.Add(new OwnerMilestone
        {
            OwnerId = durableOwnerId,
            RegisteredAt = now.AddDays(-50),
            ActivatedAt = now.AddDays(-40),
            FirstPaidAt = now.AddDays(-35)
        });

        await db.SaveChangesAsync();

        var maintenanceService = new DefaultMonitoringMaintenanceService(db, clock);
        var pruned = await maintenanceService.PruneExpiredTelemetryAsync(default);

        Assert(pruned >= 1, $"PruneExpiredTelemetryAsync returns pruned count >= 1 (got {pruned})");

        var oldCompletedExists = await db.MilestoneOutboxEvents.AnyAsync(e => e.Id == oldCompletedId);
        Assert(!oldCompletedExists, "Old completed outbox event (>30d) is pruned by maintenance service");

        var freshCompletedExists = await db.MilestoneOutboxEvents.AnyAsync(e => e.Id == freshCompletedId);
        Assert(freshCompletedExists, "Fresh completed outbox event (<=30d) is preserved");

        var oldPendingExists = await db.MilestoneOutboxEvents.AnyAsync(e => e.Id == oldPendingId);
        Assert(oldPendingExists, "Old pending outbox event (>30d) is preserved for durable retry");

        var oldFailedExists = await db.MilestoneOutboxEvents.AnyAsync(e => e.Id == oldFailedId);
        Assert(oldFailedExists, "Old failed outbox event (>30d) is preserved for audit");

        var milestoneExists = await db.OwnerMilestones.AnyAsync(m => m.OwnerId == durableOwnerId);
        Assert(milestoneExists, "Durable owner milestone is preserved indefinitely");
    }

    private sealed class FailingMilestoneTracker(IOwnerMilestoneTracker inner, bool shouldFail) : IOwnerMilestoneTracker
    {
        public bool ShouldFail { get; set; } = shouldFail;

        public Task TrackRegistrationStartedAsync(Guid attemptId, RegistrationAttributionDto? attribution, string verificationMethod, CancellationToken ct)
            => inner.TrackRegistrationStartedAsync(attemptId, attribution, verificationMethod, ct);

        public Task TrackRegistrationCompletedAsync(Guid attemptId, Guid ownerId, string stage, CancellationToken ct)
            => inner.TrackRegistrationCompletedAsync(attemptId, ownerId, stage, ct);

        public Task TrackRegistrationResentAsync(Guid attemptId, CancellationToken ct)
            => inner.TrackRegistrationResentAsync(attemptId, ct);

        public Task TrackOwnerRegisteredAsync(Guid ownerId, RegistrationAttributionDto? attribution, string verificationMethod, CancellationToken ct)
            => inner.TrackOwnerRegisteredAsync(ownerId, attribution, verificationMethod, ct);

        public Task TrackInvoiceIssuedAsync(Guid ownerId, Guid invoiceId, string kind, ActorType actor = ActorType.Owner, DateTimeOffset? issuedAt = null, CancellationToken ct = default)
        {
            if (ShouldFail) throw new InvalidOperationException("Simulated transient postcommit telemetry failure on TrackInvoiceIssuedAsync");
            return inner.TrackInvoiceIssuedAsync(ownerId, invoiceId, kind, actor, issuedAt, ct);
        }

        public Task TrackSubscriptionPaidAsync(Guid ownerId, long orderCode, int amount, ActorType actor = ActorType.System, DateTimeOffset? paidAt = null, CancellationToken ct = default)
        {
            if (ShouldFail) throw new InvalidOperationException("Simulated transient postcommit telemetry failure on TrackSubscriptionPaidAsync");
            return inner.TrackSubscriptionPaidAsync(ownerId, orderCode, amount, actor, paidAt, ct);
        }

        public Task RecordOwnerActivityAsync(Guid ownerId, ActorType actorType, string actionType, CancellationToken ct)
            => inner.RecordOwnerActivityAsync(ownerId, actorType, actionType, ct);

        public Task EnsureSubscriptionPaidMilestoneAsync(Guid ownerId, CancellationToken ct = default)
            => inner.EnsureSubscriptionPaidMilestoneAsync(ownerId, ct);

        public Task EnsureInvoiceIssuedMilestoneAsync(Guid ownerId, CancellationToken ct = default)
            => inner.EnsureInvoiceIssuedMilestoneAsync(ownerId, ct);

        public Task<int> ProcessPendingOutboxEventsAsync(CancellationToken ct = default)
            => inner.ProcessPendingOutboxEventsAsync(ct);

        public Task RepairMissingMilestonesAsync(CancellationToken ct = default)
            => inner.RepairMissingMilestonesAsync(ct);
    }

    private sealed class TestScopeFactory(AppDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope(db);

        private sealed class TestScope(AppDbContext db) : IServiceScope
        {
            public IServiceProvider ServiceProvider => new TestServiceProvider(db);
            public void Dispose() { }
        }

        private sealed class TestServiceProvider(AppDbContext db) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(AppDbContext) || serviceType == typeof(PostgresAppDbContext)) return db;
                return null;
            }
        }
    }

    private sealed class FakePayOsHandler : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":\"00\",\"data\":{}}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

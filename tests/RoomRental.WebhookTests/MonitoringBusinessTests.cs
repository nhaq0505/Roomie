using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Services;
using static MonitoringFoundationTests;

internal static class MonitoringBusinessTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("ASSERTION FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        await TestOtpAndRegistrationAttributionFlowAsync();
        await TestMilestonesAndActivityHooksAsync();
        await TestRevenueQueryAndCac30Async();
        await TestGrowthQueryAndMaturityAsync();
        await TestFilteredZeroWithNonZeroAggregateAsync();
        await TestOlderThan90DaysRawAbsentAsync();
        await TestFilteredFunnelAttributionAsync();
        await TestInvalidAndExtremeDateRangesAsync();
        await TestRealSqlitePostReadingsSuccessRollbackAndNoStallAsync();
        await TestCampaignTrackingUrlAndEndpointAsync();
        await TestMonitoringMaintenanceServiceAsync();
        await TestHttpBusinessFlowsAndDegradedTelemetryAsync();
    }

    private static async Task<(SqliteConnection Connection, SqliteAppDbContext Db)> CreateTestDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(connection).Options;
        var db = new SqliteAppDbContext(options);
        await db.Database.MigrateAsync();
        return (connection, db);
    }

    private static async Task TestOtpAndRegistrationAttributionFlowAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var _ = connection;
        await using var __ = db;

        var tracker = new OwnerMilestoneTracker(db, TimeProvider.System);

        // 1. Started event with attribution
        var attemptId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var attribution = new RegistrationAttributionDto(
            BrowserId: "browser-otp-1",
            FirstTouch: new AttributionTouchDto("facebook", "cpc", "summer_promo", "v1", "phong_tro", "/landing", null, DateTimeOffset.UtcNow.AddHours(-1)),
            LastTouch: new AttributionTouchDto("google", "search", "brand", null, null, "/register", null, DateTimeOffset.UtcNow)
        );

        await tracker.TrackRegistrationStartedAsync(attemptId, attribution, "Code", default);
        var startedFact = await db.RegistrationFacts.SingleAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "Started");
        Assert(startedFact.VerificationMethod == "Code" && startedFact.UtmSource == "facebook" && startedFact.LandingPage == "/landing",
            "Registration started fact recorded with Code method and touchpoint attribution");

        // 2. OTP Resend event
        await tracker.TrackRegistrationResentAsync(attemptId, default);
        var resendFact = await db.RegistrationFacts.SingleAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "CodeResent");
        Assert(resendFact.VerificationMethod == "Code", "OTP resend recorded CodeResent lifecycle fact");

        // 3. OTP Verified event
        var user = new AppUser
        {
            Id = ownerId,
            Email = "otp.owner@test.example",
            Name = "OTP Owner",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await tracker.TrackOwnerRegisteredAsync(ownerId, attribution, "Code", default);
        var verifiedFact = await db.RegistrationFacts.SingleAsync(x => x.OwnerId == ownerId && x.Stage == "Verified");
        Assert(verifiedFact.VerificationMethod == "Code", "OTP completion recorded Verified lifecycle fact");

        var milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == ownerId);
        Assert(milestone.RegisteredAt.HasValue, "OwnerMilestone created with RegisteredAt");

        var attr = await db.RegistrationAttributions.SingleAsync(x => x.OwnerId == ownerId);
        Assert(attr.FirstTouchSource == "facebook" && attr.LastTouchSource == "google" && attr.VerificationMethod == "Code",
            "RegistrationAttribution preserved distinct first and last touch");

        // 4. Bypass OTP registration flow
        var bypassOwner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "bypass.owner@test.example",
            Name = "Bypass Owner",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(bypassOwner);
        await db.SaveChangesAsync();

        await tracker.TrackRegistrationStartedAsync(bypassOwner.Id, null, "Bypass", default);
        await tracker.TrackOwnerRegisteredAsync(bypassOwner.Id, null, "Bypass", default);

        var bypassFact = await db.RegistrationFacts.SingleAsync(x => x.OwnerId == bypassOwner.Id && x.Stage == "Bypassed");
        Assert(bypassFact.VerificationMethod == "Bypass", "Bypass registration recorded Bypassed lifecycle fact");

        var bypassMilestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == bypassOwner.Id);
        Assert(bypassMilestone.RegisteredAt.HasValue, "Bypass milestone created with RegisteredAt");

        // 5. Telemetry error does not crash auth or caller
        // Non-existent owner in Users should safely return without throwing
        var unrecordedId = Guid.NewGuid();
        await tracker.TrackOwnerRegisteredAsync(unrecordedId, null, "Code", default);
        var unrecordedMilestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == unrecordedId);
        Assert(unrecordedMilestone == null, "Telemetry gracefully handles non-existent user without throwing");
    }

    private static async Task TestMilestonesAndActivityHooksAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var tracker = new OwnerMilestoneTracker(db, TimeProvider.System);
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "activity.owner@test.example",
            Name = "Activity Owner",
            PasswordHash = "fixture",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        // Initialize registration milestone
        await tracker.TrackOwnerRegisteredAsync(owner.Id, null, "Code", default);
        var milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == null, "New owner has ActivatedAt == null before first monthly invoice");

        // 1. Deposit invoice does NOT activate owner
        var depositId = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, depositId, "Deposit", ActorType.Owner, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == null, "Deposit invoice does not activate owner");

        // 2. First Monthly invoice activates owner
        var monthlyId = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, monthlyId, "Monthly", ActorType.Owner, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt != null && milestone.FirstActivatedInvoiceId == monthlyId,
            "Direct issued monthly invoice activates owner with ActivatedAt and FirstActivatedInvoiceId");

        // 3. Second Monthly invoice preserves original activation milestone
        var origActivatedAt = milestone.ActivatedAt;
        var secondMonthlyId = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, secondMonthlyId, "Monthly", ActorType.Owner, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == origActivatedAt && milestone.FirstActivatedInvoiceId == monthlyId,
            "Subsequent monthly invoice preserves original activation timestamp and FirstActivatedInvoiceId");

        // 4. System sweep invoice generation increments SystemActionCount, NOT OwnerActionCount
        var today = ApiSupport.VietnamDate(DateTimeOffset.UtcNow);
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.System, "InvoiceSweep", default);
        var activity = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(activity.SystemActionCount == 1 && activity.OwnerActionCount == 3 && activity.HasOwnerAction, // 1 registration + 2 monthly invoices
            "System sweep increments SystemActionCount without incrementing OwnerActionCount");

        // 5. Explicit owner domain actions increment OwnerActionCount
        var actions = new[]
        {
            "BuildingCreate", "BuildingUpdate", "BuildingArchive", "BuildingRestore",
            "RoomCreate", "RoomUpdate", "RoomArchive", "RoomRestore",
            "OccupancyMoveIn", "OccupancyMoveOut", "ReadingRecorded",
            "DepositSettlement", "InvoiceCorrection", "ManualReceipt"
        };
        foreach (var action in actions)
        {
            await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.Owner, action, default);
        }
        activity = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(activity.OwnerActionCount == 3 + actions.Length && activity.HasOwnerAction,
            $"All domain owner actions incremented OwnerActionCount to {activity.OwnerActionCount}");

        // 6. Tenant portal open and checkout attempt record system actions
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.System, "TenantPortalOpen", default);
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.System, "TenantCheckoutAttempt", default);
        activity = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(activity.SystemActionCount == 3, "Tenant portal open and checkout attempt record SystemActionCount");
    }

    private static async Task TestRevenueQueryAndCac30Async()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var revenueService = new DefaultRevenueMonitoringQueryService(db, clock);
        var tracker = new OwnerMilestoneTracker(db, clock);

        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "rev.owner@test.example",
            Name = "Revenue Owner",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-40)
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        await tracker.TrackOwnerRegisteredAsync(owner.Id, null, "Code", default);

        // 1. Subscription Paid event sets FirstPaidAt
        var orderCode = 10001L;
        var paidAt = DateTimeOffset.UtcNow.AddDays(-35);
        db.PayOsAttempts.Add(new PayOsAttempt
        {
            OrderCode = orderCode,
            OwnerId = owner.Id,
            Kind = "Subscription",
            Amount = 199_000,
            Status = PaymentAttemptStatus.Paid,
            PaidAt = paidAt,
            SubscriptionDays = 30,
            Description = "ReHouse Pro 30d",
            CreatedAt = paidAt.AddMinutes(-5),
            ExpiresAt = paidAt.AddMinutes(10).ToUnixTimeSeconds()
        });
        await db.SaveChangesAsync();

        await tracker.TrackSubscriptionPaidAsync(owner.Id, orderCode, 199_000, ActorType.System, paidAt, default);
        var milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.FirstPaidAt.HasValue && milestone.FirstPaidOrderCode == orderCode,
            "Subscription paid records FirstPaidAt and FirstPaidOrderCode on milestone");

        // 2. Duplicate subscription payment confirmation is idempotent
        await tracker.TrackSubscriptionPaidAsync(owner.Id, orderCode, 199_000, ActorType.System, paidAt, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.FirstPaidOrderCode == orderCode, "Duplicate subscription paid confirmation is idempotent");

        // 3. Exclusions: Tenant invoices and admin-granted Pro are NOT in revenue
        // Tenant invoice attempt
        db.PayOsAttempts.Add(new PayOsAttempt
        {
            OrderCode = 20002L,
            OwnerId = owner.Id,
            InvoiceId = Guid.NewGuid(),
            Kind = "Rent",
            Amount = 3_500_000,
            Status = PaymentAttemptStatus.Paid,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-2),
            Description = "Tenant Rent Invoice",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(10).ToUnixTimeSeconds()
        });
        // Admin-granted Pro (no PayOsAttempt, just user SubscriptionTier / SubscriptionExpiresAt)
        var adminGrantedUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "admin.pro@test.example",
            Name = "Admin Pro",
            PasswordHash = "hash",
            SubscriptionTier = SubscriptionTier.Pro,
            SubscriptionExpiresAt = DateTimeOffset.UtcNow.AddDays(365),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        db.Users.Add(adminGrantedUser);
        await db.SaveChangesAsync();

        var today = ApiSupport.VietnamDate(DateTimeOffset.UtcNow);
        var filterAll = new MonitoringFilterRequest(today.AddDays(-60), today, null, null, null);
        var revResp = await revenueService.GetRevenueMetricsAsync(filterAll, default);
        Assert(revResp.Summary.TotalSubscriptionRevenueVnd == 199_000m && revResp.Summary.PaidSubscriptionCount == 1,
            "Revenue query counts only confirmed subscription orders, excluding tenant rent and admin-granted Pro");

        // 4. Campaign Costs: Cash + Imputed Labor
        var campDate = today.AddDays(-35);
        db.CampaignCosts.Add(new MarketingCampaignCost
        {
            Campaign = "fb_launch",
            Channel = "facebook",
            Source = "fb_ad",
            Date = campDate,
            CashVnd = 1_000_000m,
            PersonHours = 4.0m,
            HourlyRate = null, // Missing valuation!
            UpdatedByName = "Admin"
        });
        await db.SaveChangesAsync();

        var costResp = await revenueService.GetRevenueMetricsAsync(filterAll, default);
        Assert(costResp.Summary.TotalMarketingSpendVnd == 1_000_000m && costResp.Summary.TotalMarketingHours == 4.0m,
            "Marketing spend without hourly rate counts cash (1,000,000) and person-hours (4h) with null imputed labor");
        Assert(costResp.Summary.Cac30.NullReason == "MissingValuation" && !costResp.Summary.Cac30.IsAvailable,
            "CAC30 returns MissingValuation when person hours > 0 but hourly rate is omitted");

        // Now set explicit HourlyRate
        var costItem = await db.CampaignCosts.SingleAsync(c => c.Campaign == "fb_launch");
        costItem.HourlyRate = 150_000m;
        await db.SaveChangesAsync();

        costResp = await revenueService.GetRevenueMetricsAsync(filterAll, default);
        Assert(costResp.Summary.TotalMarketingSpendVnd == 1_600_000m && costResp.Summary.TotalMarketingHours == 4.0m,
            "Marketing spend correctly sums cash (1,000,000) + imputed labor (4h * 150,000 = 600,000) = 1,600,000");

        // 5. CAC30 Null Reasons:
        // Reason a: MissingCostData (when period has registrations but no costs)
        var filterNoCost = new MonitoringFilterRequest(today.AddDays(-20), today.AddDays(-15), null, null, null);
        var respNoCost = await revenueService.GetRevenueMetricsAsync(filterNoCost, default);
        Assert(respNoCost.Summary.Cac30.NullReason == "MissingCostData" && !respNoCost.Summary.Cac30.IsAvailable && respNoCost.Summary.Cac30.ValueVnd == null,
            "CAC30 returns MissingCostData when period has no cost data");

        // Reason b: NoRegistrations (cost exists but 0 registrations)
        var filterNoReg = new MonitoringFilterRequest(campDate.AddDays(1), campDate.AddDays(1), null, null, null);
        db.CampaignCosts.Add(new MarketingCampaignCost
        {
            Campaign = "empty_camp",
            Date = campDate.AddDays(1),
            CashVnd = 500_000m,
            PersonHours = 0m,
            UpdatedByName = "Admin"
        });
        await db.SaveChangesAsync();
        var respNoReg = await revenueService.GetRevenueMetricsAsync(filterNoReg, default);
        Assert(respNoReg.Summary.Cac30.NullReason == "NoRegistrations" && !respNoReg.Summary.Cac30.IsAvailable,
            "CAC30 returns NoRegistrations when cost exists but 0 registrations in period");

        // Reason c: CohortImmature (registration within last 30 days)
        var filterImmature = new MonitoringFilterRequest(today.AddDays(-10), today, null, null, null);
        db.CampaignCosts.Add(new MarketingCampaignCost
        {
            Campaign = "recent_camp",
            Date = today.AddDays(-5),
            CashVnd = 500_000m,
            PersonHours = 0m,
            UpdatedByName = "Admin"
        });
        var recentUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "recent@test.example",
            Name = "Recent User",
            PasswordHash = "h",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        db.Users.Add(recentUser);
        await db.SaveChangesAsync();
        await tracker.TrackOwnerRegisteredAsync(recentUser.Id, null, "Code", default);
        var respImmature = await revenueService.GetRevenueMetricsAsync(filterImmature, default);
        Assert(respImmature.Summary.Cac30.NullReason == "CohortImmature" && !respImmature.Summary.Cac30.IsAvailable,
            "CAC30 returns CohortImmature when cohort period is within the last 30 days");

        // Reason d: ZeroDenominator (mature cohort with registrations and cost, but 0 paid conversions within 30 days)
        var matureDate = today.AddDays(-50);
        var filterZeroDenom = new MonitoringFilterRequest(matureDate, matureDate, null, null, null);
        db.CampaignCosts.Add(new MarketingCampaignCost
        {
            Campaign = "zero_conv_camp",
            Date = matureDate,
            CashVnd = 2_000_000m,
            PersonHours = 0m,
            UpdatedByName = "Admin"
        });
        var freeUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "free@test.example",
            Name = "Free User",
            PasswordHash = "h",
            CreatedAt = ApiSupport.StartOfVietnamDate(matureDate).AddHours(2)
        };
        db.Users.Add(freeUser);
        await db.SaveChangesAsync();
        await tracker.TrackOwnerRegisteredAsync(freeUser.Id, null, "Code", default);
        var freeMilestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == freeUser.Id);
        freeMilestone.RegisteredAt = freeUser.CreatedAt;
        await db.SaveChangesAsync();

        var respZeroDenom = await revenueService.GetRevenueMetricsAsync(filterZeroDenom, default);
        Assert(respZeroDenom.Summary.Cac30.NullReason == "ZeroDenominator" && !respZeroDenom.Summary.Cac30.IsAvailable,
            "CAC30 returns ZeroDenominator when mature cohort has 0 paid conversions within 30 days");

        // Reason e: Valid Numeric CAC30
        var filterValid = new MonitoringFilterRequest(campDate, campDate, null, null, null);
        // Owner registered on campDate (AddDays(-35)) and paid on AddDays(-35) (within 30 days)
        // Spend on campDate = 1,600,000 VND. Conversions = 1. CAC = 1,600,000 VND.
        // Update owner.CreatedAt to campDate
        owner.CreatedAt = ApiSupport.StartOfVietnamDate(campDate).AddHours(1);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        milestone.RegisteredAt = owner.CreatedAt;
        milestone.FirstPaidAt = milestone.RegisteredAt.Value.AddDays(5);
        await db.SaveChangesAsync();

        var respValid = await revenueService.GetRevenueMetricsAsync(filterValid, default);
        Assert(respValid.Summary.Cac30.IsAvailable && respValid.Summary.Cac30.NullReason == null && respValid.Summary.Cac30.ValueVnd == 1_600_000m,
            $"CAC30 correctly calculates valid numeric metric: expected 1,600,000 VND, got {respValid.Summary.Cac30.ValueVnd} VND");
    }

    private static async Task TestGrowthQueryAndMaturityAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);
        var tracker = new OwnerMilestoneTracker(db, clock);

        var now = DateTimeOffset.UtcNow;
        var today = ApiSupport.VietnamDate(now);

        // 1. Seed events in Vietnam timezone
        var startUtc = ApiSupport.StartOfVietnamDate(today.AddDays(-2));
        db.AnalyticsEvents.AddRange(
            new ClientAnalyticsEvent
            {
                EventId = "evt-g-1",
                BrowserId = "b-1",
                SessionId = "s-1",
                EventType = "page_view",
                Path = "/home",
                UtmSource = "facebook",
                UtmMedium = "cpc",
                ServerTimestamp = startUtc.AddHours(2)
            },
            new ClientAnalyticsEvent
            {
                EventId = "evt-g-2",
                BrowserId = "b-2",
                SessionId = "s-2",
                EventType = "cta_click",
                Path = "/register",
                CtaId = "hero_cta",
                UtmSource = "google",
                UtmMedium = "cpc",
                ServerTimestamp = startUtc.AddHours(4)
            }
        );

        // 2. Seed owner with milestone and activity
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "growth.owner@test.example",
            Name = "Growth Owner",
            PasswordHash = "h",
            CreatedAt = startUtc.AddHours(5)
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        await tracker.TrackOwnerRegisteredAsync(owner.Id, new RegistrationAttributionDto("b-1", new AttributionTouchDto("facebook", "cpc", null, null, null, null, null)), "Code", default);
        await tracker.TrackInvoiceIssuedAsync(owner.Id, Guid.NewGuid(), "Monthly", ActorType.Owner, default);

        var filter = new MonitoringFilterRequest(today.AddDays(-7), today, null, null, null);
        var growth = await growthService.GetGrowthMetricsAsync(filter, default);

        Assert(growth.Summary.TotalSessions == 2 && growth.Summary.UniqueVisitors == 2,
            "Growth summary calculates sessions and unique visitors");
        Assert(growth.Summary.Registrations == 1 && growth.Summary.ActivatedOwners == 1,
            "Growth summary calculates registrations and activated owners");
        Assert(growth.Summary.CurrentWau == 1 && growth.Summary.CurrentMau == 1,
            "Growth summary WAU and MAU include active owner");

        Assert(growth.Sources.Count > 0, "Growth metrics returns traffic sources");
        Assert(growth.TopCtas.Count == 1 && growth.TopCtas[0].CtaId == "hero_cta",
            "Growth metrics returns top CTA performance");

        // 3. Retention matrix with nullable immature future cells
        Assert(growth.RetentionMatrix.Count > 0, "Retention matrix contains cohorts");
        var latestCohort = growth.RetentionMatrix.Last();
        Assert(latestCohort.RetentionPercentages[0] == 100.0, "Month 0 retention is 100%");
        // Future month must be null (not 0.0)
        Assert(latestCohort.RetentionPercentages.Count >= 2 && latestCohort.RetentionPercentages[1] == null,
            "Retention matrix immature future month is null (not 0.0)");

        // 4. Funnel facts & attribution filter consistency
        Assert(growth.Funnel != null, "Growth metrics returns registration funnel");
        Assert(growth.Funnel!.StartedCount >= 0, "Funnel started count is non-negative");

        // Test consistent filtering (never mix first touch with last touch)
        // Owner has FirstTouch(facebook, cpc)
        var matchFilter = new MonitoringFilterRequest(today.AddDays(-7), today, "facebook", null, null);
        var matchGrowth = await growthService.GetGrowthMetricsAsync(matchFilter, default);
        Assert(matchGrowth.Summary.Registrations == 1, "Matching source filter includes owner");

        var nonMatchFilter = new MonitoringFilterRequest(today.AddDays(-7), today, "google", null, null);
        var nonMatchGrowth = await growthService.GetGrowthMetricsAsync(nonMatchFilter, default);
        Assert(nonMatchGrowth.Summary.Registrations == 0, "Non-matching source filter excludes owner");

        // 5. Daily series points
        Assert(growth.DailySeries.Count >= 8, "Daily growth points contain all filtered dates");
    }

    private static async Task TestFilteredZeroWithNonZeroAggregateAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);

        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var targetDate = today.AddDays(-5); // within 90d raw retention
        var dayStart = ApiSupport.StartOfVietnamDate(targetDate);

        // 1. Raw event exists only for "google" on targetDate
        db.AnalyticsEvents.Add(new ClientAnalyticsEvent
        {
            EventId = "evt-zero-google",
            BrowserId = "b-zero-1",
            SessionId = "s-zero-google",
            EventType = "page_view",
            Path = "/home",
            UtmSource = "google",
            ServerTimestamp = dayStart.AddHours(2)
        });

        // 2. DailyAggregateMetric has TotalSessions = 150 (all-traffic)
        db.DailyAggregateMetrics.Add(new DailyAggregateMetric
        {
            Date = targetDate,
            TotalSessions = 150,
            DistinctVisitors = 120,
            CalculatedAt = now
        });
        await db.SaveChangesAsync();

        // 3. Query filtered by source="facebook"
        // Target date has 0 facebook sessions. It MUST remain 0, NEVER fallback to 150!
        var filteredResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: targetDate,
            ToDate: targetDate,
            Source: "facebook"
        ), default);

        var dayPoint = filteredResp.DailySeries.Single(p => p.Date == targetDate);
        Assert(dayPoint.Sessions == 0,
            $"Real filtered zero must stay 0 even with nonzero aggregate (got {dayPoint.Sessions})");
        Assert(filteredResp.Summary.TotalSessions == 0,
            $"Filtered summary TotalSessions must be 0, not all-traffic aggregate (got {filteredResp.Summary.TotalSessions})");

        // 4. Unfiltered query inside 90d must report the 1 raw session, NOT fallback to 150
        var unfiltResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: targetDate,
            ToDate: targetDate
        ), default);

        var unfiltPoint = unfiltResp.DailySeries.Single(p => p.Date == targetDate);
        Assert(unfiltPoint.Sessions == 1,
            $"Unfiltered daily series with raw coverage must use raw events count (1), not aggregate (got {unfiltPoint.Sessions})");
        Assert(unfiltResp.Summary.TotalSessions == 1,
            $"Unfiltered summary TotalSessions with raw coverage must be 1 (got {unfiltResp.Summary.TotalSessions})");
    }

    private static async Task TestOlderThan90DaysRawAbsentAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);

        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var oldDate = today.AddDays(-120); // >90d raw retention cutoff: proven raw coverage missing

        // Insert aggregate for oldDate
        db.DailyAggregateMetrics.Add(new DailyAggregateMetric
        {
            Date = oldDate,
            TotalSessions = 300,
            DistinctVisitors = 220,
            CalculatedAt = now
        });
        await db.SaveChangesAsync();

        // 1. Unfiltered query >90d:
        // Daily series sessions uses aggregate (dimensions match!)
        // Summary UniqueVisitors must be NULL (cannot sum daily distinct as period distinct)
        // Summary TotalSessions sums daily aggregate = 300
        var unfiltResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: oldDate,
            ToDate: oldDate
        ), default);

        var oldPoint = unfiltResp.DailySeries.Single(p => p.Date == oldDate);
        Assert(oldPoint.Sessions == 300,
            $"Unfiltered daily point >90d uses aggregate TotalSessions (expected 300, got {oldPoint.Sessions})");
        Assert(unfiltResp.Summary.UniqueVisitors == null,
            "Summary UniqueVisitors across missing raw coverage must be null (cannot sum daily distinct)");
        Assert(unfiltResp.Summary.TotalSessions == 300,
            $"Summary TotalSessions across missing raw coverage with complete aggregate must be 300 (got {unfiltResp.Summary.TotalSessions})");
        Assert(unfiltResp.TopCtas.Count == 0,
            "Top CTAs must be empty when raw events are pruned");

        // 2. Filtered query >90d (source="facebook"):
        // Dimensions DO NOT MATCH (DailyAggregateMetric is all-traffic).
        // Must report explicit NULL, NEVER fallback to 300 and NEVER fake 0!
        var filtResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: oldDate,
            ToDate: oldDate,
            Source: "facebook"
        ), default);

        var filtPoint = filtResp.DailySeries.Single(p => p.Date == oldDate);
        Assert(filtPoint.Sessions == null,
            "Filtered daily point >90d must report null (dimensions do not match aggregate)");
        Assert(filtResp.Summary.TotalSessions == null,
            "Filtered summary TotalSessions >90d must report null");
        Assert(filtResp.Summary.UniqueVisitors == null,
            "Filtered summary UniqueVisitors >90d must report null");
    }

    private static async Task TestFilteredFunnelAttributionAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);

        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var targetDate = today.AddDays(-2);
        var dayTime = ApiSupport.StartOfVietnamDate(targetDate).AddHours(3);

        var attemptA = Guid.NewGuid();
        var attemptB = Guid.NewGuid();
        var attemptC = Guid.NewGuid();

        // Attempt A: facebook
        db.RegistrationFacts.AddRange(
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptA,
                Stage = "Started",
                UtmSource = "facebook",
                UtmCampaign = "spring",
                OccurredAt = dayTime
            },
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptA,
                Stage = "Verified",
                UtmSource = "facebook",
                UtmCampaign = "spring",
                OccurredAt = dayTime.AddMinutes(2)
            }
        );

        // Attempt B: google
        db.RegistrationFacts.AddRange(
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptB,
                Stage = "Started",
                UtmSource = "google",
                UtmCampaign = "promo",
                OccurredAt = dayTime
            },
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptB,
                Stage = "CodeResent",
                UtmSource = "google",
                UtmCampaign = "promo",
                OccurredAt = dayTime.AddMinutes(1)
            }
        );

        // Attempt C: Direct / Unattributed attempt
        db.RegistrationFacts.AddRange(
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptC,
                Stage = "Started",
                UtmSource = null,
                UtmCampaign = null,
                OccurredAt = dayTime
            },
            new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptC,
                Stage = "Bypassed",
                VerificationMethod = "Bypass",
                UtmSource = null,
                UtmCampaign = null,
                OccurredAt = dayTime.AddMinutes(1)
            }
        );
        await db.SaveChangesAsync();

        // 1. Filtered by source=facebook
        var fbResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: targetDate,
            ToDate: targetDate,
            Source: "facebook"
        ), default);

        Assert(fbResp.Funnel != null, "Funnel exists on filtered response");
        Assert(fbResp.Funnel!.StartedCount == 1,
            $"Filtered funnel StartedCount must be 1 (got {fbResp.Funnel.StartedCount})");
        Assert(fbResp.Funnel.VerifiedCount == 1,
            $"Filtered funnel VerifiedCount must be 1 (got {fbResp.Funnel.VerifiedCount})");
        Assert(fbResp.Funnel.CodeResentCount == 0,
            $"Filtered funnel CodeResentCount must be 0 (got {fbResp.Funnel.CodeResentCount})");
        Assert(fbResp.Funnel.BypassedCount == 0,
            $"Filtered funnel BypassedCount must be 0 (got {fbResp.Funnel.BypassedCount})");
        Assert(fbResp.Funnel.UnknownAttemptCount == 1,
            $"Filtered funnel UnknownAttemptCount must report 1 unattributed attempt (got {fbResp.Funnel.UnknownAttemptCount})");

        // 2. Filtered by source=google
        var googleResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: targetDate,
            ToDate: targetDate,
            Source: "google"
        ), default);

        Assert(googleResp.Funnel!.StartedCount == 1,
            $"Google funnel StartedCount must be 1 (got {googleResp.Funnel.StartedCount})");
        Assert(googleResp.Funnel.CodeResentCount == 1,
            $"Google funnel CodeResentCount must be 1 (got {googleResp.Funnel.CodeResentCount})");
        Assert(googleResp.Funnel.VerifiedCount == 0,
            $"Google funnel VerifiedCount must be 0 (got {googleResp.Funnel.VerifiedCount})");

        // 3. Unfiltered
        var allResp = await growthService.GetGrowthMetricsAsync(new MonitoringFilterRequest(
            FromDate: targetDate,
            ToDate: targetDate
        ), default);

        Assert(allResp.Funnel!.StartedCount == 3,
            $"Unfiltered funnel StartedCount must be 3 (got {allResp.Funnel.StartedCount})");
        Assert(allResp.Funnel.CodeResentCount == 1,
            $"Unfiltered funnel CodeResentCount must be 1 (got {allResp.Funnel.CodeResentCount})");
        Assert(allResp.Funnel.VerifiedCount == 1,
            $"Unfiltered funnel VerifiedCount must be 1 (got {allResp.Funnel.VerifiedCount})");
        Assert(allResp.Funnel.BypassedCount == 1,
            $"Unfiltered funnel BypassedCount must be 1 (got {allResp.Funnel.BypassedCount})");
        Assert(allResp.Funnel.UnknownAttemptCount == 1,
            $"Unfiltered funnel UnknownAttemptCount must be 1 (got {allResp.Funnel.UnknownAttemptCount})");
    }

    private static async Task TestInvalidAndExtremeDateRangesAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var growthService = new DefaultGrowthMonitoringQueryService(db, clock);
        var revenueService = new DefaultRevenueMonitoringQueryService(db, clock);

        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);

        // 1. Extreme range: MinValue to MaxValue
        var extremeFilter = new MonitoringFilterRequest(
            FromDate: DateOnly.MinValue,
            ToDate: DateOnly.MaxValue
        );
        var extremeGrowth = await growthService.GetGrowthMetricsAsync(extremeFilter, default);
        var extremeRevenue = await revenueService.GetRevenueMetricsAsync(extremeFilter, default);

        Assert(extremeGrowth.DailySeries.Count <= 366,
            $"Extreme range must clamp to max 366 daily points (got {extremeGrowth.DailySeries.Count})");
        Assert(extremeRevenue.Summary.PaidSubscriptionCount >= 0,
            "Extreme revenue query completed without hanging or error");

        // 2. Future dates: clamped to today
        var futureFilter = new MonitoringFilterRequest(
            FromDate: today.AddDays(10),
            ToDate: today.AddDays(50)
        );
        var futureGrowth = await growthService.GetGrowthMetricsAsync(futureFilter, default);
        Assert(futureGrowth.DailySeries.Count >= 1 && futureGrowth.DailySeries.All(p => p.Date <= today),
            "Future range clamped to today");

        // 3. Inverted range: fromDate > toDate
        var invertedFilter = new MonitoringFilterRequest(
            FromDate: today,
            ToDate: today.AddDays(-10)
        );
        var invertedGrowth = await growthService.GetGrowthMetricsAsync(invertedFilter, default);
        Assert(invertedGrowth.DailySeries.Count == 11,
            $"Inverted range safely swapped and returned 11 points (got {invertedGrowth.DailySeries.Count})");
    }

    private static async Task TestRealSqlitePostReadingsSuccessRollbackAndNoStallAsync()
    {
        var dbFilePath = Path.Combine(Path.GetTempPath(), $"roomie_readings_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbFilePath};Mode=ReadWriteCreate;Cache=Shared";

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
            builder.Services.AddMonitoringCore(builder.Configuration);
            builder.Services.AddScoped<InvoiceService>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, MonitoringFixtureAuth>("fixture", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(options => options.AddPolicy("analytics",
                _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));

            await using var app = builder.Build();
            app.UseMiddleware<ApiExceptionMiddleware>();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            var routes = app.MapGroup("/api").RequireAuthorization(policy => policy.RequireRole("Owner"));
            BillingEndpoints.MapReadingsRoutes(routes);
            app.MapMonitoringEndpoints();
            await app.StartAsync();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();

            var ownerId = Guid.NewGuid();
            var user = new AppUser
            {
                Id = ownerId,
                Email = "readings.owner@test.example",
                Name = "Readings Owner",
                PasswordHash = "fixture",
                Role = UserRole.Owner,
                Status = OwnerStatus.Approved,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10)
            };
            db.Users.Add(user);

            var building = new Building
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Name = "Tòa Nhà Thử Nghiệm",
                Address = "123 Đường Test"
            };
            var room = new Room
            {
                Id = Guid.NewGuid(),
                BuildingId = building.Id,
                Name = "P101",
                Rent = 2_500_000m,
                ElectricityRate = 3_500m,
                WaterFee = 50_000m,
                WaterPerPerson = false,
                ServiceFee = 30_000m
            };
            var occupancy = new Occupancy
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                TenantName = "Người Thuê A",
                Phone = "0912345678",
                People = 1,
                StartDate = new DateOnly(2026, 8, 1),
                IsActive = true
            };
            var milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = DateTimeOffset.UtcNow.AddDays(-10),
                ActivatedAt = null,
                FirstActivatedInvoiceId = null
            };

            db.Buildings.Add(building);
            db.Rooms.Add(room);
            db.Occupancies.Add(occupancy);
            db.OwnerMilestones.Add(milestone);
            await db.SaveChangesAsync();

            var client = app.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            client.DefaultRequestHeaders.Add("X-Fixture-Role", "Owner");
            client.DefaultRequestHeaders.Add("X-Fixture-UserId", ownerId.ToString());

            // 1. Rollback case: POST /readings with invalid current reading (-10) must rollback, not stall, and not activate owner
            var badReadingReq = new HttpRequestMessage(HttpMethod.Post, "/api/readings")
            {
                Content = JsonContent.Create(new
                {
                    RoomId = room.Id,
                    Period = "2026-09",
                    CurrentReading = -10m
                })
            };
            var badResp = await client.SendAsync(badReadingReq);
            Assert(badResp.StatusCode == HttpStatusCode.BadRequest,
                $"Invalid reading rejected with 400 Bad Request (got {badResp.StatusCode})");

            var unactivatedMilestone = await db.OwnerMilestones.AsNoTracking().SingleAsync(x => x.OwnerId == ownerId);
            Assert(unactivatedMilestone.ActivatedAt == null,
                "Rollback path does not prematurely activate owner milestone (ActivatedAt remains null)");
            Assert(await db.Readings.CountAsync(r => r.OccupancyId == occupancy.Id) == 0,
                "Rollback path does not insert reading");
            Assert(await db.Invoices.CountAsync(i => i.OccupancyId == occupancy.Id) == 0,
                "Rollback path does not insert invoice");

            // 2. Success case: POST /readings with valid reading must succeed without stalling SQLite, commit outer transaction, and activate owner
            var goodReadingReq = new HttpRequestMessage(HttpMethod.Post, "/api/readings")
            {
                Content = JsonContent.Create(new
                {
                    RoomId = room.Id,
                    Period = "2026-09",
                    CurrentReading = 150m
                })
            };
            var goodResp = await client.SendAsync(goodReadingReq);
            Assert(goodResp.StatusCode == HttpStatusCode.Created,
                $"Valid reading succeeds with 201 Created without SQLite stalls (got {goodResp.StatusCode})");

            var createdReading = await db.Readings.AsNoTracking().SingleAsync(r => r.OccupancyId == occupancy.Id);
            Assert(createdReading.CurrentReading == 150m && createdReading.Period == "2026-09",
                "Reading successfully committed to disposable SQLite DB");

            var createdInvoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.OccupancyId == occupancy.Id && i.Period == "2026-09");
            Assert(createdInvoice.LifecycleState == InvoiceLifecycleState.Issued,
                "Monthly invoice issued and committed");

            var activatedMilestone = await db.OwnerMilestones.AsNoTracking().SingleAsync(x => x.OwnerId == ownerId);
            Assert(activatedMilestone.ActivatedAt != null,
                "Owner milestone successfully activated after outer transaction commit");
            Assert(activatedMilestone.FirstActivatedInvoiceId == createdInvoice.Id,
                $"FirstActivatedInvoiceId matches committed invoice (got {activatedMilestone.FirstActivatedInvoiceId})");
        }
        finally
        {
            // Clean up temporary database file
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbFilePath)) File.Delete(dbFilePath); } catch { }
        }
    }

    private static async Task TestCampaignTrackingUrlAndEndpointAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var revenueService = new DefaultRevenueMonitoringQueryService(db, clock);

        // 1. BuildTrackingUrl unit check
        var generated = revenueService.BuildTrackingUrl(
            "https://roomie.io.vn/dang-ky",
            "facebook",
            "cpc",
            "black_friday",
            "phong_tro",
            "banner_blue"
        );
        Assert(generated.Contains("utm_source=facebook") &&
               generated.Contains("utm_medium=cpc") &&
               generated.Contains("utm_campaign=black_friday") &&
               generated.Contains("utm_term=phong_tro") &&
               generated.Contains("utm_content=banner_blue"),
            "BuildTrackingUrl correctly formats all UTM parameters on target URL");

        // 2. Relative targetUrl auto-prefixed
        var relativeUrl = revenueService.BuildTrackingUrl("/pricing", "zalo", "chat", "zalo_oa");
        Assert(relativeUrl.StartsWith("/pricing?") && relativeUrl.Contains("utm_source=zalo"),
            "BuildTrackingUrl preserves relative paths with query parameters");
    }

    private static async Task TestMonitoringMaintenanceServiceAsync()
    {
        var (connection, db) = await CreateTestDbAsync();
        await using var connectionScope = connection;
        await using var dbScope = db;

        var clock = TimeProvider.System;
        var maintenance = new DefaultMonitoringMaintenanceService(db, clock);

        var now = DateTimeOffset.UtcNow;
        var today = ApiSupport.VietnamDate(now);

        // 1. Seed expired vs valid telemetry
        // Raw event: 95 days old (should prune), 80 days old (should keep)
        db.AnalyticsEvents.AddRange(
            new ClientAnalyticsEvent
            {
                EventId = "evt-old-1",
                BrowserId = "b-old",
                SessionId = "s-old",
                EventType = "page_view",
                Path = "/home",
                ServerTimestamp = now.AddDays(-95)
            },
            new ClientAnalyticsEvent
            {
                EventId = "evt-keep-1",
                BrowserId = "b-keep",
                SessionId = "s-keep",
                EventType = "page_view",
                Path = "/home",
                ServerTimestamp = now.AddDays(-80)
            }
        );

        // Owner activity: 14 months old (should prune), 6 months old (should keep)
        var ownerId = Guid.NewGuid();
        db.OwnerDailyActivities.AddRange(
            new OwnerDailyActivity
            {
                OwnerId = ownerId,
                ActivityDate = today.AddMonths(-14),
                OwnerActionCount = 1,
                HasOwnerAction = true,
                FirstActionAt = now.AddMonths(-14),
                LastActionAt = now.AddMonths(-14)
            },
            new OwnerDailyActivity
            {
                OwnerId = ownerId,
                ActivityDate = today.AddMonths(-6),
                OwnerActionCount = 1,
                HasOwnerAction = true,
                FirstActionAt = now.AddMonths(-6),
                LastActionAt = now.AddMonths(-6)
            }
        );

        // Milestone: 14 months old (MUST NEVER BE PRUNED)
        var oldUser = new AppUser
        {
            Id = ownerId,
            Email = "old.milestone@test.example",
            Name = "Old Owner",
            PasswordHash = "h",
            CreatedAt = now.AddMonths(-14)
        };
        db.Users.Add(oldUser);
        db.OwnerMilestones.Add(new OwnerMilestone
        {
            OwnerId = ownerId,
            RegisteredAt = now.AddMonths(-14),
            ActivatedAt = now.AddMonths(-13),
            CreatedAt = now.AddMonths(-14),
            UpdatedAt = now.AddMonths(-13)
        });

        await db.SaveChangesAsync();

        var prunedCount = await maintenance.PruneExpiredTelemetryAsync(default);
        Assert(prunedCount == 2, $"Pruned exactly 2 expired records: rawweb event (95d) and activity (14mo), pruned={prunedCount}");

        var remainingEvents = await db.AnalyticsEvents.CountAsync();
        Assert(remainingEvents == 1, "Preserved 80-day raw event within 90-day retention window");

        var remainingActivities = await db.OwnerDailyActivities.CountAsync();
        Assert(remainingActivities == 1, "Preserved 6-month activity within 13-month retention window");

        var milestoneStillExists = await db.OwnerMilestones.AnyAsync(x => x.OwnerId == ownerId);
        Assert(milestoneStillExists, "OwnerMilestones preserved indefinitely and never pruned");

        // 2. Rollup daily aggregate
        var rollupDate = today.AddDays(-1);
        var rollup = await maintenance.RollupDailyAggregateAsync(rollupDate, default);
        Assert(rollup.Date == rollupDate, "Daily aggregate rollup successfully created metric record");

        var aggregateInDb = await db.DailyAggregateMetrics.SingleOrDefaultAsync(x => x.Date == rollupDate);
        Assert(aggregateInDb != null, "Daily aggregate metric persisted in database");
    }

    private static async Task TestHttpBusinessFlowsAndDegradedTelemetryAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddMonitoringCore(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, MonitoringFixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("analytics",
            _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMonitoringEndpoints();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // 1. Authenticated Owner submitting analytics attaches authenticated OwnerId
        var ownerId = Guid.NewGuid();
        var clientOwner = app.GetTestClient();
        clientOwner.BaseAddress = new Uri("http://localhost");
        clientOwner.DefaultRequestHeaders.Add("X-Fixture-Role", "Owner");
        clientOwner.DefaultRequestHeaders.Add("X-Fixture-UserId", ownerId.ToString());

        var eventReq = new AnalyticsEventBatchRequest(
            BrowserId: "browser-owner-auth",
            SessionId: "session-owner-auth",
            Events: new[]
            {
                new AnalyticsEventItemDto("evt-auth-owner-1", "page_view", "Dashboard", "/dashboard", UtmSource: "email")
            }
        );
        var postEvt = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events")
        {
            Content = JsonContent.Create(eventReq),
            Headers = { { "Origin", "http://localhost" } }
        };
        var evtResp = await clientOwner.SendAsync(postEvt);
        Assert(evtResp.StatusCode == HttpStatusCode.OK, "Authenticated owner can post analytics events");

        var recordedEvt = await db.AnalyticsEvents.SingleAsync(e => e.EventId == "evt-auth-owner-1");
        Assert(recordedEvt.OwnerId == ownerId, "Authenticated owner ID is authoritatively bound to event");

        // 2. Degraded telemetry: when database is busy or telemetry throws, milestone tracker does not crash caller
        var brokenTracker = new OwnerMilestoneTracker(
            new BrokenScopeFactory(), TimeProvider.System);
        // TrackInvoiceIssuedAsync or TrackOwnerRegisteredAsync safely catch exceptions without bubbling
        await brokenTracker.TrackInvoiceIssuedAsync(Guid.NewGuid(), Guid.NewGuid(), "Monthly", ActorType.Owner, default);
        Assert(true, "Degraded telemetry safely suppresses exception on milestone tracking failure");
    }

    private sealed class BrokenScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Simulated DB connection pool failure");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class RoomTenantLifecycleHttpTests
{
    private static readonly Guid OwnerAId = Guid.NewGuid();
    private static readonly Guid OwnerBId = Guid.NewGuid();

    public static async Task RunAsync()
    {
        await BuildingArchiveCascadeAndRestoreAsync();
        await LandlordMultiTenantIsolationAsync();
        await FreeTierRoomQuotaAsync();
        await OccupancyHeadcountInvoiceRecalculationAsync();
        await DepositLedgerDeductionAndRefundAsync();
        await MoveoutSettlementAndMeterBaselineAdvancementAsync();
        await OccupancyInitialMeterReadingAdvancementAsync();
        await ContractAndCt01DocumentsExportAsync();
        await TenantSelfCheckinAndMeterSubmissionWorkflowAsync();
    }

    private static async Task BuildingArchiveCascadeAndRestoreAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        // 1. Create a building
        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Toa Nha Sen Vang", "123 Le Loi"));
        Assert(bldgResp.StatusCode == HttpStatusCode.Created, "building created with 201 Created");
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();
        var bldgId = bldg!.Id;

        // 2. Create 2 rooms in this building
        var r1Resp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldgId, "Phong 101", 3_000_000, 3_500, 100_000, true, 50_000, 10));
        var r2Resp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldgId, "Phong 102", 3_500_000, 3_500, 100_000, true, 50_000, 20));
        Assert(r1Resp.StatusCode == HttpStatusCode.Created && r2Resp.StatusCode == HttpStatusCode.Created,
            "two rooms created in building");
        var r1Id = (await r1Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var r2Id = (await r2Resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 3. Delete (soft-archive) building -> cascades to both rooms
        var delResp = await client.DeleteAsync($"/api/buildings/{bldgId}");
        Assert(delResp.StatusCode == HttpStatusCode.OK, "soft-archiving building succeeds with 200 OK");
        var delJson = await delResp.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = delJson.GetProperty("archiveBatchId").GetGuid();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var buildingRow = await db.Buildings.FindAsync(bldgId);
            var roomRows = await db.Rooms.Where(r => r.BuildingId == bldgId).ToListAsync();
            Assert(buildingRow!.ArchivedAt != null && buildingRow.ArchiveBatchId == batchId,
                "building has ArchivedAt timestamp and archiveBatchId");
            Assert(roomRows.Count == 2 && roomRows.All(r => r.ArchivedAt != null && r.ArchiveBatchId == batchId),
                "soft-archive cascaded to all non-archived rooms with matching archiveBatchId");
        }

        // 4. Restore building -> restores building and all rooms belonging to ArchiveBatchId
        var restoreResp = await client.PostAsync($"/api/buildings/{bldgId}/restore", null);
        Assert(restoreResp.StatusCode == HttpStatusCode.OK, "restoring building succeeds with 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var buildingRow = await db.Buildings.FindAsync(bldgId);
            var roomRows = await db.Rooms.Where(r => r.BuildingId == bldgId).ToListAsync();
            Assert(buildingRow!.ArchivedAt == null && buildingRow.ArchiveBatchId == null,
                "restored building has null ArchivedAt and null ArchiveBatchId");
            Assert(roomRows.All(r => r.ArchivedAt == null && r.ArchiveBatchId == null),
                "restored building restored all associated rooms");
        }

        // 5. Block archive if a room has an active tenant
        var occResp = await client.PostAsJsonAsync($"/api/rooms/{r1Id}/occupancy", new OccupancyRequest(
            "Le Van C", "0901112233", 2, VietnamCalendar.TodayVietnam().AddDays(-5), 0));
        Assert(occResp.StatusCode == HttpStatusCode.OK, "active occupancy added to room 101");

        var blockedArchive = await client.DeleteAsync($"/api/buildings/{bldgId}");
        Assert(blockedArchive.StatusCode == HttpStatusCode.Conflict,
            "archiving building with active tenant is blocked with 409 Conflict");
    }

    private static async Task LandlordMultiTenantIsolationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // Owner A creates Building A and Room A
        client.DefaultRequestHeaders.Remove("X-Fixture-Owner");
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgAResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Owner A Tower", "District 1"));
        var bldgA = await bldgAResp.Content.ReadFromJsonAsync<BuildingDto>();
        var roomAResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldgA!.Id, "Room A-101", 2_000_000, 3_000, 50_000, false, 0, 0));
        var roomA = await roomAResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomAId = roomA.GetProperty("id").GetGuid();

        // Switch to Owner B
        client.DefaultRequestHeaders.Remove("X-Fixture-Owner");
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerBId.ToString());

        // Owner B creates Building B and Room B
        var bldgBResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Owner B House", "District 7"));
        var bldgB = await bldgBResp.Content.ReadFromJsonAsync<BuildingDto>();
        var roomBResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldgB!.Id, "Room B-101", 2_500_000, 3_000, 50_000, false, 0, 0));

        // 1. Owner B cannot see Owner A's buildings
        var bldgListB = await client.GetFromJsonAsync<List<JsonElement>>("/api/buildings");
        Assert(bldgListB != null && bldgListB.All(b => b.GetProperty("id").GetGuid() != bldgA.Id),
            "Owner B building list does not include Owner A building");

        // 2. Owner B cannot see Owner A's rooms
        var roomListB = await client.GetFromJsonAsync<List<JsonElement>>("/api/rooms");
        Assert(roomListB != null && roomListB.All(r => r.GetProperty("id").GetGuid() != roomAId),
            "Owner B room list does not include Owner A room");

        // 3. Owner B cannot update Owner A's room
        var patchResp = await client.PatchAsJsonAsync($"/api/rooms/{roomAId}", new RoomUpdateRequest(
            "Hacked Name", 1_000_000, 1_000, 10_000, false, 0));
        Assert(patchResp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            "Owner B cannot update Owner A's room (404/403)");

        // 4. Owner B cannot delete Owner A's building
        var delResp = await client.DeleteAsync($"/api/buildings/{bldgA.Id}");
        Assert(delResp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            "Owner B cannot delete Owner A's building (404/403)");

        // 5. Owner B cannot create a room in Owner A's building
        var createInAResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldgA.Id, "Sneaky Room", 1_000_000, 3_000, 50_000, false, 0, 0));
        Assert(createInAResp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            "Owner B cannot create a room in Owner A's building (404/403)");
    }

    private static async Task FreeTierRoomQuotaAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Quota Complex", "Hanoi"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        // Create 5 rooms (the free tier maximum)
        for (var i = 1; i <= 5; i++)
        {
            var rResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
                bldg!.Id, $"Free Room {i}", 2_000_000, 3_000, 50_000, false, 0, 0));
            Assert(rResp.StatusCode == HttpStatusCode.Created, $"room {i} of 5 created successfully under Free tier");
        }

        // Attempting to create the 6th room on Free tier returns 403 Forbidden
        var sixthResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Free Room 6", 2_000_000, 3_000, 50_000, false, 0, 0));
        Assert(sixthResp.StatusCode == HttpStatusCode.Forbidden,
            "6th room creation on Free tier returns 403 Forbidden");

        // Upgrade Owner A to Pro
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.FindAsync(OwnerAId);
            owner!.SubscriptionTier = SubscriptionTier.Pro;
            owner.SubscriptionExpiresAt = DateTimeOffset.UtcNow.AddMonths(1);
            await db.SaveChangesAsync();
        }

        // Now creating 6th and 7th room succeeds
        var pro6th = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Pro Room 6", 2_000_000, 3_000, 50_000, false, 0, 0));
        var pro7th = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg.Id, "Pro Room 7", 2_000_000, 3_000, 50_000, false, 0, 0));
        Assert(pro6th.StatusCode == HttpStatusCode.Created && pro7th.StatusCode == HttpStatusCode.Created,
            "Pro owner can create more than 5 rooms");
    }

    private static async Task OccupancyHeadcountInvoiceRecalculationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Headcount Tower", "Hue"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        // Create room with water per person enabled: 60,000 VND / person
        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Headcount Room 1", 3_000_000, 3_500, 60_000, true, 20_000, 0));
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();

        // Create occupancy with initial headcount = 1
        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Tran Thi B", "0912345678", 1, VietnamCalendar.TodayVietnam().AddDays(-10), 0));
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occupancyId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        // Insert a Draft monthly invoice for this occupancy
        Guid draftInvoiceId = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var draftInvoice = new Invoice
            {
                Id = draftInvoiceId,
                OwnerId = OwnerAId,
                RoomId = roomId,
                BuildingId = bldg.Id,
                OccupancyId = occupancyId,
                RoomName = "Headcount Room 1",
                BuildingName = "Headcount Tower",
                TenantName = "Tran Thi B",
                Period = VietnamCalendar.CurrentVietnamPeriod(),
                Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Draft,
                Rent = 3_000_000,
                ElectricityRate = 3_500,
                ElectricityAmount = 0,
                WaterRate = 60_000,
                WaterPerPerson = true,
                WaterAmount = 60_000, // 60,000 * 1
                ServiceRate = 20_000,
                ServiceAmount = 20_000,
                Total = 3_080_000, // 3M + 0 + 60k + 20k
                Paid = 0,
                Version = 1,
                DueDate = VietnamCalendar.TodayVietnam().AddDays(5)
            };
            db.Invoices.Add(draftInvoice);
            await db.SaveChangesAsync();
        }

        // Update occupancy headcount from 1 to 4
        var updateResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyUpdateRequest(
            occupancyId, "Tran Thi B", "0912345678", 4));
        Assert(updateResp.StatusCode == HttpStatusCode.OK, "occupancy headcount updated to 4 with 200 OK");

        // Verify draft invoice was automatically recalculated and revision was recorded
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var invoice = await db.Invoices.FindAsync(draftInvoiceId);
            Assert(invoice!.WaterAmount == 240_000, "water amount recalculated to 4 * 60,000 = 240,000");
            Assert(invoice.Total == 3_260_000, "total recalculated to 3,000,000 + 240,000 + 20,000 = 3,260,000");
            Assert(invoice.Version == 2, "draft invoice version incremented after headcount update");

            var revisions = await db.InvoiceRevisions.Where(r => r.InvoiceId == draftInvoiceId).ToListAsync();
            Assert(revisions.Count == 1 && revisions[0].Reason.Contains("số người ở"),
                "audit revision recorded with reason describing headcount update");
        }
    }

    private static async Task DepositLedgerDeductionAndRefundAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Deposit Villa", "Da Nang"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();
        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Deposit Room 1", 4_000_000, 3_500, 100_000, false, 0, 0));
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();

        // Create occupancy with deposit of 500,000
        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Deposit Tenant", "0988776655", 1, VietnamCalendar.TodayVietnam().AddDays(-30), 500_000));
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occupancyId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        Guid depositInvoiceId;
        Guid debtInvoiceId = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var depositInvoice = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Kind == "Deposit");
            depositInvoiceId = depositInvoice.Id;
            depositInvoice.Paid = 500_000;
            depositInvoice.Status = InvoiceStatus.Paid;
            depositInvoice.LifecycleState = InvoiceLifecycleState.Issued;

            // Add an issued debt invoice for 300,000
            db.Invoices.Add(new Invoice
            {
                Id = debtInvoiceId,
                OwnerId = OwnerAId,
                RoomId = roomId,
                BuildingId = bldg.Id,
                OccupancyId = occupancyId,
                RoomName = "Deposit Room 1",
                BuildingName = "Deposit Villa",
                TenantName = "Deposit Tenant",
                Period = "2026-09",
                Kind = "Monthly",
                LifecycleState = InvoiceLifecycleState.Issued,
                Total = 300_000,
                Paid = 0,
                Status = InvoiceStatus.Unpaid,
                Version = 1,
                DueDate = VietnamCalendar.TodayVietnam().AddDays(5)
            });
            await db.SaveChangesAsync();
        }

        // 1. Deduction: deduct 150,000 from deposit into debt invoice
        var idempotencyKeyDeduct = Guid.NewGuid().ToString();
        var deductResp = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(150_000, "Deduction", "Khấu trừ nợ tháng 9", idempotencyKeyDeduct, debtInvoiceId, 1, 1));
        Assert(deductResp.StatusCode == HttpStatusCode.OK, "deposit deduction into debt invoice succeeds with 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var debt = await db.Invoices.FindAsync(debtInvoiceId);
            Assert(debt!.Paid == 150_000 && debt.Status == InvoiceStatus.Partial && debt.Version == 2,
                "target debt invoice paid updated to 150,000, status Partial, version incremented");
            var receipt = await db.Receipts.SingleAsync(r => r.InvoiceId == debtInvoiceId);
            Assert(receipt.Amount == 150_000 && receipt.Source == "Deposit",
                "payment receipt created with Source = 'Deposit'");
        }

        // 2. Idempotency test: resend deduction with same key and payload returns existing settlement
        var replayDeduct = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(150_000, "Deduction", "Khấu trừ nợ tháng 9", idempotencyKeyDeduct, debtInvoiceId));
        Assert(replayDeduct.StatusCode == HttpStatusCode.OK,
            "deduction retry with identical idempotency key is idempotent and returns 200 OK");

        // Resend same key with different amount returns 409 Conflict
        var conflictDeduct = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(100_000, "Deduction", "Khấu trừ khác", idempotencyKeyDeduct, debtInvoiceId));
        Assert(conflictDeduct.StatusCode == HttpStatusCode.Conflict,
            "deduction retry with same idempotency key but altered payload returns 409 Conflict");

        // 3. Refund: remaining held is 500,000 - 150,000 = 350,000. Refund 100,000 to tenant
        var idempotencyKeyRefund = Guid.NewGuid().ToString();
        var refundResp = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(100_000, "Refund", "Hoàn một phần cọc", idempotencyKeyRefund, null));
        Assert(refundResp.StatusCode == HttpStatusCode.OK, "deposit partial refund succeeds with 200 OK");

        // 4. Over-settlement guard: remaining held is 250,000. Attempting to settle 300,000 returns 400 BadRequest
        var oversettleResp = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(300_000, "Refund", "Vuot qua so con lai", Guid.NewGuid().ToString(), null));
        Assert(oversettleResp.StatusCode == HttpStatusCode.BadRequest,
            "settling more than remaining held deposit returns 400 BadRequest");
    }

    private static async Task MoveoutSettlementAndMeterBaselineAdvancementAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Moveout Plaza", "Can Tho"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();
        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Moveout Room 1", 3_000_000, 3_500, 50_000, false, 0, 100)); // Initial reading = 100
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();

        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Moveout Tenant", "0933445566", 1, VietnamCalendar.TodayVietnam().AddDays(-60), 300_000));
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occupancyId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        Guid depositInvoiceId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var depositInvoice = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Kind == "Deposit");
            depositInvoiceId = depositInvoice.Id;
            depositInvoice.Paid = 300_000;
            depositInvoice.Status = InvoiceStatus.Paid;
            depositInvoice.LifecycleState = InvoiceLifecycleState.Issued;

            // Add readings advancing physical meter: 100 -> 180 -> 240
            db.Readings.AddRange(
                new MonthlyReading
                {
                    RoomId = roomId, OccupancyId = occupancyId, Period = "2026-08",
                    PreviousReading = 100, CurrentReading = 180, Consumption = 80,
                    BillingStartDate = new DateOnly(2026, 8, 1), BillingEndDate = new DateOnly(2026, 8, 31),
                    RecordedAt = DateTimeOffset.UtcNow.AddDays(-30)
                },
                new MonthlyReading
                {
                    RoomId = roomId, OccupancyId = occupancyId, Period = "2026-09",
                    PreviousReading = 180, CurrentReading = 240, Consumption = 60,
                    BillingStartDate = new DateOnly(2026, 9, 1), BillingEndDate = new DateOnly(2026, 9, 30),
                    RecordedAt = DateTimeOffset.UtcNow.AddDays(-1)
                }
            );

            // Add an unpaid monthly invoice
            db.Invoices.Add(new Invoice
            {
                OwnerId = OwnerAId, RoomId = roomId, BuildingId = bldg.Id, OccupancyId = occupancyId,
                RoomName = "Moveout Room 1", BuildingName = "Moveout Plaza", TenantName = "Moveout Tenant",
                Period = "2026-09", Kind = "Monthly", LifecycleState = InvoiceLifecycleState.Issued,
                Total = 100_000, Paid = 0, Status = InvoiceStatus.Unpaid, Version = 1,
                DueDate = VietnamCalendar.TodayVietnam().AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        // 1. Moveout preflight check: blocked while unpaid invoice exists
        var moveoutCheckBlocked = await client.GetFromJsonAsync<JsonElement>($"/api/rooms/{roomId}/moveout");
        Assert(!moveoutCheckBlocked.GetProperty("canComplete").GetBoolean() &&
               moveoutCheckBlocked.GetProperty("blockedReason").GetString() != null,
            "moveout preflight check is blocked when unpaid invoices exist");

        // Settle the unpaid invoice
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var monthly = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Kind == "Monthly");
            monthly.Paid = monthly.Total;
            monthly.Status = InvoiceStatus.Paid;
            await db.SaveChangesAsync();
        }

        // Moveout preflight check now allowed
        var moveoutCheckAllowed = await client.GetFromJsonAsync<JsonElement>($"/api/rooms/{roomId}/moveout");
        Assert(moveoutCheckAllowed.GetProperty("canComplete").GetBoolean() &&
               moveoutCheckAllowed.GetProperty("heldDeposit").GetDecimal() == 300_000,
            "moveout preflight check is allowed with 300,000 held deposit once debt settled");

        // 2. Complete moveout settlement: held = 300,000; refund 200,000; retained 100,000
        var requestId = Guid.NewGuid();
        long depositVersion;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            depositVersion = (await db.Invoices.FindAsync(depositInvoiceId))!.Version;
        }

        var completeResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/complete-moveout",
            new CompleteMoveoutRequest(occupancyId, requestId, 200_000, 300_000, depositVersion));
        Assert(completeResp.StatusCode == HttpStatusCode.OK, "complete moveout succeeds with 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var occ = await db.Occupancies.FindAsync(occupancyId);
            Assert(!occ!.IsActive && occ.EndDate != null, "occupancy marked inactive with EndDate set");
            Assert(occ.TenantTokenHash == null && occ.ProtectedTenantToken == null,
                "tenant tokens cleared upon move-out");

            var settlements = await db.DepositSettlements.Where(s => s.InvoiceId == depositInvoiceId).ToListAsync();
            Assert(settlements.Count == 2, "two settlements created (refund + retained)");
            Assert(settlements.Any(s => s.Kind == "Refund" && s.Amount == 200_000), "refund settlement recorded for 200,000");
            Assert(settlements.Any(s => s.Kind == "Deduction" && s.Amount == 100_000), "retained settlement recorded for 100,000");

            // 3. Verify meter baseline advancement to prevent anti-regression!
            var room = await db.Rooms.FindAsync(roomId);
            Assert(room!.InitialReading == 240,
                "room InitialReading advanced to highest historical reading (240) preventing anti-regression");
        }

        // 4. Idempotency of complete moveout
        var repeatResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/complete-moveout",
            new CompleteMoveoutRequest(occupancyId, requestId, 200_000, 300_000, depositVersion));
        Assert(repeatResp.StatusCode == HttpStatusCode.OK,
            "complete moveout with same requestId is idempotent and returns 200 OK");

        // 5. Further deposit settlements are blocked after moveout completion
        var postMoveoutSettle = await client.PostAsJsonAsync($"/api/invoices/{depositInvoiceId}/deposit-settlements",
            new DepositSettlementRequest(10_000, "Refund", "Late refund", Guid.NewGuid().ToString(), null));
        Assert(postMoveoutSettle.StatusCode == HttpStatusCode.Conflict,
            "deposit settlement after completed move-out is rejected with 409 Conflict");
    }

    private static async Task OccupancyInitialMeterReadingAdvancementAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Meter Building", "Danang"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        // Regression: Room created with initial reading 120 rejects check-in with 100 even before any monthly reading
        var preconfiguredRoomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Room Preconfigured-120", 2_000_000, 3_500, 50_000, false, 0, 120));
        var preconfiguredRoomJson = await preconfiguredRoomResp.Content.ReadFromJsonAsync<JsonElement>();
        var preconfiguredRoomId = preconfiguredRoomJson.GetProperty("id").GetGuid();

        var rollbackBeforeMonthlyResp = await client.PostAsJsonAsync($"/api/rooms/{preconfiguredRoomId}/occupancy", new OccupancyRequest(
            "Rollback Tenant", "0901234567", 1, VietnamCalendar.TodayVietnam(), 0, 100));
        Assert(rollbackBeforeMonthlyResp.StatusCode == HttpStatusCode.BadRequest,
            "check-in with 100 on room having InitialReading 120 without monthly readings is rejected with 400");

        var invalidScaleResp = await client.PostAsJsonAsync($"/api/rooms/{preconfiguredRoomId}/occupancy", new OccupancyRequest(
            "Scale Tenant", "0901234567", 1, VietnamCalendar.TodayVietnam(), 0, 125.1234m));
        Assert(invalidScaleResp.StatusCode == HttpStatusCode.BadRequest,
            "check-in with meter reading having > 3 decimal places is rejected with 400");

        // Room created without tenant: initial reading defaults to 0
        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Room Meter-1", 2_000_000, 3_500, 50_000, false, 0));
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();

        // 1. First tenant check-in sets initial electric reading to 120
        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Tenant One", "0901112233", 1, VietnamCalendar.TodayVietnam().AddDays(-30), 0, 120));
        Assert(occResp.StatusCode == HttpStatusCode.OK, "first tenant check-in accepted with initial reading 120");
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(occJson.GetProperty("initialReading").GetDecimal() == 120m, "room initialReading updated to 120");
        Assert(occJson.GetProperty("previousReading").GetDecimal() == 120m, "room previousReading starts at 120");

        var occId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        // Add monthly reading 180 (consumption 60) and move out
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Readings.Add(new MonthlyReading
            {
                RoomId = roomId, OccupancyId = occId, Period = "2026-09",
                PreviousReading = 120, CurrentReading = 180, Consumption = 60,
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();
        }

        var moveoutResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/end-occupancy",
            new CompleteMoveoutRequest(occId, Guid.NewGuid(), 0, 0, null));
        Assert(moveoutResp.StatusCode == HttpStatusCode.OK, "moveout completed, baseline is now 180");

        // 2. Second tenant check-in: rollback below 180 rejected
        var invalidOccResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Tenant Two", "0904445566", 1, VietnamCalendar.TodayVietnam(), 0, 150));
        Assert(invalidOccResp.StatusCode == HttpStatusCode.BadRequest, "check-in with meter rollback below baseline is rejected with 400");

        // 3. Second tenant check-in: advance to 185 accepted
        var validOccResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Tenant Two", "0904445566", 1, VietnamCalendar.TodayVietnam(), 0, 185));
        Assert(validOccResp.StatusCode == HttpStatusCode.OK, "check-in with valid meter advance is accepted");
        var validOccJson = await validOccResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(validOccJson.GetProperty("initialReading").GetDecimal() == 185m, "room initialReading updated to 185");
        Assert(validOccJson.GetProperty("previousReading").GetDecimal() == 185m, "room previousReading starts at 185");
    }

    private static async Task ContractAndCt01DocumentsExportAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Nha Tro Binh Minh", "123 Nguyen Trai, Quan 1"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Phong 201", 3_500_000, 3_800, 100_000, true, 50_000, 45));
        var roomId = (await roomResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Nguyen Van Khach", "0909123456", 2, new DateOnly(2026, 10, 1), 3_500_000, 45));
        Assert(occResp.StatusCode == HttpStatusCode.OK, "occupancy created for contract document test");
        var occId = (await occResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("occupancy").GetProperty("id").GetGuid();

        // 1. GET pre-filled document data
        var getDocResp = await client.GetAsync($"/api/rooms/{roomId}/documents");
        Assert(getDocResp.StatusCode == HttpStatusCode.OK, "GET /api/rooms/{id}/documents succeeds");
        var docDto = await getDocResp.Content.ReadFromJsonAsync<OccupancyDocumentDto>();
        Assert(docDto != null && docDto.TenantName == "Nguyen Van Khach", "document DTO pre-fills tenant name");
        Assert(docDto!.Rent == 3_500_000 && docDto.RentInWords == "Ba triệu năm trăm nghìn đồng chẵn",
            "document DTO pre-fills rent and Vietnamese words");
        Assert(docDto.DepositRequired == 3_500_000 && docDto.InitialReading == 45m,
            "document DTO pre-fills deposit amount and initial meter reading");

        // 2. PUT legal & contract details (persisted across owner, building, and occupancy)
        var updateReq = new UpdateOccupancyDocumentRequest(
            OccupancyId: occId,
            OwnerPhone: "0911222333",
            OwnerCitizenId: "079088001122",
            OwnerDateOfBirth: new DateOnly(1988, 5, 10),
            OwnerCitizenIdIssuedDate: new DateOnly(2022, 1, 15),
            OwnerCitizenIdIssuedPlace: "Cuc Canh sat QLHC ve TTXH",
            OwnerPermanentAddress: "123 Nguyen Trai, Quan 1, TP.HCM",
            BuildingAddress: "123 Nguyen Trai, Phuong Ben Thanh, Quan 1, TP.HCM",
            PoliceStationName: "Cong an Phuong Ben Thanh, Quan 1, TP.HCM",
            TenantName: "Nguyen Van Khach",
            TenantPhone: "0909123456",
            TenantEmail: "khach@example.com",
            TenantCitizenId: "079099005566",
            TenantDateOfBirth: new DateOnly(1999, 8, 20),
            TenantGender: "Nam",
            TenantCitizenIdIssuedDate: new DateOnly(2023, 6, 1),
            TenantCitizenIdIssuedPlace: "Cuc Canh sat QLHC ve TTXH",
            TenantPermanentAddress: "45 Le Duan, Da Nang",
            ContractEndDate: new DateOnly(2027, 10, 1),
            PaymentDueDay: 5,
            RoomAreaSqm: 24.5m,
            HandoverAssets: "01 dieu hoa, 01 tu lanh, 01 giuong",
            AdditionalTerms: "Khong nuoi thu cung",
            HouseholdHeadRelation: "Nguoi thue tro",
            CoResidents: [new CoResidentDto("Tran Thi Vo", new DateOnly(2000, 2, 2), "Nu", "079200007788", "Vo")]);

        var putResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents", updateReq);
        Assert(putResp.StatusCode == HttpStatusCode.OK, "PUT /api/rooms/{id}/documents saves legal details");
        var updatedDto = await putResp.Content.ReadFromJsonAsync<OccupancyDocumentDto>();
        Assert(updatedDto!.OwnerCitizenId == "079088001122" && updatedDto.TenantCitizenId == "079099005566",
            "saved Citizen IDs returned in updated DTO");
        Assert(updatedDto.PoliceStationName == "Cong an Phuong Ben Thanh, Quan 1, TP.HCM" && updatedDto.CoResidents.Count == 1,
            "saved PoliceStationName and CoResidents returned in updated DTO");

        // 3. Download Contract .docx and CT01 .docx
        var contractDocxResp = await client.GetAsync($"/api/rooms/{roomId}/documents/contract.docx");
        Assert(contractDocxResp.StatusCode == HttpStatusCode.OK, "GET contract.docx returns 200 OK");
        var contractBytes = await contractDocxResp.Content.ReadAsByteArrayAsync();
        Assert(contractBytes.Length > 1000 && contractBytes[0] == 'P' && contractBytes[1] == 'K',
            "contract.docx is a valid non-empty OpenXML zip archive");

        var ct01DocxResp = await client.GetAsync($"/api/rooms/{roomId}/documents/ct01.docx");
        Assert(ct01DocxResp.StatusCode == HttpStatusCode.OK, "GET ct01.docx returns 200 OK");
        var ct01Bytes = await ct01DocxResp.Content.ReadAsByteArrayAsync();
        Assert(ct01Bytes.Length > 1000 && ct01Bytes[0] == 'P' && ct01Bytes[1] == 'K',
            "ct01.docx is a valid non-empty OpenXML zip archive");

        // 4. Edge cases: vacant room returns 409 Conflict for GET documents / contract.docx / ct01.docx
        var vacantRoomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg.Id, "Phong Trong 202", 3_000_000, 3_500, 80_000, false, 0, 0));
        var vacantRoomId = (await vacantRoomResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var vacantDocGet = await client.GetAsync($"/api/rooms/{vacantRoomId}/documents");
        var vacantContractGet = await client.GetAsync($"/api/rooms/{vacantRoomId}/documents/contract.docx");
        var vacantCt01Get = await client.GetAsync($"/api/rooms/{vacantRoomId}/documents/ct01.docx");
        Assert(vacantDocGet.StatusCode == HttpStatusCode.Conflict &&
               vacantContractGet.StatusCode == HttpStatusCode.Conflict &&
               vacantCt01Get.StatusCode == HttpStatusCode.Conflict,
            "vacant room returns 409 Conflict for document preview and docx export endpoints");

        // 5. Edge case: stale OccupancyId on PUT /api/rooms/{id}/documents returns 409 Conflict
        var stalePutResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents",
            updateReq with { OccupancyId = Guid.NewGuid() });
        Assert(stalePutResp.StatusCode == HttpStatusCode.Conflict, "PUT /documents with stale OccupancyId returns 409 Conflict");

        // 6. Edge cases: invalid CitizenId, ContractEndDate < StartDate, PaymentDueDay out of [1,28], RoomAreaSqm out of [0,10000] -> 400 BadRequest
        var badCitizenResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents",
            updateReq with { TenantCitizenId = "12345" });
        var badEndDateResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents",
            updateReq with { ContractEndDate = new DateOnly(2026, 9, 30) });
        var badDueDayResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents",
            updateReq with { PaymentDueDay = 29 });
        var badAreaResp = await client.PutAsJsonAsync($"/api/rooms/{roomId}/documents",
            updateReq with { RoomAreaSqm = -5m });
        Assert(badCitizenResp.StatusCode == HttpStatusCode.BadRequest &&
               badEndDateResp.StatusCode == HttpStatusCode.BadRequest &&
               badDueDayResp.StatusCode == HttpStatusCode.BadRequest &&
               badAreaResp.StatusCode == HttpStatusCode.BadRequest,
            "PUT /documents rejects invalid CitizenId, ContractEndDate < StartDate, PaymentDueDay > 28, and negative RoomAreaSqm with 400 BadRequest");

        // 7. Cross-owner isolation: OwnerB cannot read or export OwnerA's room documents (404 NotFound)
        using var ownerBGet = new HttpRequestMessage(HttpMethod.Get, $"/api/rooms/{roomId}/documents");
        ownerBGet.Headers.Add("X-Fixture-Owner", OwnerBId.ToString());
        using var ownerBResp = await client.SendAsync(ownerBGet);
        Assert(ownerBResp.StatusCode == HttpStatusCode.NotFound, "cross-owner GET /documents returns 404 NotFound");

        // 8. Legacy DB values: negative RoomAreaSqm clamped to null and out-of-range PaymentDueDay clamped to 5 on GET /documents
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var occ = await db.Occupancies.SingleAsync(o => o.Id == occId);
            occ.RoomAreaSqm = -12.5m;
            occ.PaymentDueDay = 0;
            occ.HouseholdHeadRelation = "   ";
            await db.SaveChangesAsync();
        }
        var clampedDto = await client.GetFromJsonAsync<OccupancyDocumentDto>($"/api/rooms/{roomId}/documents");
        Assert(clampedDto!.RoomAreaSqm is null && clampedDto.PaymentDueDay == 5 && clampedDto.HouseholdHeadRelation == "Người thuê trọ",
            "GET /documents clamps legacy negative RoomAreaSqm to null, PaymentDueDay 0 to 5, and blank HouseholdHeadRelation to default");
    }

    private static async Task TenantSelfCheckinAndMeterSubmissionWorkflowAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerAId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Nha Tro Thong Minh", "88 Dien Bien Phu, Binh Thanh"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Phong 301", 3_200_000, 3_500, 100_000, true, 50_000, 100));
        var roomId = (await roomResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Khach Tam Thoi", "0901000111", 1, VietnamCalendar.TodayVietnam().AddDays(-10), 3_200_000, 100));
        Assert(occResp.StatusCode == HttpStatusCode.OK, "occupancy created for self-checkin & meter submission test");
        var occRoot = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occElement = occRoot.GetProperty("occupancy");
        var occId = occElement.GetProperty("id").GetGuid();
        var token = occElement.GetProperty("token").GetString()!;
        Assert(!string.IsNullOrWhiteSpace(token), "tenant token issued on occupancy creation");

        // 1. Primary tenant self check-in via POST /api/tenant/{token}/self-checkin
        var primaryCheckinReq = new TenantSelfCheckinRequest(
            Target: "tenant",
            FullName: "Nguyen Thi Thue",
            CitizenId: "079199001234",
            DateOfBirth: new DateOnly(1999, 4, 15),
            Gender: "Nữ",
            CitizenIdIssuedDate: new DateOnly(2022, 8, 10),
            CitizenIdIssuedPlace: "Cục Cảnh sát QLHC về TTXH",
            PermanentAddress: "100 Tran Hung Dao, Quan 1, TP.HCM",
            Phone: "0908889999",
            Email: "thue@example.com");
        var primaryResp = await client.PostAsJsonAsync($"/api/tenant/{token}/self-checkin", primaryCheckinReq);
        Assert(primaryResp.StatusCode == HttpStatusCode.OK, "primary tenant self-checkin returns 200 OK");
        var primaryJson = await primaryResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(primaryJson.GetProperty("tenantName").GetString() == "Nguyen Thi Thue" &&
               primaryJson.GetProperty("tenantCitizenId").GetString() == "079199001234" &&
               primaryJson.GetProperty("people").GetInt32() == 1,
            "primary tenant self-checkin updates tenantName, tenantCitizenId, and preserves people = 1");

        // 2. Co-resident self check-in via POST /api/tenant/{token}/self-checkin (target = coResident)
        var coResidentCheckinReq = new TenantSelfCheckinRequest(
            Target: "coResident",
            FullName: "Tran Van Cung O",
            CitizenId: "079200005678",
            DateOfBirth: new DateOnly(2000, 1, 20),
            Gender: "Nam",
            Relation: "Em ruột");
        var coResResp = await client.PostAsJsonAsync($"/api/tenant/{token}/self-checkin", coResidentCheckinReq);
        Assert(coResResp.StatusCode == HttpStatusCode.OK, "co-resident self-checkin returns 200 OK");
        var coResJson = await coResResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(coResJson.GetProperty("people").GetInt32() == 2 &&
               coResJson.GetProperty("coResidents").GetArrayLength() == 1,
            "co-resident self-checkin increments people to 2 and appends co-resident");

        // Verify GET /api/rooms/{id}/documents reflects self check-in and TenantSelfCheckinAt
        var docDto = await client.GetFromJsonAsync<OccupancyDocumentDto>($"/api/rooms/{roomId}/documents");
        Assert(docDto != null &&
               docDto.TenantName == "Nguyen Thi Thue" &&
               docDto.TenantCitizenId == "079199001234" &&
               docDto.TenantPhone == "0908889999" &&
               docDto.TenantEmail == "thue@example.com" &&
               docDto.People == 2 &&
               docDto.CoResidents.Count == 1 &&
               docDto.CoResidents[0].CitizenId == "079200005678" &&
               docDto.TenantSelfCheckinAt != null,
            "GET /api/rooms/{id}/documents reflects primary tenant, co-resident, People=2, and non-null TenantSelfCheckinAt");

        // 3. DELETE /api/tenant/{token}/co-residents/{citizenId} removes co-resident and updates People
        var delCoResResp = await client.DeleteAsync($"/api/tenant/{token}/co-residents/079200005678");
        Assert(delCoResResp.StatusCode == HttpStatusCode.OK, "DELETE /api/tenant/{token}/co-residents/{citizenId} returns 200 OK");
        var afterDelJson = await delCoResResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(afterDelJson.GetProperty("people").GetInt32() == 1 &&
               afterDelJson.GetProperty("coResidents").GetArrayLength() == 0,
            "removing co-resident updates People back to 1 and empties coResidents");

        // 4. POST /api/tenant/{token}/meter-submission validation & happy path
        byte[] validPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];
        byte[] validJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
        byte[] validWebp = System.Text.Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        byte[] invalidMagic = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 not an image");
        byte[] oversizedPng = new byte[2 * 1024 * 1024 + 128];
        Array.Copy(validPng, oversizedPng, validPng.Length);

        // 3b. Foreign tenant self-checkin with Passport number (M87654321) and identity photo upload
        var foreignCheckinReq = new TenantSelfCheckinRequest(
            Target: "tenant",
            FullName: "KIM MIN JUN",
            CitizenId: "m87654321",
            DateOfBirth: new DateOnly(1995, 8, 15),
            Gender: "Nam",
            PermanentAddress: "Quốc tịch: Hàn Quốc (KOR)",
            Phone: "0908889999",
            Email: "kim@example.com");
        var foreignResp = await client.PostAsJsonAsync($"/api/tenant/{token}/self-checkin", foreignCheckinReq);
        Assert(foreignResp.StatusCode == HttpStatusCode.OK, "foreign tenant self-checkin with Passport M87654321 returns 200 OK");
        var foreignJson = await foreignResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(foreignJson.GetProperty("tenantName").GetString() == "KIM MIN JUN" &&
               foreignJson.GetProperty("tenantCitizenId").GetString() == "M87654321" &&
               foreignJson.GetProperty("tenantPermanentAddress").GetString() == "Quốc tịch: Hàn Quốc (KOR)",
            "foreign tenant self-checkin normalizes Passport CitizenId to uppercase M87654321 and saves nationality in PermanentAddress");

        static MultipartFormDataContent BuildIdentityPhotoForm(byte[] fileBytes, string fileName, string target = "tenant")
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "photo", fileName);
            form.Add(new StringContent(target), "target");
            return form;
        }

        using (var badIdMagicForm = BuildIdentityPhotoForm(invalidMagic, "passport.pdf"))
        {
            var badIdMagicResp = await client.PostAsync($"/api/tenant/{token}/identity-photo", badIdMagicForm);
            Assert(badIdMagicResp.StatusCode == HttpStatusCode.BadRequest, "identity-photo rejects non-image magic bytes with 400 BadRequest");
        }

        using (var oversizedIdForm = BuildIdentityPhotoForm(oversizedPng, "huge-passport.png"))
        {
            var oversizedIdResp = await client.PostAsync($"/api/tenant/{token}/identity-photo", oversizedIdForm);
            Assert(oversizedIdResp.StatusCode == HttpStatusCode.RequestEntityTooLarge, "identity-photo rejects >2MB photo with 413 PayloadTooLarge");
        }

        using (var validIdForm = BuildIdentityPhotoForm(validPng, "passport-kim.png"))
        {
            var validIdResp = await client.PostAsync($"/api/tenant/{token}/identity-photo", validIdForm);
            Assert(validIdResp.StatusCode == HttpStatusCode.OK, "POST /api/tenant/{token}/identity-photo returns 200 OK for valid PNG");
            var idPhotoDto = await validIdResp.Content.ReadFromJsonAsync<TenantIdentityPhotoDto>();
            Assert(idPhotoDto != null && idPhotoDto.TargetKey == "tenant" && idPhotoDto.PhotoContentType == "image/png",
                "identity-photo DTO returns target='tenant' and image/png content type");
        }

        var docAfterIdPhoto = await client.GetFromJsonAsync<OccupancyDocumentDto>($"/api/rooms/{roomId}/documents");
        Assert(docAfterIdPhoto != null &&
               docAfterIdPhoto.TenantCitizenId == "M87654321" &&
               docAfterIdPhoto.HasTenantIdentityPhoto &&
               !string.IsNullOrWhiteSpace(docAfterIdPhoto.TenantIdentityPhotoFileName),
            "GET /api/rooms/{id}/documents reports HasTenantIdentityPhoto=true and non-empty TenantIdentityPhotoFileName");

        var ownerIdPhotoResp = await client.GetAsync($"/api/rooms/{roomId}/documents/identity-photo?download=true");
        Assert(ownerIdPhotoResp.StatusCode == HttpStatusCode.OK, "GET /api/rooms/{id}/documents/identity-photo returns 200 OK");
        var ownerIdPhotoBytes = await ownerIdPhotoResp.Content.ReadAsByteArrayAsync();
        Assert(ownerIdPhotoBytes.SequenceEqual(validPng), "GET /api/rooms/{id}/documents/identity-photo returns exact uploaded image bytes");
        var cacheControl = ownerIdPhotoResp.Headers.CacheControl?.ToString() ?? string.Empty;
        var nosniff = ownerIdPhotoResp.Headers.TryGetValues("X-Content-Type-Options", out var xcto) ? string.Join(",", xcto) : string.Empty;
        var csp = ownerIdPhotoResp.Headers.TryGetValues("Content-Security-Policy", out var cspVals) ? string.Join(",", cspVals) : string.Empty;
        var contentDisposition = ownerIdPhotoResp.Content.Headers.ContentDisposition?.DispositionType ?? string.Empty;
        Assert(cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase) &&
               nosniff.Contains("nosniff", StringComparison.OrdinalIgnoreCase) &&
               csp.Contains("sandbox", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(contentDisposition, "attachment", StringComparison.OrdinalIgnoreCase),
            "GET /api/rooms/{id}/documents/identity-photo?download=true sets no-store, nosniff, sandbox CSP, and attachment disposition headers");

        static MultipartFormDataContent BuildMeterForm(byte[] fileBytes, string fileName, string reading, string? note = null)
        {
            var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "photo", fileName);
            form.Add(new StringContent(reading), "submittedReading");
            if (note is not null) form.Add(new StringContent(note), "note");
            return form;
        }

        using (var badMagicForm = BuildMeterForm(invalidMagic, "meter.pdf", "145"))
        {
            var badMagicResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", badMagicForm);
            Assert(badMagicResp.StatusCode == HttpStatusCode.BadRequest, "meter-submission rejects non-image magic bytes with 400 BadRequest");
        }

        using (var oversizedForm = BuildMeterForm(oversizedPng, "huge.png", "145"))
        {
            var oversizedResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", oversizedForm);
            Assert(oversizedResp.StatusCode == HttpStatusCode.RequestEntityTooLarge, "meter-submission rejects >2MB photo with 413 PayloadTooLarge");
        }

        using (var rollbackForm = BuildMeterForm(validPng, "meter.png", "95"))
        {
            var rollbackResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", rollbackForm);
            Assert(rollbackResp.StatusCode == HttpStatusCode.BadRequest, "meter-submission rejects reading < previousReading (95 < 100) with 400 BadRequest");
        }

        TenantMeterSubmissionDto? submittedDto;
        using (var validForm = BuildMeterForm(validPng, "meter.png", "145", "Chi so dien thang nay"))
        {
            var validResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", validForm);
            Assert(validResp.StatusCode == HttpStatusCode.OK, "valid PNG meter-submission returns 200 OK");
            submittedDto = await validResp.Content.ReadFromJsonAsync<TenantMeterSubmissionDto>();
            Assert(submittedDto != null &&
                   submittedDto.Status == "Pending" &&
                   submittedDto.PreviousReading == 100m &&
                   submittedDto.SubmittedReading == 145m &&
                   submittedDto.TenantNote == "Chi so dien thang nay" &&
                   submittedDto.PhotoContentType == "image/png",
                "meter-submission DTO has Pending status, PreviousReading=100, SubmittedReading=145, and image/png content type");
        }

        var submissionId = submittedDto!.Id;

        // Verify GET /api/tenant/{token} includes meterSubmission with Pending status
        var portalJson = await client.GetFromJsonAsync<JsonElement>($"/api/tenant/{token}");
        Assert(portalJson.GetProperty("meterSubmission").GetProperty("status").GetString() == "Pending" &&
               portalJson.GetProperty("meterSubmission").GetProperty("submittedReading").GetDecimal() == 145m,
            "GET /api/tenant/{token} returns Pending meterSubmission");

        // Verify GET /api/readings/submissions returns Pending submission for OwnerA
        var ownerSubmissions = await client.GetFromJsonAsync<List<TenantMeterSubmissionDto>>("/api/readings/submissions");
        Assert(ownerSubmissions != null && ownerSubmissions.Count == 1 &&
               ownerSubmissions[0].Id == submissionId && ownerSubmissions[0].Status == "Pending",
            "GET /api/readings/submissions returns Pending submission for landlord");

        // Verify photo endpoint returns exact bytes
        var photoBytes = await client.GetByteArrayAsync($"/api/readings/submissions/{submissionId}/photo");
        Assert(photoBytes.SequenceEqual(validPng), "GET /api/readings/submissions/{id}/photo returns uploaded image bytes");

        // 5. POST /api/readings/submissions/{id}/reject marks Rejected with reason and allows tenant to resubmit (JPEG & WebP)
        var rejectResp = await client.PostAsJsonAsync($"/api/readings/submissions/{submissionId}/reject",
            new RejectMeterSubmissionRequest("Anh bi loa sang, vui long chup lai"));
        Assert(rejectResp.StatusCode == HttpStatusCode.OK, "POST /api/readings/submissions/{id}/reject returns 200 OK");
        var rejectedDto = await rejectResp.Content.ReadFromJsonAsync<TenantMeterSubmissionDto>();
        Assert(rejectedDto!.Status == "Rejected" && rejectedDto.RejectReason == "Anh bi loa sang, vui long chup lai",
            "submission marked Rejected with landlord reason");

        using (var resubmitJpeg = BuildMeterForm(validJpeg, "retake.jpg", "147", "Chup lai JPEG"))
        {
            var resubmitJpegResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", resubmitJpeg);
            Assert(resubmitJpegResp.StatusCode == HttpStatusCode.OK, "resubmit with valid JPEG succeeds");
        }

        using (var resubmitWebp = BuildMeterForm(validWebp, "retake.webp", "148", "Chup lai WebP ro net"))
        {
            var resubmitWebpResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", resubmitWebp);
            Assert(resubmitWebpResp.StatusCode == HttpStatusCode.OK, "resubmit with valid WebP succeeds");
            var resubmittedDto = await resubmitWebpResp.Content.ReadFromJsonAsync<TenantMeterSubmissionDto>();
            Assert(resubmittedDto!.Id == submissionId &&
                   resubmittedDto.Status == "Pending" &&
                   resubmittedDto.SubmittedReading == 148m &&
                   resubmittedDto.RejectReason == null &&
                   resubmittedDto.PhotoContentType == "image/webp",
                "resubmission resets status to Pending, updates reading to 148, clears RejectReason, and stores image/webp");
        }

        // 6. Cross-owner isolation: OwnerB cannot approve, reject, or view photo of OwnerA's submission
        using (var crossOwnerApprove = new HttpRequestMessage(HttpMethod.Post, $"/api/readings/submissions/{submissionId}/approve")
        {
            Content = JsonContent.Create(new ApproveMeterSubmissionRequest(148m))
        })
        {
            crossOwnerApprove.Headers.Add("X-Fixture-Owner", OwnerBId.ToString());
            using var crossResp = await client.SendAsync(crossOwnerApprove);
            Assert(crossResp.StatusCode == HttpStatusCode.NotFound, "cross-owner approve is blocked with 404 NotFound");
        }

        // 7. POST /api/readings/submissions/{id}/approve creates MonthlyReading + Issued Monthly Invoice atomically
        var approveResp = await client.PostAsJsonAsync($"/api/readings/submissions/{submissionId}/approve",
            new ApproveMeterSubmissionRequest(150m));
        Assert(approveResp.StatusCode == HttpStatusCode.OK, "POST /api/readings/submissions/{id}/approve returns 200 OK");
        var approvedDto = await approveResp.Content.ReadFromJsonAsync<TenantMeterSubmissionDto>();
        Assert(approvedDto!.Status == "Approved" &&
               approvedDto.ApprovedReading == 150m &&
               approvedDto.ReadingId != null &&
               approvedDto.InvoiceId != null,
            "approved submission records Approved status, ApprovedReading=150, ReadingId, and InvoiceId");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reading = await db.Readings.FindAsync(approvedDto.ReadingId);
            var invoice = await db.Invoices.FindAsync(approvedDto.InvoiceId);
            Assert(reading != null && reading.PreviousReading == 100m && reading.CurrentReading == 150m && reading.Consumption == 50m,
                "MonthlyReading created with PreviousReading=100, CurrentReading=150, Consumption=50");
            Assert(invoice != null && invoice.Kind == "Monthly" && invoice.LifecycleState == InvoiceLifecycleState.Issued &&
                   invoice.ElectricityAmount == 50m * 3_500m,
                "Issued Monthly Invoice created atomically with accurate electricity amount");
        }

        // Re-approving or tenant resubmitting after approval returns 409 Conflict
        var doubleApproveResp = await client.PostAsJsonAsync($"/api/readings/submissions/{submissionId}/approve",
            new ApproveMeterSubmissionRequest(150m));
        Assert(doubleApproveResp.StatusCode == HttpStatusCode.Conflict, "approving an already-approved submission returns 409 Conflict");
    }

    private static WebApplication CreateTestApp(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "http://localhost",
            ["WebhookBaseUrl"] = "https://callbacks.example.invalid",
            ["RequireVerifiedPaymentForTenantLink"] = "false"
        });

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        builder.Services.AddSingleton<TenantTokenService>();
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddScoped<PayOsService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddAuthentication(FixtureAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(FixtureAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("tenant-payment",
            _ => RateLimitPartition.GetNoLimiter("fixture")));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(
                new AppUser { Id = OwnerAId, Name = "Owner A", Email = "ownerA@example.com", PasswordHash = "unused", Role = UserRole.Owner, SubscriptionTier = SubscriptionTier.Free },
                new AppUser { Id = OwnerBId, Name = "Owner B", Email = "ownerB@example.com", PasswordHash = "unused", Role = UserRole.Owner, SubscriptionTier = SubscriptionTier.Free }
            );
            db.SaveChanges();
        }

        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOwnerManagementEndpoints();
        app.MapBillingEndpoints();
        app.MapDepositEndpoints();
        app.MapMoveoutEndpoints();
        app.MapPaymentEndpoints();

        return app;
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FixtureAuth";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var owner = Request.Headers["X-Fixture-Owner"].FirstOrDefault() ?? OwnerAId.ToString();
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, owner),
                new Claim(ClaimTypes.Role, "Owner")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

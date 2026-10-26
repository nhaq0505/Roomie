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
        var occ2Id = validOccJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        // 4. Multi-period Draft readings/invoices: PATCH /api/rooms/{id}/occupancy InitialReading only updates the FIRST period
        Guid readingPeriod1Id;
        Guid readingPeriod2Id;
        Guid draftPeriod1Id;
        Guid draftPeriod2Id;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r1 = new MonthlyReading
            {
                RoomId = roomId, OccupancyId = occ2Id, Period = "2026-09",
                PreviousReading = 185, CurrentReading = 220, Consumption = 35,
                ElectricityRate = 3_500, MonthlyRentRate = 2_000_000, WaterRate = 50_000,
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var r2 = new MonthlyReading
            {
                RoomId = roomId, OccupancyId = occ2Id, Period = "2026-10",
                PreviousReading = 220, CurrentReading = 260, Consumption = 40,
                ElectricityRate = 3_500, MonthlyRentRate = 2_000_000, WaterRate = 50_000,
                RecordedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            var d1 = new Invoice
            {
                OwnerId = OwnerAId, BuildingId = bldg.Id, RoomId = roomId, OccupancyId = occ2Id,
                RoomName = "Room Meter-1", BuildingName = "Meter Building", TenantName = "Tenant Two",
                Period = "2026-09", Kind = "Monthly", LifecycleState = InvoiceLifecycleState.Draft,
                PreviousReading = 185, CurrentReading = 220, Consumption = 35, ElectricityRate = 3_500,
                WaterRate = 50_000, WaterPerPerson = true,
                Rent = 2_000_000, ElectricityAmount = 122_500, WaterAmount = 50_000, ServiceAmount = 0,
                Total = 2_172_500, Paid = 0, Status = InvoiceStatus.Unpaid, IssuedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var d2 = new Invoice
            {
                OwnerId = OwnerAId, BuildingId = bldg.Id, RoomId = roomId, OccupancyId = occ2Id,
                RoomName = "Room Meter-1", BuildingName = "Meter Building", TenantName = "Tenant Two",
                Period = "2026-10", Kind = "Monthly", LifecycleState = InvoiceLifecycleState.Draft,
                PreviousReading = 220, CurrentReading = 260, Consumption = 40, ElectricityRate = 3_500,
                WaterRate = 50_000, WaterPerPerson = true,
                Rent = 2_000_000, ElectricityAmount = 140_000, WaterAmount = 50_000, ServiceAmount = 0,
                Total = 2_190_000, Paid = 0, Status = InvoiceStatus.Unpaid, IssuedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            db.Readings.AddRange(r1, r2);
            db.Invoices.AddRange(d1, d2);
            await db.SaveChangesAsync();
            readingPeriod1Id = r1.Id;
            readingPeriod2Id = r2.Id;
            draftPeriod1Id = d1.Id;
            draftPeriod2Id = d2.Id;
        }

        // Reject InitialReading > firstReading.CurrentReading (230 > 220)
        var patchAboveFirstCurrentResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy",
            new OccupancyUpdateRequest(occ2Id, "Tenant Two", "0904445566", 1, InitialReading: 230));
        Assert(patchAboveFirstCurrentResp.StatusCode == HttpStatusCode.BadRequest,
            "PATCH occupancy rejects InitialReading > first reading CurrentReading (230 > 220) with 400 BadRequest");

        // Reject InitialReading < prior occupancy floor (175 < 180)
        var patchBelowPriorFloorResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy",
            new OccupancyUpdateRequest(occ2Id, "Tenant Two", "0904445566", 1, InitialReading: 175));
        Assert(patchBelowPriorFloorResp.StatusCode == HttpStatusCode.BadRequest,
            "PATCH occupancy rejects InitialReading < prior occupancy floor (175 < 180) with 400 BadRequest");

        // Valid PATCH InitialReading = 190 updates ONLY the first period (2026-09) and leaves 2026-10 untouched
        var patchValidResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy",
            new OccupancyUpdateRequest(occ2Id, "Tenant Two", "0904445566", 1, InitialReading: 190));
        Assert(patchValidResp.StatusCode == HttpStatusCode.OK, "PATCH occupancy with InitialReading=190 succeeds");
        var patchValidJson = await patchValidResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert(patchValidJson.GetProperty("initialReading").GetDecimal() == 190m, "room initialReading updated to 190");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r1 = await db.Readings.AsNoTracking().SingleAsync(x => x.Id == readingPeriod1Id);
            var r2 = await db.Readings.AsNoTracking().SingleAsync(x => x.Id == readingPeriod2Id);
            var d1 = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == draftPeriod1Id);
            var d2 = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == draftPeriod2Id);

            Assert(r1.PreviousReading == 190m && r1.Consumption == 30m,
                "first period reading (2026-09) updated to PreviousReading=190, Consumption=30");
            Assert(d1.PreviousReading == 190m && d1.Consumption == 30m && d1.ElectricityAmount == 105_000m && d1.Total == 2_155_000m,
                "first period Draft invoice (2026-09) recalculated to PreviousReading=190, Consumption=30, ElectricityAmount=105000, Total=2155000");

            Assert(r2.PreviousReading == 220m && r2.Consumption == 40m,
                "second period reading (2026-10) preserves PreviousReading=220, Consumption=40");
            Assert(d2.PreviousReading == 220m && d2.Consumption == 40m && d2.ElectricityAmount == 140_000m && d2.Total == 2_190_000m,
                "second period Draft invoice (2026-10) preserves PreviousReading=220, Consumption=40, ElectricityAmount=140000, Total=2190000");
        }

        // Simultaneous PATCH of People (1 -> 2) and InitialReading (190 -> 192) records sequential revisions without unique key collision
        var patchBothResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy",
            new OccupancyUpdateRequest(occ2Id, "Tenant Two", "0904445566", 2, InitialReading: 192));
        Assert(patchBothResp.StatusCode == HttpStatusCode.OK,
            "PATCH occupancy updating both People and InitialReading simultaneously succeeds with 200 OK");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var d1 = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == draftPeriod1Id);
            var d2 = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == draftPeriod2Id);
            var d1Revs = await db.InvoiceRevisions.AsNoTracking()
                .Where(x => x.InvoiceId == draftPeriod1Id)
                .OrderBy(x => x.Revision)
                .Select(x => x.Revision)
                .ToListAsync();
            Assert(d1.PreviousReading == 192m && d1.Consumption == 28m && d1.ElectricityAmount == 98_000m && d1.WaterAmount == 100_000m && d1.Total == 2_198_000m,
                "first period Draft invoice updates both WaterAmount and ElectricityAmount when People and InitialReading change together");
            Assert(d2.PreviousReading == 220m && d2.Consumption == 40m && d2.ElectricityAmount == 140_000m && d2.WaterAmount == 100_000m && d2.Total == 2_240_000m,
                "second period Draft invoice updates WaterAmount while preserving PreviousReading=220");
            Assert(d1Revs.SequenceEqual([1, 2, 3]),
                "first period Draft invoice records sequential revisions 1, 2, 3 across single and combined occupancy updates");
        }

        // Once any Monthly invoice is Issued, PATCH InitialReading is blocked with 409 Conflict
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var d1 = await db.Invoices.SingleAsync(x => x.Id == draftPeriod1Id);
            d1.LifecycleState = InvoiceLifecycleState.Issued;
            await db.SaveChangesAsync();
        }
        var patchAfterIssuedResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy",
            new OccupancyUpdateRequest(occ2Id, "Tenant Two", "0904445566", 1, InitialReading: 195));
        Assert(patchAfterIssuedResp.StatusCode == HttpStatusCode.Conflict,
            "PATCH occupancy rejects InitialReading change with 409 Conflict when an Issued Monthly invoice exists");
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
        Assert(occResp.StatusCode == HttpStatusCode.OK, "occupancy created for retired endpoints & landlord identity photo test");
        var occRoot = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occElement = occRoot.GetProperty("occupancy");
        var occId = occElement.GetProperty("id").GetGuid();
        var token = occElement.GetProperty("token").GetString()!;
        Assert(!string.IsNullOrWhiteSpace(token), "tenant token issued on occupancy creation");

        // 1. Removed tenant self-checkin & meter submission endpoints must return 404 NotFound
        var selfCheckinResp = await client.PostAsJsonAsync($"/api/tenant/{token}/self-checkin", new
        {
            target = "tenant",
            fullName = "Nguyen Thi Thue",
            citizenId = "079199001234"
        });
        var deleteCoResResp = await client.DeleteAsync($"/api/tenant/{token}/co-residents/079200005678");
        var tenantIdPhotoGetResp = await client.GetAsync($"/api/tenant/{token}/identity-photo");
        var tenantMeterPhotoGetResp = await client.GetAsync($"/api/tenant/{token}/meter-submission/photo");
        var ownerSubmissionsGetResp = await client.GetAsync("/api/readings/submissions");
        var ownerSubPhotoGetResp = await client.GetAsync($"/api/readings/submissions/{Guid.NewGuid()}/photo");
        var ownerSubApproveResp = await client.PostAsJsonAsync($"/api/readings/submissions/{Guid.NewGuid()}/approve", new { currentReading = 150 });
        var ownerSubRejectResp = await client.PostAsJsonAsync($"/api/readings/submissions/{Guid.NewGuid()}/reject", new { reason = "test" });

        byte[] validPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];
        using var multipartForm = new MultipartFormDataContent();
        multipartForm.Add(new ByteArrayContent(validPng), "photo", "test.png");
        multipartForm.Add(new StringContent("150"), "submittedReading");
        var tenantMeterPostResp = await client.PostAsync($"/api/tenant/{token}/meter-submission", multipartForm);

        using var idPhotoForm = new MultipartFormDataContent();
        idPhotoForm.Add(new ByteArrayContent(validPng), "photo", "id.png");
        var tenantIdPhotoPostResp = await client.PostAsync($"/api/tenant/{token}/identity-photo", idPhotoForm);

        Assert(selfCheckinResp.StatusCode == HttpStatusCode.NotFound &&
               deleteCoResResp.StatusCode == HttpStatusCode.NotFound &&
               tenantIdPhotoGetResp.StatusCode == HttpStatusCode.NotFound &&
               tenantIdPhotoPostResp.StatusCode == HttpStatusCode.NotFound &&
               tenantMeterPostResp.StatusCode == HttpStatusCode.NotFound &&
               tenantMeterPhotoGetResp.StatusCode == HttpStatusCode.NotFound &&
               ownerSubmissionsGetResp.StatusCode == HttpStatusCode.NotFound &&
               ownerSubPhotoGetResp.StatusCode == HttpStatusCode.NotFound &&
               ownerSubApproveResp.StatusCode == HttpStatusCode.NotFound &&
               ownerSubRejectResp.StatusCode == HttpStatusCode.NotFound,
            "all retired tenant self-checkin and meter-submission endpoints return 404 NotFound");

        // 2. GET /api/tenant/{token} remains active for invoice viewing without meterSubmission fields
        var portalJson = await client.GetFromJsonAsync<JsonElement>($"/api/tenant/{token}");
        Assert(portalJson.TryGetProperty("room", out _) &&
               portalJson.TryGetProperty("occupancy", out _) &&
               portalJson.TryGetProperty("invoices", out _) &&
               !portalJson.TryGetProperty("meterSubmission", out _) &&
               !portalJson.TryGetProperty("currentPeriodHasReading", out _),
            "GET /api/tenant/{token} returns room/occupancy/invoices and omits retired meterSubmission fields");

        // 3. Landlord identity photo endpoint GET /api/rooms/{id}/documents/identity-photo remains active
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TenantIdentityDocuments.Add(new TenantIdentityDocument
            {
                OwnerId = OwnerAId,
                BuildingId = bldg.Id,
                RoomId = roomId,
                OccupancyId = occId,
                TargetKey = "tenant",
                PhotoFileName = "passport-kim.png",
                PhotoContentType = "image/png",
                PhotoSizeBytes = validPng.LongLength,
                PhotoSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                PhotoContent = validPng,
                UploadedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var docAfterIdPhoto = await client.GetFromJsonAsync<OccupancyDocumentDto>($"/api/rooms/{roomId}/documents");
        Assert(docAfterIdPhoto != null &&
               docAfterIdPhoto.HasTenantIdentityPhoto &&
               docAfterIdPhoto.TenantIdentityPhotoFileName == "passport-kim.png",
            "GET /api/rooms/{id}/documents reports HasTenantIdentityPhoto=true and TenantIdentityPhotoFileName");

        var ownerIdPhotoResp = await client.GetAsync($"/api/rooms/{roomId}/documents/identity-photo?download=true");
        Assert(ownerIdPhotoResp.StatusCode == HttpStatusCode.OK, "GET /api/rooms/{id}/documents/identity-photo returns 200 OK");
        var ownerIdPhotoBytes = await ownerIdPhotoResp.Content.ReadAsByteArrayAsync();
        Assert(ownerIdPhotoBytes.SequenceEqual(validPng), "GET /api/rooms/{id}/documents/identity-photo returns exact image bytes");
        var cacheControl = ownerIdPhotoResp.Headers.CacheControl?.ToString() ?? string.Empty;
        var nosniff = ownerIdPhotoResp.Headers.TryGetValues("X-Content-Type-Options", out var xcto) ? string.Join(",", xcto) : string.Empty;
        var csp = ownerIdPhotoResp.Headers.TryGetValues("Content-Security-Policy", out var cspVals) ? string.Join(",", cspVals) : string.Empty;
        var contentDisposition = ownerIdPhotoResp.Content.Headers.ContentDisposition?.DispositionType ?? string.Empty;
        Assert(cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase) &&
               nosniff.Contains("nosniff", StringComparison.OrdinalIgnoreCase) &&
               csp.Contains("sandbox", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(contentDisposition, "attachment", StringComparison.OrdinalIgnoreCase),
            "GET /api/rooms/{id}/documents/identity-photo?download=true sets no-store, nosniff, sandbox CSP, and attachment disposition headers");
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

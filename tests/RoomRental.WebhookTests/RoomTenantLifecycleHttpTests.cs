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
            "Le Van C", "0901112233", 2, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), 0));
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
            "Tran Thi B", "0912345678", 1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), 0));
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
                Period = ApiSupport.VietnamDate(DateTimeOffset.UtcNow).ToString("yyyy-MM"),
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
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
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
            "Deposit Tenant", "0988776655", 1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), 500_000));
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
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5))
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
            "Moveout Tenant", "0933445566", 1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)), 300_000));
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
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
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

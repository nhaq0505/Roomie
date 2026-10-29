using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class AccountingBillingHttpTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    public static async Task RunAsync()
    {
        await AutomaticZaloQueueAsync();
        await MeterReadingAntiRegressionAsync();
        await AutomaticMonthlyInvoiceCalculationAsync();
        await WaterByMeterBillingAndAntiRegressionAsync();
        await SupplementalInvoiceCreationAsync();
        await InvoiceListSummaryAsync();
        await InvoiceCorrectionAndAuditRevisionsAsync();
        await CashReceiptIdempotencyAndOverpaymentAsync();
        await ClosedXmlExcelReportExportAsync();
    }

    private static async Task InvoiceListSummaryAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        var commands = new List<string>();
        await using var app = CreateTestApp(fixture.ConnectionString, commandLog: commands.Add);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());
        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Summary", "Room", 100, 1, 0, false, 0, 0);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var buildingId = await db.Rooms.Where(x => x.Id == roomId).Select(x => x.BuildingId).SingleAsync();
            Invoice Row(string kind, string period, decimal total, decimal paid, InvoiceLifecycleState state, string tenant) => new()
            {
                OwnerId = OwnerId, BuildingId = buildingId, RoomId = roomId, OccupancyId = occupancyId,
                BuildingName = "Summary", RoomName = "Room", TenantName = tenant, Kind = kind, Period = period,
                Total = total, Paid = paid, LifecycleState = state,
                Status = paid >= total ? InvoiceStatus.Paid : paid > 0 ? InvoiceStatus.Partial : InvoiceStatus.Unpaid
            };
            var foreign = Row("Supplemental", "2026-08", 9999, 0, InvoiceLifecycleState.Issued, "Foreign");
            foreign.OwnerId = Guid.NewGuid();
            db.Invoices.AddRange(
                Row("Supplemental", "2026-08", 100, 40, InvoiceLifecycleState.Issued, "Match"),
                Row("Monthly", "2026-08", 200, 250, InvoiceLifecycleState.Issued, "Paid"),
                Row("Deposit", "2026-08", 500, 100, InvoiceLifecycleState.Issued, "Deposit"),
                Row("Supplemental", "2026-09", 900, 0, InvoiceLifecycleState.Draft, "Match"), foreign);
            await db.SaveChangesAsync();
        }

        async Task Check(string filter, int count, decimal billed, decimal paid, decimal outstanding, int drafts)
        {
            commands.Clear();
            var response = await client.GetAsync("/api/invoices?page=1&pageSize=1" + filter);
            Assert(response.StatusCode == HttpStatusCode.OK, "invoice summary returns 200: " + filter);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var totals = body.GetProperty("totals");
            Assert(body.GetProperty("total").GetInt32() == count && body.GetProperty("draftCount").GetInt32() == drafts &&
                totals.GetProperty("billed").GetDecimal() == billed && totals.GetProperty("paid").GetDecimal() == paid &&
                totals.GetProperty("outstanding").GetDecimal() == outstanding && body.GetProperty("items").GetArrayLength() == Math.Min(1, count),
                "invoice summary preserves scope, filters, full-result totals and empty zeros: " + filter);
            Assert(commands.Count == 4 && commands.Count(x => x.Contains("GROUP BY", StringComparison.Ordinal)) == 1,
                "invoice list uses one summary plus page/reservations/occupancies queries: " + filter);
        }
        await Check("", 4, 800, 390, 410, 1);
        await Check("&period=2026-08", 3, 800, 390, 410, 0);
        await Check("&lifecycleState=Draft", 1, 0, 0, 0, 1);
        await Check("&status=Paid", 1, 200, 250, -50, 0);
        await Check("&kind=Deposit", 1, 500, 100, 400, 0);
        await Check("&search=match", 2, 100, 40, 60, 1);
        await Check("&occupancyId=" + Guid.NewGuid(), 0, 0, 0, 0, 0);
        var legacy = await client.GetFromJsonAsync<JsonElement>("/api/invoices");
        Assert(legacy.ValueKind == JsonValueKind.Array && legacy.GetArrayLength() == 4,
            "invoice list without page retains legacy array response");

        commands.Clear();
        var dashboardRes = await client.GetAsync("/api/dashboard");
        Assert(dashboardRes.StatusCode == HttpStatusCode.OK, "dashboard returns 200");
        var dashboard = await dashboardRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert(dashboard.GetProperty("totalBilled").GetDecimal() == 300 &&
               dashboard.GetProperty("totalPaid").GetDecimal() == 290 &&
               dashboard.GetProperty("totalOutstanding").GetDecimal() == 10,
               "dashboard aggregates issued non-deposit invoices correctly");
        Assert(commands.Count(x => x.Contains("\"Invoices\"", StringComparison.Ordinal)) == 1 &&
               commands.Count(x => x.Contains("GROUP BY", StringComparison.Ordinal)) == 1,
               "dashboard uses exactly one aggregate query on invoices table");

        commands.Clear();
        var cachedDashboardRes = await client.GetAsync("/api/dashboard");
        Assert(cachedDashboardRes.StatusCode == HttpStatusCode.OK, "cached dashboard returns 200");
        Assert(commands.Count == 0, "subsequent dashboard call hits in-memory cache with 0 DB queries");
    }

    private static async Task AutomaticZaloQueueAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString, autoZalo: true); await app.StartAsync();
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());
        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Auto Zalo", "Auto 101", 100000, 1000, 0, false, 0, 0);
        var reading = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 10));
        Assert(reading.StatusCode == HttpStatusCode.Created, "auto-Zalo reading endpoint still creates invoice while gateway disconnected");
        var requestId = Guid.NewGuid();
        var body = new SupplementalInvoiceRequest(requestId, occupancyId, "Auto charge", 1000, new DateOnly(2026, 10, 30));
        Assert((await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices", body)).StatusCode == HttpStatusCode.Created, "supplemental endpoint creates invoice with automatic queue");
        await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices", body);
        Guid draftId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.ZaloReminders.CountAsync() == 2 && await db.ZaloReminders.CountAsync(x => x.InvoiceId == requestId) == 1,
                "HTTP monthly and supplemental creation queue once despite replay");
            var room = await db.Rooms.SingleAsync(x => x.Id == roomId);
            var draft = new Invoice { OwnerId = OwnerId, BuildingId = room.BuildingId, RoomId = roomId, OccupancyId = occupancyId,
                RoomName = "Auto 101", BuildingName = "Auto Zalo", TenantName = "Fixture", Period = "2026-09", LifecycleState = InvoiceLifecycleState.Draft };
            draftId = draft.Id; db.Invoices.Add(draft); await db.SaveChangesAsync();
            Assert(!await db.ZaloReminders.AnyAsync(x => x.InvoiceId == draftId), "unpublished HTTP fixture draft has no reminder");
        }
        var publish = new PublishInvoiceRequest(Guid.NewGuid(), 1, 100000, 1000, 0, 0);
        Assert((await client.PostAsJsonAsync($"/api/invoices/{draftId}/publish", publish)).StatusCode == HttpStatusCode.OK, "publish endpoint queues draft on publication");
        await client.PostAsJsonAsync($"/api/invoices/{draftId}/publish", publish);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.ZaloReminders.CountAsync(x => x.InvoiceId == draftId) == 1 && await db.ZaloReminders.AllAsync(x => x.Status == "Pending"),
                "publication replay leaves one pending reminder without provider calls");
        }
    }

    private static async Task MeterReadingAntiRegressionAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Anti-Regression Building", "Room AR-1",
            rent: 2_000_000, elecRate: 3_500, waterFee: 50_000, waterPerPerson: false, serviceFee: 20_000, initialReading: 100);

        // 1. Reading before occupancy start date is rejected (occupancy starts 2026-08-01)
        var preOccupancyResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-07", 120));
        Assert(preOccupancyResp.StatusCode == HttpStatusCode.BadRequest,
            "reading before occupancy start date is rejected with 400 BadRequest");

        // 2. Reading for future month is rejected (e.g. 2099-01)
        var futureResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2099-01", 120));
        Assert(futureResp.StatusCode == HttpStatusCode.BadRequest,
            "reading for future period is rejected with 400 BadRequest");

        // 3. Record valid reading for 2026-08: 100 -> 150
        var r1Resp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 150));
        Assert(r1Resp.StatusCode == HttpStatusCode.Created, "first monthly reading (150) created with 201 Created");

        // 4. Reading lower than previous reading (140 < 150) is rejected
        var lowerResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-09", 140));
        Assert(lowerResp.StatusCode == HttpStatusCode.BadRequest,
            "reading lower than previous reading (140 < 150) is rejected with 400 BadRequest");

        // 5. Record valid reading for 2026-09: 150 -> 210
        var r2Resp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-09", 210));
        Assert(r2Resp.StatusCode == HttpStatusCode.Created, "second monthly reading (210) created with 201 Created");

        // 6. Out-of-order period (attempting to record 2026-08 when 2026-09 already exists) is rejected with 409 Conflict
        var outOfOrderResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 220));
        Assert(outOfOrderResp.StatusCode == HttpStatusCode.Conflict,
            "out-of-order reading period is rejected with 409 Conflict");

        // 7. Move-out baseline test: move out old tenant and advance baseline
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Mark all invoices paid to satisfy moveout
            var invoices = await db.Invoices.Where(i => i.OccupancyId == occupancyId).ToListAsync();
            foreach (var inv in invoices) { inv.Paid = inv.Total; inv.Status = InvoiceStatus.Paid; }
            await db.SaveChangesAsync();
        }

        var moveoutResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/complete-moveout",
            new CompleteMoveoutRequest(occupancyId, Guid.NewGuid(), 0, 0, null));
        Assert(moveoutResp.StatusCode == HttpStatusCode.OK, "moveout completed successfully");

        // Verify room.InitialReading was advanced to 210
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var room = await db.Rooms.FindAsync(roomId);
            Assert(room!.InitialReading == 210, "room InitialReading advanced to baseline (210)");
        }

        // Add new occupancy to the same room
        var newOccResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "New Tenant", "0977665544", 1, new DateOnly(2026, 9, 15), 0));
        Assert(newOccResp.StatusCode == HttpStatusCode.OK, "new tenant moved into room");

        // New tenant cannot enter reading lower than baseline 210 (e.g. 200)
        var regressedResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-09", 200));
        Assert(regressedResp.StatusCode == HttpStatusCode.BadRequest,
            "new tenant reading lower than moveout baseline (200 < 210) is rejected with 400 BadRequest");
    }

    private static async Task AutomaticMonthlyInvoiceCalculationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        // Room: Rent = 2,500,000; ElecRate = 4,000; WaterFee = 80,000 per person; Headcount = 3; Service = 50,000
        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Calc Building", "Room Calc-1",
            rent: 2_500_000, elecRate: 4_000, waterFee: 80_000, waterPerPerson: true, serviceFee: 50_000,
            initialReading: 50, people: 3);

        // Record reading: 50 -> 125 (Consumption = 75 kWh)
        var readingResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 125));
        Assert(readingResp.StatusCode == HttpStatusCode.Created, "reading created and triggered invoice generation");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-08" && i.Kind == "Monthly");

        // Rent = 2,500,000
        Assert(invoice.Rent == 2_500_000, "invoice Rent matches monthly room rent");
        // Electricity = 75 * 4,000 = 300,000
        Assert(invoice.ElectricityAmount == 300_000, "invoice Electricity matches consumption * electricity rate (75 * 4,000 = 300,000)");
        // Water = 80,000 * 3 people = 240,000
        Assert(invoice.WaterAmount == 240_000, "invoice Water matches water rate * snapshot occupants (80,000 * 3 = 240,000)");
        // Service = 50,000
        Assert(invoice.ServiceAmount == 50_000, "invoice Service matches service fee (50,000)");
        // Total = 2,500,000 + 300,000 + 240,000 + 50,000 = 3,090,000
        Assert(invoice.Total == 3_090_000, "invoice Total correctly calculated as sum of all fee components (3,090,000)");
        Assert(invoice.Paid == 0 && invoice.Status == InvoiceStatus.Unpaid, "new invoice has Paid = 0 and Status = Unpaid");
        Assert(invoice.LifecycleState == InvoiceLifecycleState.Issued, "invoice lifecycle state is Issued");
        Assert(invoice.DueDate == new DateOnly(2026, 9, 5), "due date set to 5th of next month (2026-09-05)");
    }

    private static async Task WaterByMeterBillingAndAntiRegressionAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest("Water Meter Building", "Address"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        // Create room with WaterByMeter = true, WaterFee = 15,000 VND/m³, InitialWaterReading = 20
        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, "Room WM-1", Rent: 2_500_000, ElectricityRate: 3_500, WaterFee: 15_000,
            WaterPerPerson: false, ServiceFee: 50_000, InitialReading: 100, WaterByMeter: true, InitialWaterReading: 20));
        Assert(roomResp.StatusCode == HttpStatusCode.Created, "room with WaterByMeter = true created with 201 Created");
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();
        Assert(roomJson.GetProperty("waterByMeter").GetBoolean() && roomJson.GetProperty("initialWaterReading").GetDecimal() == 20,
            "room DTO exposes waterByMeter = true and initialWaterReading = 20");

        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            "Water Tenant", "0911223344", 2, new DateOnly(2026, 8, 1), 0));
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occupancyId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        // 1. Missing CurrentWaterReading when WaterByMeter = true is rejected with 400 BadRequest
        var missingWaterResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 150));
        Assert(missingWaterResp.StatusCode == HttpStatusCode.BadRequest,
            "missing CurrentWaterReading when WaterByMeter = true is rejected with 400 BadRequest");

        // 2. Regressed CurrentWaterReading (18 < 20) is rejected with 400 BadRequest
        var regressedWaterResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 150, CurrentWaterReading: 18));
        Assert(regressedWaterResp.StatusCode == HttpStatusCode.BadRequest,
            "regressed CurrentWaterReading (18 < 20) is rejected with 400 BadRequest");

        // 3. Valid reading: Elec 100 -> 150 (50 kWh * 3,500 = 175,000), Water 20 -> 28 (8 m³ * 15,000 = 120,000)
        var validResp = await client.PostAsJsonAsync("/api/readings/draft", new ReadingRequest(roomId, "2026-08", 150, CurrentWaterReading: 28));
        Assert(validResp.StatusCode == HttpStatusCode.Created, "valid water meter reading (20 -> 28) created as Draft with 201 Created");

        Guid augInvoiceId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-08");
            augInvoiceId = inv.Id;
            Assert(inv.WaterByMeter == true && inv.PreviousWaterReading == 20 && inv.CurrentWaterReading == 28 &&
                   inv.WaterConsumption == 8 && inv.WaterAmount == 120_000 && inv.Total == 2_845_000,
                "Draft invoice snapshots WaterByMeter, Previous/CurrentWaterReading (20 -> 28), WaterConsumption (8), and WaterAmount (120,000)");
        }

        // 4. Updating InitialWaterReading on room/occupancy while first invoice is still Draft recalculates water consumption (15 -> 28 = 13 m³ * 15,000 = 195,000)
        var updateOccResp = await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyUpdateRequest(
            occupancyId, "Water Tenant", "0911223344", 2, InitialReading: 100, InitialWaterReading: 15));
        Assert(updateOccResp.StatusCode == HttpStatusCode.OK, "updating InitialWaterReading while invoice is Draft succeeds");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invoices.FindAsync(augInvoiceId);
            Assert(inv!.PreviousWaterReading == 15 && inv.WaterConsumption == 13 && inv.WaterAmount == 195_000,
                "Draft invoice recalculates PreviousWaterReading (15), WaterConsumption (13), and WaterAmount (195,000)");
        }

        // Issue August invoice and record September reading (Water 28 -> 35 = 7 m³ * 15,000 = 105,000)
        var issueResp = await client.PostAsync($"/api/invoices/{augInvoiceId}/issue", null);
        Assert(issueResp.StatusCode == HttpStatusCode.OK, "August invoice issued");

        var sepResp = await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-09", 200, CurrentWaterReading: 35));
        Assert(sepResp.StatusCode == HttpStatusCode.Created, "September reading (Water 28 -> 35) created");

        Guid sepInvoiceId;
        long sepVersion;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sep = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-09");
            sepInvoiceId = sep.Id;
            sepVersion = sep.Version;
            Assert(sep.PreviousWaterReading == 28 && sep.CurrentWaterReading == 35 && sep.WaterConsumption == 7 && sep.WaterAmount == 105_000,
                "September invoice anchors PreviousWaterReading at 28 and calculates WaterAmount = 105,000");

            // Mark all invoices paid for move-out test after correction
        }

        // 5. Correcting CurrentWaterReading on latest invoice (35 -> 40 = 12 m³ * 15,000 = 180,000)
        var corrWaterResp = await client.PostAsJsonAsync($"/api/invoices/{sepInvoiceId}/corrections",
            new InvoiceCorrectionRequest(Guid.NewGuid(), sepVersion, "Chốt lại đồng hồ nước tháng 9", CurrentWaterReading: 40));
        Assert(corrWaterResp.StatusCode == HttpStatusCode.OK, "correcting CurrentWaterReading on latest invoice succeeds");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sep = await db.Invoices.FindAsync(sepInvoiceId);
            Assert(sep!.CurrentWaterReading == 40 && sep.WaterConsumption == 12 && sep.WaterAmount == 180_000,
                "corrected invoice updates CurrentWaterReading (40), WaterConsumption (12), and WaterAmount (180,000)");

            var invoices = await db.Invoices.Where(i => i.OccupancyId == occupancyId).ToListAsync();
            foreach (var inv in invoices) { inv.Paid = inv.Total; inv.Status = InvoiceStatus.Paid; }
            await db.SaveChangesAsync();
        }

        // 6. Move-out advances room.InitialWaterReading to 40
        var moveoutResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/complete-moveout",
            new CompleteMoveoutRequest(occupancyId, Guid.NewGuid(), 0, 0, null));
        Assert(moveoutResp.StatusCode == HttpStatusCode.OK, "moveout completed for WaterByMeter room");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var room = await db.Rooms.FindAsync(roomId);
            Assert(room!.InitialWaterReading == 40, "room InitialWaterReading advanced to latest water meter baseline (40) after move-out");
        }
    }

    private static async Task SupplementalInvoiceCreationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Supp Building", "Room Supp-1",
            rent: 2_000_000, elecRate: 3_000, waterFee: 50_000, waterPerPerson: false, serviceFee: 0, initialReading: 0);

        var requestId = Guid.NewGuid();
        var dueDate = VietnamCalendar.TodayVietnam().AddDays(7);
        const string desc = "Thay bóng đèn và sửa khóa cửa";
        const decimal amount = 175_000;

        // 1. Create supplemental invoice
        var suppResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices",
            new SupplementalInvoiceRequest(requestId, occupancyId, desc, amount, dueDate));
        Assert(suppResp.StatusCode == HttpStatusCode.Created, "supplemental invoice created with 201 Created");
        var suppDto = await suppResp.Content.ReadFromJsonAsync<InvoiceDto>();
        Assert(suppDto != null && suppDto.Id == requestId && suppDto.Kind == "Supplemental" &&
               suppDto.Total == amount && suppDto.Description == desc,
            "supplemental invoice contains requested description, amount, and Supplemental kind");

        // 2. Idempotent replay with same RequestId and identical payload returns 200 OK
        var replayResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices",
            new SupplementalInvoiceRequest(requestId, occupancyId, desc, amount, dueDate));
        Assert(replayResp.StatusCode == HttpStatusCode.OK,
            "supplemental invoice replay with matching RequestId is idempotent (200 OK)");

        // 3. Replay with conflicting amount returns 409 Conflict
        var conflictResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices",
            new SupplementalInvoiceRequest(requestId, occupancyId, desc, 999_000, dueDate));
        Assert(conflictResp.StatusCode == HttpStatusCode.Conflict,
            "supplemental invoice replay with altered amount returns 409 Conflict");
    }

    private static async Task InvoiceCorrectionAndAuditRevisionsAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Correction Building", "Room Corr-1",
            rent: 3_000_000, elecRate: 3_500, waterFee: 50_000, waterPerPerson: false, serviceFee: 0, initialReading: 0);

        // Record reading for 2026-08: 0 -> 100
        await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 100));
        // Record reading for 2026-09: 100 -> 200
        await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-09", 200));

        Guid invoiceAugId, invoiceSepId;
        long augVersion, sepVersion;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aug = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-08");
            var sep = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-09");
            invoiceAugId = aug.Id;
            augVersion = aug.Version;
            invoiceSepId = sep.Id;
            sepVersion = sep.Version;
        }

        // 1. Correct latest invoice (2026-09): adjust rent to 2,800,000
        var corrResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceSepId}/corrections",
            new InvoiceCorrectionRequest(Guid.NewGuid(), sepVersion, "Giam tien phong theo thoa thuan", Rent: 2_800_000));
        Assert(corrResp.StatusCode == HttpStatusCode.OK, "invoice correction succeeds on latest issued invoice");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sep = await db.Invoices.FindAsync(invoiceSepId);
            Assert(sep!.Rent == 2_800_000 && sep.Version == sepVersion + 1,
                "invoice Rent updated and Version incremented");

            var revision = await db.InvoiceRevisions.SingleAsync(r => r.InvoiceId == invoiceSepId);
            Assert(revision.Reason == "Giam tien phong theo thoa thuan" &&
                   !string.IsNullOrEmpty(revision.BeforeJson) && !string.IsNullOrEmpty(revision.AfterJson),
                "immutable InvoiceRevision recorded with audit before and after snapshots");
        }

        // 2. Attempting to correct earlier month's (2026-08) meter reading is BLOCKED because later reading exists
        var blockedCorrResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceAugId}/corrections",
            new InvoiceCorrectionRequest(Guid.NewGuid(), augVersion, "Sua so dien thang cu", CurrentReading: 110));
        Assert(blockedCorrResp.StatusCode == HttpStatusCode.Conflict,
            "meter correction is blocked with 409 Conflict when a later reading already anchors the room");
    }

    private static async Task CashReceiptIdempotencyAndOverpaymentAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Cash Building", "Room Cash-1",
            rent: 2_000_000, elecRate: 3_000, waterFee: 50_000, waterPerPerson: false, serviceFee: 0, initialReading: 0);

        // Record reading to generate an issued invoice of 2,000,000 + 300,000 + 50,000 = 2,350,000
        await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 100));

        Guid invoiceId;
        long version;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId && i.Period == "2026-08");
            invoiceId = inv.Id;
            version = inv.Version;
        }

        // Partial confirmation is rejected without writing a receipt.
        var partialResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/receipts",
            new ReceiptRequest(1_000_000, "Partial rejected", "partial-key", version));
        Assert(partialResp.StatusCode == HttpStatusCode.Conflict, "partial cash confirmation is rejected");

        // Full confirmation settles the invoice.
        const string idempotencyKey = "cash-receipt-key-001";
        var receiptResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/receipts",
            new ReceiptRequest(2_350_000, "Thu tien mat dot 1", idempotencyKey, version));
        Assert(receiptResp.StatusCode == HttpStatusCode.OK, "full cash receipt recorded with 200 OK");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invoices.FindAsync(invoiceId);
            Assert(inv!.Paid == 2_350_000 && inv.Status == InvoiceStatus.Paid && (inv.Total - inv.Paid) == 0,
                "invoice is fully paid with zero outstanding");
            var receipt = await db.Receipts.SingleAsync(r => r.InvoiceId == invoiceId);
            Assert(receipt.Amount == 2_350_000 && receipt.Source == "Manual" && receipt.IdempotencyKey == idempotencyKey,
                "payment receipt created with Source = 'Manual'");
        }

        // 2. Idempotent replay: sending identical receipt request returns existing receipt without double credit
        var replayResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/receipts",
            new ReceiptRequest(2_350_000, "Thu tien mat dot 1", idempotencyKey));
        Assert(replayResp.StatusCode == HttpStatusCode.OK, "receipt retry is idempotent (200 OK)");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.Receipts.CountAsync(r => r.InvoiceId == invoiceId) == 1,
                "no duplicate receipt created on replay");
            var inv = await db.Invoices.FindAsync(invoiceId);
            Assert(inv!.Paid == 2_350_000, "invoice paid amount unchanged by replay");
        }

        // A settled invoice cannot receive another receipt.
        var overpayResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/receipts",
            new ReceiptRequest(2_000_000, "Thu qua so tien", "overpay-key", null));
        Assert(overpayResp.StatusCode == HttpStatusCode.Conflict,
            "cash overpayment is prevented with 409 Conflict");
    }

    private static async Task ClosedXmlExcelReportExportAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreateTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", OwnerId.ToString());

        // Setup data with potential formula injection string
        const string formulaAttackString = "=SUM(1+2)*CMD|' /C calc'!A0";
        var (roomId, occupancyId) = await SetupRoomAndOccupancyAsync(client, "Export Plaza", "Room XP-1",
            rent: 2_000_000, elecRate: 3_000, waterFee: 50_000, waterPerPerson: false, serviceFee: 0,
            initialReading: 0, tenantName: formulaAttackString);

        // Record a reading and a payment receipt
        await client.PostAsJsonAsync("/api/readings", new ReadingRequest(roomId, "2026-08", 100));

        Guid invoiceId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inv = await db.Invoices.SingleAsync(i => i.OccupancyId == occupancyId);
            invoiceId = inv.Id;
        }

        var recResp = await client.PostAsJsonAsync($"/api/invoices/{invoiceId}/receipts",
            new ReceiptRequest(2_350_000, "+cmd|' /C calc'!A1", "export-receipt-idem"));
        Assert(recResp.StatusCode == HttpStatusCode.OK, "receipt for export test created with 200 OK");

        // 1. Receipts export: GET /api/reports/receipts/export.xlsx
        var receiptsExportResp = await client.GetAsync("/api/reports/receipts/export.xlsx");
        Assert(receiptsExportResp.StatusCode == HttpStatusCode.OK, "receipts export returns 200 OK");
        Assert(receiptsExportResp.Content.Headers.ContentType?.MediaType ==
               "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "receipts export content type is valid OpenXML spreadsheet");

        var receiptsBytes = await receiptsExportResp.Content.ReadAsByteArrayAsync();
        using (var wb = new XLWorkbook(new MemoryStream(receiptsBytes)))
        {
            Assert(wb.Worksheets.Count == 2, "receipts workbook contains exactly 2 worksheets");
            Assert(wb.Worksheets.Contains("Tổng hợp") && wb.Worksheets.Contains("Chi tiết"),
                "worksheets named 'Tổng hợp' and 'Chi tiết'");

            var summary = wb.Worksheet("Tổng hợp");
            Assert(summary.Cell(1, 1).GetString() == "Thu tiền", "summary sheet has title 'Thu tiền'");

            var details = wb.Worksheet("Chi tiết");
            var lastRow = details.LastRowUsed()?.RowNumber() ?? 0;
            Assert(lastRow >= 2, "details sheet contains header row and data rows");

            // Verify typed numeric amount in column 2 ("Số tiền")
            var amountCell = details.Cell(2, 2);
            Assert(amountCell.DataType == XLDataType.Number && amountCell.GetValue<decimal>() == 2_350_000,
                "amount cell is typed numeric with value 2,350,000");

            // Verify formula injection sanitization in Note column and TenantName column (quotePrefix)
            var noteCell = details.Cell(2, 4);
            Assert(noteCell.Style.IncludeQuotePrefix || noteCell.GetString().StartsWith("'"),
                "formula injection character '+' in note is sanitized via quotePrefix");
            Assert(noteCell.Style.NumberFormat.Format == "@", "note cell format is plain text (@)");

            var tenantCell = details.Cell(2, 13);
            Assert(tenantCell.Style.IncludeQuotePrefix || tenantCell.GetString().StartsWith("'"),
                "formula injection character '=' in tenant name is sanitized via quotePrefix");
            Assert(tenantCell.Style.NumberFormat.Format == "@", "tenant cell format is plain text (@)");
        }

        // A separate unpaid charge remains in the debt report after the original invoice is settled.
        var debtCharge = await client.PostAsJsonAsync($"/api/rooms/{roomId}/supplemental-invoices",
            new SupplementalInvoiceRequest(Guid.NewGuid(), occupancyId, "Export debt fixture", 500_000, VietnamCalendar.TodayVietnam()));
        Assert(debtCharge.StatusCode == HttpStatusCode.Created, "unpaid debt export fixture created");

        // 2. Debt export: GET /api/reports/debt/export.xlsx
        var debtExportResp = await client.GetAsync("/api/reports/debt/export.xlsx");
        Assert(debtExportResp.StatusCode == HttpStatusCode.OK, "debt export returns 200 OK");
        var debtBytes = await debtExportResp.Content.ReadAsByteArrayAsync();
        using (var wb = new XLWorkbook(new MemoryStream(debtBytes)))
        {
            Assert(wb.Worksheets.Contains("Tổng hợp") && wb.Worksheets.Contains("Chi tiết"),
                "debt workbook contains 'Tổng hợp' and 'Chi tiết'");
            var details = wb.Worksheet("Chi tiết");
            var outstandingCell = details.Cell(2, 7); // "Còn nợ hiện tại"
            Assert(outstandingCell.DataType == XLDataType.Number, "debt outstanding cell is typed numeric");
        }

        // 3. Deposits export: GET /api/reports/deposits/export.xlsx
        var depositsExportResp = await client.GetAsync("/api/reports/deposits/export.xlsx");
        Assert(depositsExportResp.StatusCode == HttpStatusCode.OK, "deposits export returns 200 OK");
        var depositsBytes = await depositsExportResp.Content.ReadAsByteArrayAsync();
        using (var wb = new XLWorkbook(new MemoryStream(depositsBytes)))
        {
            Assert(wb.Worksheets.Contains("Tổng hợp") && wb.Worksheets.Contains("Chi tiết"),
                "deposits workbook contains 'Tổng hợp' and 'Chi tiết'");
        }
    }

    private static async Task<(Guid RoomId, Guid OccupancyId)> SetupRoomAndOccupancyAsync(
        HttpClient client, string bldgName, string roomName, decimal rent, decimal elecRate,
        decimal waterFee, bool waterPerPerson, decimal serviceFee, decimal initialReading,
        int people = 1, string tenantName = "Test Tenant")
    {
        var bldgResp = await client.PostAsJsonAsync("/api/buildings", new BuildingRequest(bldgName, "Address"));
        var bldg = await bldgResp.Content.ReadFromJsonAsync<BuildingDto>();

        var roomResp = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(
            bldg!.Id, roomName, rent, elecRate, waterFee, waterPerPerson, serviceFee, initialReading));
        var roomJson = await roomResp.Content.ReadFromJsonAsync<JsonElement>();
        var roomId = roomJson.GetProperty("id").GetGuid();

        var occResp = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest(
            tenantName, "0911223344", people, new DateOnly(2026, 8, 1), 0));
        var occJson = await occResp.Content.ReadFromJsonAsync<JsonElement>();
        var occupancyId = occJson.GetProperty("occupancy").GetProperty("id").GetGuid();

        return (roomId, occupancyId);
    }

    private sealed class QueueOnlyGateway : IZaloGateway
    {
        public bool Enabled => true;
        public Task<ZaloConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct) => Task.FromResult(new ZaloConnection("Disconnected"));
        public Task<ZaloSendResult> SendAsync(Guid ownerId, ZaloSendRequest request, CancellationToken ct) => throw new Exception("No provider calls in automatic enqueue tests");
    }

    private static WebApplication CreateTestApp(string connectionString, bool autoZalo = false, Action<string>? commandLog = null)
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

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            if (commandLog is not null) options.LogTo(commandLog, [RelationalEventId.CommandExecuted]);
        });
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        builder.Services.AddSingleton<TenantTokenService>();
        if (autoZalo)
        {
            builder.Services.AddSingleton<IZaloGateway, QueueOnlyGateway>();
            builder.Services.AddScoped<ZaloReminderService>();
        }
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddScoped<PayOsService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
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
            db.Users.Add(new AppUser { Id = OwnerId, Name = "Owner", Email = "owner@accounting.invalid", PasswordHash = "unused", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Pro });
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
        app.MapReportsEndpoints();
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
        public const string SchemeName = "FixtureAuthAccounting";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var owner = Request.Headers["X-Fixture-Owner"].FirstOrDefault() ?? OwnerId.ToString();
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, owner),
                new Claim(ClaimTypes.Role, "Owner")
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

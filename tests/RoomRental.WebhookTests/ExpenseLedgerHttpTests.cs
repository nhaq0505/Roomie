using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

/// <summary>Expense ledger, attachments, recurring expenses, profit/loss and EVN reconciliation over PostgreSQL QA.</summary>
internal static class ExpenseLedgerHttpTests
{
    private static readonly Guid ProOwner = Guid.NewGuid();
    private static readonly Guid FreeOwner = Guid.NewGuid();
    private static readonly Guid OtherOwner = Guid.NewGuid();
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    public static async Task RunAsync()
    {
        UnitChecks();
        await ExpenseCrudValidationAndIsolationAsync();
        await ConcurrentIdempotentCreateAsync();
        await AttachmentRulesAsync();
        await RecurringGenerationAsync();
        await ProfitLossAsync();
        await ElectricityReconciliationAsync();
    }

    private static void UnitChecks()
    {
        Assert(ExpenseEndpointsProbe.Detect(PngBytes) == "image/png", "magic bytes detect PNG");
        Assert(ExpenseEndpointsProbe.Detect([0xFF, 0xD8, 0xFF, 0xE0]) == "image/jpeg", "magic bytes detect JPEG");
        Assert(ExpenseEndpointsProbe.Detect(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")) == "image/webp", "magic bytes detect WebP");
        Assert(ExpenseEndpointsProbe.Detect(PdfBytes) == "application/pdf", "magic bytes detect PDF");
        Assert(ExpenseEndpointsProbe.Detect(Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")) is null, "SVG is rejected");
        Assert(ExpenseEndpointsProbe.Detect(Encoding.ASCII.GetBytes("<html><script>alert(1)</script>")) is null, "HTML is rejected");
        Assert(ExpenseEndpointsProbe.Sanitize("../../etc/passwd.jpg", ".png") == "passwd.png", "file name drops path segments and uses detected extension");
        Assert(ExpenseEndpointsProbe.Sanitize("<script>.pdf", ".pdf") == "_script_.pdf", "file name replaces markup characters");
        Assert(ExpenseEndpointsProbe.Sanitize("   ", ".jpg") == "chung-tu.jpg", "empty file name falls back to a default");
        Assert(ExpenseEndpointsProbe.Sanitize(new string('a', 300), ".jpg").Length == 120, "file name is capped at 120 characters");

        var template = new RecurringExpense { Category = "Internet", Amount = 1, DayOfMonth = 10, StartPeriod = "2026-01" };
        var due = RecurringExpenseGenerator.DuePeriods(template, new DateOnly(2026, 10, 9)).Select(row => row.Period).ToList();
        Assert(due.SequenceEqual(["2026-08", "2026-09"]), "backfill is limited to two months and skips a not-yet-due current month");
        due = RecurringExpenseGenerator.DuePeriods(template, new DateOnly(2026, 10, 10)).Select(row => row.Period).ToList();
        Assert(due.SequenceEqual(["2026-08", "2026-09", "2026-10"]), "current month is due on its day of month");
        template.EndPeriod = "2026-08";
        due = RecurringExpenseGenerator.DuePeriods(template, new DateOnly(2026, 10, 10)).Select(row => row.Period).ToList();
        Assert(due.SequenceEqual(["2026-08"]), "end period stops generation");
    }

    private static async Task ExpenseCrudValidationAndIsolationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        await using var app = CreateTestApp(fixture.ConnectionString, new MutableClock(DateTimeOffset.UtcNow));
        await app.StartAsync();
        using var pro = Client(app, ProOwner);
        using var free = Client(app, FreeOwner);
        using var other = Client(app, OtherOwner);
        var today = VietnamCalendar.TodayVietnam();
        var (buildingId, roomId, _) = await SetupRoomAsync(pro, "Ledger", "L-101");
        var otherBuildingId = await CreateBuildingAsync(pro, "Second");

        var create = Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 350_000, "Thay vòi nước", buildingId, roomId), "exp-1");
        var created = await create;
        Assert(created.StatusCode == HttpStatusCode.Created, "create expense returns 201");
        var expense = (await created.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert(expense.BuildingName == "Ledger" && expense.RoomName == "L-101" && expense.Version == 1, "expense DTO carries live names and version 1");

        var replay = await Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 350_000, "Thay vòi nước", buildingId, roomId), "exp-1");
        Assert(replay.StatusCode == HttpStatusCode.OK && (await replay.Content.ReadFromJsonAsync<ExpenseDto>())!.Id == expense.Id,
            "same idempotency key and body replays the original expense");
        var conflict = await Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 360_000, "Thay vòi nước", buildingId, roomId), "exp-1");
        Assert(conflict.StatusCode == HttpStatusCode.Conflict, "same idempotency key with a different body is rejected");

        foreach (var (request, name) in new (ExpenseRequest, string)[]
        {
            (new(today, "Foo", 1000, null, null, null), "unknown category"),
            (new(today, "Other", 0, null, null, null), "zero amount"),
            (new(today, "Other", 1000.5m, null, null, null), "fractional VND amount"),
            (new(today, "Other", 10_000_000_001m, null, null, null), "amount above 10 billion"),
            (new(today.AddDays(1), "Other", 1000, null, null, null), "future Vietnam date"),
            (new(today, "Other", 1000, new string('x', 501), null, null), "description above 500 characters"),
            (new(today, "Other", 1000, null, otherBuildingId, roomId), "room outside selected building"),
        })
        {
            var response = await Post(pro, "/api/expenses", request);
            Assert(response.StatusCode == HttpStatusCode.BadRequest, $"validation rejects {name}");
        }

        await Post(pro, "/api/expenses", new ExpenseRequest(today, "Internet", 200_000, null, buildingId, null));
        await Post(pro, "/api/expenses", new ExpenseRequest(today, "Tax", 1_000_000, null, null, null));
        var list = (await pro.GetFromJsonAsync<ExpenseListDto>($"/api/expenses?dateFrom={today:yyyy-MM-dd}&dateTo={today:yyyy-MM-dd}&pageSize=1"))!;
        Assert(list.TotalCount == 3 && list.Items.Count == 1, "list paginates while reporting the full count");
        Assert(list.Totals.Total == 1_550_000 && list.Totals.Count == 3, "list totals cover the whole filtered set, not just the page");
        Assert(list.Totals.ByCategory.First().Category == "Tax" && list.Totals.ByCategory.Count == 3, "category totals are sorted by amount");
        var byBuilding = (await pro.GetFromJsonAsync<ExpenseListDto>($"/api/expenses?buildingId={buildingId}"))!;
        Assert(byBuilding.TotalCount == 2 && byBuilding.Totals.Total == 550_000, "building filter excludes shared expenses");
        var byCategory = (await pro.GetFromJsonAsync<ExpenseListDto>("/api/expenses?category=Tax"))!;
        Assert(byCategory.TotalCount == 1, "category filter applies");
        var empty = (await pro.GetFromJsonAsync<ExpenseListDto>("/api/expenses?dateFrom=2001-01-01&dateTo=2001-01-31"))!;
        Assert(empty.TotalCount == 0 && empty.Totals.Total == 0 && empty.Totals.ByCategory.Count == 0, "empty set totals are zero");

        var noVersion = await pro.PutAsJsonAsync($"/api/expenses/{expense.Id}", new ExpenseRequest(today, "Repair", 400_000, null, buildingId, roomId));
        Assert(noVersion.StatusCode == HttpStatusCode.BadRequest, "update requires expectedVersion");
        var stale = await pro.PutAsJsonAsync($"/api/expenses/{expense.Id}", new ExpenseRequest(today, "Repair", 400_000, null, buildingId, roomId, ExpectedVersion: 9));
        Assert(stale.StatusCode == HttpStatusCode.Conflict, "stale update returns 409");
        var updated = await pro.PutAsJsonAsync($"/api/expenses/{expense.Id}", new ExpenseRequest(today, "Repair", 400_000, "Đã sửa", buildingId, roomId, ExpectedVersion: 1));
        var updatedDto = (await updated.Content.ReadFromJsonAsync<ExpenseDto>())!;
        Assert(updated.StatusCode == HttpStatusCode.OK && updatedDto.Version == 2 && updatedDto.Amount == 400_000, "update increments version");

        Assert((await other.GetAsync($"/api/expenses/{expense.Id}")).StatusCode == HttpStatusCode.NotFound, "another owner cannot read the expense");
        Assert((await other.PutAsJsonAsync($"/api/expenses/{expense.Id}", new ExpenseRequest(today, "Repair", 1, null, null, null, ExpectedVersion: 2))).StatusCode == HttpStatusCode.NotFound,
            "another owner cannot update the expense");
        Assert((await other.DeleteAsync($"/api/expenses/{expense.Id}?expectedVersion=2")).StatusCode == HttpStatusCode.NotFound, "another owner cannot delete the expense");
        Assert((await Post(other, "/api/expenses", new ExpenseRequest(today, "Repair", 1, null, buildingId, null))).StatusCode == HttpStatusCode.NotFound,
            "another owner cannot attach an expense to a foreign building");
        Assert((await other.GetFromJsonAsync<ExpenseListDto>("/api/expenses"))!.TotalCount == 0, "another owner's list is empty");

        // Utility (EVN) fields.
        var (freeBuilding, _, _) = await SetupRoomAsync(free, "Free", "F-1");
        Assert((await Post(free, "/api/expenses", new ExpenseRequest(today, "Electricity", 900_000, null, freeBuilding, null, "2026-08", 300))).StatusCode == HttpStatusCode.Forbidden,
            "EVN reconciliation fields require Pro");
        Assert((await Post(free, "/api/expenses", new ExpenseRequest(today, "Electricity", 900_000, null, freeBuilding, null))).StatusCode == HttpStatusCode.Created,
            "Free owners can record plain expenses");
        Assert((await Post(pro, "/api/expenses", new ExpenseRequest(today, "Water", 1, null, buildingId, null, "2026-08", 1))).StatusCode == HttpStatusCode.BadRequest,
            "EVN fields only apply to electricity");
        Assert((await Post(pro, "/api/expenses", new ExpenseRequest(today, "Electricity", 1, null, null, null, "2026-08", 1))).StatusCode == HttpStatusCode.BadRequest,
            "EVN fields need a building");
        Assert((await Post(pro, "/api/expenses", new ExpenseRequest(today, "Electricity", 1, null, buildingId, null, "2026-08", null))).StatusCode == HttpStatusCode.BadRequest,
            "EVN period and kWh are entered together");
        Assert((await Post(pro, "/api/expenses", new ExpenseRequest(today, "Electricity", 1, null, buildingId, null, "2026-08", 300.5m))).StatusCode == HttpStatusCode.Created,
            "valid EVN bill is accepted");

        // Archived links: new links are blocked, unchanged links stay editable.
        var archivedLinked = (await (await Post(pro, "/api/expenses", new ExpenseRequest(today, "Cleaning", 50_000, null, otherBuildingId, null))).Content.ReadFromJsonAsync<ExpenseDto>())!;
        var archiveResponse = await pro.DeleteAsync($"/api/buildings/{otherBuildingId}");
        Assert(archiveResponse.IsSuccessStatusCode, "fixture building archived");
        Assert((await Post(pro, "/api/expenses", new ExpenseRequest(today, "Cleaning", 1, null, otherBuildingId, null))).StatusCode == HttpStatusCode.Conflict,
            "new expenses cannot link an archived building");
        var keepLink = await pro.PutAsJsonAsync($"/api/expenses/{archivedLinked.Id}",
            new ExpenseRequest(today, "Cleaning", 60_000, null, otherBuildingId, null, ExpectedVersion: 1));
        Assert(keepLink.StatusCode == HttpStatusCode.OK, "existing expense on an archived building stays editable");

        Assert((await pro.DeleteAsync($"/api/expenses/{expense.Id}")).StatusCode == HttpStatusCode.BadRequest, "delete requires expectedVersion");
        Assert((await pro.DeleteAsync($"/api/expenses/{expense.Id}?expectedVersion=1")).StatusCode == HttpStatusCode.Conflict, "stale delete returns 409");
        Assert((await pro.DeleteAsync($"/api/expenses/{expense.Id}?expectedVersion=2")).StatusCode == HttpStatusCode.OK, "delete succeeds with current version");
        Assert((await pro.GetAsync($"/api/expenses/{expense.Id}")).StatusCode == HttpStatusCode.NotFound, "deleted expense is hidden");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.Expenses.AnyAsync(row => row.Id == expense.Id && row.DeletedAt != null), "delete is a soft delete");
        }
        var deletedReplay = await Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 350_000, "Thay vòi nước", buildingId, roomId), "exp-1");
        Assert(deletedReplay.StatusCode == HttpStatusCode.Conflict, "replaying a key of a deleted expense does not resurrect it");
    }

    private static async Task ConcurrentIdempotentCreateAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        await using var app = CreateTestApp(fixture.ConnectionString, new MutableClock(DateTimeOffset.UtcNow));
        await app.StartAsync();
        var today = VietnamCalendar.TodayVietnam();
        var request = new ExpenseRequest(today, "Salary", 5_000_000, "Lương tháng", null, null);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
        {
            var client = Client(app, ProOwner);
            return Post(client, "/api/expenses", request, "concurrent-key");
        }));
        Assert(responses.All(response => response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict),
            "concurrent duplicate submissions resolve without server errors");
        Assert(responses.Count(response => response.StatusCode == HttpStatusCode.Created) == 1, "exactly one concurrent submission creates the expense");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert(await db.Expenses.CountAsync(row => row.OwnerId == ProOwner) == 1, "concurrent idempotent submissions persist one expense");
    }

    private static async Task AttachmentRulesAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        await using var app = CreateTestApp(fixture.ConnectionString, new MutableClock(DateTimeOffset.UtcNow));
        await app.StartAsync();
        using var pro = Client(app, ProOwner);
        using var free = Client(app, FreeOwner);
        using var other = Client(app, OtherOwner);
        var today = VietnamCalendar.TodayVietnam();
        var expense = (await (await Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 100_000, null, null, null))).Content.ReadFromJsonAsync<ExpenseDto>())!;
        var freeExpense = (await (await Post(free, "/api/expenses", new ExpenseRequest(today, "Repair", 100_000, null, null, null))).Content.ReadFromJsonAsync<ExpenseDto>())!;

        Assert((await Upload(free, freeExpense.Id, PngBytes, "a.png")).StatusCode == HttpStatusCode.Forbidden, "Free owners cannot upload attachments");
        var uploaded = await Upload(pro, expense.Id, PngBytes, "hoa-don.png");
        Assert(uploaded.StatusCode == HttpStatusCode.Created, "Pro owner uploads a PNG");
        var attachment = (await uploaded.Content.ReadFromJsonAsync<ExpenseAttachmentDto>())!;
        Assert(attachment.ContentType == "image/png" && attachment.SizeBytes == PngBytes.Length, "attachment metadata uses the detected type");

        var download = await pro.GetAsync($"/api/expenses/{expense.Id}/attachments/{attachment.Id}");
        Assert(download.StatusCode == HttpStatusCode.OK && (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(PngBytes), "owner downloads identical bytes");
        Assert(download.Content.Headers.ContentType?.MediaType == "image/png", "download content type is the detected type");
        Assert(download.Content.Headers.ContentDisposition?.DispositionType == "inline", "images are served inline");
        Assert(download.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) && nosniff.Contains("nosniff"), "download sets nosniff");
        Assert(download.Headers.CacheControl?.NoStore == true && download.Headers.CacheControl.Private, "download is private and not stored");
        var pdf = (await (await Upload(pro, expense.Id, PdfBytes, "bien-lai.pdf")).Content.ReadFromJsonAsync<ExpenseAttachmentDto>())!;
        var pdfDownload = await pro.GetAsync($"/api/expenses/{expense.Id}/attachments/{pdf.Id}");
        Assert(pdfDownload.Content.Headers.ContentDisposition?.DispositionType == "attachment", "PDF is served as a download");

        Assert((await Upload(pro, expense.Id, Encoding.ASCII.GetBytes("<html><body>x</body></html>"), "fake.jpg", "image/jpeg")).StatusCode == HttpStatusCode.BadRequest,
            "HTML renamed to .jpg is rejected");
        Assert((await Upload(pro, expense.Id, Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "x.svg", "image/svg+xml")).StatusCode == HttpStatusCode.BadRequest,
            "SVG is rejected");
        var oversized = new byte[ExpenseLimits.MaxAttachmentBytes + 1];
        PngBytes.CopyTo(oversized, 0);
        Assert((await Upload(pro, expense.Id, oversized, "big.png")).StatusCode == HttpStatusCode.RequestEntityTooLarge, "files above 2 MB are rejected");

        for (var index = 0; index < 3; index++) await Upload(pro, expense.Id, PngBytes, $"p{index}.png");
        Assert((await Upload(pro, expense.Id, PngBytes, "sixth.png")).StatusCode == HttpStatusCode.Conflict, "a sixth attachment on one expense is rejected");
        var listed = (await pro.GetFromJsonAsync<List<ExpenseAttachmentDto>>($"/api/expenses/{expense.Id}/attachments"))!;
        Assert(listed.Count == 5, "attachment list returns five items");
        var dto = (await pro.GetFromJsonAsync<ExpenseDto>($"/api/expenses/{expense.Id}"))!;
        Assert(dto.AttachmentCount == 5, "expense DTO reports attachment count");

        Assert((await other.GetAsync($"/api/expenses/{expense.Id}/attachments/{attachment.Id}")).StatusCode == HttpStatusCode.NotFound,
            "another owner cannot download the attachment");
        Assert((await other.DeleteAsync($"/api/expenses/{expense.Id}/attachments/{attachment.Id}")).StatusCode == HttpStatusCode.NotFound,
            "another owner cannot delete the attachment");

        var second = (await (await Post(pro, "/api/expenses", new ExpenseRequest(today, "Repair", 1_000, null, null, null))).Content.ReadFromJsonAsync<ExpenseDto>())!;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExpenseAttachments.Add(new ExpenseAttachment
            {
                OwnerId = ProOwner, ExpenseId = second.Id, FileName = "quota.png", ContentType = "image/png",
                SizeBytes = ExpenseLimits.MaxAttachmentBytesPerOwner - 10, Sha256 = new string('0', 64), Content = [1]
            });
            await db.SaveChangesAsync();
        }
        Assert((await Upload(pro, second.Id, PngBytes, "over.png")).StatusCode == HttpStatusCode.Conflict, "owner quota of 200 MB is enforced");
        var usage = (await pro.GetFromJsonAsync<ExpenseAttachmentUsageDto>("/api/expenses/attachments/usage"))!;
        Assert(usage.LimitBytes == ExpenseLimits.MaxAttachmentBytesPerOwner && usage.UsedBytes > usage.LimitBytes - 100, "usage endpoint reports quota");

        Assert((await pro.DeleteAsync($"/api/expenses/{expense.Id}/attachments/{attachment.Id}")).StatusCode == HttpStatusCode.OK, "owner deletes an attachment");
        Assert((await pro.GetFromJsonAsync<List<ExpenseAttachmentDto>>($"/api/expenses/{expense.Id}/attachments"))!.Count == 4, "deleted attachment disappears");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.SingleAsync(row => row.Id == ProOwner);
            owner.SubscriptionTier = SubscriptionTier.Free;
            await db.SaveChangesAsync();
        }
        Assert((await pro.GetAsync($"/api/expenses/{expense.Id}/attachments/{pdf.Id}")).StatusCode == HttpStatusCode.OK, "attachments stay downloadable after Pro expires");
        Assert((await Upload(pro, expense.Id, PngBytes, "after.png")).StatusCode == HttpStatusCode.Forbidden, "uploads stop after Pro expires");

        var deleted = await pro.DeleteAsync($"/api/expenses/{expense.Id}?expectedVersion=1");
        Assert(deleted.StatusCode == HttpStatusCode.OK, "expense with attachments can be deleted");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(!await db.ExpenseAttachments.AnyAsync(row => row.ExpenseId == expense.Id), "deleting an expense frees its attachments");
        }
        Assert((await pro.GetAsync($"/api/expenses/{expense.Id}/attachments/{pdf.Id}")).StatusCode == HttpStatusCode.NotFound,
            "attachments of a deleted expense are not served");
    }

    private static async Task RecurringGenerationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        // 10:00 on 15/10/2026 in Vietnam.
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero));
        await using var app = CreateTestApp(fixture.ConnectionString, clock);
        await app.StartAsync();
        using var pro = Client(app, ProOwner);
        using var free = Client(app, FreeOwner);
        var (buildingId, _, _) = await SetupRoomAsync(pro, "Recurring", "R-1");
        var archivedBuilding = await CreateBuildingAsync(pro, "Recurring archived");

        Assert((await Post(free, "/api/recurring-expenses", new RecurringExpenseRequest("Internet", 250_000, null, null, null, 5, "2026-10", null))).StatusCode == HttpStatusCode.Forbidden,
            "Free owners cannot create recurring expenses");
        Assert((await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Internet", 250_000, null, null, null, 29, "2026-10", null))).StatusCode == HttpStatusCode.BadRequest,
            "day of month above 28 is rejected");
        Assert((await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Internet", 250_000, null, null, null, 5, "2026-10", "2026-09"))).StatusCode == HttpStatusCode.BadRequest,
            "end period before start period is rejected");

        var internetResponse = await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Internet", 250_000, "FPT", buildingId, null, 10, "2026-07", null), "rec-1");
        Assert(internetResponse.StatusCode == HttpStatusCode.Created, "recurring template is created");
        var internet = (await internetResponse.Content.ReadFromJsonAsync<RecurringExpenseDto>())!;
        Assert(internet.LastGeneratedPeriod == "2026-10" && internet.SkipReason is null, "creation immediately generates due months");
        var replay = await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Internet", 250_000, "FPT", buildingId, null, 10, "2026-07", null), "rec-1");
        Assert(replay.StatusCode == HttpStatusCode.OK, "recurring create is idempotent");
        var generated = await GeneratedAsync(app, internet.Id);
        Assert(generated.SequenceEqual(["2026-08", "2026-09", "2026-10"]), "backfill is limited to the last two months");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var october = await db.Expenses.SingleAsync(row => row.RecurringExpenseId == internet.Id && row.RecurrencePeriod == "2026-10");
            Assert(october.ExpenseDate == new DateOnly(2026, 10, 10) && october.Amount == 250_000 && october.BuildingId == buildingId,
                "generated expense copies template values and the day of month");
        }

        var salary = (await (await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Salary", 3_000_000, null, null, null, 20, "2026-10", null))).Content.ReadFromJsonAsync<RecurringExpenseDto>())!;
        Assert((await GeneratedAsync(app, salary.Id)).Count == 0, "a template is not generated before its day of month");
        clock.Now = new DateTimeOffset(2026, 10, 19, 17, 0, 0, TimeSpan.Zero); // 00:00 on 20/10 in Vietnam
        var run = await GenerateAllAsync(app);
        Assert(run.Created == 1 && (await GeneratedAsync(app, salary.Id)).SequenceEqual(["2026-10"]), "worker generates on the Vietnam day of month");
        Assert((await GenerateAllAsync(app)).Created == 0, "rerunning the worker creates nothing");
        var parallel = await Task.WhenAll(GenerateAllAsync(app), GenerateAllAsync(app));
        Assert(parallel.Sum(result => result.Created) == 0 && parallel.All(result => result.Failed == 0), "overlapping runs never duplicate");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var september = await db.Expenses.SingleAsync(row => row.RecurringExpenseId == internet.Id && row.RecurrencePeriod == "2026-09");
            var response = await pro.DeleteAsync($"/api/expenses/{september.Id}?expectedVersion={september.Version}");
            Assert(response.StatusCode == HttpStatusCode.OK, "a generated expense can be deleted");
        }
        await GenerateAllAsync(app);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.Expenses.CountAsync(row => row.RecurringExpenseId == internet.Id && row.RecurrencePeriod == "2026-09") == 1,
                "a deleted generated expense is never recreated");
        }

        var paused = await pro.PostAsJsonAsync($"/api/recurring-expenses/{salary.Id}/pause", new RecurringExpenseStateRequest(salary.Version));
        var pausedDto = (await paused.Content.ReadFromJsonAsync<RecurringExpenseDto>())!;
        Assert(paused.StatusCode == HttpStatusCode.OK && pausedDto.IsPaused && pausedDto.SkipReason == RecurringExpenseGenerator.SkipPaused, "template can be paused");
        clock.Now = new DateTimeOffset(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);
        await GenerateAllAsync(app);
        Assert((await GeneratedAsync(app, salary.Id)).SequenceEqual(["2026-10"]), "paused templates do not generate");
        Assert((await GeneratedAsync(app, internet.Id)).Contains("2026-11"), "active templates continue the next month");
        var resumed = await pro.PostAsJsonAsync($"/api/recurring-expenses/{salary.Id}/resume", new RecurringExpenseStateRequest(pausedDto.Version));
        Assert(resumed.StatusCode == HttpStatusCode.OK && (await GeneratedAsync(app, salary.Id)).SequenceEqual(["2026-10", "2026-11"]),
            "resume immediately generates the due month");

        var stale = await pro.PutAsJsonAsync($"/api/recurring-expenses/{internet.Id}",
            new RecurringExpenseRequest("Internet", 300_000, null, buildingId, null, 10, "2026-07", null, ExpectedVersion: 99));
        Assert(stale.StatusCode == HttpStatusCode.Conflict, "stale recurring update returns 409");
        var edited = await pro.PutAsJsonAsync($"/api/recurring-expenses/{internet.Id}",
            new RecurringExpenseRequest("Internet", 300_000, null, buildingId, null, 10, "2026-07", "2026-11", ExpectedVersion: internet.Version));
        Assert(edited.StatusCode == HttpStatusCode.OK, "recurring template can be edited");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert((await db.Expenses.SingleAsync(row => row.RecurringExpenseId == internet.Id && row.RecurrencePeriod == "2026-10")).Amount == 250_000,
                "editing a template does not rewrite generated expenses");
        }
        clock.Now = new DateTimeOffset(2026, 12, 15, 3, 0, 0, TimeSpan.Zero);
        await GenerateAllAsync(app);
        Assert(!(await GeneratedAsync(app, internet.Id)).Contains("2026-12"), "end period stops generation");

        var archivedTemplate = (await (await Post(pro, "/api/recurring-expenses", new RecurringExpenseRequest("Cleaning", 100_000, null, archivedBuilding, null, 20, "2026-12", null))).Content.ReadFromJsonAsync<RecurringExpenseDto>())!;
        Assert((await pro.DeleteAsync($"/api/buildings/{archivedBuilding}")).IsSuccessStatusCode, "fixture building archived");
        clock.Now = new DateTimeOffset(2026, 12, 21, 3, 0, 0, TimeSpan.Zero);
        await GenerateAllAsync(app);
        Assert((await GeneratedAsync(app, archivedTemplate.Id)).Count == 0, "templates on archived buildings are skipped");
        var templates = (await pro.GetFromJsonAsync<List<RecurringExpenseDto>>("/api/recurring-expenses"))!;
        Assert(templates.Single(row => row.Id == archivedTemplate.Id).SkipReason == RecurringExpenseGenerator.SkipLocationArchived, "archived template reports its skip reason");
        Assert(templates.Single(row => row.Id == internet.Id).SkipReason == RecurringExpenseGenerator.SkipEnded, "ended template reports its skip reason");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.SingleAsync(row => row.Id == ProOwner);
            owner.SubscriptionTier = SubscriptionTier.Free;
            await db.SaveChangesAsync();
        }
        clock.Now = new DateTimeOffset(2027, 1, 21, 3, 0, 0, TimeSpan.Zero);
        Assert((await GenerateAllAsync(app)).Created == 0, "no generation after Pro expires");
        templates = (await pro.GetFromJsonAsync<List<RecurringExpenseDto>>("/api/recurring-expenses"))!;
        Assert(templates.Single(row => row.Id == salary.Id).SkipReason == RecurringExpenseGenerator.SkipProRequired, "template reports Pro requirement");
        var templateCount = templates.Count;
        var currentSalary = templates.Single(row => row.Id == salary.Id);
        Assert((await pro.DeleteAsync($"/api/recurring-expenses/{salary.Id}?expectedVersion={currentSalary.Version}")).StatusCode == HttpStatusCode.OK,
            "templates can be deleted without Pro");
        Assert((await pro.GetFromJsonAsync<List<RecurringExpenseDto>>("/api/recurring-expenses"))!.Count == templateCount - 1, "deleted template disappears");
    }

    private static async Task ProfitLossAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        await using var app = CreateTestApp(fixture.ConnectionString, new MutableClock(DateTimeOffset.UtcNow));
        await app.StartAsync();
        using var pro = Client(app, ProOwner);
        using var free = Client(app, FreeOwner);
        const string range = "dateFrom=2026-08-01&dateTo=2026-10-31";
        Assert((await free.GetAsync($"/api/reports/profit-loss?{range}")).StatusCode == HttpStatusCode.Forbidden, "profit/loss requires Pro");
        Assert((await free.GetAsync($"/api/reports/profit-loss/export.xlsx?{range}")).StatusCode == HttpStatusCode.Forbidden, "profit/loss export requires Pro");

        var (buildingId, roomId, occupancyId) = await SetupRoomAsync(pro, "PL Plaza", "PL-1");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Invoice NewInvoice(string kind, string period, decimal total, Guid owner) => new()
            {
                OwnerId = owner, BuildingId = buildingId, RoomId = roomId, OccupancyId = occupancyId, BuildingName = "PL Plaza",
                RoomName = "PL-1", TenantName = "Tenant", Kind = kind, Period = period, Total = total, Paid = total,
                LifecycleState = InvoiceLifecycleState.Issued, Status = InvoiceStatus.Paid
            };
            var charge = NewInvoice("Supplemental", "2026-08", 2_000_000, ProOwner);
            var deposit = NewInvoice("Deposit", "2026-08", 3_000_000, ProOwner);
            var foreign = NewInvoice("Supplemental", "2026-08", 9_000_000, OtherOwner);
            db.Invoices.AddRange(charge, deposit, foreign);
            await db.SaveChangesAsync();
            db.Receipts.AddRange(
                // 23:30 on 31/08 in Vietnam → August.
                new PaymentReceipt { OwnerId = ProOwner, InvoiceId = charge.Id, Amount = 1_000_000, Source = "Manual", CreatedAt = new DateTimeOffset(2026, 8, 31, 16, 30, 0, TimeSpan.Zero) },
                // 00:30 on 01/09 in Vietnam → September.
                new PaymentReceipt { OwnerId = ProOwner, InvoiceId = charge.Id, Amount = 500_000, Source = "PayOS", CreatedAt = new DateTimeOffset(2026, 8, 31, 17, 30, 0, TimeSpan.Zero) },
                new PaymentReceipt { OwnerId = ProOwner, InvoiceId = charge.Id, Amount = 200_000, Source = "Deposit", CreatedAt = new DateTimeOffset(2026, 9, 5, 3, 0, 0, TimeSpan.Zero) },
                new PaymentReceipt { OwnerId = ProOwner, InvoiceId = deposit.Id, Amount = 3_000_000, Source = "Manual", CreatedAt = new DateTimeOffset(2026, 8, 10, 3, 0, 0, TimeSpan.Zero) },
                new PaymentReceipt { OwnerId = OtherOwner, InvoiceId = foreign.Id, Amount = 9_000_000, Source = "Manual", CreatedAt = new DateTimeOffset(2026, 8, 10, 3, 0, 0, TimeSpan.Zero) });
            db.Expenses.AddRange(
                new Expense { OwnerId = ProOwner, BuildingId = buildingId, Category = "Repair", Amount = 300_000, ExpenseDate = new DateOnly(2026, 8, 31), Description = "=HYPERLINK(\"http://evil\")" },
                new Expense { OwnerId = ProOwner, Category = "Tax", Amount = 100_000, ExpenseDate = new DateOnly(2026, 9, 1) },
                new Expense { OwnerId = ProOwner, Category = "Tax", Amount = 999_000, ExpenseDate = new DateOnly(2026, 9, 2), DeletedAt = DateTimeOffset.UtcNow },
                new Expense { OwnerId = ProOwner, Category = "Tax", Amount = 777_000, ExpenseDate = new DateOnly(2026, 11, 1) },
                new Expense { OwnerId = OtherOwner, Category = "Tax", Amount = 555_000, ExpenseDate = new DateOnly(2026, 9, 1) });
            await db.SaveChangesAsync();
        }

        var report = (await pro.GetFromJsonAsync<ProfitLossReportDto>($"/api/reports/profit-loss?{range}"))!;
        Assert(report.Months.Select(row => row.Month).SequenceEqual(["2026-08", "2026-09", "2026-10"]), "every month in range is present");
        Assert(report.Months[0].Income == 1_000_000 && report.Months[0].Expenses == 300_000 && report.Months[0].Net == 700_000,
            "August income uses the Vietnam receipt date and excludes deposits");
        Assert(report.Months[1].Income == 500_000 && report.Months[1].Expenses == 100_000, "September income/expenses respect Vietnam month boundaries");
        Assert(report.Months[2].Income == 0 && report.Months[2].Expenses == 0, "empty month is zero");
        Assert(report.Totals.Income == 1_500_000 && report.Totals.Expenses == 400_000 && report.Totals.Net == 1_100_000,
            "totals exclude deposit receipts, deposit credits, deleted and foreign rows");
        Assert(report.SharedExpensesExcluded == 0, "no shared exclusion without a building filter");
        Assert(report.ExpensesByCategory.Count == 2 && report.ExpensesByCategory[0].Category == "Repair", "expense categories are aggregated");

        var filtered = (await pro.GetFromJsonAsync<ProfitLossReportDto>($"/api/reports/profit-loss?{range}&buildingId={buildingId}"))!;
        Assert(filtered.Totals.Expenses == 300_000 && filtered.SharedExpensesExcluded == 100_000 && filtered.Totals.Income == 1_500_000,
            "building filter drops shared expenses and reports the excluded amount");
        var otherBuilding = (await pro.GetFromJsonAsync<ProfitLossReportDto>($"/api/reports/profit-loss?{range}&buildingId={Guid.NewGuid()}"))!;
        Assert(otherBuilding.Totals.Income == 0 && otherBuilding.Totals.Expenses == 0, "unknown building yields zero totals");

        Assert((await pro.GetAsync("/api/reports/profit-loss")).StatusCode == HttpStatusCode.BadRequest, "profit/loss requires a date range");
        Assert((await pro.GetAsync("/api/reports/profit-loss?dateFrom=2024-01-01&dateTo=2026-01-31")).StatusCode == HttpStatusCode.BadRequest,
            "profit/loss range is capped at 24 months");

        var export = await pro.GetAsync($"/api/reports/profit-loss/export.xlsx?{range}");
        Assert(export.StatusCode == HttpStatusCode.OK, "profit/loss export returns 200");
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        Assert(workbook.Worksheets.Contains("Tổng hợp") && workbook.Worksheets.Contains("Theo tháng") && workbook.Worksheets.Contains("Chi tiết chi phí"),
            "profit/loss workbook has summary, monthly and detail sheets");
        var monthly = workbook.Worksheet("Theo tháng");
        Assert(monthly.Cell(2, 2).DataType == XLDataType.Number && monthly.Cell(2, 2).GetValue<decimal>() == 1_000_000, "monthly income is numeric");
        var details = workbook.Worksheet("Chi tiết chi phí");
        Assert(details.Cell(2, 3).DataType == XLDataType.Number, "expense amount is numeric");
        var note = details.Cell(2, 6);
        Assert((note.Style.IncludeQuotePrefix || note.GetString().StartsWith('\'')) && note.Style.NumberFormat.Format == "@", "formula-like expense notes are neutralized");
    }

    private static async Task ElectricityReconciliationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync();
        await using var app = CreateTestApp(fixture.ConnectionString, new MutableClock(DateTimeOffset.UtcNow));
        await app.StartAsync();
        using var pro = Client(app, ProOwner);
        using var free = Client(app, FreeOwner);
        Assert((await free.GetAsync("/api/reports/electricity-reconciliation?periodFrom=2026-08&periodTo=2026-09")).StatusCode == HttpStatusCode.Forbidden,
            "electricity reconciliation requires Pro");
        var (buildingId, roomId, occupancyId) = await SetupRoomAsync(pro, "EVN House", "E-1");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Invoice NewInvoice(string period, decimal kwh, decimal amount, InvoiceLifecycleState state, string kind = "Supplemental") => new()
            {
                OwnerId = ProOwner, BuildingId = buildingId, RoomId = roomId, OccupancyId = occupancyId, BuildingName = "EVN House",
                RoomName = "E-1", TenantName = "Tenant", Kind = kind, Period = period, Total = amount, Paid = 0, Consumption = kwh,
                ElectricityAmount = amount, LifecycleState = state, Status = InvoiceStatus.Unpaid
            };
            db.Invoices.AddRange(
                NewInvoice("2026-08", 250, 875_000, InvoiceLifecycleState.Issued),
                NewInvoice("2026-08", 150, 525_000, InvoiceLifecycleState.Issued),
                NewInvoice("2026-08", 999, 9_999_000, InvoiceLifecycleState.Draft),
                NewInvoice("2026-08", 0, 3_000_000, InvoiceLifecycleState.Issued, "Deposit"),
                NewInvoice("2026-09", 120, 420_000, InvoiceLifecycleState.Issued));
            db.Expenses.AddRange(
                new Expense { OwnerId = ProOwner, BuildingId = buildingId, Category = "Electricity", Amount = 900_000, ExpenseDate = new DateOnly(2026, 8, 20), UtilityPeriod = "2026-08", UtilityKwh = 300 },
                new Expense { OwnerId = ProOwner, BuildingId = buildingId, Category = "Electricity", Amount = 600_000, ExpenseDate = new DateOnly(2026, 8, 21), UtilityPeriod = "2026-08", UtilityKwh = 200 },
                new Expense { OwnerId = ProOwner, BuildingId = buildingId, Category = "Electricity", Amount = 1, ExpenseDate = new DateOnly(2026, 8, 21), UtilityPeriod = "2026-08", UtilityKwh = 5000, DeletedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var report = (await pro.GetFromJsonAsync<ElectricityReconciliationDto>("/api/reports/electricity-reconciliation?periodFrom=2026-08&periodTo=2026-09"))!;
        Assert(report.Rows.Count == 2 && report.Rows[0].Period == "2026-09", "rows are listed per building and period, newest first");
        var august = report.Rows.Single(row => row.Period == "2026-08");
        Assert(august.EvnKwh == 500 && august.EvnAmount == 1_500_000 && august.EvnBillCount == 2, "multiple EVN meters are summed; deleted bills ignored");
        Assert(august.BilledKwh == 400 && august.BilledAmount == 1_400_000, "only issued non-deposit invoices count as billed electricity");
        Assert(august.LossKwh == 100 && august.LossPercent == 20m && august.Margin == -100_000 && august.DraftInvoiceCount == 1,
            "loss, margin and draft warning are computed");
        var september = report.Rows.Single(row => row.Period == "2026-09");
        Assert(september.EvnKwh is null && september.LossPercent is null && september.LossNullReason == "NoEvnBill",
            "missing EVN bill is reported as no data, not zero");
        Assert((await pro.GetAsync("/api/reports/electricity-reconciliation?periodFrom=2026-09&periodTo=2026-08")).StatusCode == HttpStatusCode.BadRequest,
            "reversed period range is rejected");
        Assert((await pro.GetAsync("/api/reports/electricity-reconciliation?periodFrom=2024-01&periodTo=2026-01")).StatusCode == HttpStatusCode.BadRequest,
            "period range is capped at 24 months");
        var export = await pro.GetAsync("/api/reports/electricity/export.xlsx?periodFrom=2026-08&periodTo=2026-09");
        Assert(export.StatusCode == HttpStatusCode.OK, "electricity export returns 200");
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Chi tiết");
        Assert(sheet.Cell(3, 3).DataType == XLDataType.Number && sheet.Cell(2, 3).IsEmpty(), "EVN kWh is numeric and blank when missing");
    }

    private static async Task<List<string>> GeneratedAsync(WebApplication app, Guid templateId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Expenses.Where(row => row.RecurringExpenseId == templateId).OrderBy(row => row.RecurrencePeriod)
            .Select(row => row.RecurrencePeriod!).ToListAsync();
    }

    private static async Task<RecurringGenerationResult> GenerateAllAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecurringExpenseGenerator>().GenerateAllAsync(default);
    }

    private static HttpClient Client(WebApplication app, Guid ownerId)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", ownerId.ToString());
        return client;
    }

    private static Task<HttpResponseMessage> Post<T>(HttpClient client, string url, T body, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) message.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(message);
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid expenseId, byte[] bytes, string fileName, string contentType = "application/octet-stream")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return client.PostAsync($"/api/expenses/{expenseId}/attachments", form);
    }

    private static async Task<Guid> CreateBuildingAsync(HttpClient client, string name) =>
        (await (await client.PostAsJsonAsync("/api/buildings", new BuildingRequest(name, "Address"))).Content.ReadFromJsonAsync<BuildingDto>())!.Id;

    private static async Task<(Guid BuildingId, Guid RoomId, Guid OccupancyId)> SetupRoomAsync(HttpClient client, string buildingName, string roomName)
    {
        var building = (await (await client.PostAsJsonAsync("/api/buildings", new BuildingRequest(buildingName, "Address"))).Content.ReadFromJsonAsync<BuildingDto>())!;
        var roomResponse = await client.PostAsJsonAsync("/api/rooms", new RoomCreateRequest(building.Id, roomName, 2_000_000, 3_500, 0, false, 0, 0));
        var roomId = (await roomResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var occupancyResponse = await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new OccupancyRequest("Tenant", "0911223344", 1, new DateOnly(2026, 8, 1), 0));
        var occupancyId = (await occupancyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("occupancy").GetProperty("id").GetGuid();
        return (building.Id, roomId, occupancyId);
    }

    private static WebApplication CreateTestApp(string connectionString, TimeProvider clock)
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
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<TenantTokenService>();
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddScoped<PayOsService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddScoped<RecurringExpenseGenerator>();
        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(clock);
        builder.Services.AddAuthentication(FixtureAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(FixtureAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("tenant-payment", _ => RateLimitPartition.GetNoLimiter("fixture")));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(
                new AppUser { Id = ProOwner, Name = "Pro", Email = "pro@expense.invalid", PasswordHash = "unused", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Pro },
                new AppUser { Id = FreeOwner, Name = "Free", Email = "free@expense.invalid", PasswordHash = "unused", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Free },
                new AppUser { Id = OtherOwner, Name = "Other", Email = "other@expense.invalid", PasswordHash = "unused", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Pro });
            db.SaveChanges();
        }
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOwnerManagementEndpoints();
        app.MapBillingEndpoints();
        app.MapReportsEndpoints();
        app.MapExpenseEndpoints();
        return app;
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FixtureAuthExpenses";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var owner = Request.Headers["X-Fixture-Owner"].FirstOrDefault() ?? ProOwner.ToString();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner), new Claim(ClaimTypes.Role, "Owner")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private static class ExpenseEndpointsProbe
    {
        public static string? Detect(byte[] bytes) => ExpenseEndpoints.DetectFileType(bytes)?.ContentType;
        public static string Sanitize(string name, string extension) => ExpenseEndpoints.SanitizeFileName(name, extension);
    }
}

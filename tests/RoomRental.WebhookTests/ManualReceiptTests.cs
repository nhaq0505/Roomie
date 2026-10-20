using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

internal static class ManualReceiptTests
{
    private const string Key = "synthetic-checksum-key-only-for-in-process-tests";
    public static async Task RunAsync()
    {
        using (var f = new PayOsFixture())
        {
            var attempt = await Pending(f);
            var http = new CancellationHandler(attempt);
            var service = new ManualReceiptService(f.Db, f.CreateService(http));
            var result = await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(result.Status == "Paid" && result.Paid == 125_000 && result.Outstanding == 0, "cash receipt cancels payOS and settles the invoice");
            Check(http.Gets == 1 && http.Cancels == 1 && http.ValidCancel, "cancellation uses the owner credentials and fixed provider endpoint with a reason");
            Check(attempt.Status == PaymentAttemptStatus.Failed && attempt.ProviderStatus == "CANCELLED" &&
                (await f.Db.Receipts.SingleAsync()).Source == "Manual", "provider-confirmed cancellation and manual receipt commit together");
            var replay = await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(replay.Id == result.Id && await f.Db.Receipts.CountAsync() == 1 && http.Cancels == 1, "receipt retry is idempotent without calling payOS again");
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(99_999), null, default), 409, "changed idempotent receipt payload conflicts");
            var data = new Dictionary<string, string> { ["orderCode"] = attempt.OrderCode.ToString(), ["amount"] = "100000", ["code"] = "00", ["reference"] = "late-synthetic-payment" };
            using var webhook = JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, data, signature = PayOsSignature.Create(Key, data) }));
            await Reject(() => f.CreateService(http).ProcessWebhookAsync(f.OwnerId, webhook.RootElement, default), 409, "late webhook cannot double-credit a cash-settled invoice");
            Check(await f.Db.Receipts.CountAsync() == 1, "late webhook failure preserves the single cash receipt");
        }
        using (var f = new PayOsFixture())
        {
            var attempt = await Pending(f); var http = new CancellationHandler(attempt);
            var service = new ManualReceiptService(f.Db, f.CreateService(http));
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(5_000), null, default), 409,
                "partial cash receipt is rejected before provider cancellation");
            Check(http.Gets == 0 && http.Cancels == 0 && await f.Db.Receipts.CountAsync() == 0 && f.Invoice.Paid == 25_000,
                "partial receipt leaves provider reservation and balances unchanged");
            var result = await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(result.Status == "Paid" && result.Outstanding == 0,
                "confirmation settles the entire remaining balance of a historical partial invoice");
        }
        foreach (var terminal in new[] { "CANCELLED", "EXPIRED" })
        {
            using var f = new PayOsFixture(); var attempt = await Pending(f); var http = new CancellationHandler(attempt) { BeforeStatus = terminal };
            await new ManualReceiptService(f.Db, f.CreateService(http)).RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(http.Cancels == 0 && attempt.ProviderStatus == terminal, "already " + terminal + " order permits cash after signed verification");
        }
        foreach (var before in new[] { true, false })
        {
            using var f = new PayOsFixture(); var attempt = await Pending(f);
            var http = new CancellationHandler(attempt) { BeforeStatus = before ? "PAID" : "PENDING", BeforePaid = before ? 100_000 : 0, AfterStatus = "PAID", AfterPaid = 100_000 };
            await Reject(() => new ManualReceiptService(f.Db, f.CreateService(http)).RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default), 409,
                before ? "already-paid payOS order refuses duplicate cash" : "payment winning the cancellation race refuses duplicate cash");
            Check(await f.Db.Receipts.CountAsync(x => x.Source == "payOS") == 1 && await f.Db.Receipts.CountAsync(x => x.Source == "Manual") == 0 &&
                (await f.Db.Invoices.SingleAsync()).Paid == 125_000, "verified online payment reconciles once instead of recording cash");
        }
        foreach (var scenario in new[] { "signature", "order", "amount", "missing-paid", "partial", "paid-without-money", "pending", "timeout", "get-timeout", "http-error", "malformed" })
        {
            using var f = new PayOsFixture(); var attempt = await Pending(f); var http = new CancellationHandler(attempt) { Fault = scenario };
            var code = scenario is "partial" or "paid-without-money" or "pending" ? 409 : 502;
            await Reject(() => new ManualReceiptService(f.Db, f.CreateService(http)).RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default), code,
                "uncertain cancellation refuses cash: " + scenario);
            f.Db.ChangeTracker.Clear();
            Check(await f.Db.Receipts.CountAsync() == 0 && (await f.Db.PayOsAttempts.SingleAsync()).Status == PaymentAttemptStatus.Pending &&
                (await f.Db.Invoices.SingleAsync()).Paid == 25_000, "failed cancellation leaves all financial records unchanged: " + scenario);
        }
        using (var f = new PayOsFixture())
        {
            var attempt = await Pending(f); var http = new CancellationHandler(attempt) { Fault = "timeout" };
            var service = new ManualReceiptService(f.Db, f.CreateService(http));
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default), 502, "cancellation timeout is retryable");
            f.Db.ChangeTracker.Clear(); http.Fault = "";
            var result = await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(result.Status == "Paid" && http.Cancels == 1 && await f.Db.Receipts.CountAsync() == 1, "retry after remote cancellation timeout verifies status without cancelling twice");
        }
        using (var f = new PayOsFixture())
        {
            var attempt = await Pending(f); var http = new CancellationHandler(attempt);
            var service = new ManualReceiptService(f.Db, f.CreateService(http));
            EventHandler<Microsoft.EntityFrameworkCore.SavingChangesEventArgs> fail = (_, _) => throw new InvalidOperationException("Synthetic commit failure");
            f.Db.SavingChanges += fail;
            try { await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default); throw new Exception("Missing synthetic failure"); }
            catch (InvalidOperationException e) when (e.Message == "Synthetic commit failure") { }
            finally { f.Db.SavingChanges -= fail; }
            f.Db.ChangeTracker.Clear();
            Check(await f.Db.Receipts.CountAsync() == 0 && (await f.Db.PayOsAttempts.SingleAsync()).Status == PaymentAttemptStatus.Pending,
                "database failure rolls back local cancellation state and cash receipt");
            await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(http.Cancels == 1 && await f.Db.Receipts.CountAsync() == 1, "retry recovers when provider cancellation succeeded before database failure");
        }
        using (var f = new PayOsFixture())
        {
            var attempt = await Pending(f); var http = new CancellationHandler(attempt);
            var service = new ManualReceiptService(f.Db, f.CreateService(http));
            await Reject(() => service.RecordAsync(Guid.NewGuid(), f.Invoice.Id, Request(100_000), null, default), 404, "other owner cannot cancel an invoice order");
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_001), null, default), 409, "overpayment validation happens before cancellation");
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(1) with { ExpectedVersion = 999 }, null, default), 409, "stale invoice validation happens before cancellation");
            Check(http.Gets == 0 && http.Cancels == 0, "invalid cash requests never contact the provider");
            attempt.CheckoutUrl = null; attempt.PaymentLinkId = null; await f.Db.SaveChangesAsync();
            http.Fault = "http-error";
            await Reject(() => service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default), 502, "unknown create reservation cannot be cleared after a failed provider lookup");
            Check(http.Cancels == 0 && await f.Db.Receipts.CountAsync() == 0, "unknown reservation stays pending without a confirmed cancellation");
            f.Db.ChangeTracker.Clear(); http.Fault = "";
            await service.RecordAsync(f.OwnerId, f.Invoice.Id, Request(100_000), null, default);
            Check(http.Cancels == 1 && await f.Db.Receipts.CountAsync() == 1, "legacy reservation without link metadata is recovered through signed order-code lookup");
        }
        foreach (var paid in new[] { 1m, 100_000m })
        {
            using var f = new PayOsFixture(); var attempt = await Pending(f);
            var http = new CancellationHandler(attempt) { BeforeStatus = "CANCELLED", BeforePaid = paid };
            var result = await f.CreateService(http).ReconcileInvoiceAsync(f.OwnerId, f.Invoice.Id, default);
            Check(paid == 1 ? result == "Partial" && attempt.Status == PaymentAttemptStatus.Pending :
                result == "Paid" && attempt.Status == PaymentAttemptStatus.Paid,
                "reconciliation never releases a cancelled link with received money: " + paid);
            await Reject(() => new ManualReceiptService(f.Db, f.CreateService(http)).RecordAsync(f.OwnerId, f.Invoice.Id,
                Request(100_000), null, default), 409, "prior reconciliation cannot bypass the cash-versus-online payment guard");
        }
        await ConcurrentCashOnPostgresAsync();
    }

    private static async Task ConcurrentCashOnPostgresAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_PAYMENT_POSTGRES_CONNECTION"))) return;
        using var f = new PayOsFixture();
        var attempt = await Pending(f);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        var http = new CancellationHandler(attempt)
        {
            BeforeGet = async () =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(f.PostgresTestConnection!).Options;
        async Task<bool> SubmitAsync()
        {
            await using var db = new AppDbContext(options);
            var payOs = new PayOsService(db, f.Protection, http, new ConfigurationBuilder().Build(), NullLogger<PayOsService>.Instance);
            try
            {
                await new ManualReceiptService(db, payOs).RecordAsync(f.OwnerId, f.Invoice.Id,
                    Request(100_000) with { IdempotencyKey = Guid.NewGuid().ToString() }, null, default);
                return true;
            }
            catch (Exception e) when (IsConcurrencyConflict(e))
            { return false; }
        }
        var outcomes = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        Check(outcomes.Count(x => x) == 1, "concurrent PostgreSQL cash receipts have exactly one committing winner");
        f.Db.ChangeTracker.Clear();
        Check(await f.Db.Receipts.CountAsync() == 1 && (await f.Db.Invoices.SingleAsync()).Paid == 125_000 &&
            (await f.Db.PayOsAttempts.SingleAsync()).Status == PaymentAttemptStatus.Failed,
            "concurrent cash cancellation cannot duplicate receipts or over-credit the invoice");
    }

    private static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or ApiException { StatusCode: 409 } ||
                current is Npgsql.PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }

    private static ReceiptRequest Request(decimal amount) => new(amount, "Synthetic cash receipt", "synthetic-cash-idempotency");
    private static async Task<PayOsAttempt> Pending(PayOsFixture f)
    {
        var attempt = new PayOsAttempt { OwnerId = f.OwnerId, InvoiceId = f.Invoice.Id, OrderCode = 9_654_321_123, Amount = 100_000,
            CheckoutUrl = "https://pay.payos.vn/web/synthetic-cash-switch", PaymentLinkId = "synthetic-cash-switch", Status = PaymentAttemptStatus.Pending };
        f.Db.PayOsAttempts.Add(attempt); await f.Db.SaveChangesAsync(); return attempt;
    }
    private static async Task Reject(Func<Task> run, int code, string name)
    {
        try { await run(); } catch (ApiException e) when (e.StatusCode == code) { Check(true, name); return; }
        throw new Exception(name);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }

    private sealed class CancellationHandler(PayOsAttempt attempt) : HttpMessageHandler, IHttpClientFactory
    {
        public string BeforeStatus = "PENDING", AfterStatus = "CANCELLED", Fault = "";
        public decimal BeforePaid, AfterPaid;
        public int Gets, Cancels;
        public bool ValidCancel;
        public Func<Task>? BeforeGet;
        private bool cancelled;
        public HttpClient CreateClient(string name) => name == "payOS" ? new HttpClient(this, false) : throw new Exception("Unexpected client");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.GetLeftPart(UriPartial.Authority) != "https://api-merchant.payos.vn" ||
                request.Headers.GetValues("x-client-id").Single() != "synthetic-client-id" ||
                request.Headers.GetValues("x-api-key").Single() != "synthetic-api-key") throw new Exception("Invalid provider request");
            var cancel = request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == $"/v2/payment-requests/{attempt.OrderCode}/cancel";
            if (cancel)
            {
                Interlocked.Increment(ref Cancels); cancelled = true;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                ValidCancel = body.RootElement.GetProperty("cancellationReason").GetString()!.Contains("tiền mặt");
                if (Fault == "timeout") throw new TaskCanceledException();
            }
            else if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == $"/v2/payment-requests/{attempt.OrderCode}")
            {
                Interlocked.Increment(ref Gets);
                if (BeforeGet is not null) await BeforeGet();
                if (Fault == "get-timeout") throw new TaskCanceledException();
            }
            else throw new Exception("Unexpected external request");
            var data = new Dictionary<string, string> { ["orderCode"] = attempt.OrderCode.ToString(), ["amount"] = attempt.Amount.ToString(),
                ["amountPaid"] = (cancelled ? AfterPaid : BeforePaid).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["status"] = cancelled ? AfterStatus : BeforeStatus, ["id"] = "synthetic-switch-payment" };
            if (Fault == "partial") { data["amountPaid"] = "1"; data["status"] = "PROCESSING"; }
            if (Fault == "paid-without-money") data["status"] = "PAID";
            if (cancel && Fault == "pending") data["status"] = "PENDING";
            if (cancel && Fault == "order") data["orderCode"] = "1";
            if (cancel && Fault == "amount") data["amount"] = "1";
            if (cancel && Fault == "missing-paid") data.Remove("amountPaid");
            var signature = cancel && Fault == "signature" ? new string('0', 64) : PayOsSignature.Create(Key, data);
            var json = Fault == "malformed" ? "[]" : JsonSerializer.Serialize(new { code = "00", data, signature });
            return new HttpResponseMessage(Fault == "http-error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

internal static class BatchPaymentTests
{
    public static async Task RunAsync()
    {
        using var f = new PayOsFixture();
        var second = new Invoice { OwnerId = f.OwnerId, RoomId = f.Invoice.RoomId, BuildingId = f.Invoice.BuildingId,
            OccupancyId = f.Invoice.OccupancyId, RoomName = "Second", BuildingName = "Fixture", TenantName = "Tenant",
            Period = "2026-09", Total = 200_000, Paid = 50_000 };
        var foreign = new Invoice { OwnerId = f.OwnerId, RoomId = Guid.NewGuid(), OccupancyId = Guid.NewGuid(),
            RoomName = "Other room", BuildingName = "Fixture", TenantName = "Other", Period = "2026-09", Total = 900_000 };
        f.Db.Invoices.AddRange(second, foreign); await f.Db.SaveChangesAsync();
        using var http = new FakePayOsHandler("synthetic-checksum-key-only-for-in-process-tests");
        var service = f.CreateService(http);
        var checkout = await service.CreateOrGetCheckoutAsync(f.OwnerId, f.Invoice, "fixture-token", default, allInvoices: true);
        Check(checkout.Amount == 250_000 && http.CreateCalls == 1, "batch uses outstanding balances across all issued invoices of one occupancy");
        var attempt = await f.Db.PayOsAttempts.SingleAsync();
        Check(second.PaymentReservationId == attempt.Id && f.Invoice.PaymentReservationId == attempt.Id && foreign.PaymentReservationId is null,
            "batch reserves each member without reserving another occupancy");
        Check(await PaymentReservations.Pending(f.Db).CountAsync() == 2, "pending reservation query includes non-anchor batch members");
        var again = await service.CreateOrGetCheckoutAsync(f.OwnerId, f.Invoice, "fixture-token", default, allInvoices: true);
        Check(again.OrderCode == checkout.OrderCode && http.CreateCalls == 1, "reopening batch reuses the original signed order");
        await Reject(() => service.CreateOrGetCheckoutAsync(f.OwnerId, second, "fixture-token", default), "a batch member cannot start a separate checkout");
        var receipts = new ManualReceiptService(f.Db, service);
        await Reject(() => receipts.RecordAsync(f.OwnerId, second.Id, new ReceiptRequest(second.Total - second.Paid, "fixture", "fixture-cash", null), null, default),
            "cash receipt cannot silently cancel or duplicate a batch payment");
        var allocations = JsonSerializer.Deserialize<InvoiceAllocation[]>(attempt.InvoiceAllocationsJson!)!;
        Check(allocations.Length == 2 && allocations.Sum(x => x.Amount) == 250_000, "immutable batch allocation snapshot matches provider amount");
        var data = new Dictionary<string, string> { ["orderCode"] = attempt.OrderCode.ToString(), ["amount"] = attempt.Amount.ToString(), ["reference"] = "batch-fixture-ref", ["code"] = "00" };
        var badData = new Dictionary<string, string>(data) { ["amount"] = "1" };
        using (var bad = JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, data = badData,
            signature = PayOsSignature.Create("synthetic-checksum-key-only-for-in-process-tests", badData) }))) {
            await Reject(() => service.ProcessWebhookAsync(f.OwnerId, bad.RootElement, default), "signed batch callback with wrong amount cannot credit any member");
            Check(await f.Db.Receipts.CountAsync() == 0, "invalid batch callback leaves all invoices unchanged");
        }
        using var webhook = JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, data,
            signature = PayOsSignature.Create("synthetic-checksum-key-only-for-in-process-tests", data) }));
        await service.ProcessWebhookAsync(f.OwnerId, webhook.RootElement, default);
        await service.ProcessWebhookAsync(f.OwnerId, webhook.RootElement, default);
        Check(second.Paid == 200_000 && f.Invoice.Paid == 125_000 && foreign.Paid == 0, "verified batch webhook allocates once to the intended invoices");
        Check(await f.Db.Receipts.CountAsync() == 2 && await f.Db.ProcessedWebhooks.CountAsync() == 1,
            "duplicate batch webhook creates no additional receipts");
        Check(await PaymentReservations.Pending(f.Db).CountAsync() == 0, "paid batch releases all invoice reservations");
    }
    private static async Task Reject(Func<Task> action, string name) {
        try { await action(); throw new Exception(name); } catch (ApiException ex) when (ex.StatusCode == 409) { Console.WriteLine("PASS: " + name); }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
}

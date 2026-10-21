using System.Text.Json;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

internal static class ManualPaymentAssessmentTests
{
    public static async Task RunAsync()
    {
        using var f = new PayOsFixture();
        var request = new ManualPaymentRequest
        {
            OwnerId = f.OwnerId, OccupancyId = f.Invoice.OccupancyId, Amount = 100_000,
            AllocationsJson = JsonSerializer.Serialize(new[] { new InvoiceAllocation(f.Invoice.Id, 100_000) }),
            BankBin = "970436", AccountNumber = "fixture-only", AccountName = "Fixture",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        async Task<ManualPaymentAssessment> State() => (await ManualPaymentAssessment.LoadAsync(f.Db, f.OwnerId, [request], default))[request.Id];
        Check((await State()).CanConfirm, "Expired QR reuse window does not reject acknowledgment of actual funds received");
        f.Db.Invoices.Local.Single().Paid = 125_000;
        await f.Db.SaveChangesAsync();
        Check((await State()) is { Status: "AlreadyPaid", CanConfirm: false, CanDismiss: true, Outstanding: 0 },
            "Previously paid invoice is review-only even for an expired legacy request");
        f.Invoice.Paid = 30_000;
        await f.Db.SaveChangesAsync();
        Check((await State()) is { Status: "InvoiceChanged", CanConfirm: false, Outstanding: 95_000 },
            "Legacy partial receipt invalidates the snapshot without crediting the remaining balance");
        f.Invoice.Paid = 25_000;
        var attempt = new PayOsAttempt { OwnerId = f.OwnerId, InvoiceId = f.Invoice.Id, OrderCode = 99887766,
            Amount = 100_000, CheckoutUrl = "https://pay.payos.vn/web/fixture-only", Status = PaymentAttemptStatus.Pending };
        f.Db.PayOsAttempts.Add(attempt);
        await f.Db.SaveChangesAsync();
        Check((await State()) is { Status: "PaymentPending", CanConfirm: false, CanDismiss: false },
            "Pending provider reservation blocks both QR confirmation and dismissal");
        // Batch members can differ from the order anchor; every member remains protected.
        attempt.InvoiceId = Guid.NewGuid();
        f.Invoice.PaymentReservationId = attempt.Id;
        await f.Db.SaveChangesAsync();
        Check((await State()).Status == "PaymentPending", "Batch payment reservation protects member invoice from manual QR confirmation");
        request.DismissedAt = DateTimeOffset.UtcNow;
        Check((await State()) is { Status: "Dismissed", CanConfirm: false, CanDismiss: false },
            "Dismissed QR never becomes confirmable again");
        request.DismissedAt = null;
        request.AllocationsJson = JsonSerializer.Serialize(new[] { new InvoiceAllocation(f.Invoice.Id, 50_000), new InvoiceAllocation(f.Invoice.Id, 50_000) });
        Check((await State()).Status == "Unavailable", "Duplicate allocations cannot bypass full-balance validation");
        Check(!f.Db.Receipts.Any(), "Request assessment never creates financial receipts");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}

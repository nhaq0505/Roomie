using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed record ManualReceiptResult(Guid Id, Guid InvoiceId, decimal Amount, decimal Paid, decimal Outstanding, string Status, long Version);

public sealed class ManualReceiptService(AppDbContext db, PayOsService payOs)
{
    public async Task<ManualReceiptResult> RecordAsync(Guid ownerId, Guid invoiceId, ReceiptRequest request,
        string? headerKey, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(headerKey) ? request.IdempotencyKey?.Trim() : headerKey.Trim();
        if (key is { Length: > 100 }) throw new ApiException(400, "Idempotency key must be 100 characters or fewer.");
        if (request.Amount < 0 || request.Amount > int.MaxValue || request.Amount != decimal.Truncate(request.Amount))
            throw new ApiException(400, "Khoản thu phải là số VND nguyên từ 0 đến giới hạn thanh toán.");
        if (request.Amount <= 0) throw new ApiException(400, "Receipt amount must be positive.");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 500 }) throw new ApiException(400, "Receipt note must be 500 characters or fewer.");

        VerifiedPayOsPayment? paidOnline;
        await using (var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct))
        {
            var invoice = await db.Invoices.SingleOrDefaultAsync(x => x.Id == invoiceId && x.OwnerId == ownerId, ct)
                ?? throw new ApiException(404, "Invoice was not found.");
            if (!string.IsNullOrWhiteSpace(key))
            {
                var prior = await db.Receipts.SingleOrDefaultAsync(x => x.InvoiceId == invoiceId && x.IdempotencyKey == key, ct);
                if (prior is not null)
                {
                    if (prior.Amount != request.Amount || !string.Equals(prior.Note, note, StringComparison.Ordinal))
                        throw new ApiException(409, "This idempotency key was already used for a different receipt.");
                    await transaction.CommitAsync(ct);
                    return Result(invoice, prior);
                }
            }
            if (invoice.LifecycleState != InvoiceLifecycleState.Issued)
                throw new ApiException(409, "Draft invoices cannot receive payments. Publish the invoice first.");
            if (request.ExpectedVersion is long expected && invoice.Version != expected)
                throw new ApiException(409, "Invoice changed. Refresh it and retry the receipt.");
            if (request.Amount > invoice.Total - invoice.Paid)
                throw new ApiException(409, "Receipt amount cannot exceed the outstanding balance.");

            // Keep the pending reservation until cancellation and the receipt commit together.
            // A concurrent checkout cannot reserve another order in the intervening window.
            paidOnline = await payOs.CancelPendingForManualReceiptAsync(ownerId, invoiceId, ct);
            if (paidOnline is null)
            {
                var receipt = new PaymentReceipt
                {
                    OwnerId = ownerId, InvoiceId = invoiceId, Amount = request.Amount, Note = note,
                    IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key,
                    Source = "Manual", CreatedAt = DateTimeOffset.UtcNow
                };
                db.Receipts.Add(receipt);
                invoice.Paid += receipt.Amount;
                invoice.Version++;
                InvoiceService.RecalculateStatus(invoice);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Result(invoice, receipt);
            }
            await transaction.RollbackAsync(ct);
        }
        // Reconcile only the signed, exact-amount provider payment. Never add manual cash as well.
        db.ChangeTracker.Clear();
        await payOs.RecordVerifiedPaymentAfterCancellationCheckAsync(ownerId, paidOnline, ct);
        throw new ApiException(409, "payOS đã xác nhận thanh toán. Hóa đơn đã được cập nhật; không ghi thêm khoản thu tiền mặt.");
    }

    private static ManualReceiptResult Result(Invoice invoice, PaymentReceipt receipt) =>
        new(receipt.Id, invoice.Id, receipt.Amount, invoice.Paid, Math.Max(0, invoice.Total - invoice.Paid), invoice.Status.ToString(), invoice.Version);
}

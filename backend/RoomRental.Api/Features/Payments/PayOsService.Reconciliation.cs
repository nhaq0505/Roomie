using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed partial class PayOsService
{
    public async Task<object> ReconcileAsync(Guid ownerId, long orderCode, CancellationToken cancellationToken)
    {
        var attempt = await db.PayOsAttempts.SingleOrDefaultAsync(row => row.OwnerId == ownerId && row.OrderCode == orderCode, cancellationToken);
        if (attempt is null) throw new ApiException(StatusCodes.Status404NotFound, "Payment order was not found.");
        if (attempt.Status == PaymentAttemptStatus.Paid) return new { status = "Paid" };
        var providerStatus = attempt.Kind == "Subscription"
            ? await ReconcileSubscriptionAttemptAsync(ownerId, attempt, cancellationToken)
            : await ReconcileAttemptAsync(ownerId, attempt, cancellationToken);
        return new { status = providerStatus, checkoutUrl = attempt.CheckoutUrl };
    }

    public async Task<string> ReconcileInvoiceAsync(Guid ownerId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var attempt = await db.PayOsAttempts.Where(row => row.OwnerId == ownerId && row.InvoiceId == invoiceId &&
                (row.Status == PaymentAttemptStatus.Pending || row.Status == PaymentAttemptStatus.Paid))
            .OrderByDescending(row => row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return "NoPayment";
        if (attempt.Status == PaymentAttemptStatus.Paid) return "Paid";
        return await ReconcileAttemptAsync(ownerId, attempt, cancellationToken);
    }

    public async Task<(int Checked, int Failed)> ReconcilePendingBatchAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.PayOsAttempts.AsNoTracking().Where(row => row.Status == PaymentAttemptStatus.Pending)
            .OrderBy(row => row.CreatedAt).Select(row => new { row.OwnerId, row.OrderCode, row.Kind, row.CreatedAt }).Take(100).ToListAsync(cancellationToken);
        var pendingOrders = candidates
            .OrderBy(row => LastReconcileAttemptByOrder.GetValueOrDefault(row.OrderCode, DateTimeOffset.MinValue))
            .ThenBy(row => row.CreatedAt)
            .Take(12)
            .ToList();
        var failed = 0;
        foreach (var order in pendingOrders)
        {
            LastReconcileAttemptByOrder[order.OrderCode] = DateTimeOffset.UtcNow;
            try
            {
                if (order.Kind == "Subscription")
                    await ReconcileSubscriptionOrderAsync(order.OwnerId, order.OrderCode, cancellationToken);
                else
                    await ReconcileAsync(order.OwnerId, order.OrderCode, cancellationToken);
                LastReconcileAttemptByOrder.TryRemove(order.OrderCode, out _);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Automatic payOS reconciliation failed for order {OrderCode} (Kind={Kind}).", order.OrderCode, order.Kind);
                db.ChangeTracker.Clear();
                failed++;
            }
        }
        if (failed > 0) logger.LogWarning("Automatic payOS reconciliation failed for {Count} pending order(s).", failed);
        return (pendingOrders.Count, failed);
    }

    private async Task<string> ReconcileAttemptAsync(Guid ownerId, PayOsAttempt attempt, CancellationToken cancellationToken)
    {
        var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(row => row.OwnerId == ownerId, cancellationToken);
        if (settings is null) throw new ApiException(StatusCodes.Status400BadRequest, "Configure payOS before reconciling an order.");
        var credentials = Decrypt(settings);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/v2/payment-requests/{attempt.OrderCode}");
        request.Headers.TryAddWithoutValidation("x-client-id", credentials.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", credentials.ApiKey);
        using var response = await httpClientFactory.CreateClient("payOS").SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var providerCode = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        if (!response.IsSuccessStatusCode || providerCode != "00" || !root.TryGetProperty("data", out var data) ||
            !root.TryGetProperty("signature", out var signature) || !PayOsSignature.VerifyData(credentials.ChecksumKey, data, signature.GetString()) ||
            !TryGetLong(data, "orderCode", out var responseOrderCode) || responseOrderCode != attempt.OrderCode ||
            !TryGetDecimal(data, "amount", out var responseAmount) || responseAmount != attempt.Amount)
            throw new ApiException(StatusCodes.Status502BadGateway, "payOS returned an invalid or unavailable order status.");

        var status = ReadString(data, "status") ?? "Pending";
        var providerStatusChanged = !string.Equals(attempt.ProviderStatus, status, StringComparison.Ordinal);
        attempt.ProviderStatus = status;
        if (status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            // Terminal link status alone is insufficient: preserve the reservation if money
            // was received or the signed response does not prove that nothing was collected.
            if (!TryGetDecimal(data, "amountPaid", out var terminalPaid) || terminalPaid < 0) return "Pending";
            if (terminalPaid == 0)
            {
                attempt.Status = PaymentAttemptStatus.Failed;
                await db.SaveChangesAsync(cancellationToken);
                return status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Expired";
            }
            if (terminalPaid != attempt.Amount)
            {
                await db.SaveChangesAsync(cancellationToken);
                return "Partial";
            }
            // Exact, verified payment evidence is reconciled below even for a closed link.
        }

        var linkId = ReadString(data, "id") ?? ReadString(data, "paymentLinkId");
        var changed = false;
        if (string.IsNullOrWhiteSpace(attempt.CheckoutUrl) && !string.IsNullOrWhiteSpace(linkId))
        {
            attempt.PaymentLinkId = linkId;
            attempt.CheckoutUrl = $"https://pay.payos.vn/web/{Uri.EscapeDataString(linkId)}";
            changed = true;
        }

        if (changed || providerStatusChanged)
            await db.SaveChangesAsync(cancellationToken);

        if (!TryGetDecimal(data, "amountPaid", out var amountPaid) || amountPaid <= 0)
            return status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ? "Pending" : status;
        if (amountPaid != attempt.Amount) return "Partial";
        var reference = TryFindTransactionReference(data) ?? ReadString(data, "id") ?? $"reconcile-{attempt.OrderCode}";
        await RecordSuccessfulPaymentAsync(ownerId, attempt, amountPaid, reference, cancellationToken);
        return "Paid";
    }

    private async Task RecordSuccessfulPaymentAsync(Guid ownerId, PayOsAttempt attempt, decimal amount, string reference, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var duplicate = await db.ProcessedWebhooks.SingleOrDefaultAsync(row => row.OwnerId == ownerId && row.Reference == reference, cancellationToken);
        if (duplicate is not null)
        {
            if (duplicate.OrderCode != attempt.OrderCode)
                throw new ApiException(StatusCodes.Status409Conflict, "This payOS reference is already associated with another order.");
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var trackedAttempt = await db.PayOsAttempts.SingleAsync(row => row.Id == attempt.Id && row.OwnerId == ownerId, cancellationToken);
        await db.Entry(trackedAttempt).ReloadAsync(cancellationToken);
        if (trackedAttempt.Status == PaymentAttemptStatus.Paid)
        {
            if (!string.Equals(trackedAttempt.ProviderReference, reference, StringComparison.Ordinal))
                throw new ApiException(StatusCodes.Status409Conflict, "This payOS order was already processed with another transaction reference.");
            db.ProcessedWebhooks.Add(new ProcessedWebhook { OwnerId = ownerId, Reference = reference, OrderCode = attempt.OrderCode });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var invoice = await db.Invoices.SingleAsync(row => row.Id == trackedAttempt.InvoiceId && row.OwnerId == ownerId, cancellationToken);
        await db.Entry(invoice).ReloadAsync(cancellationToken);
        if (invoice.LifecycleState != InvoiceLifecycleState.Issued)
            throw new ApiException(StatusCodes.Status409Conflict, "A draft invoice cannot receive a payOS payment.");
        if (amount != trackedAttempt.Amount || amount > invoice.Total - invoice.Paid)
            throw new ApiException(StatusCodes.Status409Conflict, "Payment exceeds the invoice balance.");

        db.Receipts.Add(new PaymentReceipt
        {
            OwnerId = ownerId,
            InvoiceId = invoice.Id,
            Amount = amount,
            Note = "Thanh toán trực tuyến qua payOS",
            Source = "payOS",
            ExternalReference = reference,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.ProcessedWebhooks.Add(new ProcessedWebhook { OwnerId = ownerId, Reference = reference, OrderCode = trackedAttempt.OrderCode });
        invoice.Paid += amount;
        invoice.Version++;
        InvoiceService.RecalculateStatus(invoice);
        trackedAttempt.Status = PaymentAttemptStatus.Paid;
        trackedAttempt.ProviderReference = reference;
        trackedAttempt.PaidAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }


}

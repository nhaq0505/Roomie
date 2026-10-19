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
    internal async Task<VerifiedPayOsPayment?> CancelPendingForManualReceiptAsync(Guid ownerId, Guid invoiceId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment cancellation requires the manual receipt transaction.");
        var attempt = await db.PayOsAttempts.SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.InvoiceId == invoiceId &&
            x.Status == PaymentAttemptStatus.Pending, ct);
        if (attempt is null) return null;
        // Even a legacy reservation without local link metadata must be verified by
        // order code. Never infer cancellation from missing metadata or a failed lookup.
        if (attempt.Kind != "Invoice") throw new ApiException(409, "Giao dịch payOS không thuộc loại hóa đơn.");
        var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct)
            ?? throw new ApiException(409, "Không thể kiểm tra giao dịch payOS. Vui lòng kiểm tra cấu hình thanh toán.");
        var credentials = Decrypt(settings);
        try
        {
            using var query = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/v2/payment-requests/{attempt.OrderCode}");
            var status = await ReadCancellationStatusAsync(query, attempt, credentials, ct);
            if (status.PaidAmount > 0 || status.Status == "PAID") return PaidOrReject(status, attempt);
            if (status.Status is not ("CANCELLED" or "EXPIRED"))
            {
                if (status.Status is not ("PENDING" or "PROCESSING"))
                    throw new ApiException(409, "Chưa xác nhận được trạng thái payOS. Khoản thu tiền mặt chưa được ghi nhận.");
                using var cancel = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v2/payment-requests/{attempt.OrderCode}/cancel")
                {
                    Content = JsonContent.Create(new { cancellationReason = "Chủ trọ ghi nhận khoản thu tiền mặt hoặc chuyển khoản ngoài." })
                };
                status = await ReadCancellationStatusAsync(cancel, attempt, credentials, ct);
                if (status.PaidAmount > 0 || status.Status == "PAID") return PaidOrReject(status, attempt);
            }
            if (status.Status is not ("CANCELLED" or "EXPIRED"))
                throw new ApiException(409, "payOS chưa xác nhận hủy giao dịch. Khoản thu tiền mặt chưa được ghi nhận.");
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.ProviderStatus = status.Status;
            // Save only with the manual receipt; the caller owns the transaction.
            return null;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            throw new ApiException(502, "Không thể xác nhận hủy giao dịch payOS. Khoản thu tiền mặt chưa được ghi nhận; vui lòng thử lại.");
        }
    }

    private async Task<CancellationStatus> ReadCancellationStatusAsync(HttpRequestMessage request, PayOsAttempt attempt,
        (string ClientId, string ApiKey, string ChecksumKey) credentials, CancellationToken ct)
    {
        request.Headers.TryAddWithoutValidation("x-client-id", credentials.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", credentials.ApiKey);
        using var response = await httpClientFactory.CreateClient("payOS").SendAsync(request, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        if (!response.IsSuccessStatusCode || root.ValueKind != JsonValueKind.Object || ReadString(root, "code") != "00" ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.String ||
            !PayOsSignature.VerifyData(credentials.ChecksumKey, data, signature.GetString()) ||
            !TryGetLong(data, "orderCode", out var order) || order != attempt.OrderCode ||
            !TryGetDecimal(data, "amount", out var amount) || amount != attempt.Amount ||
            !TryGetDecimal(data, "amountPaid", out var paid) || paid < 0 || paid != decimal.Truncate(paid))
            throw new ApiException(502, "payOS trả về trạng thái không hợp lệ. Khoản thu tiền mặt chưa được ghi nhận.");
        return new((ReadString(data, "status") ?? "").ToUpperInvariant(), paid,
            TryFindTransactionReference(data) ?? ReadString(data, "id") ?? $"reconcile-{attempt.OrderCode}");
    }

    private static VerifiedPayOsPayment PaidOrReject(CancellationStatus status, PayOsAttempt attempt)
    {
        if (status.PaidAmount != attempt.Amount)
            throw new ApiException(409, "Giao dịch payOS đã có tiền hoặc chưa đủ dữ liệu xác nhận. Vui lòng đối soát trước khi ghi nhận tiền mặt.");
        return new(attempt.Id, attempt.OrderCode, status.PaidAmount, status.Reference);
    }

    internal async Task RecordVerifiedPaymentAfterCancellationCheckAsync(Guid ownerId, VerifiedPayOsPayment payment, CancellationToken ct)
    {
        var attempt = await db.PayOsAttempts.SingleAsync(x => x.Id == payment.AttemptId && x.OwnerId == ownerId &&
            x.OrderCode == payment.OrderCode && x.Kind == "Invoice", ct);
        await RecordSuccessfulPaymentAsync(ownerId, attempt, payment.Amount, payment.Reference, ct);
    }

    private sealed record CancellationStatus(string Status, decimal PaidAmount, string Reference);


}

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
    public async Task<bool> ProcessWebhookAsync(Guid ownerId, JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out var data) || !root.TryGetProperty("signature", out var signatureElement))
            throw new ApiException(StatusCodes.Status400BadRequest, "Malformed payOS callback payload.");

        if (!TryGetLong(data, "orderCode", out var orderCode) || !TryGetDecimal(data, "amount", out var amount))
            throw new ApiException(StatusCodes.Status400BadRequest, "Payment callback is missing its order or amount.");

        var attempt = await db.PayOsAttempts.SingleOrDefaultAsync(row => row.OrderCode == orderCode, cancellationToken);
        if (attempt is not null && attempt.Kind == "Subscription")
        {
            var adminCreds = await GetAdminCredentialsAsync(cancellationToken)
                ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Payment channel is not configured.");

            if (!PayOsSignature.VerifyData(adminCreds.ChecksumKey, data, signatureElement.GetString()))
                throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid payOS signature.");

            var providerCode = ReadString(data, "code");
            if (!root.TryGetProperty("success", out var successElement) || successElement.ValueKind != JsonValueKind.True)
                return true;
            if (providerCode != "00") return true;

            var reference = ReadString(data, "reference");
            if (string.IsNullOrWhiteSpace(reference))
                throw new ApiException(StatusCodes.Status400BadRequest, "Payment callback is missing its transaction reference.");

            if (attempt.Status == PaymentAttemptStatus.Paid)
            {
                if (string.Equals(attempt.ProviderReference, reference, StringComparison.Ordinal)) return true;
                throw new ApiException(StatusCodes.Status409Conflict, "This payOS order was already processed with another transaction reference.");
            }
            if (amount != attempt.Amount) throw new ApiException(StatusCodes.Status409Conflict, "Payment amount does not match the subscription order.");

            await _webhookService.HandleSubscriptionPaymentSuccessAsync(attempt, amount, reference, cancellationToken);
            return true;
        }

        // Invoice callbacks must remain scoped to the owner in the callback URL.
        // Subscription callbacks above use the central admin channel separately.
        if (attempt is not null && attempt.OwnerId != ownerId) attempt = null;
        var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(row => row.OwnerId == ownerId, cancellationToken);
        if (settings is null) throw new ApiException(StatusCodes.Status401Unauthorized, "Payment channel is not configured.");

        if (!PayOsSignature.VerifyData(Decrypt(settings).ChecksumKey, data, signatureElement.GetString()))
            throw new ApiException(StatusCodes.Status401Unauthorized, "Invalid payOS signature.");

        var invoiceProviderCode = ReadString(data, "code");
        if (!root.TryGetProperty("success", out var invoiceSuccess) || invoiceSuccess.ValueKind != JsonValueKind.True)
            return true;
        if (invoiceProviderCode != "00") return true;

        if (attempt is null) return true;

        var invoiceReference = ReadString(data, "reference");
        if (string.IsNullOrWhiteSpace(invoiceReference))
            throw new ApiException(StatusCodes.Status400BadRequest, "Payment callback is missing its transaction reference.");
        if (attempt.Status == PaymentAttemptStatus.Paid)
        {
            if (string.Equals(attempt.ProviderReference, invoiceReference, StringComparison.Ordinal)) return true;
            throw new ApiException(StatusCodes.Status409Conflict, "This payOS order was already processed with another transaction reference.");
        }
        if (amount != attempt.Amount) throw new ApiException(StatusCodes.Status409Conflict, "Payment amount does not match the invoice order.");

        await RecordSuccessfulPaymentAsync(attempt.OwnerId, attempt, amount, invoiceReference, cancellationToken);
        return true;
    }


}

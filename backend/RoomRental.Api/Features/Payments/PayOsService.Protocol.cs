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
    private (string ClientId, string ApiKey, string ChecksumKey) Decrypt(OwnerPaymentSettings settings)
    {
        try
        {
            return (settings.ClientId, _protector.Unprotect(settings.ProtectedApiKey), _protector.Unprotect(settings.ProtectedChecksumKey));
        }
        catch (CryptographicException exception)
        {
            logger.LogError(exception, "Stored payOS credentials could not be decrypted for owner {OwnerId}.", settings.OwnerId);
            throw new ApiException(StatusCodes.Status500InternalServerError, "Saved payment credentials cannot be read. Save the payment settings again.");
        }
    }

    private static bool IsPaymentReservationConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected)
                return true;
            if (current is SqliteException sqlite && (sqlite.SqliteErrorCode is 5 or 6 || sqlite.SqliteExtendedErrorCode == 2067))
                return true;
        }
        return false;
    }

    private static long NewOrderCode() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1_000 + RandomNumberGenerator.GetInt32(0, 1_000);

    private static PaymentCheckoutDto ToCheckoutDto(PayOsAttempt attempt)
    {
        var providerStatus = NormalizeStatus(attempt.ProviderStatus);
        var status = attempt.Status switch
        {
            PaymentAttemptStatus.Paid => "paid",
            PaymentAttemptStatus.Failed => providerStatus switch
            {
                "cancelled" => "cancelled",
                "expired" => "expired",
                _ => "failed"
            },
            // A provider status alone never confirms payment; only the protected reconciliation
            // path that records the matching amount/transaction may move the attempt to Paid.
            _ => "pending"
        };
        return new PaymentCheckoutDto(attempt.CheckoutUrl!, attempt.QrCode, attempt.Amount, attempt.OrderCode,
            attempt.AccountName, attempt.AccountNumber, attempt.BankBin, attempt.Description, status, attempt.ExpiresAt);
    }

    private static string? NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();

    private static bool TryGetLong(JsonElement data, string name, out long value)
    {
        value = 0;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var property)) return false;
        return property.ValueKind == JsonValueKind.Number ? property.TryGetInt64(out value) :
            property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDecimal(JsonElement data, string name, out decimal value)
    {
        value = 0;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var property)) return false;
        return property.ValueKind == JsonValueKind.Number ? property.TryGetDecimal(out value) :
            property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(),
                System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string? ReadString(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ValueKind == JsonValueKind.Number ? property.GetRawText() : null;
    }

    private static string? TryFindTransactionReference(JsonElement data)
    {
        if (!data.TryGetProperty("transactions", out var transactions)) return null;
        var pending = new Stack<JsonElement>();
        pending.Push(transactions);
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("reference", out var reference) && reference.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(reference.GetString()))
                    return reference.GetString();
                foreach (var property in item.EnumerateObject()) pending.Push(property.Value);
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in item.EnumerateArray()) pending.Push(child);
            }
        }
        return null;
    }
}

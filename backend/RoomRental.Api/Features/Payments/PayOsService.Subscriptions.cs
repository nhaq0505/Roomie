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
    public async Task<(string ClientId, string ApiKey, string ChecksumKey)?> GetAdminCredentialsAsync(CancellationToken cancellationToken)
    {
        var configClientId = configuration["AdminPayOS:ClientId"] ?? configuration["PAYOS_ADMIN_CLIENT_ID"] ?? configuration["PAYOS_CLIENT_ID"];
        var configApiKey = configuration["AdminPayOS:ApiKey"] ?? configuration["PAYOS_ADMIN_API_KEY"] ?? configuration["PAYOS_API_KEY"];
        var configChecksumKey = configuration["AdminPayOS:ChecksumKey"] ?? configuration["PAYOS_ADMIN_CHECKSUM_KEY"] ?? configuration["PAYOS_CHECKSUM_KEY"];
        if (!string.IsNullOrWhiteSpace(configClientId) && !string.IsNullOrWhiteSpace(configApiKey) && !string.IsNullOrWhiteSpace(configChecksumKey))
            return (configClientId, configApiKey, configChecksumKey);

        var adminUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Admin, cancellationToken);
        if (adminUser is not null)
        {
            var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == adminUser.Id, cancellationToken);
            if (settings is not null) return Decrypt(settings);
        }

        var anySettings = await db.PaymentSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (anySettings is not null) return Decrypt(anySettings);

        return null;
    }

    public async Task<PaymentCheckoutDto> CreateSubscriptionCheckoutAsync(Guid ownerId, string? packageId, CancellationToken cancellationToken)
    {
        var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == ownerId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
        if (owner.Role != UserRole.Owner || owner.Status != OwnerStatus.Approved)
            throw new ApiException(StatusCodes.Status403Forbidden, "Tài khoản chủ trọ chưa được duyệt để nâng cấp gói cước.");

        var package = DefaultPackages.FirstOrDefault(p => p.Id == packageId) ?? DefaultPackages[0];
        var amount = decimal.ToInt32(package.Price);

        PayOsAttempt? existing = null;
        PayOsAttempt? attempt = null;
        (string ClientId, string ApiKey, string ChecksumKey)? credentials = null;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);

            existing = await db.PayOsAttempts
                .Where(row => row.OwnerId == ownerId && row.Kind == "Subscription" && row.Status == PaymentAttemptStatus.Pending)
                .OrderByDescending(row => row.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is null)
            {
                credentials = await GetAdminCredentialsAsync(cancellationToken)
                    ?? throw new ApiException(StatusCodes.Status400BadRequest, "Hệ thống chưa thiết lập cấu hình payOS của quản trị viên.");

                var createdAt = DateTimeOffset.UtcNow;
                var expiresAt = checked((int)createdAt.AddSeconds(CheckoutExpirySeconds).ToUnixTimeSeconds());
                attempt = new PayOsAttempt
                {
                    OwnerId = ownerId,
                    InvoiceId = Guid.NewGuid(),
                    Kind = "Subscription",
                    SubscriptionDays = package.Days,
                    OrderCode = NewOrderCode(),
                    Amount = amount,
                    Status = PaymentAttemptStatus.Pending,
                    CreatedAt = createdAt,
                    ExpiresAt = expiresAt
                };
                db.PayOsAttempts.Add(attempt);
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsPaymentReservationConflict(exception))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "Giao dịch nâng cấp đang được xử lý. Vui lòng thử lại sau giây lát.");
        }

        if (existing is not null)
        {
            var reconciled = await ReconcileSubscriptionAttemptAsync(ownerId, existing, cancellationToken);
            if (existing.Status == PaymentAttemptStatus.Paid)
                throw new ApiException(StatusCodes.Status409Conflict, "Gói cước đã được thanh toán thành công.");
            if (existing.Status == PaymentAttemptStatus.Pending && !string.IsNullOrWhiteSpace(existing.CheckoutUrl))
                return ToCheckoutDto(existing);
            if (existing.Status == PaymentAttemptStatus.Failed && reconciled is "Cancelled" or "Expired")
                throw new ApiException(StatusCodes.Status409Conflict, "Mã thanh toán trước đó đã hết hạn hoặc bị hủy. Vui lòng tạo lại.");
            throw new ApiException(StatusCodes.Status409Conflict, "Đang chuẩn bị mã thanh toán. Vui lòng đối soát lại trạng thái.");
        }

        var paymentCredentials = credentials!.Value;
        var reservedAttempt = attempt!;

        var publicBaseUrl = configuration["PublicBaseUrl"] ?? configuration["APP_PUBLIC_URL"] ?? "http://localhost:5173";
        if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri))
        {
            reservedAttempt.Status = PaymentAttemptStatus.Failed;
            await db.SaveChangesAsync(cancellationToken);
            throw new ApiException(StatusCodes.Status500InternalServerError, "PublicBaseUrl must be configured as an absolute URL.");
        }

        var returnUrl = new Uri(baseUri, "/account-status?payment=returned&orderCode=" + reservedAttempt.OrderCode).ToString();
        var cancelUrl = new Uri(baseUri, "/account-status?payment=cancelled&orderCode=" + reservedAttempt.OrderCode).ToString();
        var description = $"PRO{reservedAttempt.OrderCode % 100_000_000:D8}";
        var signedFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount"] = reservedAttempt.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cancelUrl"] = cancelUrl,
            ["description"] = description,
            ["orderCode"] = reservedAttempt.OrderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["returnUrl"] = returnUrl
        };
        var body = new
        {
            orderCode = reservedAttempt.OrderCode,
            amount = reservedAttempt.Amount,
            description,
            buyerName = owner.Name,
            cancelUrl,
            returnUrl,
            expiredAt = reservedAttempt.ExpiresAt!.Value,
            signature = PayOsSignature.Create(paymentCredentials.ChecksumKey, signedFields)
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v2/payment-requests")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("x-client-id", paymentCredentials.ClientId);
            request.Headers.TryAddWithoutValidation("x-api-key", paymentCredentials.ApiKey);
            using var response = await httpClientFactory.CreateClient("payOS").SendAsync(request, cancellationToken);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = json.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            if (!response.IsSuccessStatusCode || code != "00")
            {
                if ((int)response.StatusCode is >= 400 and < 500 && (int)response.StatusCode is not (408 or 429) || response.IsSuccessStatusCode)
                {
                    reservedAttempt.Status = PaymentAttemptStatus.Failed;
                    await db.SaveChangesAsync(cancellationToken);
                }
                logger.LogWarning("payOS subscription checkout creation failed with HTTP {StatusCode} and response code {ProviderCode}.", (int)response.StatusCode, code);
                throw new ApiException(StatusCodes.Status502BadGateway, "payOS could not create the subscription payment link. Check the credentials and try again.");
            }

            if (!root.TryGetProperty("data", out var data) || !root.TryGetProperty("signature", out var signatureElement) ||
                !PayOsSignature.VerifyData(paymentCredentials.ChecksumKey, data, signatureElement.GetString()) ||
                !TryGetLong(data, "orderCode", out var responseOrderCode) || responseOrderCode != reservedAttempt.OrderCode ||
                !TryGetDecimal(data, "amount", out var responseAmount) || responseAmount != reservedAttempt.Amount ||
                !data.TryGetProperty("checkoutUrl", out var checkoutUrlElement) || string.IsNullOrWhiteSpace(checkoutUrlElement.GetString()))
                throw new ApiException(StatusCodes.Status502BadGateway, "payOS returned an invalid or unsigned payment link. Reconcile the order before retrying.");

            if (TryGetLong(data, "expiredAt", out var providerExpiresAt) && providerExpiresAt != reservedAttempt.ExpiresAt)
                throw new ApiException(StatusCodes.Status502BadGateway, "payOS returned a payment link with an unexpected expiry. Reconcile the order before retrying.");

            reservedAttempt.CheckoutUrl = checkoutUrlElement.GetString();
            reservedAttempt.PaymentLinkId = data.TryGetProperty("paymentLinkId", out var linkId) ? linkId.GetString() : null;
            reservedAttempt.QrCode = ReadString(data, "qrCode");
            reservedAttempt.AccountName = ReadString(data, "accountName");
            reservedAttempt.AccountNumber = ReadString(data, "accountNumber");
            reservedAttempt.BankBin = ReadString(data, "bin");
            reservedAttempt.Description = ReadString(data, "description");
            reservedAttempt.ProviderStatus = ReadString(data, "status");
            await db.SaveChangesAsync(cancellationToken);
            return ToCheckoutDto(reservedAttempt);
        }
        catch (ApiException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Could not reach or parse the payOS payment link response for subscription.");
            throw new ApiException(StatusCodes.Status502BadGateway, "payOS response is uncertain. Reconcile the order before retrying.");
        }
    }

    public async Task<object> ReconcileSubscriptionOrderAsync(Guid ownerId, long orderCode, CancellationToken cancellationToken)
    {
        var attempt = await db.PayOsAttempts.SingleOrDefaultAsync(row => row.OwnerId == ownerId && row.OrderCode == orderCode && row.Kind == "Subscription", cancellationToken);
        if (attempt is null) throw new ApiException(StatusCodes.Status404NotFound, "Subscription order was not found.");
        if (attempt.Status == PaymentAttemptStatus.Paid) return new { status = "Paid" };
        var providerStatus = await ReconcileSubscriptionAttemptAsync(ownerId, attempt, cancellationToken);
        return new { status = providerStatus, checkoutUrl = attempt.CheckoutUrl };
    }

    private async Task<string> ReconcileSubscriptionAttemptAsync(Guid ownerId, PayOsAttempt attempt, CancellationToken cancellationToken)
    {
        var credentials = await GetAdminCredentialsAsync(cancellationToken);
        if (credentials is null) return attempt.Status.ToString();
        var adminCreds = credentials.Value;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/v2/payment-requests/{attempt.OrderCode}");
        request.Headers.TryAddWithoutValidation("x-client-id", adminCreds.ClientId);
        request.Headers.TryAddWithoutValidation("x-api-key", adminCreds.ApiKey);
        using var response = await httpClientFactory.CreateClient("payOS").SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var providerCode = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        if (!response.IsSuccessStatusCode || providerCode != "00" || !root.TryGetProperty("data", out var data) ||
            !root.TryGetProperty("signature", out var signature) || !PayOsSignature.VerifyData(adminCreds.ChecksumKey, data, signature.GetString()) ||
            !TryGetLong(data, "orderCode", out var responseOrderCode) || responseOrderCode != attempt.OrderCode ||
            !TryGetDecimal(data, "amount", out var responseAmount) || responseAmount != attempt.Amount)
            return attempt.Status.ToString();

        var status = ReadString(data, "status") ?? "Pending";
        attempt.ProviderStatus = status;
        if (status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            await db.SaveChangesAsync(cancellationToken);
            return status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) ? "Cancelled" : "Expired";
        }

        var linkId = ReadString(data, "id") ?? ReadString(data, "paymentLinkId");
        if (string.IsNullOrWhiteSpace(attempt.CheckoutUrl) && !string.IsNullOrWhiteSpace(linkId))
        {
            attempt.PaymentLinkId = linkId;
            attempt.CheckoutUrl = $"https://pay.payos.vn/web/{Uri.EscapeDataString(linkId)}";
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!TryGetDecimal(data, "amountPaid", out var amountPaid) || amountPaid <= 0)
            return status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ? "Pending" : status;
        if (amountPaid != attempt.Amount) return "Partial";

        var reference = TryFindTransactionReference(data) ?? ReadString(data, "id") ?? $"reconcile-{attempt.OrderCode}";
        await _webhookService.HandleSubscriptionPaymentSuccessAsync(attempt, amountPaid, reference, cancellationToken);
        return "Paid";
    }


}

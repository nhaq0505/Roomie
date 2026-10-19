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
    private readonly AppDbContext db;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    private readonly ILogger<PayOsService> logger;
    private readonly IDataProtector _protector;
    private readonly PaymentWebhookService _webhookService;
    private static readonly ConcurrentDictionary<long, DateTimeOffset> LastReconcileAttemptByOrder = new();
    private const string ApiBaseUrl = "https://api-merchant.payos.vn";
    private const int CheckoutExpirySeconds = 600;
    public PayOsService(AppDbContext db, IDataProtectionProvider protectionProvider, IHttpClientFactory httpClientFactory,
        IConfiguration configuration, ILogger<PayOsService> logger, PaymentWebhookService? webhookService = null)
    {
        this.db=db; this.httpClientFactory=httpClientFactory; this.configuration=configuration; this.logger=logger;
        _protector=protectionProvider.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
        _webhookService=webhookService ?? new PaymentWebhookService(db,protectionProvider,httpClientFactory,configuration);
    }

    public static readonly IReadOnlyList<SubscriptionPackageDto> DefaultPackages =
    [
        new("pro_1m", "Gói Pro 1 tháng", 30, 99000, "Không giới hạn số phòng, hỗ trợ ưu tiên trong 30 ngày"),
        new("pro_3m", "Gói Pro 3 tháng", 90, 269000, "Không giới hạn số phòng, tiết kiệm 10% trong 90 ngày"),
        new("pro_1y", "Gói Pro 1 năm", 365, 899000, "Không giới hạn số phòng, tiết kiệm 25% trong 365 ngày")
    ];

    public async Task<string> CreateOrGetCheckoutUrlAsync(Guid ownerId, Invoice invoice, string tenantToken, CancellationToken cancellationToken) =>
        (await CreateOrGetCheckoutAsync(ownerId, invoice, tenantToken, cancellationToken)).CheckoutUrl;

    public async Task<PaymentCheckoutDto> CreateOrGetCheckoutAsync(Guid ownerId, Invoice invoice, string tenantToken, CancellationToken cancellationToken)
    {
        if (invoice.OwnerId != ownerId) throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found.");
        PayOsAttempt? existing = null;
        PayOsAttempt? attempt = null;
        (string ClientId, string ApiKey, string ChecksumKey)? credentials = null;
        try
        {
            // Reserve one pending payment per invoice under a serializable transaction.
            // The partial unique index also protects this invariant across app instances.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == ownerId, cancellationToken);
            if (owner is null || owner.Role != UserRole.Owner || owner.Status != OwnerStatus.Approved)
                throw new ApiException(StatusCodes.Status403Forbidden, "The owner account is not approved to accept new payments.");

            var currentInvoice = await db.Invoices.SingleOrDefaultAsync(row => row.Id == invoice.Id && row.OwnerId == ownerId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found.");
            await db.Entry(currentInvoice).ReloadAsync(cancellationToken);
            if (currentInvoice.LifecycleState != InvoiceLifecycleState.Issued)
                throw new ApiException(StatusCodes.Status409Conflict, "Draft invoices cannot accept payOS payments. Publish the invoice first.");
            var outstanding = ApiSupport.RoundVnd(currentInvoice.Total - currentInvoice.Paid);
            if (outstanding <= 0) throw new ApiException(StatusCodes.Status409Conflict, "This invoice is already paid.");

            existing = await db.PayOsAttempts
                .Where(row => row.OwnerId == ownerId && row.InvoiceId == currentInvoice.Id && row.Status == PaymentAttemptStatus.Pending)
                .OrderByDescending(row => row.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is null)
            {
                var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(row => row.OwnerId == ownerId, cancellationToken);
                if (settings is null) throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng thiết lập payOS trước khi tạo liên kết thanh toán.");
                credentials = Decrypt(settings);

                if (outstanding > int.MaxValue)
                    throw new ApiException(StatusCodes.Status400BadRequest, "Invoice amount exceeds the payOS supported limit.");
                var amount = decimal.ToInt32(outstanding);
                if (amount <= 0) throw new ApiException(StatusCodes.Status400BadRequest, "Payment amount must be at least 1 VND.");

                var createdAt = DateTimeOffset.UtcNow;
                var expiresAt = checked((int)createdAt.AddSeconds(CheckoutExpirySeconds).ToUnixTimeSeconds());
                attempt = new PayOsAttempt
                {
                    OwnerId = ownerId,
                    InvoiceId = currentInvoice.Id,
                    OrderCode = NewOrderCode(),
                    Amount = amount,
                    Status = PaymentAttemptStatus.Pending,
                    CreatedAt = createdAt,
                    ExpiresAt = expiresAt
                };
                db.PayOsAttempts.Add(attempt);
                currentInvoice.Version++;
                await db.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsPaymentReservationConflict(exception))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "A payment attempt is already being created for this invoice. Retry shortly.");
        }

        if (existing is not null)
        {
            var reconciled = await ReconcileAttemptAsync(ownerId, existing, cancellationToken);
            if (existing.Status == PaymentAttemptStatus.Paid)
                throw new ApiException(StatusCodes.Status409Conflict, "This invoice is already paid.");
            if (existing.Status == PaymentAttemptStatus.Pending && !string.IsNullOrWhiteSpace(existing.CheckoutUrl)) return ToCheckoutDto(existing);
            if (existing.Status == PaymentAttemptStatus.Failed && reconciled is "Cancelled" or "Expired")
                throw new ApiException(StatusCodes.Status409Conflict, "The previous payOS order was cancelled. Start a new payment attempt.");
            throw new ApiException(StatusCodes.Status409Conflict, "A payment link is still being prepared. Retry after the order status has been reconciled.");
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
        var tenantPath = $"/t/{Uri.EscapeDataString(tenantToken)}";
        var returnUrl = new Uri(baseUri, tenantPath + "?payment=returned").ToString();
        var cancelUrl = new Uri(baseUri, tenantPath + "?payment=cancelled").ToString();
        var description = $"P{reservedAttempt.OrderCode % 100_000_000:D8}";
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
            buyerName = invoice.TenantName,
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
                logger.LogWarning("payOS checkout creation failed with HTTP {StatusCode} and response code {ProviderCode}.", (int)response.StatusCode, code);
                throw new ApiException(StatusCodes.Status502BadGateway, "payOS could not create the payment link. Check the credentials and try again.");
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
            // The create request's Unix expiry is persisted before contacting payOS.
            // The response schema may omit it; if signed metadata includes it, the
            // check above requires the exact requested timestamp.
            await db.SaveChangesAsync(cancellationToken);
            return ToCheckoutDto(reservedAttempt);
        }
        catch (ApiException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Could not reach or parse the payOS payment link response.");
            throw new ApiException(StatusCodes.Status502BadGateway, "payOS response is uncertain. Reconcile the order before retrying.");
        }
    }


}

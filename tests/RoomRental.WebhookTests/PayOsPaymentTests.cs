using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class PayOsPaymentTests
{
    private const string ChecksumKey = "synthetic-checksum-key-only-for-in-process-tests";
    private const string NonPayableQr = "SYNTHETIC-QA-QR-NOT-PAYABLE";
    private const string HostedCheckoutUrl = "https://pay.payos.vn/web/synthetic-qa-link";

    public static async Task RunAsync()
    {
        await SignedQrRoundTripAndPendingReuseAsync();
        await MissingProviderExpiryUsesPersistedRequestFallbackAsync();
        await ExpiredLocalQrRemainsPendingUntilProviderConfirmsExpiryAsync();
        await ProviderConfirmedTerminalStatusAllowsNewOrderNextTryAsync("EXPIRED");
        await ProviderConfirmedTerminalStatusAllowsNewOrderNextTryAsync("CANCELLED");
        await LegacyPendingUsesHostedFallbackAsync();
        await ProviderPaidStatusAloneDoesNotConfirmAsync();
        await ReconcilePaidStatusWithoutMoneyDoesNotConfirmAsync();
        await InvalidCreateResponseIsRejectedAsync(CreateResponseFault.InvalidSignature, "invalid checkout signature is rejected");
        await InvalidCreateResponseIsRejectedAsync(CreateResponseFault.WrongAmount, "checkout amount mismatch is rejected");
        await InvalidCreateResponseIsRejectedAsync(CreateResponseFault.WrongOrder, "checkout order mismatch is rejected");
        await InvalidCreateResponseIsRejectedAsync(CreateResponseFault.ExpiredAtMismatch, "unexpected signed checkout expiry is rejected");
        await WebhookRemainsTheOnlyPaymentConfirmationAsync();
        await SubscriptionPaymentAndWebhookActivatesProAsync();
    }

    private static async Task SignedQrRoundTripAndPendingReuseAsync()
    {
        using var fixture = new PayOsFixture();
        var handler = new FakePayOsHandler(ChecksumKey);
        var service = fixture.CreateService(handler);

        var first = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        Assert(first.CheckoutUrl == HostedCheckoutUrl, "signed checkout URL round-trips");
        Assert(first.QrCode == NonPayableQr, "signed non-payable QR payload round-trips");
        Assert(first.Amount == 100_000, "checkout amount is the current outstanding balance");
        Assert(first.OrderCode > 0, "checkout order code is returned");
        Assert(first.AccountName == "Synthetic QA Owner" && first.AccountNumber == "0000000000" && first.BankBin == "999999",
            "signed destination account metadata round-trips");
        Assert(first.Description == handler.LastDescription && first.Status == "pending",
            "signed description and pending status round-trip");

        var stored = await fixture.Db.PayOsAttempts.SingleAsync();
        Assert(stored.OrderCode == first.OrderCode && stored.Amount == first.Amount && stored.Status == PaymentAttemptStatus.Pending,
            "checkout attempt persists with pending status");
        Assert(stored.CheckoutUrl == first.CheckoutUrl && stored.QrCode == NonPayableQr && stored.AccountName == first.AccountName &&
               stored.AccountNumber == first.AccountNumber && stored.BankBin == first.BankBin && stored.Description == first.Description &&
               stored.ProviderStatus == "PENDING" && stored.ExpiresAt == first.ExpiresAt,
            "verified QR metadata persists with its signed payment attempt");
        Assert(stored.ExpiresAt == stored.CreatedAt.AddSeconds(600).ToUnixTimeSeconds() && handler.LastRequestedExpiry == stored.ExpiresAt,
            "new payment order requests and persists a Unix expiry exactly 600 seconds after reservation");
        Assert(handler.LastRequestSignatureValid,
            "expiredAt remains outside the payOS request signature's documented canonical fields");

        var reused = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        var expiryAfterReuse = (await fixture.Db.PayOsAttempts.SingleAsync()).ExpiresAt;
        Assert(reused.OrderCode == first.OrderCode && reused.CheckoutUrl == first.CheckoutUrl && reused.QrCode == first.QrCode &&
               reused.ExpiresAt == first.ExpiresAt && expiryAfterReuse == first.ExpiresAt,
            "pending checkout reuses the same stored order, QR, and original expiry without extending it");
        Assert(handler.CreateCalls == 1 && await fixture.Db.PayOsAttempts.CountAsync() == 1,
            "pending reuse does not create another provider order or local attempt");
        Assert(handler.UnexpectedExternalRequests == 0, "all provider traffic is intercepted in-process");
    }

    private static async Task ExpiredLocalQrRemainsPendingUntilProviderConfirmsExpiryAsync()
    {
        using var fixture = new PayOsFixture();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        const long orderCode = 8_765_432_104;
        fixture.Db.PayOsAttempts.Add(new PayOsAttempt
        {
            OwnerId = fixture.OwnerId,
            InvoiceId = fixture.Invoice.Id,
            OrderCode = orderCode,
            Amount = 100_000,
            CheckoutUrl = HostedCheckoutUrl,
            QrCode = NonPayableQr,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-11),
            ExpiresAt = expiresAt
        });
        await fixture.Db.SaveChangesAsync();

        var handler = new FakePayOsHandler(ChecksumKey);
        handler.AddKnownOrder(orderCode, 100_000);
        var service = fixture.CreateService(handler);
        var result = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        var stored = await fixture.Db.PayOsAttempts.SingleAsync();

        Assert(result.Status == "pending" && result.ExpiresAt == expiresAt && stored.Status == PaymentAttemptStatus.Pending && stored.ExpiresAt == expiresAt,
            "a locally elapsed QR expiry does not mark the order failed or change its expiry");
        Assert(handler.CreateCalls == 0 && handler.ReconcileCalls == 1 && await fixture.Db.PayOsAttempts.CountAsync() == 1,
            "an elapsed pending order is reconciled with the provider rather than replaced locally");
    }

    private static async Task MissingProviderExpiryUsesPersistedRequestFallbackAsync()
    {
        using var fixture = new PayOsFixture();
        var handler = new FakePayOsHandler(ChecksumKey) { CreateFault = CreateResponseFault.ExpiryOmitted };
        var service = fixture.CreateService(handler);
        var result = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        var stored = await fixture.Db.PayOsAttempts.SingleAsync();

        Assert(result.ExpiresAt == handler.LastRequestedExpiry && stored.ExpiresAt == handler.LastRequestedExpiry,
            "when create response omits expiredAt, the stored request expiry is returned as the fallback");
    }

    private static async Task ProviderConfirmedTerminalStatusAllowsNewOrderNextTryAsync(string terminalStatus)
    {
        using var fixture = new PayOsFixture();
        const long orderCode = 8_765_432_105;
        fixture.Db.PayOsAttempts.Add(new PayOsAttempt
        {
            OwnerId = fixture.OwnerId,
            InvoiceId = fixture.Invoice.Id,
            OrderCode = orderCode,
            Amount = 100_000,
            CheckoutUrl = HostedCheckoutUrl,
            QrCode = NonPayableQr,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-11),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()
        });
        await fixture.Db.SaveChangesAsync();

        var handler = new FakePayOsHandler(ChecksumKey) { ReconcileProviderStatus = terminalStatus };
        handler.AddKnownOrder(orderCode, 100_000);
        var service = fixture.CreateService(handler);
        var providerConfirmed = false;
        try { await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default); }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status409Conflict) { providerConfirmed = true; }
        Assert(providerConfirmed && await fixture.Db.PayOsAttempts.SingleAsync() is { Status: PaymentAttemptStatus.Failed },
            $"provider-confirmed {terminalStatus} marks the old attempt terminal");

        var next = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        Assert(next.Status == "pending" && handler.ReconcileCalls == 1 && handler.CreateCalls == 1 &&
               await fixture.Db.PayOsAttempts.CountAsync() == 2,
            $"a new payment order can be created on the next request after provider confirms {terminalStatus}");
    }

    private static async Task LegacyPendingUsesHostedFallbackAsync()
    {
        using var fixture = new PayOsFixture();
        const long orderCode = 8_765_432_101;
        fixture.Db.PayOsAttempts.Add(new PayOsAttempt
        {
            OwnerId = fixture.OwnerId,
            InvoiceId = fixture.Invoice.Id,
            OrderCode = orderCode,
            Amount = 100_000,
            CheckoutUrl = HostedCheckoutUrl,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await fixture.Db.SaveChangesAsync();

        var handler = new FakePayOsHandler(ChecksumKey);
        handler.AddKnownOrder(orderCode, 100_000);
        var service = fixture.CreateService(handler);
        var result = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);

        Assert(result.OrderCode == orderCode && result.QrCode == null,
            "legacy pending checkout without QR metadata remains explicitly QR-less");
        Assert(result.CheckoutUrl == HostedCheckoutUrl && result.Status == "pending",
            "legacy pending checkout retains its hosted-page fallback");
        Assert(result.ExpiresAt is null && (await fixture.Db.PayOsAttempts.SingleAsync()).ExpiresAt is null,
            "legacy pending attempts without stored expiry do not get a fabricated deadline");
        Assert(handler.CreateCalls == 0 && await fixture.Db.PayOsAttempts.CountAsync() == 1,
            "legacy checkout reuse creates no second payment attempt");
    }

    private static async Task ProviderPaidStatusAloneDoesNotConfirmAsync()
    {
        using var fixture = new PayOsFixture();
        var handler = new FakePayOsHandler(ChecksumKey) { CreateProviderStatus = "PAID" };
        var service = fixture.CreateService(handler);
        var result = await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);

        Assert(result.Status == "pending", "provider PAID status alone does not mark a checkout paid");
        Assert(await fixture.Db.PayOsAttempts.SingleAsync() is { Status: PaymentAttemptStatus.Pending, ProviderStatus: "PAID" } &&
               await fixture.Db.Receipts.CountAsync() == 0 && await fixture.Db.Invoices.SingleAsync() is { Paid: 25_000 },
            "provider PAID status without confirmed transaction data creates no receipt or invoice credit");
    }

    private static async Task ReconcilePaidStatusWithoutMoneyDoesNotConfirmAsync()
    {
        using var fixture = new PayOsFixture();
        const long orderCode = 8_765_432_103;
        fixture.Db.PayOsAttempts.Add(new PayOsAttempt
        {
            OwnerId = fixture.OwnerId,
            InvoiceId = fixture.Invoice.Id,
            OrderCode = orderCode,
            Amount = 100_000,
            CheckoutUrl = HostedCheckoutUrl,
            QrCode = NonPayableQr,
            Status = PaymentAttemptStatus.Pending
        });
        await fixture.Db.SaveChangesAsync();

        var handler = new FakePayOsHandler(ChecksumKey) { ReconcileProviderStatus = "PAID", ReconcileAmountPaid = 0 };
        handler.AddKnownOrder(orderCode, 100_000);
        var service = fixture.CreateService(handler);
        var status = await service.ReconcileInvoiceAsync(fixture.OwnerId, fixture.Invoice.Id, default);

        Assert(status == "Pending", "reconcile requires a matching paid amount before confirming a PAID provider status");
        Assert(await fixture.Db.PayOsAttempts.SingleAsync() is { Status: PaymentAttemptStatus.Pending } &&
               await fixture.Db.Receipts.CountAsync() == 0 && await fixture.Db.Invoices.SingleAsync() is { Paid: 25_000 },
            "reconcile without a matching amount creates no receipt or invoice credit");
    }

    private static async Task InvalidCreateResponseIsRejectedAsync(CreateResponseFault fault, string name)
    {
        using var fixture = new PayOsFixture();
        var handler = new FakePayOsHandler(ChecksumKey) { CreateFault = fault };
        var service = fixture.CreateService(handler);
        var rejected = false;
        try
        {
            await service.CreateOrGetCheckoutAsync(fixture.OwnerId, fixture.Invoice, "synthetic-tenant-token", default);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status502BadGateway)
        {
            rejected = true;
        }

        Assert(rejected, name);
        Assert(handler.CreateCalls == 1 && await fixture.Db.Receipts.CountAsync() == 0,
            "rejected checkout response creates no receipt");
        Assert(handler.UnexpectedExternalRequests == 0, "checkout validation uses only the in-process provider fake");
    }

    private static async Task WebhookRemainsTheOnlyPaymentConfirmationAsync()
    {
        using var fixture = new PayOsFixture();
        const long orderCode = 8_765_432_102;
        fixture.Db.PayOsAttempts.Add(new PayOsAttempt
        {
            OwnerId = fixture.OwnerId,
            InvoiceId = fixture.Invoice.Id,
            OrderCode = orderCode,
            Amount = 100_000,
            CheckoutUrl = HostedCheckoutUrl,
            QrCode = NonPayableQr,
            Status = PaymentAttemptStatus.Pending
        });
        await fixture.Db.SaveChangesAsync();

        var handler = new FakePayOsHandler(ChecksumKey);
        var service = fixture.CreateService(handler);
        using var webhook = CreateSignedWebhook(orderCode, 100_000, "synthetic-reference-1", ChecksumKey);

        var originalSettings = await fixture.Db.PaymentSettings.SingleAsync();
        var otherOwner = new AppUser { Name = "Other synthetic owner", Email = "other@example.invalid", PasswordHash = "unused", Role = UserRole.Owner, Status = OwnerStatus.Approved };
        fixture.Db.Users.Add(otherOwner);
        fixture.Db.PaymentSettings.Add(new OwnerPaymentSettings
        {
            OwnerId = otherOwner.Id, ClientId = originalSettings.ClientId,
            ProtectedApiKey = originalSettings.ProtectedApiKey, ProtectedChecksumKey = originalSettings.ProtectedChecksumKey
        });
        await fixture.Db.SaveChangesAsync();
        Assert(await service.ProcessWebhookAsync(otherOwner.Id, webhook.RootElement, default),
            "another owner's callback safely acknowledges an unknown invoice order");
        Assert(await fixture.Db.Receipts.CountAsync() == 0 && fixture.Invoice.Paid == 25_000 &&
               await fixture.Db.PayOsAttempts.SingleAsync() is { Status: PaymentAttemptStatus.Pending },
            "even a shared signing key cannot credit another owner's invoice");
        var unconfiguredRejected = false;
        try { await service.ProcessWebhookAsync(Guid.NewGuid(), webhook.RootElement, default); }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status401Unauthorized) { unconfiguredRejected = true; }
        Assert(unconfiguredRejected && await fixture.Db.Receipts.CountAsync() == 0,
            "an unconfigured callback owner cannot fall back to another owner's credentials");

        Assert(await service.ProcessWebhookAsync(fixture.OwnerId, webhook.RootElement, default), "valid signed webhook is acknowledged");
        Assert(await fixture.Db.Receipts.CountAsync() == 1 &&
               await fixture.Db.PayOsAttempts.SingleAsync() is { Status: PaymentAttemptStatus.Paid, ProviderReference: "synthetic-reference-1" },
            "webhook marks the order paid and creates one receipt");
        Assert(await fixture.Db.Invoices.SingleAsync() is { Paid: 125_000, Status: InvoiceStatus.Paid },
            "webhook credits only the checkout amount to the invoice");

        Assert(await service.ProcessWebhookAsync(fixture.OwnerId, webhook.RootElement, default), "duplicate signed webhook is acknowledged");
        Assert(await fixture.Db.Receipts.CountAsync() == 1 && await fixture.Db.ProcessedWebhooks.CountAsync() == 1 &&
               await fixture.Db.Invoices.SingleAsync() is { Paid: 125_000 },
            "webhook replay remains idempotent");

        using var wrongAmount = CreateSignedWebhook(orderCode, 99_999, "synthetic-reference-2", ChecksumKey);
        var wrongAmountRejected = false;
        try { await service.ProcessWebhookAsync(fixture.OwnerId, wrongAmount.RootElement, default); }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status409Conflict) { wrongAmountRejected = true; }
        Assert(wrongAmountRejected && await fixture.Db.Receipts.CountAsync() == 1,
            "validly signed webhook with wrong amount is rejected without another receipt");
        Assert(handler.Calls == 0, "webhook confirmation uses no provider request or browser return signal");
    }

    private static async Task SubscriptionPaymentAndWebhookActivatesProAsync()
    {
        using var fixture = new PayOsFixture();
        var handler = new FakePayOsHandler(ChecksumKey);
        var webhookService = new PaymentWebhookService(fixture.Db, fixture.Protection, handler, new ConfigurationBuilder().Build());
        var service = fixture.CreateService(handler, webhookService);

        var adminId = Guid.NewGuid();
        fixture.Db.Users.Add(new AppUser
        {
            Id = adminId,
            Name = "System Administrator",
            Email = "admin@system.local",
            PasswordHash = "unused-hash",
            Role = UserRole.Admin,
            Status = OwnerStatus.Approved
        });
        var protector = fixture.Protection.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
        fixture.Db.PaymentSettings.Add(new OwnerPaymentSettings
        {
            OwnerId = adminId,
            ClientId = "admin-client-id",
            ProtectedApiKey = protector.Protect("admin-api-key"),
            ProtectedChecksumKey = protector.Protect(ChecksumKey)
        });
        await fixture.Db.SaveChangesAsync();

        var checkout = await service.CreateSubscriptionCheckoutAsync(fixture.OwnerId, "pro_1m", default);
        Assert(checkout.OrderCode > 0, "subscription checkout order code is generated");
        Assert(checkout.Amount == 99_000, "subscription checkout amount matches package");

        var attempt = await fixture.Db.PayOsAttempts.SingleAsync(a => a.OrderCode == checkout.OrderCode);
        Assert(attempt.Kind == "Subscription" && attempt.SubscriptionDays == 30, "subscription attempt metadata stored correctly");

        using var webhook = CreateSignedWebhook(checkout.OrderCode, 99_000, "sub-ref-1", ChecksumKey);
        var processed = await service.ProcessWebhookAsync(adminId, webhook.RootElement, default);
        Assert(processed, "subscription webhook acknowledged");

        var owner = await fixture.Db.Users.FindAsync(fixture.OwnerId);
        Assert(owner!.IsPro && owner.SubscriptionTier == SubscriptionTier.Pro && owner.SubscriptionExpiresAt != null,
            "subscription webhook activates Pro status and sets expiration");

        var originalExpiry = owner.SubscriptionExpiresAt;
        var replayed = await service.ProcessWebhookAsync(adminId, webhook.RootElement, default);
        Assert(replayed, "duplicate subscription webhook acknowledged");
        Assert(owner.SubscriptionExpiresAt == originalExpiry, "subscription webhook replay is idempotent and preserves expiry");
    }

    private static JsonDocument CreateSignedWebhook(long orderCode, int amount, string reference, string checksumKey)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["code"] = "00",
            ["orderCode"] = orderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["reference"] = reference
        };
        var signature = PayOsSignature.Create(checksumKey, data);
        return JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, data, signature }));
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }
}

internal enum CreateResponseFault { None, InvalidSignature, WrongAmount, WrongOrder, ExpiredAtMismatch, ExpiryOmitted }

internal sealed class FakePayOsHandler(string checksumKey) : HttpMessageHandler, IHttpClientFactory
{
    private readonly Dictionary<long, int> _knownOrders = new();
    public CreateResponseFault CreateFault { get; init; }
    public int CreateCalls { get; private set; }
    public int ReconcileCalls { get; private set; }
    public int Calls { get; private set; }
    public int UnexpectedExternalRequests { get; private set; }
    public long? LastRequestedExpiry { get; private set; }
    public bool LastRequestSignatureValid { get; private set; }
    public string? LastDescription { get; private set; }
    public string CreateProviderStatus { get; init; } = "PENDING";
    public string ReconcileProviderStatus { get; init; } = "PENDING";
    public decimal ReconcileAmountPaid { get; init; }

    public HttpClient CreateClient(string name)
    {
        if (name != "payOS") throw new InvalidOperationException($"Unexpected HTTP client name: {name}");
        return new HttpClient(this, disposeHandler: false);
    }

    public void AddKnownOrder(long orderCode, int amount) => _knownOrders[orderCode] = amount;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (!Uri.TryCreate("https://api-merchant.payos.vn", UriKind.Absolute, out var allowedOrigin) ||
            request.RequestUri?.Scheme != allowedOrigin.Scheme || request.RequestUri?.Host != allowedOrigin.Host)
        {
            UnexpectedExternalRequests++;
            throw new InvalidOperationException("The in-process fake only permits the fixed payOS merchant origin.");
        }

        if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/v2/payment-requests")
        {
            CreateCalls++;
            using var requestJson = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = requestJson.RootElement;
            var orderCode = body.GetProperty("orderCode").GetInt64();
            var amount = body.GetProperty("amount").GetInt32();
            LastRequestedExpiry = body.GetProperty("expiredAt").GetInt32();
            LastDescription = body.GetProperty("description").GetString();
            var requestSignatureFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cancelUrl"] = body.GetProperty("cancelUrl").GetString()!,
                ["description"] = LastDescription ?? string.Empty,
                ["orderCode"] = orderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["returnUrl"] = body.GetProperty("returnUrl").GetString()!
            };
            LastRequestSignatureValid = body.GetProperty("signature").GetString() == PayOsSignature.Create(checksumKey, requestSignatureFields);
            if (!LastRequestSignatureValid) throw new InvalidOperationException("The request signature included unexpected fields.");
            _knownOrders[orderCode] = amount;
            var responseOrderCode = CreateFault == CreateResponseFault.WrongOrder ? orderCode + 1 : orderCode;
            var responseAmount = CreateFault == CreateResponseFault.WrongAmount ? amount + 1 : amount;
            var data = CreateSignedData(responseOrderCode, responseAmount, LastDescription ?? string.Empty, CreateProviderStatus, includeQr: true);
            return JsonResponse(data, invalidSignature: CreateFault == CreateResponseFault.InvalidSignature);
        }

        if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.StartsWith("/v2/payment-requests/", StringComparison.Ordinal))
        {
            ReconcileCalls++;
            if (!long.TryParse(request.RequestUri.AbsolutePath[("/v2/payment-requests/".Length)..], out var orderCode) ||
                !_knownOrders.TryGetValue(orderCode, out var amount))
                throw new InvalidOperationException("The fake received a reconciliation for an unknown order.");
            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["amountPaid"] = ReconcileAmountPaid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["orderCode"] = orderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["status"] = ReconcileProviderStatus
            };
            return JsonResponse(data);
        }

        throw new InvalidOperationException($"The fake received an unexpected provider request: {request.Method} {request.RequestUri?.AbsolutePath}");
    }

    private Dictionary<string, string> CreateSignedData(long orderCode, int amount, string description, string status, bool includeQr)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accountName"] = "Synthetic QA Owner",
            ["accountNumber"] = "0000000000",
            ["amount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bin"] = "999999",
            ["checkoutUrl"] = "https://pay.payos.vn/web/synthetic-qa-link",
            ["description"] = description,
            ["orderCode"] = orderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["paymentLinkId"] = "synthetic-qa-link-id",
            ["status"] = status
        };
        if (CreateFault != CreateResponseFault.ExpiryOmitted)
            data["expiredAt"] = (CreateFault == CreateResponseFault.ExpiredAtMismatch
                ? LastRequestedExpiry!.Value + 60
                : LastRequestedExpiry!.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (includeQr) data["qrCode"] = "SYNTHETIC-QA-QR-NOT-PAYABLE";
        return data;
    }

    private HttpResponseMessage JsonResponse(Dictionary<string, string> data, bool invalidSignature = false)
    {
        var signature = PayOsSignature.Create(checksumKey, data);
        if (invalidSignature) signature = new string('0', signature.Length);
        var body = JsonSerializer.Serialize(new { code = "00", data, signature });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

internal sealed class PayOsFixture : IDisposable
{
    private readonly PostgresTestDb _testDb;
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    public AppDbContext Db { get; }
    public Guid OwnerId { get; } = Guid.NewGuid();
    public Invoice Invoice { get; }
    public string PostgresTestConnection => _testDb.ConnectionString;

    public PayOsFixture()
    {
        _testDb = PostgresTestDb.CreateSync(ensureCreated: true);
        Db = _testDb.CreateDbContext();

        Db.Users.Add(new AppUser
        {
            Id = OwnerId,
            Name = "Synthetic QA Owner",
            Email = $"{OwnerId:N}@example.invalid",
            PasswordHash = "unused-test-hash",
            Role = UserRole.Owner,
            Status = OwnerStatus.Approved
        });

        var protector = _protection.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
        Db.PaymentSettings.Add(new OwnerPaymentSettings
        {
            OwnerId = OwnerId,
            ClientId = "synthetic-client-id",
            ProtectedApiKey = protector.Protect("synthetic-api-key"),
            ProtectedChecksumKey = protector.Protect("synthetic-checksum-key-only-for-in-process-tests")
        });

        Invoice = new Invoice
        {
            OwnerId = OwnerId,
            RoomId = Guid.NewGuid(),
            OccupancyId = Guid.NewGuid(),
            RoomName = "QA-ROOM-ONLY",
            BuildingName = "QA-BUILDING-ONLY",
            Period = "2026-10",
            TenantName = "Synthetic QA Tenant",
            Total = 125_000,
            Paid = 25_000,
            DueDate = new DateOnly(2026, 10, 31)
        };
        Db.Invoices.Add(Invoice);
        Db.SaveChanges();
    }

    public IDataProtectionProvider Protection => _protection;

    public PayOsService CreateService(IHttpClientFactory fakeHttpClientFactory, PaymentWebhookService? webhookService = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "http://localhost:5173"
        }).Build();
        return new PayOsService(Db, _protection, fakeHttpClientFactory, configuration, NullLogger<PayOsService>.Instance, webhookService);
    }

    public void Dispose()
    {
        Db.Dispose();
        _testDb.Dispose();
    }
}

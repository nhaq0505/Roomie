using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class PaymentWebhookService(AppDbContext db, IDataProtectionProvider protectionProvider,
    IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private string? WebhookUrl(Guid ownerId)
    {
        // Callback origin is controlled by the server, never by Host headers or client input.
        var configured = configuration["WebhookBaseUrl"] ?? configuration["PublicBaseUrl"] ?? configuration["APP_PUBLIC_URL"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.IsLoopback || !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment)) return null;
        return new Uri(origin, $"/api/payments/webhook/{ownerId}").AbsoluteUri;
    }

    private static string Version(OwnerPaymentSettings settings) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { settings.ClientId, settings.ProtectedApiKey,
            settings.ProtectedChecksumKey, settings.UpdatedAt }))));

    public async Task<PaymentSettingsDto> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        var url = WebhookUrl(ownerId);
        if (settings is null) return new(false, null, url, "NotConfigured", null, null);
        if (url is null) return new(true, settings.ClientId, null, "Failed", null,
            "Máy chủ chưa được cấu hình URL HTTPS công khai để nhận webhook.");
        var registration = await db.PaymentWebhookRegistrations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        if (registration is null || registration.SettingsVersion != Version(settings) || registration.WebhookUrl != url)
            return new(true, settings.ClientId, url, "Unverified", null, null);
        return new(true, settings.ClientId, url, registration.Status, registration.VerifiedAt, registration.Error);
    }

    public async Task<PaymentSettingsDto> ConfirmAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var settings = await db.PaymentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken)
            ?? throw new ApiException(400, "Vui lòng lưu cấu hình payOS trước khi xác minh webhook.");
        var url = WebhookUrl(ownerId);
        if (url is null) return await GetAsync(ownerId, cancellationToken);
        var status = "Failed";
        string? verifiedAt = null;
        string? error;
        try
        {
            var protector = protectionProvider.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api-merchant.payos.vn/confirm-webhook");
            request.Headers.Add("x-client-id", settings.ClientId);
            request.Headers.Add("x-api-key", protector.Unprotect(settings.ProtectedApiKey));
            request.Content = JsonContent.Create(new { webhookUrl = url });
            using var response = await httpClientFactory.CreateClient("payOS").SendAsync(request, cancellationToken);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = result.RootElement;
            if (response.IsSuccessStatusCode && root.TryGetProperty("code", out var code) && code.GetString() == "00" &&
                root.TryGetProperty("data", out var data) && data.TryGetProperty("webhookUrl", out var returnedUrl) &&
                returnedUrl.GetString() == url)
            {
                status = "Verified";
                verifiedAt = ApiSupport.UtcIsoString(DateTimeOffset.UtcNow);
                error = null;
            }
            else error = "payOS chưa xác minh được webhook. Kiểm tra bộ khóa và khả năng truy cập URL HTTPS rồi thử lại.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = "payOS phản hồi quá thời gian chờ. Cấu hình đã lưu; hãy thử xác minh lại.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or CryptographicException or InvalidOperationException)
        {
            error = "Không thể xác minh webhook với payOS. Cấu hình đã lưu; kiểm tra kết nối và bộ khóa rồi thử lại.";
        }

        // An upsert handles concurrent retries; DTO validation rejects results from older credentials/origins.
        var version = Version(settings);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentWebhookRegistrations" ("OwnerId", "SettingsVersion", "WebhookUrl", "Status", "VerifiedAt", "Error")
            VALUES ({ownerId}, {version}, {url}, {status}, {verifiedAt}, {error})
            ON CONFLICT ("OwnerId") DO UPDATE SET
                "SettingsVersion" = EXCLUDED."SettingsVersion", "WebhookUrl" = EXCLUDED."WebhookUrl",
                "Status" = EXCLUDED."Status", "VerifiedAt" = EXCLUDED."VerifiedAt", "Error" = EXCLUDED."Error";
            """, cancellationToken);
        return await GetAsync(ownerId, cancellationToken);
    }

    public async Task<bool> HandleSubscriptionPaymentSuccessAsync(
        PayOsAttempt attempt, decimal amount, string reference, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var duplicate = await db.ProcessedWebhooks.SingleOrDefaultAsync(
            row => row.OwnerId == attempt.OwnerId && row.Reference == reference, cancellationToken);
        if (duplicate is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var trackedAttempt = await db.PayOsAttempts.SingleAsync(row => row.Id == attempt.Id, cancellationToken);
        await db.Entry(trackedAttempt).ReloadAsync(cancellationToken);
        if (trackedAttempt.Status == PaymentAttemptStatus.Paid)
        {
            db.ProcessedWebhooks.Add(new ProcessedWebhook { OwnerId = attempt.OwnerId, Reference = reference, OrderCode = attempt.OrderCode });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var owner = await db.Users.SingleAsync(row => row.Id == attempt.OwnerId, cancellationToken);
        var days = attempt.SubscriptionDays ?? 30;
        var baseDate = (owner.SubscriptionExpiresAt is not null && owner.SubscriptionExpiresAt > DateTimeOffset.UtcNow)
            ? owner.SubscriptionExpiresAt.Value
            : DateTimeOffset.UtcNow;
        owner.SubscriptionTier = SubscriptionTier.Pro;
        owner.SubscriptionExpiresAt = baseDate.AddDays(days);

        trackedAttempt.Status = PaymentAttemptStatus.Paid;
        trackedAttempt.ProviderReference = reference;
        trackedAttempt.PaidAt = DateTimeOffset.UtcNow;

        db.ProcessedWebhooks.Add(new ProcessedWebhook { OwnerId = attempt.OwnerId, Reference = reference, OrderCode = trackedAttempt.OrderCode });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

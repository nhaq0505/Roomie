using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class TenantTokenService(IDataProtectionProvider protectionProvider, IConfiguration configuration)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("RoomRental.TenantAccessToken.v1");

    public (string Token, string Hash, string ProtectedToken) Create()
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token), _protector.Protect(token));
    }

    public async Task<bool> CanShareAsync(Guid ownerId, PaymentWebhookService webhookService, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("RequireVerifiedPaymentForTenantLink", true)) return true;
        return (await webhookService.GetAsync(ownerId, cancellationToken)).WebhookStatus == "Verified";
    }

    public string? Unprotect(Occupancy occupancy)
    {
        if (string.IsNullOrWhiteSpace(occupancy.ProtectedTenantToken)) return null;
        try { return _protector.Unprotect(occupancy.ProtectedTenantToken); }
        catch (CryptographicException) { return null; }
    }

    public string Url(string token)
    {
        var baseUrl = configuration["PublicBaseUrl"] ?? configuration["APP_PUBLIC_URL"] ?? "http://localhost:5173";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("PublicBaseUrl must be an absolute URL.");
        return new Uri(uri, $"/t/{Uri.EscapeDataString(token)}").ToString();
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

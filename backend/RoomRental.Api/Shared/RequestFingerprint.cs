using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoomRental.Api.Services;

public static class RequestFingerprint
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    public static string Fingerprint(object request) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, AuditJsonOptions))));

}

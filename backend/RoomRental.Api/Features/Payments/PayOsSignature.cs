using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RoomRental.Api.Services;

public static class PayOsSignature
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Create(string checksumKey, IReadOnlyDictionary<string, string> fields)
    {
        var payload = string.Join("&", fields.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool VerifyData(string checksumKey, JsonElement data, string? expectedSignature)
    {
        if (string.IsNullOrWhiteSpace(expectedSignature) || data.ValueKind != JsonValueKind.Object) return false;
        var fields = data.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={CanonicalValue(property.Value)}");
        var payload = string.Join("&", fields);
        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(checksumKey), Encoding.UTF8.GetBytes(payload));
        try
        {
            var expected = Convert.FromHexString(expectedSignature);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    private static string CanonicalValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.String => NormalizeNull(value.GetString()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(CanonicalJson)) + "]",
        JsonValueKind.Object => CanonicalJson(value),
        _ => ""
    };

    private static string CanonicalJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal);
            return "{" + string.Join(",", properties.Select(property =>
                JsonSerializer.Serialize(property.Name, JsonOptions) + ":" + CanonicalJson(property.Value))) + "}";
        }
        if (value.ValueKind == JsonValueKind.Array)
            return "[" + string.Join(",", value.EnumerateArray().Select(CanonicalJson)) + "]";
        if (value.ValueKind == JsonValueKind.String) return JsonSerializer.Serialize(value.GetString(), JsonOptions);
        if (value.ValueKind == JsonValueKind.Null) return "null";
        return value.GetRawText();
    }

    private static string NormalizeNull(string? value) =>
        string.IsNullOrEmpty(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("undefined", StringComparison.OrdinalIgnoreCase) ? "" : value;
}

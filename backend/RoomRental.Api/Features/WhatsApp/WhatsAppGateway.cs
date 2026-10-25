using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RoomRental.Api.Services;

public sealed record WhatsAppConnection(string Status, string? QrImage = null);
public sealed record WhatsAppSendResult(string Status);
public sealed record WhatsAppSendRequest(Guid AttemptId, string Phone, string Message);

public interface IWhatsAppGateway
{
    bool Enabled { get; }
    Task<WhatsAppConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct);
    Task<WhatsAppSendResult> SendAsync(Guid ownerId, WhatsAppSendRequest request, CancellationToken ct);
}

public sealed class WhatsAppGateway(IHttpClientFactory clients, IConfiguration config) : IWhatsAppGateway
{
    public bool Enabled => config.GetValue("WhatsApp:Enabled", false) && Uri.TryCreate(config["WhatsApp:GatewayUrl"], UriKind.Absolute, out _) &&
                           !string.IsNullOrWhiteSpace(config["WhatsApp:GatewayToken"]);
    private HttpClient Client()
    {
        if (!Enabled) throw new ApiException(503, "WhatsApp gateway is not configured.");
        var client = clients.CreateClient("WhatsApp");
        client.BaseAddress = new Uri(config["WhatsApp:GatewayUrl"]!.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["WhatsApp:GatewayToken"]);
        return client;
    }
    public async Task<WhatsAppConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct)
    {
        if (!Enabled && action == "status") return new("Unavailable");
        using var client = Client();
        using var response = await client.SendAsync(new HttpRequestMessage(action == "status" ? HttpMethod.Get : HttpMethod.Post,
            $"owners/{ownerId:D}/{action}"), ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(503, "WhatsApp gateway is unavailable. Try again shortly.");
        return await response.Content.ReadFromJsonAsync<WhatsAppConnection>(ct) ?? new("Unavailable");
    }
    public async Task<WhatsAppSendResult> SendAsync(Guid ownerId, WhatsAppSendRequest request, CancellationToken ct)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync($"owners/{ownerId:D}/send", request, ct);
        // A transport error after submission has an unknown delivery outcome; the worker never blindly retries it.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WhatsAppSendResult>(ct) ?? new("Unknown");
    }
}

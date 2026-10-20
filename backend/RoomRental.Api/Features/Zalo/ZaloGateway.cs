using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RoomRental.Api.Services;

public sealed record ZaloConnection(string Status, string? QrImage = null);
public sealed record ZaloSendResult(string Status);
public sealed record ZaloSendRequest(Guid AttemptId, string Phone, string Message, string BankBin, string BankAccount, long Amount, string TransferContent);

public interface IZaloGateway
{
    bool Enabled { get; }
    Task<ZaloConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct);
    Task<ZaloSendResult> SendAsync(Guid ownerId, ZaloSendRequest request, CancellationToken ct);
}

public sealed class ZaloGateway(IHttpClientFactory clients, IConfiguration config) : IZaloGateway
{
    public bool Enabled => config.GetValue("Zalo:Enabled", false) && Uri.TryCreate(config["Zalo:GatewayUrl"], UriKind.Absolute, out _) &&
                           !string.IsNullOrWhiteSpace(config["Zalo:GatewayToken"]);
    private HttpClient Client()
    {
        if (!Enabled) throw new ApiException(503, "Zalo gateway is not configured.");
        var client = clients.CreateClient("Zalo");
        client.BaseAddress = new Uri(config["Zalo:GatewayUrl"]!.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["Zalo:GatewayToken"]);
        return client;
    }
    public async Task<ZaloConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct)
    {
        if (!Enabled && action == "status") return new("Unavailable");
        using var client = Client();
        using var response = await client.SendAsync(new HttpRequestMessage(action == "status" ? HttpMethod.Get : HttpMethod.Post,
            $"owners/{ownerId:D}/{action}"), ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(503, "Zalo gateway is unavailable. Try again shortly.");
        return await response.Content.ReadFromJsonAsync<ZaloConnection>(ct) ?? new("Unavailable");
    }
    public async Task<ZaloSendResult> SendAsync(Guid ownerId, ZaloSendRequest request, CancellationToken ct)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync($"owners/{ownerId:D}/send", request, ct);
        // A transport error after submission has an unknown delivery outcome; the worker never blindly retries it.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ZaloSendResult>(ct) ?? new("Unknown");
    }
}

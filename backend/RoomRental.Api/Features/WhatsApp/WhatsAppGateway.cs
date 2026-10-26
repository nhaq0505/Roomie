using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;

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

public sealed class WhatsAppLocalGatewayHost : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment? _env;
    private readonly ILogger<WhatsAppLocalGatewayHost>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _gatewayDir;
    private readonly string _token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private Process? _process;
    private Uri? _baseUri;
    private bool _disposed;

    public WhatsAppLocalGatewayHost(IConfiguration config, IHostEnvironment? env = null, ILogger<WhatsAppLocalGatewayHost>? logger = null)
    {
        _config = config;
        _env = env;
        _logger = logger;
        _gatewayDir = ResolveGatewayDirectory(config, env);
    }

    public bool Available => _gatewayDir is not null;
    public string Token => _token;

    public static string? ResolveGatewayDirectory(IConfiguration config, IHostEnvironment? env)
    {
        var candidates = new List<string>();
        var configured = config["WhatsApp:GatewayDir"];
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "whatsapp-gateway"));
        if (env is not null && !string.IsNullOrWhiteSpace(env.ContentRootPath))
        {
            candidates.Add(Path.Combine(env.ContentRootPath, "whatsapp-gateway"));
            candidates.Add(Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "whatsapp-gateway")));
        }

        var parent = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && parent is not null; i++, parent = parent.Parent)
        {
            candidates.Add(Path.Combine(parent.FullName, "whatsapp-gateway"));
        }

        foreach (var dir in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "src", "server.mjs")) &&
                    Directory.Exists(Path.Combine(dir, "node_modules", "@whiskeysockets", "baileys")))
                {
                    return Path.GetFullPath(dir);
                }
            }
            catch { }
        }
        return null;
    }

    public async Task<Uri> EnsureStartedAsync(CancellationToken ct)
    {
        if (_gatewayDir is null)
            throw new ApiException(503, "WhatsApp gateway is not configured.");

        if (_process is { HasExited: false } && _baseUri is not null)
            return _baseUri;

        await _gate.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WhatsAppLocalGatewayHost));
            if (_process is { HasExited: false } && _baseUri is not null)
                return _baseUri;

            StopProcessCore();

            var dataDir = ResolveDataDirectory();
            var sessionKey = ResolveOrCreateSessionKey(dataDir);
            var port = AllocateLoopbackPort();
            var baseUri = new Uri($"http://127.0.0.1:{port}/");

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _gatewayDir
            };
            psi.ArgumentList.Add(Path.Combine(_gatewayDir, "src", "server.mjs"));
            psi.Environment["HOST"] = "127.0.0.1";
            psi.Environment["PORT"] = port.ToString();
            psi.Environment["WHATSAPP_DATA_DIR"] = dataDir;
            psi.Environment["WHATSAPP_GATEWAY_TOKEN"] = _token;
            psi.Environment["WHATSAPP_SESSION_KEY"] = sessionKey;
            foreach (var proxyVar in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" })
            {
                if (psi.Environment.TryGetValue(proxyVar, out var val) && val?.Contains("127.0.0.1:9", StringComparison.OrdinalIgnoreCase) == true)
                    psi.Environment.Remove(proxyVar);
            }

            var proc = Process.Start(psi) ?? throw new ApiException(503, "WhatsApp gateway is unavailable. Try again shortly.");
            _process = proc;
            _baseUri = baseUri;

            using var probeClient = new HttpClient(new SocketsHttpHandler { UseProxy = false })
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(2)
            };
            probeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline)
            {
                if (proc.HasExited)
                    throw new ApiException(503, "WhatsApp gateway is unavailable. Try again shortly.");
                try
                {
                    using var res = await probeClient.GetAsync("owners/00000000-0000-0000-0000-000000000000/status", ct);
                    if (res.IsSuccessStatusCode)
                        return baseUri;
                }
                catch when (!ct.IsCancellationRequested) { }
                await Task.Delay(50, ct);
            }

            throw new ApiException(503, "WhatsApp gateway is unavailable. Try again shortly.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Available && WhatsAppGateway.IsConfigEnabled(_config))
        {
            _ = Task.Run(async () =>
            {
                try { await EnsureStartedAsync(cancellationToken); }
                catch (Exception ex) { _logger?.LogWarning("WhatsApp embedded gateway warmup failed ({ErrorType}).", ex.GetType().Name); }
            }, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopProcessCore();
        return Task.CompletedTask;
    }

    private string ResolveDataDirectory()
    {
        var explicitDir = _config["WhatsApp:DataDir"] ?? Environment.GetEnvironmentVariable("WHATSAPP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir)) return Path.GetFullPath(explicitDir);

        var keysPath = _config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            var fullKeys = Path.GetFullPath(keysPath);
            var parent = Path.GetDirectoryName(fullKeys) ?? fullKeys;
            return Path.Combine(parent, "whatsapp-sessions");
        }

        var root = _env?.ContentRootPath;
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, ".local-data", "whatsapp-sessions");
    }

    private string ResolveOrCreateSessionKey(string dataDir)
    {
        var configuredKey = _config["WhatsApp:SessionKey"] ?? Environment.GetEnvironmentVariable("WHATSAPP_SESSION_KEY");
        if (!string.IsNullOrWhiteSpace(configuredKey)) return configuredKey.Trim();

        Directory.CreateDirectory(dataDir);
        var secretFile = Path.Combine(dataDir, ".session-secret");
        if (File.Exists(secretFile))
        {
            var existing = File.ReadAllText(secretFile).Trim();
            try
            {
                if (Convert.FromBase64String(existing).Length == 32) return existing;
            }
            catch { }
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tmpFile = secretFile + ".tmp";
        File.WriteAllText(tmpFile, generated);
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(tmpFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
        }
        File.Move(tmpFile, secretFile, overwrite: true);
        return generated;
    }

    private static int AllocateLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void StopProcessCore()
    {
        var proc = Interlocked.Exchange(ref _process, null);
        _baseUri = null;
        if (proc is null) return;
        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            proc.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopProcessCore();
        _gate.Dispose();
    }
}

public sealed class WhatsAppGateway(IHttpClientFactory clients, IConfiguration config, WhatsAppLocalGatewayHost? localHost = null) : IWhatsAppGateway
{
    public static bool IsConfigEnabled(IConfiguration config)
    {
        if (config.GetValue("Database:MigrateOnly", false)) return false;
        if (bool.TryParse(config["WhatsApp:Enabled"], out var explicitEnabled)) return explicitEnabled;
        if (bool.TryParse(config["Zalo:Enabled"], out var zaloEnabled) && !zaloEnabled) return false;
        return true;
    }

    private bool HasExternalGateway =>
        Uri.TryCreate(config["WhatsApp:GatewayUrl"], UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(config["WhatsApp:GatewayToken"]);

    public bool Enabled => IsConfigEnabled(config) && (HasExternalGateway || (localHost?.Available ?? false));

    private async Task<HttpClient> ClientAsync(CancellationToken ct)
    {
        if (!Enabled) throw new ApiException(503, "WhatsApp gateway is not configured.");
        var client = clients.CreateClient("WhatsApp");
        if (HasExternalGateway)
        {
            client.BaseAddress = new Uri(config["WhatsApp:GatewayUrl"]!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["WhatsApp:GatewayToken"]);
            return client;
        }

        var baseUri = await localHost!.EnsureStartedAsync(ct);
        client.BaseAddress = baseUri;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", localHost.Token);
        return client;
    }

    public async Task<WhatsAppConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct)
    {
        if (!Enabled && action == "status") return new("Unavailable");
        using var client = await ClientAsync(ct);
        using var response = await client.SendAsync(new HttpRequestMessage(action == "status" ? HttpMethod.Get : HttpMethod.Post,
            $"owners/{ownerId:D}/{action}"), ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(503, "WhatsApp gateway is unavailable. Try again shortly.");
        return await response.Content.ReadFromJsonAsync<WhatsAppConnection>(ct) ?? new("Unavailable");
    }

    public async Task<WhatsAppSendResult> SendAsync(Guid ownerId, WhatsAppSendRequest request, CancellationToken ct)
    {
        using var client = await ClientAsync(ct);
        using var response = await client.PostAsJsonAsync($"owners/{ownerId:D}/send", request, ct);
        // A transport error after submission has an unknown delivery outcome; the worker never blindly retries it.
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WhatsAppSendResult>(ct) ?? new("Unknown");
    }
}

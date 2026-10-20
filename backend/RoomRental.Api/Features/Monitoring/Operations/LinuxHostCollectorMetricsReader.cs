using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class HostMetricsOptions
{
    public const string SectionName = "Monitoring";
    public string HostMetricsPath { get; set; } = "/app/monitoring-host/host-metrics.json";
    public double StalenessThresholdSeconds { get; set; } = 180.0;
}

public sealed record HostCollectorMetrics(
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    int ContainerCount,
    string HealthEndpointStatus,
    string HostStatus,
    DateTimeOffset? RecordedAt,
    bool IsStale
);

public interface ILinuxHostCollectorMetricsReader
{
    Task<HostCollectorMetrics?> ReadHostMetricsAsync(CancellationToken ct = default);
}

public sealed class LinuxHostCollectorMetricsReader : ILinuxHostCollectorMetricsReader, IHostMetricsReader
{
    private readonly string _metricsPath;
    private readonly double _stalenessThresholdSeconds;
    private readonly TimeProvider _clock;
    private readonly ILogger<LinuxHostCollectorMetricsReader> _logger;

    public LinuxHostCollectorMetricsReader(
        IOptions<HostMetricsOptions> options,
        TimeProvider clock,
        ILogger<LinuxHostCollectorMetricsReader> logger)
    {
        _clock = clock;
        _logger = logger;
        _stalenessThresholdSeconds = options.Value.StalenessThresholdSeconds;

        var envPath = Environment.GetEnvironmentVariable("MONITORING_HOST_METRICS_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            _metricsPath = envPath;
        }
        else if (!string.IsNullOrWhiteSpace(options.Value.HostMetricsPath))
        {
            _metricsPath = options.Value.HostMetricsPath;
        }
        else
        {
            _metricsPath = "/app/monitoring-host/host-metrics.json";
        }
    }

    async Task<HostMetricsDto?> IHostMetricsReader.ReadHostMetricsAsync(CancellationToken ct)
    {
        var metrics = await ReadHostMetricsAsync(ct);
        if (metrics == null) return null;

        return new HostMetricsDto(
            CpuPercent: metrics.CpuPercent,
            MemoryPercent: metrics.MemoryPercent,
            DiskPercent: metrics.DiskPercent,
            ContainerCount: metrics.ContainerCount,
            HealthEndpointStatus: metrics.HealthEndpointStatus,
            HostStatus: metrics.HostStatus,
            RecordedAt: metrics.RecordedAt,
            IsStale: metrics.IsStale
        );
    }

    public Task<HostCollectorMetrics?> ReadHostMetricsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_metricsPath))
        {
            _logger.LogDebug("Host metrics file not found at {Path}", _metricsPath);
            return Task.FromResult<HostCollectorMetrics?>(null);
        }

        try
        {
            using var stream = File.OpenRead(_metricsPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            DateTimeOffset? recordedAt = null;
            if (root.TryGetProperty("generated_at", out var genProp) &&
                genProp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(genProp.GetString(), out var parsedDate))
            {
                recordedAt = parsedDate;
            }

            var now = _clock.GetUtcNow();
            var isStale = recordedAt == null || (now - recordedAt.Value).TotalSeconds > _stalenessThresholdSeconds;

            double? cpuPercent = null;
            double? memoryPercent = null;
            double? diskPercent = null;
            var hostStatus = "Unknown";

            if (root.TryGetProperty("host", out var hostProp) && hostProp.ValueKind == JsonValueKind.Object)
            {
                if (hostProp.TryGetProperty("cpu", out var cpuObj) && cpuObj.ValueKind == JsonValueKind.Object)
                {
                    if (cpuObj.TryGetProperty("percent", out var cpuVal) && cpuVal.ValueKind == JsonValueKind.Number)
                    {
                        cpuPercent = cpuVal.GetDouble();
                    }
                    if (cpuObj.TryGetProperty("status", out var cpuStatus) && cpuStatus.ValueKind == JsonValueKind.String)
                    {
                        hostStatus = cpuStatus.GetString() ?? "Unknown";
                    }
                }

                if (hostProp.TryGetProperty("memory", out var memObj) && memObj.ValueKind == JsonValueKind.Object)
                {
                    if (memObj.TryGetProperty("percent", out var memVal) && memVal.ValueKind == JsonValueKind.Number)
                    {
                        memoryPercent = memVal.GetDouble();
                    }
                }

                if (hostProp.TryGetProperty("disk", out var diskObj) && diskObj.ValueKind == JsonValueKind.Object)
                {
                    if (diskObj.TryGetProperty("percent", out var diskVal) && diskVal.ValueKind == JsonValueKind.Number)
                    {
                        diskPercent = diskVal.GetDouble();
                    }
                }
            }

            var containerCount = 0;
            if (root.TryGetProperty("containers", out var containersProp) && containersProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var _ in containersProp.EnumerateObject())
                {
                    containerCount++;
                }
            }

            var healthEndpointStatus = "unknown";
            if (root.TryGetProperty("services", out var servicesProp) && servicesProp.ValueKind == JsonValueKind.Object)
            {
                if (servicesProp.TryGetProperty("api_health", out var apiHealthProp) && apiHealthProp.ValueKind == JsonValueKind.Object)
                {
                    if (apiHealthProp.TryGetProperty("status", out var apiStatus) && apiStatus.ValueKind == JsonValueKind.String)
                    {
                        healthEndpointStatus = apiStatus.GetString() ?? "unknown";
                    }
                }
            }

            if (root.TryGetProperty("collector", out var collectorProp) && collectorProp.ValueKind == JsonValueKind.Object)
            {
                if (collectorProp.TryGetProperty("status", out var colStatus) && colStatus.ValueKind == JsonValueKind.String)
                {
                    var cStatus = colStatus.GetString();
                    if (!string.IsNullOrWhiteSpace(cStatus) && hostStatus == "Unknown")
                    {
                        hostStatus = cStatus;
                    }
                }
            }

            return Task.FromResult<HostCollectorMetrics?>(new HostCollectorMetrics(
                CpuPercent: cpuPercent,
                MemoryPercent: memoryPercent,
                DiskPercent: diskPercent,
                ContainerCount: containerCount,
                HealthEndpointStatus: healthEndpointStatus,
                HostStatus: hostStatus,
                RecordedAt: recordedAt,
                IsStale: isStale
            ));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse host metrics JSON at {Path}", _metricsPath);
            return Task.FromResult<HostCollectorMetrics?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read host metrics at {Path}", _metricsPath);
            return Task.FromResult<HostCollectorMetrics?>(null);
        }
    }
}

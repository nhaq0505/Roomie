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

public sealed record HostCollectorIncident(
    string Target,
    string Id,
    string Severity,
    string Title,
    string? CurrentValue,
    int ConsecutiveSamples,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? LastNotifiedAt,
    DateTimeOffset? ResolvedAt = null,
    double? DurationSeconds = null,
    string? ResolvedValue = null
);

public sealed record BackupWatchdogStatus(
    string Status,
    DateTimeOffset? LastSuccessAt,
    double? AgeSeconds,
    double ThresholdSeconds
);

public sealed record HostCollectorMetrics(
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    int? ContainerCount,
    string HealthEndpointStatus,
    string HostStatus,
    DateTimeOffset? RecordedAt,
    bool IsStale,
    IReadOnlyList<HostCollectorIncident>? ActiveIncidents = null,
    IReadOnlyList<HostCollectorIncident>? RecentHistory = null,
    BackupWatchdogStatus? BackupWatchdog = null
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
        var fileInfo = new FileInfo(_metricsPath);
        if (!fileInfo.Exists || fileInfo.Length > 2 * 1024 * 1024)
        {
            _logger.LogDebug("Host metrics file not found or exceeds 2MB limit at {Path}", _metricsPath);
            return Task.FromResult<HostCollectorMetrics?>(null);
        }

        try
        {
            var jsonOptions = new JsonDocumentOptions
            {
                MaxDepth = 16,
                CommentHandling = JsonCommentHandling.Skip
            };

            using var stream = File.OpenRead(_metricsPath);
            using var doc = JsonDocument.Parse(stream, jsonOptions);
            var root = doc.RootElement;

            var now = _clock.GetUtcNow();
            DateTimeOffset? recordedAt = null;
            if (root.TryGetProperty("generated_at", out var genProp) &&
                genProp.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(genProp.GetString(), out var parsedDate))
            {
                // Reject anomalous future timestamp (> 2 minutes in future)
                if (parsedDate <= now.AddMinutes(2))
                {
                    recordedAt = parsedDate;
                }
                else
                {
                    _logger.LogWarning("Host metrics generated_at is in the future: {Timestamp}", parsedDate);
                }
            }

            var isStale = recordedAt == null || (now - recordedAt.Value).TotalSeconds > _stalenessThresholdSeconds;

            double? cpuPercent = null;
            double? memoryPercent = null;
            double? diskPercent = null;
            var hostStatus = "Unknown";

            if (root.TryGetProperty("host", out var hostProp) && hostProp.ValueKind == JsonValueKind.Object)
            {
                if (hostProp.TryGetProperty("cpu", out var cpuObj) && cpuObj.ValueKind == JsonValueKind.Object)
                {
                    if (cpuObj.TryGetProperty("percent", out var cpuVal))
                    {
                        cpuPercent = ParseBoundedPercentage(cpuVal);
                    }
                }

                if (hostProp.TryGetProperty("memory", out var memObj) && memObj.ValueKind == JsonValueKind.Object)
                {
                    if (memObj.TryGetProperty("percent", out var memVal))
                    {
                        memoryPercent = ParseBoundedPercentage(memVal);
                    }
                }

                if (hostProp.TryGetProperty("disk", out var diskObj) && diskObj.ValueKind == JsonValueKind.Object)
                {
                    if (diskObj.TryGetProperty("percent", out var diskVal))
                    {
                        diskPercent = ParseBoundedPercentage(diskVal);
                    }
                }
            }

            int? containerCount = null;
            if (root.TryGetProperty("containers", out var containersProp) && containersProp.ValueKind == JsonValueKind.Object)
            {
                var runningCount = 0;
                var hasAnyContainer = false;
                foreach (var container in containersProp.EnumerateObject())
                {
                    if (container.Value.ValueKind == JsonValueKind.Object)
                    {
                        hasAnyContainer = true;
                        if (container.Value.TryGetProperty("running", out var rProp) && rProp.ValueKind == JsonValueKind.True)
                        {
                            runningCount++;
                        }
                    }
                }
                if (hasAnyContainer)
                {
                    containerCount = runningCount;
                }
            }

            var healthEndpointStatus = "unknown";
            BackupWatchdogStatus? backupWatchdog = null;

            if (root.TryGetProperty("services", out var servicesProp) && servicesProp.ValueKind == JsonValueKind.Object)
            {
                if (servicesProp.TryGetProperty("api_health", out var apiHealthProp) && apiHealthProp.ValueKind == JsonValueKind.Object)
                {
                    if (apiHealthProp.TryGetProperty("status", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                    {
                        healthEndpointStatus = sProp.GetString() ?? "unknown";
                    }
                }

                if (servicesProp.TryGetProperty("backup_watchdog", out var bwProp) && bwProp.ValueKind == JsonValueKind.Object)
                {
                    var bwStatus = "unknown";
                    if (bwProp.TryGetProperty("status", out var bwsProp) && bwsProp.ValueKind == JsonValueKind.String)
                    {
                        bwStatus = bwsProp.GetString() ?? "unknown";
                    }
                    DateTimeOffset? bwLastSuccessAt = null;
                    if (bwProp.TryGetProperty("last_success_at", out var lsaProp) &&
                        lsaProp.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(lsaProp.GetString(), out var parsedLsa))
                    {
                        bwLastSuccessAt = parsedLsa;
                    }
                    double? bwAgeSeconds = null;
                    if (bwProp.TryGetProperty("age_seconds", out var asProp) && asProp.ValueKind == JsonValueKind.Number && asProp.TryGetDouble(out var parsedAge))
                    {
                        if (!double.IsNaN(parsedAge) && !double.IsInfinity(parsedAge) && parsedAge >= 0)
                        {
                            bwAgeSeconds = parsedAge;
                        }
                    }
                    var bwThresholdSeconds = 97200.0; // 27 hours default
                    if (bwProp.TryGetProperty("threshold_seconds", out var tsProp) &&
                        tsProp.ValueKind == JsonValueKind.Number &&
                        tsProp.TryGetDouble(out var parsedTs) &&
                        !double.IsNaN(parsedTs) && !double.IsInfinity(parsedTs) && parsedTs > 0)
                    {
                        bwThresholdSeconds = parsedTs;
                    }
                    backupWatchdog = new BackupWatchdogStatus(bwStatus, bwLastSuccessAt, bwAgeSeconds, bwThresholdSeconds);
                }
            }

            var activeIncidents = new List<HostCollectorIncident>();
            var recentHistory = new List<HostCollectorIncident>();

            if (root.TryGetProperty("incidents", out var incidentsProp) && incidentsProp.ValueKind == JsonValueKind.Object)
            {
                if (incidentsProp.TryGetProperty("active", out var activeProp))
                {
                    if (activeProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var incElement in activeProp.EnumerateArray())
                        {
                            var inc = ParseIncident(incElement);
                            if (inc != null) activeIncidents.Add(inc);
                        }
                    }
                    else if (activeProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var incProp in activeProp.EnumerateObject())
                        {
                            var inc = ParseIncident(incProp.Value, incProp.Name);
                            if (inc != null) activeIncidents.Add(inc);
                        }
                    }
                }

                if (incidentsProp.TryGetProperty("recent_history", out var historyProp) && historyProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var histElement in historyProp.EnumerateArray())
                    {
                        var inc = ParseIncident(histElement);
                        if (inc != null) recentHistory.Add(inc);
                    }
                }
            }

            if (root.TryGetProperty("collector", out var collectorProp) && collectorProp.ValueKind == JsonValueKind.Object)
            {
                if (collectorProp.TryGetProperty("status", out var colStatus) && colStatus.ValueKind == JsonValueKind.String)
                {
                    var cStatus = colStatus.GetString();
                    if (!string.IsNullOrWhiteSpace(cStatus))
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
                IsStale: isStale,
                ActiveIncidents: activeIncidents,
                RecentHistory: recentHistory,
                BackupWatchdog: backupWatchdog
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

    private static double? ParseBoundedPercentage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var val))
        {
            if (!double.IsNaN(val) && !double.IsInfinity(val) && val >= 0.0 && val <= 100.0)
            {
                return val;
            }
        }
        return null;
    }

    private static HostCollectorIncident? ParseIncident(JsonElement incObj, string? fallbackTarget = null)
    {
        if (incObj.ValueKind != JsonValueKind.Object) return null;

        var target = fallbackTarget ?? "system";
        if (incObj.TryGetProperty("target", out var targetProp) && targetProp.ValueKind == JsonValueKind.String)
        {
            target = targetProp.GetString() ?? target;
        }

        var id = incObj.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() ?? $"inc_{target}"
            : $"inc_{target}";

        var severity = incObj.TryGetProperty("severity", out var sevProp) && sevProp.ValueKind == JsonValueKind.String
            ? sevProp.GetString() ?? "warning"
            : "warning";

        var title = incObj.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
            ? titleProp.GetString() ?? ""
            : "";

        var curVal = incObj.TryGetProperty("current_value", out var cvProp) && cvProp.ValueKind == JsonValueKind.String
            ? cvProp.GetString()
            : null;

        var consec = incObj.TryGetProperty("consecutive_samples", out var csProp) && csProp.ValueKind == JsonValueKind.Number
            ? csProp.GetInt32()
            : 0;

        DateTimeOffset? openedAt = null;
        if (incObj.TryGetProperty("opened_at", out var oaProp) &&
            oaProp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(oaProp.GetString(), out var parsedOa))
        {
            openedAt = parsedOa;
        }

        DateTimeOffset? lastNotifiedAt = null;
        if (incObj.TryGetProperty("last_notified_at", out var lnProp) &&
            lnProp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(lnProp.GetString(), out var parsedLn))
        {
            lastNotifiedAt = parsedLn;
        }

        DateTimeOffset? resolvedAt = null;
        if (incObj.TryGetProperty("resolved_at", out var raProp) &&
            raProp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(raProp.GetString(), out var parsedRa))
        {
            resolvedAt = parsedRa;
        }

        double? durationSeconds = null;
        if (incObj.TryGetProperty("duration_seconds", out var dsProp) && dsProp.ValueKind == JsonValueKind.Number)
        {
            durationSeconds = dsProp.GetDouble();
        }

        var resolvedValue = incObj.TryGetProperty("resolved_value", out var rvProp) && rvProp.ValueKind == JsonValueKind.String
            ? rvProp.GetString()
            : null;

        return new HostCollectorIncident(target, id, severity, title, curVal, consec, openedAt, lastNotifiedAt, resolvedAt, durationSeconds, resolvedValue);
    }
}

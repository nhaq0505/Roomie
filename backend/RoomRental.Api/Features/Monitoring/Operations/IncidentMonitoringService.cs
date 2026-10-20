using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class IncidentMonitoringService : IIncidentService
{
    private readonly AppDbContext _db;
    private readonly IRouteLatencyAggregator _routeAggregator;
    private readonly ILinuxHostCollectorMetricsReader _hostReader;
    private readonly IWorkerTelemetryTracker _workerTracker;
    private readonly IIncidentNotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly ILogger<IncidentMonitoringService> _logger;

    private static readonly List<DateTimeOffset> DbFailedProbes = new();
    private static bool DbIncidentTriggeredInMemory = false;
    private static readonly object DbProbeLock = new();

    public Func<CancellationToken, Task<bool>>? DbProbeOverride { get; set; }

    public IncidentMonitoringService(
        AppDbContext db,
        IRouteLatencyAggregator routeAggregator,
        ILinuxHostCollectorMetricsReader hostReader,
        IWorkerTelemetryTracker workerTracker,
        IIncidentNotifier notifier,
        TimeProvider clock,
        ILogger<IncidentMonitoringService> logger)
    {
        _db = db;
        _routeAggregator = routeAggregator;
        _hostReader = hostReader;
        _workerTracker = workerTracker;
        _notifier = notifier;
        _clock = clock;
        _logger = logger;
    }

    public static void ResetDbProbeHistory()
    {
        lock (DbProbeLock)
        {
            DbFailedProbes.Clear();
            DbIncidentTriggeredInMemory = false;
        }
    }

    public async Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(CancellationToken ct)
    {
        List<MonitoringIncident> list;
        try
        {
            list = await _db.MonitoringIncidents.AsNoTracking()
                .OrderByDescending(x => x.StartedAt)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query incidents from database (database may be unreachable)");
            list = new List<MonitoringIncident>();
        }

        lock (DbProbeLock)
        {
            if (DbIncidentTriggeredInMemory && !list.Any(x => x.IncidentKey == "database_disconnected" && x.State != "Resolved"))
            {
                list.Insert(0, new MonitoringIncident
                {
                    IncidentKey = "database_disconnected",
                    RuleName = "Database Connectivity Failure",
                    Severity = "Critical",
                    State = "Active",
                    Summary = "Database probe failed 3 consecutive times",
                    Details = "Application cannot connect to PostgreSQL database",
                    StartedAt = DbFailedProbes.FirstOrDefault(),
                    NotificationCount = 1
                });
            }
        }

        var activeCount = list.Count(x => x.State != "Resolved");
        var dtoList = list.Select(x => new IncidentDto(
            Id: x.Id,
            IncidentKey: x.IncidentKey,
            RuleName: x.RuleName,
            Severity: x.Severity,
            State: x.State,
            Summary: x.Summary,
            Details: x.Details,
            StartedAt: x.StartedAt,
            LastNotifiedAt: x.LastNotifiedAt,
            ResolvedAt: x.ResolvedAt,
            NotificationCount: x.NotificationCount
        )).ToList();

        return new AdminMonitoringIncidentsResponse(
            ActiveIncidentCount: activeCount,
            Incidents: dtoList
        );
    }

    public async Task<IncidentDto> UpdateIncidentStateAsync(Guid id, UpdateIncidentRequest request, CancellationToken ct)
    {
        var incident = await _db.MonitoringIncidents.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Incident not found.");

        var now = _clock.GetUtcNow();
        incident.State = request.State;
        incident.UpdatedAt = now;

        if (request.State == "Acknowledged")
        {
            incident.AcknowledgedAt = now;
        }
        else if (request.State == "Resolved")
        {
            incident.ResolvedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            incident.Details += $"\n[Admin Note]: {request.Note.Trim()}";
        }

        await _db.SaveChangesAsync(ct);

        return new IncidentDto(
            Id: incident.Id,
            IncidentKey: incident.IncidentKey,
            RuleName: incident.RuleName,
            Severity: incident.Severity,
            State: incident.State,
            Summary: incident.Summary,
            Details: incident.Details,
            StartedAt: incident.StartedAt,
            LastNotifiedAt: incident.LastNotifiedAt,
            ResolvedAt: incident.ResolvedAt,
            NotificationCount: incident.NotificationCount
        );
    }

    private enum RuleStatus
    {
        Unknown,
        Triggered,
        ExplicitlyHealthy
    }

    private sealed record RuleEvaluation(
        string IncidentKey,
        string RuleName,
        string Severity,
        string Summary,
        string Details,
        RuleStatus Status,
        bool SkipSmtpNotification = false,
        string? AlternateKey = null
    );

    public async Task TriggerIncidentCheckAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var evaluations = new List<RuleEvaluation>();

        // 1. Database Connectivity: 3 consecutive failed probes >= 1 minute apart
        bool canConnect;
        try
        {
            if (DbProbeOverride != null)
            {
                canConnect = await DbProbeOverride(ct);
            }
            else
            {
                canConnect = await _db.Database.CanConnectAsync(ct);
            }
        }
        catch
        {
            canConnect = false;
        }

        if (!canConnect)
        {
            lock (DbProbeLock)
            {
                DbFailedProbes.Add(now);
                DbFailedProbes.RemoveAll(t => (now - t).TotalMinutes > 15);

                var count = DbFailedProbes.Count;
                var durationMinutes = count >= 3 ? (now - DbFailedProbes[0]).TotalMinutes : 0;

                if (count >= 3 && durationMinutes >= 2.0)
                {
                    evaluations.Add(new RuleEvaluation(
                        IncidentKey: "database_disconnected",
                        RuleName: "Database Connectivity Failure",
                        Severity: "Critical",
                        Summary: $"Database probe failed {count} consecutive times over {durationMinutes:F1} minutes",
                        Details: "Application cannot connect to PostgreSQL database",
                        Status: RuleStatus.Triggered,
                        AlternateKey: "app_db_unreachable"
                    ));
                }
                else
                {
                    evaluations.Add(new RuleEvaluation(
                        IncidentKey: "database_disconnected",
                        RuleName: "Database Connectivity Failure",
                        Severity: "Critical",
                        Summary: $"Database probe failed ({count}/3 consecutive probes needed)",
                        Details: "Awaiting probe confirmation before alerting",
                        Status: RuleStatus.Unknown,
                        AlternateKey: "app_db_unreachable"
                    ));
                }
            }
        }
        else
        {
            lock (DbProbeLock)
            {
                DbFailedProbes.Clear();
            }
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "database_disconnected",
                RuleName: "Database Connectivity Failure",
                Severity: "Critical",
                Summary: "Database probe succeeded",
                Details: "Database connection verified healthy",
                Status: RuleStatus.ExplicitlyHealthy,
                AlternateKey: "app_db_unreachable"
            ));
        }

        // 2. API 5xx Rate: Sustained 5min, Min 100 requests, > 5.0% threshold
        var summary5m = _routeAggregator.GetWindowSummary(TimeSpan.FromMinutes(5));
        if (summary5m.TotalRequests < 100)
        {
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "high_5xx_rate",
                RuleName: "High API 5xx Error Rate",
                Severity: "Critical",
                Summary: "Insufficient requests in 5-minute window",
                Details: $"Total requests: {summary5m.TotalRequests} (< 100 required)",
                Status: RuleStatus.Unknown,
                AlternateKey: "api_5xx_rate"
            ));
        }
        else
        {
            var rate = summary5m.Api5xxRatePercent ?? 0.0;
            if (rate > 5.0)
            {
                var severity = rate >= 10.0 ? "Critical" : "Warning";
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "high_5xx_rate",
                    RuleName: "High API 5xx Error Rate",
                    Severity: severity,
                    Summary: $"API 5xx error rate is {rate}% over last 5 minutes (> 5% threshold)",
                    Details: $"Total requests: {summary5m.TotalRequests}, 5xx count: {summary5m.Status5xxCount}, 5xx rate: {rate}%",
                    Status: RuleStatus.Triggered,
                    AlternateKey: "api_5xx_rate"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "high_5xx_rate",
                    RuleName: "High API 5xx Error Rate",
                    Severity: "Warning",
                    Summary: $"API 5xx error rate normalized to {rate}%",
                    Details: $"Total requests: {summary5m.TotalRequests}, 5xx count: {summary5m.Status5xxCount}",
                    Status: RuleStatus.ExplicitlyHealthy,
                    AlternateKey: "api_5xx_rate"
                ));
            }
        }

        // 3. API P95 Latency: Sustained 10min, Min 100 requests, > 2000ms threshold
        var summary10m = _routeAggregator.GetWindowSummary(TimeSpan.FromMinutes(10));
        if (summary10m.TotalRequests < 100)
        {
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "high_p95_latency",
                RuleName: "Severe API Latency (P95 > 2s)",
                Severity: "Critical",
                Summary: "Insufficient requests in 10-minute window",
                Details: $"Total requests: {summary10m.TotalRequests} (< 100 required)",
                Status: RuleStatus.Unknown,
                AlternateKey: "api_p95_latency"
            ));
        }
        else
        {
            var p95 = summary10m.LatencyP95Ms ?? 0.0;
            if (p95 > 2000.0)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "high_p95_latency",
                    RuleName: "Severe API Latency (P95 > 2s)",
                    Severity: "Critical",
                    Summary: $"API P95 latency is {p95}ms over last 10 minutes (> 2000ms threshold)",
                    Details: $"Total requests: {summary10m.TotalRequests}, P95 latency: {p95}ms",
                    Status: RuleStatus.Triggered,
                    AlternateKey: "api_p95_latency"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "high_p95_latency",
                    RuleName: "Severe API Latency (P95 > 2s)",
                    Severity: "Warning",
                    Summary: $"API P95 latency normalized to {p95}ms",
                    Details: $"Total requests: {summary10m.TotalRequests}, P95 latency: {p95}ms",
                    Status: RuleStatus.ExplicitlyHealthy,
                    AlternateKey: "api_p95_latency"
                ));
            }
        }

        // 4. Host Telemetry & Host Collector Incidents
        var hostMetrics = await _hostReader.ReadHostMetricsAsync(ct);
        if (hostMetrics == null)
        {
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "host_collector_stale",
                RuleName: "Host Telemetry Missing",
                Severity: "Warning",
                Summary: "Host telemetry file missing",
                Details: "No host-metrics.json found",
                Status: RuleStatus.Unknown
            ));
            evaluations.Add(new RuleEvaluation("host_cpu_high", "High Host CPU Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_cpu"));
            evaluations.Add(new RuleEvaluation("host_disk_high", "High Host Disk Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_disk"));
            evaluations.Add(new RuleEvaluation("host_ram_high", "High Host RAM Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_ram"));
            evaluations.Add(new RuleEvaluation("host_app_unhealthy", "Host App Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_app"));
            evaluations.Add(new RuleEvaluation("host_db_unhealthy", "Host DB Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_db"));
        }
        else if (hostMetrics.IsStale)
        {
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "host_collector_stale",
                RuleName: "Host Telemetry Stale",
                Severity: "Warning",
                Summary: "Host collector metrics have not updated in > 180 seconds",
                Details: $"Last recorded sample: {hostMetrics.RecordedAt:O}",
                Status: RuleStatus.Triggered
            ));
            evaluations.Add(new RuleEvaluation("host_cpu_high", "High Host CPU Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_cpu"));
            evaluations.Add(new RuleEvaluation("host_disk_high", "High Host Disk Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_disk"));
            evaluations.Add(new RuleEvaluation("host_ram_high", "High Host RAM Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_ram"));
            evaluations.Add(new RuleEvaluation("host_app_unhealthy", "Host App Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_app"));
            evaluations.Add(new RuleEvaluation("host_db_unhealthy", "Host DB Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_db"));
        }
        else
        {
            evaluations.Add(new RuleEvaluation(
                IncidentKey: "host_collector_stale",
                RuleName: "Host Telemetry Stale",
                Severity: "Warning",
                Summary: "Host collector metrics fresh",
                Details: $"Last sample: {hostMetrics.RecordedAt:O}",
                Status: RuleStatus.ExplicitlyHealthy
            ));

            var activeCollectorIncidents = hostMetrics.ActiveIncidents ?? (IReadOnlyList<HostCollectorIncident>)Array.Empty<HostCollectorIncident>();
            var recentHistory = hostMetrics.RecentHistory ?? (IReadOnlyList<HostCollectorIncident>)Array.Empty<HostCollectorIncident>();

            // Process CPU
            var cpuInc = activeCollectorIncidents.FirstOrDefault(i => i.Target == "cpu");
            if (cpuInc != null)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: cpuInc.Id,
                    RuleName: cpuInc.Title,
                    Severity: cpuInc.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Warning",
                    Summary: $"Host CPU utilization >= 85% sustained ({cpuInc.CurrentValue})",
                    Details: $"Collector incident id: {cpuInc.Id}, samples: {cpuInc.ConsecutiveSamples}",
                    Status: RuleStatus.Triggered,
                    SkipSmtpNotification: true,
                    AlternateKey: "host_cpu_high"
                ));
            }
            else if (recentHistory.Any(h => h.Target == "cpu" && h.ResolvedAt != null) || (hostMetrics.CpuPercent != null && hostMetrics.CpuPercent.Value < 85.0))
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "host_cpu_high",
                    RuleName: "High Host CPU Utilization",
                    Severity: "Warning",
                    Summary: $"Host CPU usage normal ({hostMetrics.CpuPercent}%)",
                    Details: "Collector reports CPU within normal bounds",
                    Status: RuleStatus.ExplicitlyHealthy,
                    SkipSmtpNotification: true,
                    AlternateKey: "inc_cpu"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation("host_cpu_high", "High Host CPU Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_cpu"));
            }

            // Process Disk
            var diskInc = activeCollectorIncidents.FirstOrDefault(i => i.Target == "disk");
            if (diskInc != null)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: diskInc.Id,
                    RuleName: diskInc.Title,
                    Severity: diskInc.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Warning",
                    Summary: $"Host Disk utilization >= 85% sustained ({diskInc.CurrentValue})",
                    Details: $"Collector incident id: {diskInc.Id}, samples: {diskInc.ConsecutiveSamples}",
                    Status: RuleStatus.Triggered,
                    SkipSmtpNotification: true,
                    AlternateKey: "host_disk_high"
                ));
            }
            else if (recentHistory.Any(h => h.Target == "disk" && h.ResolvedAt != null) || (hostMetrics.DiskPercent != null && hostMetrics.DiskPercent.Value < 85.0))
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "host_disk_high",
                    RuleName: "High Host Disk Utilization",
                    Severity: "Warning",
                    Summary: $"Host Disk usage normal ({hostMetrics.DiskPercent}%)",
                    Details: "Collector reports Disk within normal bounds",
                    Status: RuleStatus.ExplicitlyHealthy,
                    SkipSmtpNotification: true,
                    AlternateKey: "inc_disk"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation("host_disk_high", "High Host Disk Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_disk"));
            }

            // Process RAM
            var ramInc = activeCollectorIncidents.FirstOrDefault(i => i.Target == "ram");
            if (ramInc != null)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: ramInc.Id,
                    RuleName: ramInc.Title,
                    Severity: ramInc.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Warning",
                    Summary: $"Host RAM utilization >= 85% sustained ({ramInc.CurrentValue})",
                    Details: $"Collector incident id: {ramInc.Id}, samples: {ramInc.ConsecutiveSamples}",
                    Status: RuleStatus.Triggered,
                    SkipSmtpNotification: true,
                    AlternateKey: "host_ram_high"
                ));
            }
            else if (recentHistory.Any(h => h.Target == "ram" && h.ResolvedAt != null) || (hostMetrics.MemoryPercent != null && hostMetrics.MemoryPercent.Value < 85.0))
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "host_ram_high",
                    RuleName: "High Host RAM Utilization",
                    Severity: "Warning",
                    Summary: $"Host RAM usage normal ({hostMetrics.MemoryPercent}%)",
                    Details: "Collector reports RAM within normal bounds",
                    Status: RuleStatus.ExplicitlyHealthy,
                    SkipSmtpNotification: true,
                    AlternateKey: "inc_ram"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation("host_ram_high", "High Host RAM Utilization", "Warning", "", "", RuleStatus.Unknown, AlternateKey: "inc_ram"));
            }

            // Process App container
            var appInc = activeCollectorIncidents.FirstOrDefault(i => i.Target == "app");
            if (appInc != null)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: appInc.Id,
                    RuleName: appInc.Title,
                    Severity: "Critical",
                    Summary: $"Host App Container/Health issue ({appInc.CurrentValue})",
                    Details: $"Collector incident id: {appInc.Id}, samples: {appInc.ConsecutiveSamples}",
                    Status: RuleStatus.Triggered,
                    SkipSmtpNotification: true,
                    AlternateKey: "host_app_unhealthy"
                ));
            }
            else if (recentHistory.Any(h => h.Target == "app" && h.ResolvedAt != null) || hostMetrics.HealthEndpointStatus.Equals("healthy", StringComparison.OrdinalIgnoreCase))
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "host_app_unhealthy",
                    RuleName: "Host App Container Unhealthy",
                    Severity: "Critical",
                    Summary: "Host App Container and /api/health normal",
                    Details: "Collector reports app container and probe healthy",
                    Status: RuleStatus.ExplicitlyHealthy,
                    SkipSmtpNotification: true,
                    AlternateKey: "inc_app"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation("host_app_unhealthy", "Host App Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_app"));
            }

            // Process DB container
            var dbInc = activeCollectorIncidents.FirstOrDefault(i => i.Target == "db");
            if (dbInc != null)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: dbInc.Id,
                    RuleName: dbInc.Title,
                    Severity: "Critical",
                    Summary: $"Host DB Container issue ({dbInc.CurrentValue})",
                    Details: $"Collector incident id: {dbInc.Id}, samples: {dbInc.ConsecutiveSamples}",
                    Status: RuleStatus.Triggered,
                    SkipSmtpNotification: true,
                    AlternateKey: "host_db_unhealthy"
                ));
            }
            else if (recentHistory.Any(h => h.Target == "db" && h.ResolvedAt != null))
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: "host_db_unhealthy",
                    RuleName: "Host DB Container Unhealthy",
                    Severity: "Critical",
                    Summary: "Host DB Container healthy",
                    Details: "Collector reports db container healthy",
                    Status: RuleStatus.ExplicitlyHealthy,
                    SkipSmtpNotification: true,
                    AlternateKey: "inc_db"
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation("host_db_unhealthy", "Host DB Container Unhealthy", "Critical", "", "", RuleStatus.Unknown, AlternateKey: "inc_db"));
            }
        }

        // 5. Background Workers: Enabled + Stale / Startup Grace + Per-Worker SLAs
        var workers = _workerTracker.GetStatuses();
        foreach (var worker in workers)
        {
            if (worker.WorkerName.Contains("backup", StringComparison.OrdinalIgnoreCase) ||
                worker.WorkerName.Contains("watchdog", StringComparison.OrdinalIgnoreCase))
            {
                // Host existing watchdog is sole 27h notification authority
                continue;
            }

            var workerKey = $"worker_stalled_{worker.WorkerName}";
            var altWorkerKey = $"worker_failure_{worker.WorkerName}";

            if (!worker.IsEnabled)
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: workerKey,
                    RuleName: $"Worker Stalled: {worker.WorkerName}",
                    Severity: "Warning",
                    Summary: $"Worker {worker.WorkerName} is disabled",
                    Details: "Worker is intentionally disabled in configuration",
                    Status: RuleStatus.ExplicitlyHealthy,
                    AlternateKey: altWorkerKey
                ));
            }
            else if (worker.Status == "Failed" || worker.IsStale)
            {
                var reason = worker.Status == "Failed" 
                    ? $"Worker reported failure: {worker.LastErrorMessage ?? "Unknown error"}"
                    : $"Worker exceeded SLA ({WorkerTelemetryTracker.GetWorkerSla(worker.WorkerName).TotalMinutes}m) without heartbeat";

                evaluations.Add(new RuleEvaluation(
                    IncidentKey: workerKey,
                    RuleName: $"Worker Stalled: {worker.WorkerName}",
                    Severity: "Warning",
                    Summary: $"Worker '{worker.WorkerName}' is stalled or failing",
                    Details: $"{reason}. Consecutive failures: {worker.ConsecutiveFailures}, Last error: {worker.LastErrorAt:O}",
                    Status: RuleStatus.Triggered,
                    AlternateKey: altWorkerKey
                ));
            }
            else
            {
                evaluations.Add(new RuleEvaluation(
                    IncidentKey: workerKey,
                    RuleName: $"Worker Stalled: {worker.WorkerName}",
                    Severity: "Warning",
                    Summary: $"Worker {worker.WorkerName} is healthy",
                    Details: $"Status: Healthy, Last success: {worker.LastSuccessAt:O}",
                    Status: RuleStatus.ExplicitlyHealthy,
                    AlternateKey: altWorkerKey
                ));
            }
        }

        // Apply Evaluations to Database
        foreach (var eval in evaluations)
        {
            if (eval.Status == RuleStatus.Triggered)
            {
                await RecordOrUpdateIncidentAsync(eval, now, ct);
            }
            else if (eval.Status == RuleStatus.ExplicitlyHealthy)
            {
                await ResolveIncidentIfActiveAsync(eval, now, ct);
            }
        }

        // Retry any pending recovery notices that previously failed SMTP delivery
        await RetryPendingRecoveryNoticesAsync(ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task RecordOrUpdateIncidentAsync(RuleEvaluation eval, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.MonitoringIncidents.FirstOrDefaultAsync(
            x => (x.IncidentKey == eval.IncidentKey ||
                 (eval.AlternateKey != null && (x.IncidentKey == eval.AlternateKey || x.IncidentKey.StartsWith(eval.AlternateKey))) ||
                 (eval.IncidentKey != null && x.IncidentKey.StartsWith(eval.IncidentKey)))
                 && x.State != "Resolved", ct);

        if (existing == null)
        {
            var incident = new MonitoringIncident
            {
                IncidentKey = eval.IncidentKey,
                RuleName = eval.RuleName,
                Severity = eval.Severity,
                State = "Active",
                Summary = eval.Summary,
                Details = eval.Details,
                StartedAt = now,
                LastNotifiedAt = null,
                NotificationCount = 0,
                UpdatedAt = now
            };
            _db.MonitoringIncidents.Add(incident);
            await _db.SaveChangesAsync(ct);

            if (!eval.SkipSmtpNotification)
            {
                try
                {
                    await _notifier.SendIncidentAlertAsync(incident, isRecovery: false, ct);
                    incident.LastNotifiedAt = now;
                    incident.NotificationCount = 1;
                    incident.UpdatedAt = now;
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send initial incident alert for {Key}. Will retry next cycle.", eval.IncidentKey);
                }
            }
        }
        else
        {
            existing.RuleName = eval.RuleName;
            existing.Severity = eval.Severity;
            existing.Summary = eval.Summary;
            existing.Details = eval.Details;
            existing.UpdatedAt = now;

            var needsNotification = !eval.SkipSmtpNotification &&
                (existing.LastNotifiedAt == null || (now - existing.LastNotifiedAt.Value).TotalSeconds >= 3600.0);

            if (needsNotification)
            {
                try
                {
                    await _notifier.SendIncidentAlertAsync(existing, isRecovery: false, ct);
                    existing.LastNotifiedAt = now;
                    existing.NotificationCount++;
                    existing.UpdatedAt = now;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send reminder incident alert for {Key}. Will retry next cycle.", eval.IncidentKey);
                }
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ResolveIncidentIfActiveAsync(RuleEvaluation eval, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.MonitoringIncidents.FirstOrDefaultAsync(
            x => (x.IncidentKey == eval.IncidentKey ||
                 (eval.AlternateKey != null && (x.IncidentKey == eval.AlternateKey || x.IncidentKey.StartsWith(eval.AlternateKey))) ||
                 (eval.IncidentKey != null && x.IncidentKey.StartsWith(eval.IncidentKey)))
                 && x.State != "Resolved", ct);

        if (existing != null)
        {
            existing.State = "Resolved";
            existing.ResolvedAt = now;
            existing.UpdatedAt = now;

            if (!eval.SkipSmtpNotification)
            {
                try
                {
                    await _notifier.SendIncidentAlertAsync(existing, isRecovery: true, ct);
                    existing.LastNotifiedAt = now;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send recovery alert for {Key}. Marking pending retry.", eval.IncidentKey);
                    existing.Details += "\n[Pending Recovery Notice]";
                }
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task RetryPendingRecoveryNoticesAsync(CancellationToken ct)
    {
        var pendingRecoveries = await _db.MonitoringIncidents
            .Where(x => x.State == "Resolved" && x.Details.Contains("[Pending Recovery Notice]"))
            .ToListAsync(ct);

        foreach (var incident in pendingRecoveries)
        {
            try
            {
                await _notifier.SendIncidentAlertAsync(incident, isRecovery: true, ct);
                incident.Details = incident.Details.Replace("\n[Pending Recovery Notice]", "");
                incident.LastNotifiedAt = _clock.GetUtcNow();
                _logger.LogInformation("Successfully delivered pending recovery notice for {Key}", incident.IncidentKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pending recovery notice retry failed for {Key}", incident.IncidentKey);
            }
        }
    }
}

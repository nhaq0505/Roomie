using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class IncidentMonitoringService(
    AppDbContext db,
    IOperationsMonitoringService opsService,
    IIncidentNotifier notifier,
    TimeProvider clock) : IIncidentService
{
    public async Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(CancellationToken ct)
    {
        var list = await db.MonitoringIncidents.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);

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
        var incident = await db.MonitoringIncidents.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Incident not found.");

        var now = clock.GetUtcNow();
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

        await db.SaveChangesAsync(ct);

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

    public async Task TriggerIncidentCheckAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ops = await opsService.GetOperationsMetricsAsync("1h", ct);
        var activeRuleKeys = new HashSet<string>();

        // Rule 1: 5xx Error Rate
        if (ops.Overview.Api5xxRatePercent != null && (ops.Overview.TotalRequestsLastHour ?? 0) >= 10)
        {
            var rate = ops.Overview.Api5xxRatePercent.Value;
            if (rate >= 10.0)
            {
                activeRuleKeys.Add("high_5xx_rate");
                await RecordOrUpdateIncidentAsync("high_5xx_rate", "High API 5xx Error Rate", "Critical",
                    $"API 5xx error rate is {rate}% over the last hour (>= 10% threshold)",
                    $"Total requests: {ops.Overview.TotalRequestsLastHour}, 5xx rate: {rate}%", now, ct);
            }
            else if (rate >= 5.0)
            {
                activeRuleKeys.Add("high_5xx_rate");
                await RecordOrUpdateIncidentAsync("high_5xx_rate", "Elevated API 5xx Error Rate", "Warning",
                    $"API 5xx error rate is {rate}% over the last hour (>= 5% threshold)",
                    $"Total requests: {ops.Overview.TotalRequestsLastHour}, 5xx rate: {rate}%", now, ct);
            }
        }

        // Rule 2: Latency P95
        if (ops.Overview.LatencyP95Ms != null && (ops.Overview.TotalRequestsLastHour ?? 0) >= 10)
        {
            var p95 = ops.Overview.LatencyP95Ms.Value;
            if (p95 >= 2000.0)
            {
                activeRuleKeys.Add("high_p95_latency");
                await RecordOrUpdateIncidentAsync("high_p95_latency", "Severe API Latency (P95 >= 2s)", "Critical",
                    $"API P95 latency is {p95}ms (>= 2000ms threshold)",
                    $"Observed P95 latency: {p95}ms across window {ops.Overview.Window}", now, ct);
            }
            else if (p95 >= 1000.0)
            {
                activeRuleKeys.Add("high_p95_latency");
                await RecordOrUpdateIncidentAsync("high_p95_latency", "Elevated API Latency (P95 >= 1s)", "Warning",
                    $"API P95 latency is {p95}ms (>= 1000ms threshold)",
                    $"Observed P95 latency: {p95}ms across window {ops.Overview.Window}", now, ct);
            }
        }

        // Rule 3: Database Connectivity
        if (ops.Overview.DatabaseConnected == false)
        {
            activeRuleKeys.Add("database_disconnected");
            await RecordOrUpdateIncidentAsync("database_disconnected", "Database Connectivity Failure", "Critical",
                "Database health check failed; application cannot query database",
                $"Database status: {ops.Overview.DatabaseStatus ?? "Disconnected"}", now, ct);
        }

        // Rule 4: Host Metrics
        if (ops.HostMetrics != null)
        {
            if (ops.HostMetrics.IsStale)
            {
                activeRuleKeys.Add("host_collector_stale");
                await RecordOrUpdateIncidentAsync("host_collector_stale", "Host Telemetry Stale", "Warning",
                    "Host collector metrics have not updated in > 180 seconds",
                    $"Last recorded sample: {ops.HostMetrics.RecordedAt:O}", now, ct);
            }

            if (ops.HostMetrics.CpuPercent != null && ops.HostMetrics.CpuPercent.Value >= 85.0)
            {
                var cpu = ops.HostMetrics.CpuPercent.Value;
                var severity = cpu >= 95.0 ? "Critical" : "Warning";
                activeRuleKeys.Add("host_cpu_high");
                await RecordOrUpdateIncidentAsync("host_cpu_high", "High Host CPU Utilization", severity,
                    $"Host CPU usage is {cpu}% (>= 85% threshold)",
                    $"CPU percent: {cpu}%, Host status: {ops.HostMetrics.HostStatus}", now, ct);
            }

            if (ops.HostMetrics.DiskPercent != null && ops.HostMetrics.DiskPercent.Value >= 85.0)
            {
                var disk = ops.HostMetrics.DiskPercent.Value;
                var severity = disk >= 95.0 ? "Critical" : "Warning";
                activeRuleKeys.Add("host_disk_high");
                await RecordOrUpdateIncidentAsync("host_disk_high", "High Host Disk Utilization", severity,
                    $"Host Disk usage is {disk}% (>= 85% threshold)",
                    $"Disk percent: {disk}%", now, ct);
            }
        }

        // Rule 5: Worker Consecutive Failures
        foreach (var worker in ops.Workers)
        {
            var key = $"worker_failure_{worker.WorkerName}";
            if (worker.Status == "Failed")
            {
                activeRuleKeys.Add(key);
                await RecordOrUpdateIncidentAsync(key, $"Worker Failure: {worker.WorkerName}", "Warning",
                    $"Worker '{worker.WorkerName}' reported failed status",
                    $"Last error: {worker.LastErrorMessage ?? "Unknown failure"}, Last error at: {worker.LastErrorAt:O}", now, ct);
            }
        }

        // Auto-resolve any active incidents in DB whose conditions are no longer met
        var openIncidents = await db.MonitoringIncidents.Where(x => x.State != "Resolved").ToListAsync(ct);
        foreach (var incident in openIncidents)
        {
            if (!activeRuleKeys.Contains(incident.IncidentKey))
            {
                incident.State = "Resolved";
                incident.ResolvedAt = now;
                incident.UpdatedAt = now;
                await notifier.SendIncidentAlertAsync(incident, isRecovery: true, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RecordOrUpdateIncidentAsync(
        string key,
        string ruleName,
        string severity,
        string summary,
        string details,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.MonitoringIncidents.FirstOrDefaultAsync(x => x.IncidentKey == key && x.State != "Resolved", ct);
        if (existing == null)
        {
            var incident = new MonitoringIncident
            {
                IncidentKey = key,
                RuleName = ruleName,
                Severity = severity,
                State = "Active",
                Summary = summary,
                Details = details,
                StartedAt = now,
                LastNotifiedAt = now,
                NotificationCount = 1,
                UpdatedAt = now
            };
            db.MonitoringIncidents.Add(incident);
            await db.SaveChangesAsync(ct);
            await notifier.SendIncidentAlertAsync(incident, isRecovery: false, ct);
        }
        else
        {
            existing.RuleName = ruleName;
            existing.Severity = severity;
            existing.Summary = summary;
            existing.Details = details;
            existing.UpdatedAt = now;

            // 1-hour cooldown
            if (existing.LastNotifiedAt == null || (now - existing.LastNotifiedAt.Value).TotalSeconds >= 3600.0)
            {
                existing.LastNotifiedAt = now;
                existing.NotificationCount++;
                await notifier.SendIncidentAlertAsync(existing, isRecovery: false, ct);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

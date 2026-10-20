using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed record IncidentSnapshot(
    Guid Id,
    string IncidentKey,
    string RuleName,
    string Severity,
    string State,
    string Summary,
    string Details,
    DateTimeOffset StartedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? LastNotifiedAt,
    int NotificationCount
);

public sealed record IncidentEvaluationInput(
    OperationsOverviewSnapshot? Overview,
    HostCollectorMetrics? HostMetrics,
    IReadOnlyList<WorkerExecutionStatus>? Workers,
    OperationalQueueMetrics? Queues
);

public sealed record OperationalQueueMetrics(
    int? ZaloPendingQueueCount,
    int? ZaloEligibleWindowCount,
    int? PayOsPendingReconcileCount,
    long? SmtpAcceptedNotDeliveredCount
);

public interface IIncidentAlertNotifier
{
    Task SendAlertAsync(IncidentSnapshot incident, bool isRecovery, CancellationToken ct = default);
}

public interface IIncidentEngine
{
    Task EvaluateRulesAsync(IncidentEvaluationInput input, CancellationToken ct = default);
    IReadOnlyList<IncidentSnapshot> GetIncidents();
    IncidentSnapshot? AcknowledgeIncident(Guid id, string? note = null);
    IncidentSnapshot? ResolveIncidentManually(Guid id, string? note = null);
}

public sealed class IncidentEngine : IIncidentEngine
{
    private readonly ConcurrentDictionary<string, MutableIncident> _incidentsByKey = new();
    private readonly IIncidentAlertNotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly ILogger<IncidentEngine> _logger;

    public IncidentEngine(
        IIncidentAlertNotifier notifier,
        TimeProvider clock,
        ILogger<IncidentEngine> logger)
    {
        _notifier = notifier;
        _clock = clock;
        _logger = logger;
    }

    public async Task EvaluateRulesAsync(IncidentEvaluationInput input, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var evaluatedKeys = new HashSet<string>();

        // Rule 1: 5xx Error Rate
        if (input.Overview?.Api5xxRatePercent != null && input.Overview.TotalRequestsLastHour > 10)
        {
            var rate = input.Overview.Api5xxRatePercent.Value;
            if (rate >= 10.0)
            {
                evaluatedKeys.Add("high_5xx_rate");
                await TriggerOrUpdateAsync("high_5xx_rate", "High API 5xx Error Rate", "Critical",
                    $"API 5xx error rate is {rate}% over the last hour (>= 10% threshold)",
                    $"Total requests: {input.Overview.TotalRequestsLastHour}, 5xx rate: {rate}%", now, ct);
            }
            else if (rate >= 5.0)
            {
                evaluatedKeys.Add("high_5xx_rate");
                await TriggerOrUpdateAsync("high_5xx_rate", "Elevated API 5xx Error Rate", "Warning",
                    $"API 5xx error rate is {rate}% over the last hour (>= 5% threshold)",
                    $"Total requests: {input.Overview.TotalRequestsLastHour}, 5xx rate: {rate}%", now, ct);
            }
        }

        // Rule 2: High Latency P95
        if (input.Overview?.LatencyP95Ms != null && input.Overview.TotalRequestsLastHour > 10)
        {
            var p95 = input.Overview.LatencyP95Ms.Value;
            if (p95 >= 2000.0)
            {
                evaluatedKeys.Add("high_p95_latency");
                await TriggerOrUpdateAsync("high_p95_latency", "Severe API Latency (P95 >= 2s)", "Critical",
                    $"API P95 latency is {p95}ms (>= 2000ms threshold)",
                    $"Observed P95 latency: {p95}ms across window {input.Overview.Window}", now, ct);
            }
            else if (p95 >= 1000.0)
            {
                evaluatedKeys.Add("high_p95_latency");
                await TriggerOrUpdateAsync("high_p95_latency", "Elevated API Latency (P95 >= 1s)", "Warning",
                    $"API P95 latency is {p95}ms (>= 1000ms threshold)",
                    $"Observed P95 latency: {p95}ms across window {input.Overview.Window}", now, ct);
            }
        }

        // Rule 3: Database Connectivity
        if (input.Overview?.DatabaseConnected == false)
        {
            evaluatedKeys.Add("database_disconnected");
            await TriggerOrUpdateAsync("database_disconnected", "Database Connectivity Failure", "Critical",
                "Database health check failed; application cannot query database",
                $"Database status: {input.Overview.DatabaseStatus ?? "Disconnected"}", now, ct);
        }

        // Rule 4: Host Metrics
        if (input.HostMetrics != null)
        {
            if (input.HostMetrics.IsStale)
            {
                evaluatedKeys.Add("host_collector_stale");
                await TriggerOrUpdateAsync("host_collector_stale", "Host Telemetry Stale", "Warning",
                    "Host collector metrics have not updated in > 180 seconds",
                    $"Last recorded sample: {input.HostMetrics.RecordedAt:O}", now, ct);
            }

            if (input.HostMetrics.CpuPercent != null && input.HostMetrics.CpuPercent.Value >= 85.0)
            {
                var cpu = input.HostMetrics.CpuPercent.Value;
                var severity = cpu >= 95.0 ? "Critical" : "Warning";
                evaluatedKeys.Add("host_cpu_high");
                await TriggerOrUpdateAsync("host_cpu_high", "High Host CPU Utilization", severity,
                    $"Host CPU usage is {cpu}% (>= 85% threshold)",
                    $"CPU percent: {cpu}%, Host status: {input.HostMetrics.HostStatus}", now, ct);
            }

            if (input.HostMetrics.DiskPercent != null && input.HostMetrics.DiskPercent.Value >= 85.0)
            {
                var disk = input.HostMetrics.DiskPercent.Value;
                var severity = disk >= 95.0 ? "Critical" : "Warning";
                evaluatedKeys.Add("host_disk_high");
                await TriggerOrUpdateAsync("host_disk_high", "High Host Disk Utilization", severity,
                    $"Host Disk usage is {disk}% (>= 85% threshold)",
                    $"Disk percent: {disk}%", now, ct);
            }

            if (input.HostMetrics.MemoryPercent != null && input.HostMetrics.MemoryPercent.Value >= 85.0)
            {
                var mem = input.HostMetrics.MemoryPercent.Value;
                var severity = mem >= 95.0 ? "Critical" : "Warning";
                evaluatedKeys.Add("host_memory_high");
                await TriggerOrUpdateAsync("host_memory_high", "High Host Memory Utilization", severity,
                    $"Host Memory usage is {mem}% (>= 85% threshold)",
                    $"Memory percent: {mem}%", now, ct);
            }
        }

        // Rule 5: Worker Consecutive Failures
        if (input.Workers != null)
        {
            foreach (var worker in input.Workers)
            {
                var key = $"worker_failure_{worker.WorkerName}";
                if (worker.ConsecutiveFailures >= 3 || worker.Status == "Failed")
                {
                    evaluatedKeys.Add(key);
                    var severity = worker.ConsecutiveFailures >= 3 ? "Critical" : "Warning";
                    await TriggerOrUpdateAsync(key, $"Worker Failure: {worker.WorkerName}", severity,
                        $"Worker '{worker.WorkerName}' has failed {worker.ConsecutiveFailures} consecutive times",
                        $"Last error: {worker.LastErrorMessage ?? "Unknown failure"}, Last error at: {worker.LastErrorAt:O}", now, ct);
                }
            }
        }

        // Auto-resolve any active incident whose condition cleared
        foreach (var (key, inc) in _incidentsByKey)
        {
            if (inc.State != "Resolved" && !evaluatedKeys.Contains(key))
            {
                // Condition cleared!
                await ResolveAutoAsync(inc, now, ct);
            }
        }
    }

    public IReadOnlyList<IncidentSnapshot> GetIncidents()
    {
        return _incidentsByKey.Values
            .Select(x => x.ToSnapshot())
            .OrderByDescending(x => x.StartedAt)
            .ToList();
    }

    public IncidentSnapshot? AcknowledgeIncident(Guid id, string? note = null)
    {
        var target = _incidentsByKey.Values.FirstOrDefault(x => x.Id == id);
        if (target == null) return null;

        lock (target.SyncRoot)
        {
            if (target.State == "Active")
            {
                target.State = "Acknowledged";
                target.AcknowledgedAt = _clock.GetUtcNow();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    target.Details += $"\n[Ack Note]: {note}";
                }
            }
            return target.ToSnapshot();
        }
    }

    public IncidentSnapshot? ResolveIncidentManually(Guid id, string? note = null)
    {
        var target = _incidentsByKey.Values.FirstOrDefault(x => x.Id == id);
        if (target == null) return null;

        lock (target.SyncRoot)
        {
            if (target.State != "Resolved")
            {
                target.State = "Resolved";
                target.ResolvedAt = _clock.GetUtcNow();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    target.Details += $"\n[Resolution Note]: {note}";
                }
            }
            return target.ToSnapshot();
        }
    }

    private async Task TriggerOrUpdateAsync(
        string key,
        string ruleName,
        string severity,
        string summary,
        string details,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var inc = _incidentsByKey.GetOrAdd(key, k => new MutableIncident
        {
            IncidentKey = k,
            RuleName = ruleName,
            Severity = severity,
            State = "Active",
            Summary = summary,
            Details = details,
            StartedAt = now
        });

        bool sendAlert = false;
        IncidentSnapshot snapshot;

        lock (inc.SyncRoot)
        {
            inc.RuleName = ruleName;
            inc.Severity = severity;
            inc.Summary = summary;
            inc.Details = details;

            if (inc.State == "Resolved")
            {
                // Re-activated!
                inc.State = "Active";
                inc.StartedAt = now;
                inc.ResolvedAt = null;
                inc.AcknowledgedAt = null;
                inc.LastNotifiedAt = now;
                inc.NotificationCount = 1;
                sendAlert = true;
            }
            else if (inc.LastNotifiedAt == null)
            {
                // First notification
                inc.LastNotifiedAt = now;
                inc.NotificationCount = 1;
                sendAlert = true;
            }
            else if ((now - inc.LastNotifiedAt.Value).TotalSeconds >= 3600.0)
            {
                // 1-hour cooldown passed, send reminder
                inc.LastNotifiedAt = now;
                inc.NotificationCount++;
                sendAlert = true;
            }

            snapshot = inc.ToSnapshot();
        }

        if (sendAlert)
        {
            try
            {
                await _notifier.SendAlertAsync(snapshot, isRecovery: false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send alert for incident {Key}", key);
            }
        }
    }

    private async Task ResolveAutoAsync(MutableIncident inc, DateTimeOffset now, CancellationToken ct)
    {
        IncidentSnapshot snapshot;
        lock (inc.SyncRoot)
        {
            if (inc.State == "Resolved") return;
            inc.State = "Resolved";
            inc.ResolvedAt = now;
            snapshot = inc.ToSnapshot();
        }

        try
        {
            await _notifier.SendAlertAsync(snapshot, isRecovery: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send recovery alert for incident {Key}", inc.IncidentKey);
        }
    }

    private sealed class MutableIncident
    {
        public readonly object SyncRoot = new();
        public Guid Id { get; } = Guid.NewGuid();
        public required string IncidentKey { get; init; }
        public required string RuleName { get; set; }
        public required string Severity { get; set; }
        public string State { get; set; } = "Active";
        public required string Summary { get; set; }
        public required string Details { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public DateTimeOffset? LastNotifiedAt { get; set; }
        public int NotificationCount { get; set; }

        public IncidentSnapshot ToSnapshot()
        {
            return new IncidentSnapshot(
                Id: Id,
                IncidentKey: IncidentKey,
                RuleName: RuleName,
                Severity: Severity,
                State: State,
                Summary: Summary,
                Details: Details,
                StartedAt: StartedAt,
                AcknowledgedAt: AcknowledgedAt,
                ResolvedAt: ResolvedAt,
                LastNotifiedAt: LastNotifiedAt,
                NotificationCount: NotificationCount
            );
        }
    }
}

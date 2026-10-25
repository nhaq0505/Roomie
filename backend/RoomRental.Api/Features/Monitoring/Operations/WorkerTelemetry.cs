using System.Collections.Concurrent;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed record WorkerExecutionStatus(
    string WorkerName,
    bool IsEnabled,
    string Status,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastErrorAt,
    string? LastErrorMessage,
    int ConsecutiveFailures,
    bool IsStale
);

public interface IWorkerTelemetryTracker
{
    void RecordHeartbeat(string workerName, bool isEnabled, string status, string? errorMessage = null);
    void RecordStart(string workerName);
    void RecordSuccess(string workerName);
    void RecordFailure(string workerName, string errorMessage);
    IReadOnlyList<WorkerExecutionStatus> GetStatuses();
    Task ExecuteTrackedAsync(string workerName, Func<CancellationToken, Task> action, CancellationToken ct = default);
}

public sealed class WorkerTelemetryTracker : IWorkerTelemetryTracker, IWorkerStateTracker
{
    private readonly ConcurrentDictionary<string, WorkerState> _workers = new();
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _trackerCreatedAt;

    public WorkerTelemetryTracker(TimeProvider clock)
    {
        _clock = clock;
        _trackerCreatedAt = clock.GetUtcNow();
    }

    public static TimeSpan GetWorkerSla(string workerName) => workerName switch
    {
        "recovery-email" => TimeSpan.FromMinutes(5),
        "zalo" => TimeSpan.FromMinutes(5),
        "whatsapp" => TimeSpan.FromMinutes(5),
        "reconcile" => TimeSpan.FromMinutes(10),
        "cleanup" => TimeSpan.FromMinutes(20),
        "ops-aggregation" => TimeSpan.FromHours(2),
        "incident-check" => TimeSpan.FromMinutes(10),
        "monitoring-maintenance" => TimeSpan.FromHours(2),
        "invoice-sweep" => TimeSpan.FromHours(26),
        "recurring-expenses" => TimeSpan.FromHours(3),
        _ => TimeSpan.FromMinutes(15)
    };

    public void RecordHeartbeat(string workerName, bool isEnabled, string status, string? errorMessage = null)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.IsEnabled = isEnabled;
            state.UpdatedAt = now;

            if (status.Equals("Running", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = "Running";
                state.LastStartedAt = now;
                // LastSuccessAt must NOT be modified
            }
            else if (status.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = "Healthy";
                state.LastSuccessAt = now;
                state.LastErrorMessage = null;
                state.ConsecutiveFailures = 0;
            }
            else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = "Failed";
                state.LastErrorAt = now;
                state.LastErrorMessage = errorMessage;
                state.ConsecutiveFailures++;
                // LastSuccessAt must NOT be modified
            }
            else
            {
                state.Status = status;
            }
        }
    }

    public void RecordStart(string workerName)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.Status = "Running";
            state.LastStartedAt = now;
            state.UpdatedAt = now;
            // LastSuccessAt must NOT be modified
        }
    }

    public void RecordSuccess(string workerName)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.Status = "Healthy";
            state.LastSuccessAt = now;
            state.LastErrorMessage = null;
            state.ConsecutiveFailures = 0;
            state.UpdatedAt = now;
        }
    }

    public void RecordFailure(string workerName, string errorMessage)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.Status = "Failed";
            state.LastErrorAt = now;
            state.LastErrorMessage = errorMessage;
            state.ConsecutiveFailures++;
            state.UpdatedAt = now;
            // LastSuccessAt must NOT be modified
        }
    }

    public IReadOnlyList<WorkerExecutionStatus> GetStatuses()
    {
        var now = _clock.GetUtcNow();

        return _workers.Values.Select(w =>
        {
            lock (w.SyncRoot)
            {
                bool isStale;
                if (!w.IsEnabled)
                {
                    isStale = false;
                }
                else if (w.LastSuccessAt == null)
                {
                    // Startup grace period: 10 minutes from creation or first start before marking stale
                    var elapsed = now - (w.LastStartedAt ?? _trackerCreatedAt);
                    isStale = elapsed > TimeSpan.FromMinutes(10);
                }
                else
                {
                    var sla = GetWorkerSla(w.WorkerName);
                    isStale = (now - w.LastSuccessAt.Value) > sla;
                }

                return new WorkerExecutionStatus(
                    WorkerName: w.WorkerName,
                    IsEnabled: w.IsEnabled,
                    Status: w.Status,
                    LastStartedAt: w.LastStartedAt,
                    LastSuccessAt: w.LastSuccessAt,
                    LastErrorAt: w.LastErrorAt,
                    LastErrorMessage: w.LastErrorMessage,
                    ConsecutiveFailures: w.ConsecutiveFailures,
                    IsStale: isStale
                );
            }
        }).OrderBy(w => w.WorkerName).ToList();
    }

    public Task RecordWorkerHeartbeatAsync(string workerName, bool isEnabled, string status, string? errorMessage, CancellationToken ct)
    {
        RecordHeartbeat(workerName, isEnabled, status, errorMessage);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkerStatusDto>> GetWorkerStatusesAsync(CancellationToken ct)
    {
        var statuses = GetStatuses();
        IReadOnlyList<WorkerStatusDto> dtos = statuses.Select(s => new WorkerStatusDto(
            s.WorkerName,
            s.IsEnabled,
            s.Status,
            s.LastStartedAt,
            s.LastSuccessAt,
            s.LastErrorAt,
            s.LastErrorMessage,
            s.IsStale
        )).ToList();

        return Task.FromResult(dtos);
    }

    public async Task ExecuteTrackedAsync(string workerName, Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        RecordStart(workerName);
        try
        {
            await action(ct);
            RecordSuccess(workerName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordFailure(workerName, ex.Message);
            throw;
        }
    }

    private sealed class WorkerState
    {
        public readonly object SyncRoot = new();
        public string WorkerName { get; }
        public bool IsEnabled { get; set; } = true;
        public string Status { get; set; } = "Healthy";
        public DateTimeOffset? LastStartedAt { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastErrorAt { get; set; }
        public string? LastErrorMessage { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public WorkerState(string workerName)
        {
            WorkerName = workerName;
        }
    }
}

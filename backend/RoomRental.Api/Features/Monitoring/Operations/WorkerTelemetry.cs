using System.Collections.Concurrent;

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

public sealed class WorkerTelemetryTracker : IWorkerTelemetryTracker
{
    private readonly ConcurrentDictionary<string, WorkerState> _workers = new();
    private readonly TimeProvider _clock;

    public WorkerTelemetryTracker(TimeProvider clock)
    {
        _clock = clock;
    }

    public void RecordHeartbeat(string workerName, bool isEnabled, string status, string? errorMessage = null)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.IsEnabled = isEnabled;
            state.Status = status;
            state.UpdatedAt = now;
            if (status == "Healthy")
            {
                state.LastSuccessAt = now;
                state.ConsecutiveFailures = 0;
            }
            else if (status == "Failed")
            {
                state.LastErrorAt = now;
                state.LastErrorMessage = errorMessage;
                state.ConsecutiveFailures++;
            }
        }
    }

    public void RecordStart(string workerName)
    {
        var state = _workers.GetOrAdd(workerName, name => new WorkerState(name));
        var now = _clock.GetUtcNow();

        lock (state.SyncRoot)
        {
            state.LastStartedAt = now;
            state.UpdatedAt = now;
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
        }
    }

    public IReadOnlyList<WorkerExecutionStatus> GetStatuses()
    {
        var now = _clock.GetUtcNow();
        var staleCutoff = now.AddMinutes(-10);

        return _workers.Values.Select(w =>
        {
            lock (w.SyncRoot)
            {
                var isStale = w.UpdatedAt < staleCutoff;
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

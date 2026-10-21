using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using RoomRental.Api.Features.Monitoring.Operations;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring;

public sealed class MonitoringScheduleOptions
{
    public const string SectionName = "Monitoring:Schedule";
    public bool IncidentChecksEnabled { get; set; } = true;
    public bool MaintenanceEnabled { get; set; } = true;
    [Range(60, 300)]
    public int IncidentIntervalSeconds { get; set; } = 60;
    [Range(300, 3600)]
    public int MaintenanceIntervalSeconds { get; set; } = 3600;
    [Range(1, 89)]
    public int RollupDays { get; set; } = 7;
}

// Independent, serial loops: slow maintenance must not delay incident probes.
// This scheduler is for the single API instance deployment (no distributed lease).
public sealed class MonitoringBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IWorkerTelemetryTracker tracker,
    TimeProvider clock,
    IOptions<MonitoringScheduleOptions> options,
    ILogger<MonitoringBackgroundWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value;
        return Task.WhenAll(
            RunLoopAsync("incident-check", schedule.IncidentChecksEnabled,
                TimeSpan.FromSeconds(schedule.IncidentIntervalSeconds), async ct =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IIncidentService>().TriggerIncidentCheckAsync(ct);
                }, stoppingToken),
            RunLoopAsync("monitoring-maintenance", schedule.MaintenanceEnabled,
                TimeSpan.FromSeconds(schedule.MaintenanceIntervalSeconds), async ct =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var maintenance = scope.ServiceProvider.GetRequiredService<IMonitoringMaintenanceService>();
                    var today = ApiSupport.VietnamDate(clock.GetUtcNow());
                    // Recompute closed days to incorporate late events/corrections. Never roll up
                    // today as a completed day, or prune source data before a successful rollup.
                    for (var daysAgo = schedule.RollupDays; daysAgo >= 1; daysAgo--)
                        await maintenance.RollupDailyAggregateAsync(today.AddDays(-daysAgo), ct);
                    await maintenance.PruneExpiredTelemetryAsync(ct);
                }, stoppingToken));
    }

    private async Task RunLoopAsync(string name, bool enabled, TimeSpan interval,
        Func<CancellationToken, Task> action, CancellationToken ct)
    {
        tracker.RecordHeartbeat(name, enabled, enabled ? "Running" : "Disabled");
        if (!enabled) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await tracker.ExecuteTrackedAsync(name, action, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitoring worker {Worker} cycle failed; retrying next interval.", name);
            }

            try { await Task.Delay(interval, clock, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}

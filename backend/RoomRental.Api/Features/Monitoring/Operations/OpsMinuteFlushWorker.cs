using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoomRental.Api.Data;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class OpsMinuteFlushWorker(
    IServiceScopeFactory scopeFactory,
    IRouteLatencyAggregator routeAggregator,
    TimeProvider clock,
    ILogger<OpsMinuteFlushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tracker = scope.ServiceProvider.GetRequiredService<IWorkerStateTracker>();

                await tracker.RecordWorkerHeartbeatAsync("ops-aggregation", isEnabled: true, status: "Running", errorMessage: null, stoppingToken);

                // Drain completed buckets from previous minute
                var currentMinuteEpoch = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
                var completedBuckets = routeAggregator.DrainCompletedMinuteBuckets(currentMinuteEpoch);

                if (completedBuckets.Count > 0)
                {
                    db.RouteMinuteBuckets.AddRange(completedBuckets);
                    await db.SaveChangesAsync(stoppingToken);
                }

                // Ops 30-day retention on RouteMinuteBuckets
                var cutoff30d = clock.GetUtcNow().AddDays(-30);
                await db.RouteMinuteBuckets
                    .Where(x => x.MinuteTimestamp < cutoff30d)
                    .ExecuteDeleteAsync(stoppingToken);

                await tracker.RecordWorkerHeartbeatAsync("ops-aggregation", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ops minute flush and retention cycle failed.");
                try
                {
                    await using var errorScope = scopeFactory.CreateAsyncScope();
                    var tracker = errorScope.ServiceProvider.GetRequiredService<IWorkerStateTracker>();
                    await tracker.RecordWorkerHeartbeatAsync("ops-aggregation", isEnabled: true, status: "Failed", errorMessage: ex.Message, stoppingToken);
                }
                catch
                {
                    // Ignore secondary error recording failure
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

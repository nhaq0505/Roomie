using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class MonitoringAggregationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<MonitoringAggregationWorker> logger) : BackgroundService
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

                await tracker.RecordWorkerHeartbeatAsync("aggregation", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);

                var now = clock.GetUtcNow();
                var today = DateOnly.FromDateTime(now.Date);
                var yesterday = today.AddDays(-1);

                await AggregateDateAsync(db, today, stoppingToken);
                await AggregateDateAsync(db, yesterday, stoppingToken);

                await tracker.RecordWorkerHeartbeatAsync("aggregation", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Monitoring aggregation cycle failed.");
                try
                {
                    await using var errorScope = scopeFactory.CreateAsyncScope();
                    var tracker = errorScope.ServiceProvider.GetRequiredService<IWorkerStateTracker>();
                    await tracker.RecordWorkerHeartbeatAsync("aggregation", isEnabled: true, status: "Failed", errorMessage: ex.Message, stoppingToken);
                }
                catch
                {
                    // Ignore secondary logging failure
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task AggregateDateAsync(AppDbContext db, DateOnly date, CancellationToken ct)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var totalSessions = await db.AnalyticsEvents.AsNoTracking()
            .Where(x => x.ServerTimestamp >= dayStart && x.ServerTimestamp <= dayEnd)
            .Select(x => x.SessionId)
            .Distinct()
            .CountAsync(ct);

        var distinctVisitors = await db.AnalyticsEvents.AsNoTracking()
            .Where(x => x.ServerTimestamp >= dayStart && x.ServerTimestamp <= dayEnd)
            .Select(x => x.BrowserId)
            .Distinct()
            .CountAsync(ct);

        var registrations = await db.Users.AsNoTracking()
            .Where(x => x.CreatedAt >= dayStart && x.CreatedAt <= dayEnd)
            .CountAsync(ct);

        var activatedOwners = await db.OwnerMilestones.AsNoTracking()
            .Where(x => x.ActivatedAt >= dayStart && x.ActivatedAt <= dayEnd)
            .CountAsync(ct);

        var marketingCosts = await db.CampaignCosts.AsNoTracking()
            .Where(x => x.Date == date)
            .ToListAsync(ct);

        var spend = marketingCosts.Sum(x => x.CashVnd);
        var hours = marketingCosts.Sum(x => x.PersonHours);

        var metric = await db.DailyAggregateMetrics.SingleOrDefaultAsync(x => x.Date == date, ct);
        if (metric == null)
        {
            metric = new DailyAggregateMetric
            {
                Date = date,
                TotalSessions = totalSessions,
                DistinctVisitors = distinctVisitors,
                Registrations = registrations,
                ActivatedOwners = activatedOwners,
                MarketingSpendVnd = spend,
                MarketingHours = hours,
                CalculatedAt = DateTimeOffset.UtcNow
            };
            db.DailyAggregateMetrics.Add(metric);
        }
        else
        {
            metric.TotalSessions = totalSessions;
            metric.DistinctVisitors = distinctVisitors;
            metric.Registrations = registrations;
            metric.ActivatedOwners = activatedOwners;
            metric.MarketingSpendVnd = spend;
            metric.MarketingHours = hours;
            metric.CalculatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}

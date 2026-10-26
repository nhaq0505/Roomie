using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring;


public sealed class DefaultMonitoringMaintenanceService(AppDbContext db, TimeProvider clock) : IMonitoringMaintenanceService
{
    public async Task<int> PruneExpiredTelemetryAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var pruned = 0;

        // 1. Raw Web Events (90 days)
        var rawCutoff = now.AddDays(-90);
        var oldEvents = await db.AnalyticsEvents
            .Where(e => e.ServerTimestamp < rawCutoff)
            .ToListAsync(ct);
        if (oldEvents.Count > 0)
        {
            db.AnalyticsEvents.RemoveRange(oldEvents);
            pruned += oldEvents.Count;
        }

        // 2. Owner Daily Activities (13 months)
        var activityCutoff = today.AddMonths(-13);
        var oldActivities = await db.OwnerDailyActivities
            .Where(a => a.ActivityDate < activityCutoff)
            .ToListAsync(ct);
        if (oldActivities.Count > 0)
        {
            db.OwnerDailyActivities.RemoveRange(oldActivities);
            pruned += oldActivities.Count;
        }

        // 3. Daily Aggregate Metrics (13 months)
        var oldAggregates = await db.DailyAggregateMetrics
            .Where(a => a.Date < activityCutoff)
            .ToListAsync(ct);
        if (oldAggregates.Count > 0)
        {
            db.DailyAggregateMetrics.RemoveRange(oldAggregates);
            pruned += oldAggregates.Count;
        }

        if (oldEvents.Count > 0 || oldActivities.Count > 0 || oldAggregates.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // 4. Completed Milestone Outbox Events (30 days retention; preserve Pending, Failed, and retry records)
        var outboxCutoff = now.AddDays(-30);
        var prunedOutbox = await db.MilestoneOutboxEvents
            .Where(e => e.Status == "Completed" && (e.ProcessedAt != null ? e.ProcessedAt < outboxCutoff : e.OccurredAt < outboxCutoff))
            .ExecuteDeleteAsync(ct);
        pruned += prunedOutbox;

        return pruned;
    }

    public async Task<DailyAggregateMetric> RollupDailyAggregateAsync(DateOnly date, CancellationToken ct)
    {
        var startUtc = ApiSupport.StartOfVietnamDate(date);
        var endUtc = ApiSupport.EndOfVietnamDate(date);

        var daySessions = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ServerTimestamp >= startUtc && e.ServerTimestamp < endUtc)
            .Select(e => e.SessionId)
            .Distinct()
            .CountAsync(ct);

        var dayVisitors = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ServerTimestamp >= startUtc && e.ServerTimestamp < endUtc)
            .Select(e => e.BrowserId)
            .Distinct()
            .CountAsync(ct);

        var registrations = await db.OwnerMilestones
            .CountAsync(x => x.RegisteredAt >= startUtc && x.RegisteredAt < endUtc, ct);

        var activations = await db.OwnerMilestones
            .CountAsync(x => x.ActivatedAt >= startUtc && x.ActivatedAt < endUtc, ct);

        var wauStart = date.AddDays(-6);
        var activeOwnersWau = await db.OwnerDailyActivities.AsNoTracking()
            .Where(x => x.ActivityDate >= wauStart && x.ActivityDate <= date && x.HasOwnerAction)
            .Select(x => x.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var mauStart = date.AddDays(-29);
        var activeOwnersMau = await db.OwnerDailyActivities.AsNoTracking()
            .Where(x => x.ActivityDate >= mauStart && x.ActivityDate <= date && x.HasOwnerAction)
            .Select(x => x.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var subRevenue = await db.PayOsAttempts
            .Where(x => x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid && x.PaidAt >= startUtc && x.PaidAt < endUtc)
            .SumAsync(x => (decimal)x.Amount, ct);

        var costs = await db.CampaignCosts
            .Where(x => x.Date == date)
            .ToListAsync(ct);

        var marketingSpend = costs.Sum(x => x.CashVnd + (x.PersonHours > 0 && x.HourlyRate != null ? x.PersonHours * x.HourlyRate.Value : 0m));
        var marketingHours = costs.Sum(x => x.PersonHours);

        var aggregate = await db.DailyAggregateMetrics.SingleOrDefaultAsync(x => x.Date == date, ct);
        if (aggregate is null)
        {
            aggregate = new DailyAggregateMetric
            {
                Date = date,
                TotalSessions = daySessions,
                DistinctVisitors = dayVisitors,
                Registrations = registrations,
                ActivatedOwners = activations,
                ActiveOwnersWau = activeOwnersWau,
                ActiveOwnersMau = activeOwnersMau,
                SubscriptionRevenueVnd = subRevenue,
                MarketingSpendVnd = marketingSpend,
                MarketingHours = marketingHours,
                TotalRequests = 0,
                Total5xxErrors = 0,
                CalculatedAt = clock.GetUtcNow()
            };
            db.DailyAggregateMetrics.Add(aggregate);
        }
        else
        {
            aggregate.TotalSessions = daySessions;
            aggregate.DistinctVisitors = dayVisitors;
            aggregate.Registrations = registrations;
            aggregate.ActivatedOwners = activations;
            aggregate.ActiveOwnersWau = activeOwnersWau;
            aggregate.ActiveOwnersMau = activeOwnersMau;
            aggregate.SubscriptionRevenueVnd = subRevenue;
            aggregate.MarketingSpendVnd = marketingSpend;
            aggregate.MarketingHours = marketingHours;
            aggregate.CalculatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return aggregate;
    }
}

public sealed class DefaultOperationsMonitoringService : IOperationsMonitoringService
{
    public Task<AdminMonitoringOperationsResponse> GetOperationsMetricsAsync(string? window, CancellationToken ct)
    {
        throw new NotImplementedException("Operations monitoring queries are unimplemented in T0; will be implemented in T3.");
    }

    public Task RecordRouteMetricAsync(string routeTemplate, string httpMethod, int statusCode, double latencyMs, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class DefaultWorkerStateTracker(AppDbContext db, TimeProvider clock) : IWorkerStateTracker
{
    public async Task RecordWorkerHeartbeatAsync(
        string workerName,
        bool isEnabled,
        string status,
        string? errorMessage,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var worker = await db.WorkerHeartbeats.SingleOrDefaultAsync(x => x.WorkerName == workerName, ct);
        if (worker is null)
        {
            worker = new WorkerHeartbeatState
            {
                WorkerName = workerName,
                IsEnabled = isEnabled,
                Status = status,
                LastStartedAt = now,
                LastSuccessAt = status == "Healthy" ? now : null,
                LastErrorAt = status == "Failed" ? now : null,
                LastErrorMessage = errorMessage,
                ConsecFailures = status == "Failed" ? 1 : 0,
                UpdatedAt = now
            };
            db.WorkerHeartbeats.Add(worker);
        }
        else
        {
            worker.IsEnabled = isEnabled;
            worker.Status = status;
            worker.UpdatedAt = now;
            if (status.Equals("Running", StringComparison.OrdinalIgnoreCase))
            {
                worker.LastStartedAt = now;
            }
            else if (status.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            {
                worker.LastSuccessAt = now;
                worker.ConsecFailures = 0;
                worker.LastErrorMessage = null;
            }
            else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                worker.LastErrorAt = now;
                worker.LastErrorMessage = errorMessage;
                worker.ConsecFailures++;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WorkerStatusDto>> GetWorkerStatusesAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var list = await db.WorkerHeartbeats.AsNoTracking().ToListAsync(ct);
        return list.Select(w => new WorkerStatusDto(
            w.WorkerName,
            w.IsEnabled,
            w.Status,
            w.LastStartedAt,
            w.LastSuccessAt,
            w.LastErrorAt,
            w.LastErrorMessage,
            w.UpdatedAt < now.AddMinutes(-10)
        )).ToList();
    }
}

public sealed class DefaultHostMetricsReader : IHostMetricsReader
{
    public Task<HostMetricsDto?> ReadHostMetricsAsync(CancellationToken ct)
    {
        return Task.FromResult<HostMetricsDto?>(null);
    }
}

public sealed class DefaultIncidentService : IIncidentService
{
    public Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(CancellationToken ct)
    {
        throw new NotImplementedException("Incident monitoring is unimplemented in T0; will be implemented in T3.");
    }

    public Task<IncidentDto> UpdateIncidentStateAsync(Guid id, UpdateIncidentRequest request, CancellationToken ct)
    {
        throw new NotImplementedException("Incident updating is unimplemented in T0; will be implemented in T3.");
    }

    public Task TriggerIncidentCheckAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class NullIncidentNotifier : IIncidentNotifier
{
    public Task SendIncidentAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

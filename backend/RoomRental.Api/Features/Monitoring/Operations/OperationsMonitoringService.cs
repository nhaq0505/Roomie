using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring.Operations;

public sealed class OperationsMonitoringService(
    AppDbContext db,
    IRouteLatencyAggregator routeAggregator,
    IHostMetricsReader hostReader,
    IWorkerStateTracker workerTracker,
    TimeProvider clock) : IOperationsMonitoringService
{
    public Task RecordRouteMetricAsync(string routeTemplate, string httpMethod, int statusCode, double latencyMs, CancellationToken ct)
    {
        routeAggregator.Record(routeTemplate, httpMethod, statusCode, latencyMs);
        return Task.CompletedTask;
    }

    public async Task<AdminMonitoringOperationsResponse> GetOperationsMetricsAsync(string? window, CancellationToken ct)
    {
        var timeWindow = window == "24h" ? TimeSpan.FromHours(24) : TimeSpan.FromHours(1);
        
        bool? dbConnected = null;
        string? dbStatus = null;
        try
        {
            dbConnected = await db.Database.CanConnectAsync(ct);
            dbStatus = dbConnected.Value ? "Healthy" : "Unhealthy";
        }
        catch
        {
            dbConnected = false;
            dbStatus = "Unhealthy";
        }

        var overviewSnapshot = routeAggregator.GetOverviewSnapshot(timeWindow, dbConnected, dbStatus);
        var overviewDto = new OpsOverviewDto(
            OverallStatus: overviewSnapshot.OverallStatus,
            Api5xxRatePercent: overviewSnapshot.Api5xxRatePercent,
            LatencyP95Ms: overviewSnapshot.LatencyP95Ms,
            TotalRequestsLastHour: overviewSnapshot.TotalRequestsLastHour,
            DatabaseConnected: overviewSnapshot.DatabaseConnected,
            DatabaseStatus: overviewSnapshot.DatabaseStatus,
            Window: window ?? "1h"
        );

        var routeSnapshots = routeAggregator.GetRouteSnapshots(timeWindow);
        var routeMetrics = routeSnapshots.Select(r => new RouteLatencyMetricDto(
            RouteTemplate: r.RouteTemplate,
            HttpMethod: r.HttpMethod,
            RequestCount: r.RequestCount,
            Status5xxCount: r.Status5xxCount,
            P95LatencyMs: r.P95LatencyMs,
            AvgLatencyMs: r.AvgLatencyMs,
            MaxLatencyMs: r.MaxLatencyMs
        )).ToList();

        var hostMetrics = await hostReader.ReadHostMetricsAsync(ct);
        var workers = await workerTracker.GetWorkerStatusesAsync(ct);

        int? zaloPendingCount = null;
        int? zaloEligibleCount = null;
        int? payOsPendingCount = null;
        long? smtpPendingCount = null;

        if (dbConnected == true)
        {
            var nowMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
            zaloPendingCount = await db.ZaloReminders.AsNoTracking().CountAsync(x => x.Status == "Pending", ct);
            zaloEligibleCount = await db.ZaloReminders.AsNoTracking().CountAsync(x => x.Status == "Pending" && x.QueuedAtMs <= nowMs, ct);
            payOsPendingCount = await db.PayOsAttempts.AsNoTracking().CountAsync(x => x.Status == PaymentAttemptStatus.Pending, ct);
            smtpPendingCount = await db.PasswordRecoveries.AsNoTracking().CountAsync(x => x.ProtectedCode != null || x.NotificationPending, ct);
        }

        var queueStats = new OperationalQueueStatsDto(
            ZaloPendingQueueCount: zaloPendingCount,
            ZaloEligibleWindowCount: zaloEligibleCount,
            PayOsPendingReconcileCount: payOsPendingCount,
            SmtpAcceptedNotDeliveredCount: smtpPendingCount
        );

        return new AdminMonitoringOperationsResponse(
            Overview: overviewDto,
            RouteMetrics: routeMetrics,
            HostMetrics: hostMetrics,
            Workers: workers,
            Queues: queueStats
        );
    }
}

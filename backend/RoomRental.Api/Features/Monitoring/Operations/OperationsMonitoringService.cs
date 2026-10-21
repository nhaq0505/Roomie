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

        HostCollectorMetrics? collectorMetrics = null;
        HostMetricsDto? hostMetrics = null;
        if (hostReader is ILinuxHostCollectorMetricsReader linuxReader)
        {
            collectorMetrics = await linuxReader.ReadHostMetricsAsync(ct);
            if (collectorMetrics != null)
            {
                hostMetrics = new HostMetricsDto(
                    CpuPercent: collectorMetrics.CpuPercent,
                    MemoryPercent: collectorMetrics.MemoryPercent,
                    DiskPercent: collectorMetrics.DiskPercent,
                    ContainerCount: collectorMetrics.ContainerCount,
                    HealthEndpointStatus: collectorMetrics.HealthEndpointStatus,
                    HostStatus: collectorMetrics.HostStatus,
                    RecordedAt: collectorMetrics.RecordedAt,
                    IsStale: collectorMetrics.IsStale
                );
            }
        }
        else
        {
            hostMetrics = await hostReader.ReadHostMetricsAsync(ct);
        }

        var workersList = (await workerTracker.GetWorkerStatusesAsync(ct)).ToList();

        // Surface authoritative collector backupWatchdog as WorkerStatusDto named "backup-watchdog"
        if (!workersList.Any(w => w.WorkerName.Contains("backup", StringComparison.OrdinalIgnoreCase) || w.WorkerName.Contains("watchdog", StringComparison.OrdinalIgnoreCase)))
        {
            var bw = collectorMetrics?.BackupWatchdog;
            var isStale = collectorMetrics == null || collectorMetrics.IsStale || (bw != null && bw.AgeSeconds.HasValue && bw.AgeSeconds.Value > bw.ThresholdSeconds);

            string status;
            if (collectorMetrics == null || collectorMetrics.IsStale || bw == null || bw.Status.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                status = "Unknown";
            }
            else if (bw.Status.Equals("healthy", StringComparison.OrdinalIgnoreCase))
            {
                status = isStale ? "Unknown" : "Healthy";
            }
            else
            {
                status = isStale ? "Unknown" : "Degraded";
            }

            workersList.Add(new WorkerStatusDto(
                WorkerName: "backup-watchdog",
                IsEnabled: bw != null && !bw.Status.Equals("unknown", StringComparison.OrdinalIgnoreCase),
                Status: status,
                LastStartedAt: null,
                LastSuccessAt: bw?.LastSuccessAt,
                LastErrorAt: null,
                LastErrorMessage: bw != null && bw.Status.Equals("stale", StringComparison.OrdinalIgnoreCase)
                    ? $"Backup age exceeds threshold ({bw.ThresholdSeconds}s)"
                    : null,
                IsStale: isStale
            ));
        }

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
            Workers: workersList,
            Queues: queueStats,
            HistogramBuckets: routeAggregator.GetWindowSummary(timeWindow).HistogramBuckets
        );
    }
}

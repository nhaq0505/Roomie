using Microsoft.Extensions.DependencyInjection.Extensions;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Features.Monitoring.Operations;

namespace Microsoft.Extensions.DependencyInjection;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoringCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Core tracking & intake services (foundation implementations)
        services.AddScoped<IClientAnalyticsService, ClientAnalyticsService>();
        services.AddScoped<IOwnerMilestoneTracker>(sp => new OwnerMilestoneTracker(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<TimeProvider>()));

        // Query & Ops services registration points (Worker C and Worker B/T1 register or replace these)
        services.TryAddScoped<IGrowthMonitoringQueryService, DefaultGrowthMonitoringQueryService>();
        services.TryAddScoped<IRevenueMonitoringQueryService, DefaultRevenueMonitoringQueryService>();
        services.TryAddScoped<IMonitoringMaintenanceService, DefaultMonitoringMaintenanceService>();
        services.TryAddScoped<IOperationsMonitoringService, DefaultOperationsMonitoringService>();
        services.TryAddScoped<IWorkerStateTracker, DefaultWorkerStateTracker>();
        services.TryAddScoped<IHostMetricsReader, DefaultHostMetricsReader>();
        services.TryAddScoped<IIncidentService, DefaultIncidentService>();
        services.TryAddScoped<IIncidentNotifier, NullIncidentNotifier>();

        return services;
    }

    public static IServiceCollection AddMonitoringOperations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HostMetricsOptions>(configuration.GetSection(HostMetricsOptions.SectionName));
        services.Configure<IncidentNotificationOptions>(configuration.GetSection(IncidentNotificationOptions.SectionName));

        services.AddSingleton<IRouteLatencyAggregator, RouteLatencyAggregator>();
        services.AddSingleton<IWorkerTelemetryTracker, WorkerTelemetryTracker>();
        services.AddSingleton<IIncidentAlertNotifier, SmtpIncidentNotifier>();
        services.AddSingleton<IIncidentEngine, IncidentEngine>();

        services.AddScoped<IHostMetricsReader, LinuxHostCollectorMetricsReader>();
        services.AddScoped<ILinuxHostCollectorMetricsReader, LinuxHostCollectorMetricsReader>();
        services.AddScoped<IOperationsMonitoringService, OperationsMonitoringService>();
        services.AddScoped<IIncidentService, IncidentMonitoringService>();
        services.AddScoped<IIncidentNotifier, SmtpIncidentAlertNotifier>();

        services.AddHostedService<MonitoringAggregationWorker>();

        return services;
    }
}

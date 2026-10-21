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
        services.AddOptions<MonitoringScheduleOptions>()
            .Bind(configuration.GetSection(MonitoringScheduleOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.RemoveAll<IRouteLatencyAggregator>();
        services.AddSingleton<IRouteLatencyAggregator, RouteLatencyAggregator>();

        services.RemoveAll<IWorkerStateTracker>();
        services.RemoveAll<IWorkerTelemetryTracker>();
        services.RemoveAll<WorkerTelemetryTracker>();
        services.AddSingleton<WorkerTelemetryTracker>();
        services.AddSingleton<IWorkerTelemetryTracker>(sp => sp.GetRequiredService<WorkerTelemetryTracker>());
        services.AddSingleton<IWorkerStateTracker>(sp => sp.GetRequiredService<WorkerTelemetryTracker>());

        services.RemoveAll<IHostMetricsReader>();
        services.RemoveAll<ILinuxHostCollectorMetricsReader>();
        services.AddScoped<IHostMetricsReader, LinuxHostCollectorMetricsReader>();
        services.AddScoped<ILinuxHostCollectorMetricsReader, LinuxHostCollectorMetricsReader>();

        services.RemoveAll<IOperationsMonitoringService>();
        services.AddScoped<IOperationsMonitoringService, OperationsMonitoringService>();

        services.RemoveAll<IIncidentService>();
        services.AddScoped<IIncidentService, IncidentMonitoringService>();

        services.RemoveAll<IAlertEmailSender>();
        services.AddScoped<IAlertEmailSender, SmtpAlertEmailSender>();

        services.RemoveAll<IIncidentNotifier>();
        services.AddScoped<IIncidentNotifier, SmtpIncidentNotifier>();

        services.AddHostedService<OpsMinuteFlushWorker>();
        services.AddHostedService<MonitoringBackgroundWorker>();

        return services;
    }
}

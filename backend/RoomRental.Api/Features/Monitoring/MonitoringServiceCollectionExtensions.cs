using Microsoft.Extensions.DependencyInjection.Extensions;
using RoomRental.Api.Features.Monitoring;

namespace Microsoft.Extensions.DependencyInjection;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoringCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Core tracking & intake services (foundation implementations)
        services.AddScoped<IClientAnalyticsService, ClientAnalyticsService>();
        services.AddScoped<IOwnerMilestoneTracker, OwnerMilestoneTracker>();

        // Query & Ops services registration points (Worker C and Worker B/T1 register or replace these)
        services.TryAddScoped<IGrowthMonitoringQueryService, DefaultGrowthMonitoringQueryService>();
        services.TryAddScoped<IRevenueMonitoringQueryService, DefaultRevenueMonitoringQueryService>();
        services.TryAddScoped<IOperationsMonitoringService, DefaultOperationsMonitoringService>();
        services.TryAddScoped<IWorkerStateTracker, DefaultWorkerStateTracker>();
        services.TryAddScoped<IHostMetricsReader, DefaultHostMetricsReader>();
        services.TryAddScoped<IIncidentService, DefaultIncidentService>();
        services.TryAddScoped<IIncidentNotifier, NullIncidentNotifier>();

        return services;
    }
}

using RoomRental.Api.Domain;

namespace RoomRental.Api.Features.Monitoring;

public interface IClientAnalyticsService
{
    Task<AnalyticsEventBatchResponse> RecordClientEventsAsync(
        AnalyticsEventBatchRequest request,
        Guid? authenticatedOwnerId,
        string? clientIp,
        CancellationToken ct);
}

public interface IOwnerMilestoneTracker
{
    Task TrackOwnerRegisteredAsync(
        Guid ownerId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct);

    Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        CancellationToken ct);

    Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        CancellationToken ct);

    Task RecordOwnerActivityAsync(
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct);
}

public interface IGrowthMonitoringQueryService
{
    Task<AdminMonitoringGrowthResponse> GetGrowthMetricsAsync(
        MonitoringFilterRequest filter,
        CancellationToken ct);
}

public interface IRevenueMonitoringQueryService
{
    Task<AdminMonitoringRevenueResponse> GetRevenueMetricsAsync(
        MonitoringFilterRequest filter,
        CancellationToken ct);

    Task<IReadOnlyList<AdminCampaignCostItemDto>> GetCampaignCostsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct);

    Task<AdminCampaignCostItemDto> UpsertCampaignCostAsync(
        UpsertCampaignCostRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct);
}

public interface IOperationsMonitoringService
{
    Task<AdminMonitoringOperationsResponse> GetOperationsMetricsAsync(
        CancellationToken ct);

    Task RecordRouteMetricAsync(
        string routeTemplate,
        string httpMethod,
        int statusCode,
        double latencyMs,
        CancellationToken ct);
}

public interface IWorkerStateTracker
{
    Task RecordWorkerHeartbeatAsync(
        string workerName,
        bool isEnabled,
        string status,
        string? errorMessage,
        CancellationToken ct);

    Task<IReadOnlyList<WorkerStatusDto>> GetWorkerStatusesAsync(
        CancellationToken ct);
}

public interface IHostMetricsReader
{
    Task<HostMetricsDto?> ReadHostMetricsAsync(
        CancellationToken ct);
}

public interface IIncidentService
{
    Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(
        CancellationToken ct);

    Task<IncidentDto> UpdateIncidentStateAsync(
        Guid id,
        UpdateIncidentRequest request,
        CancellationToken ct);

    Task TriggerIncidentCheckAsync(
        CancellationToken ct);
}

public interface IIncidentNotifier
{
    Task SendIncidentAlertAsync(
        MonitoringIncident incident,
        bool isRecovery,
        CancellationToken ct);
}

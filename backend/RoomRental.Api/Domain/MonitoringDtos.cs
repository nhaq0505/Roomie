namespace RoomRental.Api.Domain;

// Client Analytics Event Intake
public sealed record AnalyticsEventItemDto(
    string EventId,
    string EventType,
    string? EventName,
    string Path,
    string? ReferrerPath = null,
    string? CtaId = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? UtmContent = null,
    string? UtmTerm = null,
    DateTimeOffset? ClientTimestamp = null
);

public sealed record AnalyticsEventBatchRequest(
    string BrowserId,
    string SessionId,
    IReadOnlyList<AnalyticsEventItemDto>? Events = null
);

public sealed record AnalyticsEventBatchResponse(
    int ProcessedCount,
    int RejectedCount,
    int DedupedCount = 0
);

// Authoritative Touch Point Attribution
public sealed record AttributionTouchDto(
    string? Source = null,
    string? Medium = null,
    string? Campaign = null,
    string? Content = null,
    string? Term = null,
    string? LandingPage = null,
    string? Referrer = null,
    DateTimeOffset? OccurredAt = null
);

// Backward-compatible registration attribution context with independent First and Last Touch
public sealed record RegistrationAttributionDto(
    string? BrowserId = null,
    AttributionTouchDto? FirstTouch = null,
    AttributionTouchDto? LastTouch = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null,
    string? UtmContent = null,
    string? UtmTerm = null,
    string? LandingPage = null,
    string? Referrer = null
);

// Query Filters
public sealed record MonitoringFilterRequest(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Source = null,
    string? Channel = null,
    string? Campaign = null
);

// Admin Growth Tab DTOs
public sealed record GrowthSummaryDto(
    int? TotalSessions,
    int? UniqueVisitors,
    int Registrations,
    int ActivatedOwners,
    int CurrentWau,
    int CurrentMau
);

public sealed record TrafficSourceStatsDto(
    string Source,
    string? Medium,
    int? Sessions,
    int Registrations,
    int ActivatedOwners
);

public sealed record CtaPerformanceDto(
    string CtaId,
    string Path,
    int Clicks
);

public sealed record CohortRetentionRowDto(
    string CohortPeriod,
    int CohortSize,
    IReadOnlyList<double?> RetentionPercentages
);

public sealed record CohortMaturityDto(
    double? ActivationRate7Day,
    int ActivationCohortMaturedCount,
    double? FirstPaidRate30Day,
    int PaidCohortMaturedCount
);

public sealed record DailyGrowthPointDto(
    DateOnly Date,
    int? Sessions,
    int Registrations,
    int Activations,
    int ActiveOwners
);

public sealed record RegistrationFunnelDto(
    int StartedCount,
    int CodeResentCount,
    int VerifiedCount,
    int BypassedCount,
    int UnknownAttemptCount = 0
);

public sealed record AdminMonitoringGrowthResponse(
    GrowthSummaryDto Summary,
    IReadOnlyList<TrafficSourceStatsDto> Sources,
    IReadOnlyList<CtaPerformanceDto> TopCtas,
    IReadOnlyList<CohortRetentionRowDto> RetentionMatrix,
    CohortMaturityDto CohortMaturity,
    IReadOnlyList<DailyGrowthPointDto> DailySeries,
    RegistrationFunnelDto? Funnel = null
);

// Admin Revenue Tab DTOs
public sealed record Cac30MetricDto(
    decimal? ValueVnd,
    bool IsAvailable,
    string? NullReason
);

public sealed record RevenueSummaryDto(
    decimal TotalSubscriptionRevenueVnd,
    int PaidSubscriptionCount,
    decimal TotalMarketingSpendVnd,
    decimal TotalMarketingHours,
    Cac30MetricDto Cac30
);

public sealed record SubscriptionOrderRevenueDto(
    long OrderCode,
    Guid OwnerId,
    int AmountVnd,
    int? Days,
    DateTimeOffset PaidAt
);

public sealed record CampaignCostSummaryDto(
    string Campaign,
    string? Channel,
    decimal CashVnd,
    decimal PersonHours,
    decimal? ImputedLaborCostVnd,
    decimal TotalCostVnd
);

public sealed record DailyRevenuePointDto(
    DateOnly Date,
    decimal SubscriptionRevenueVnd,
    decimal MarketingSpendVnd
);

public sealed record AdminMonitoringRevenueResponse(
    RevenueSummaryDto Summary,
    IReadOnlyList<SubscriptionOrderRevenueDto> SubscriptionOrders,
    IReadOnlyList<CampaignCostSummaryDto> CampaignCosts,
    IReadOnlyList<DailyRevenuePointDto> DailySeries
);

// Admin Operations Tab DTOs
public sealed record OpsOverviewDto(
    string OverallStatus,
    double? Api5xxRatePercent,
    double? LatencyP95Ms,
    long? TotalRequestsLastHour,
    bool? DatabaseConnected,
    string? DatabaseStatus,
    string? Window = "1h"
);

public sealed record RouteLatencyMetricDto(
    string RouteTemplate,
    string HttpMethod,
    int RequestCount,
    int Status5xxCount,
    double? P95LatencyMs,
    double? AvgLatencyMs,
    double? MaxLatencyMs
);

public sealed record HostMetricsDto(
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    int ContainerCount,
    string HealthEndpointStatus,
    string HostStatus,
    DateTimeOffset? RecordedAt,
    bool IsStale
);

public sealed record WorkerStatusDto(
    string WorkerName,
    bool IsEnabled,
    string Status,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastErrorAt,
    string? LastErrorMessage,
    bool IsStale
);

public sealed record OperationalQueueStatsDto(
    int? ZaloPendingQueueCount,
    int? ZaloEligibleWindowCount,
    int? PayOsPendingReconcileCount,
    long? SmtpAcceptedNotDeliveredCount
);

public sealed record AdminMonitoringOperationsResponse(
    OpsOverviewDto Overview,
    IReadOnlyList<RouteLatencyMetricDto> RouteMetrics,
    HostMetricsDto? HostMetrics,
    IReadOnlyList<WorkerStatusDto> Workers,
    OperationalQueueStatsDto Queues
);

// Admin Incidents DTOs
public sealed record IncidentDto(
    Guid Id,
    string IncidentKey,
    string RuleName,
    string Severity,
    string State,
    string Summary,
    string Details,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastNotifiedAt,
    DateTimeOffset? ResolvedAt,
    int NotificationCount
);

public sealed record AdminMonitoringIncidentsResponse(
    int ActiveIncidentCount,
    IReadOnlyList<IncidentDto> Incidents
);

public sealed record UpdateIncidentRequest(
    string State,
    string? Note = null
);

// Campaign Metadata Management DTOs
public sealed record MarketingCampaignDto(
    Guid Id,
    string Code,
    string Name,
    string? Channel,
    string? Source,
    string? UtmCampaign,
    string? UtmMedium,
    string? UtmSource,
    string? TargetUrl,
    bool IsActive,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? TrackingUrl = null
);

public sealed record CreateCampaignRequest(
    string Code,
    string Name,
    string? Channel = null,
    string? Source = null,
    string? UtmCampaign = null,
    string? UtmMedium = null,
    string? UtmSource = null,
    string? TargetUrl = null,
    bool IsActive = true
);

public sealed record UpdateCampaignRequest(
    string Name,
    string? Channel = null,
    string? Source = null,
    string? UtmCampaign = null,
    string? UtmMedium = null,
    string? UtmSource = null,
    string? TargetUrl = null,
    bool? IsActive = null
);

// Campaign Costs Management DTOs
public sealed record AdminCampaignCostItemDto(
    Guid Id,
    DateOnly Date,
    string Campaign,
    string? Channel,
    string? Source,
    decimal CashVnd,
    decimal PersonHours,
    decimal? HourlyRate,
    string? Note,
    string UpdatedByName,
    DateTimeOffset UpdatedAt
);

public sealed record UpsertCampaignCostRequest(
    DateOnly Date,
    string Campaign,
    string? Channel,
    string? Source,
    decimal CashVnd,
    decimal PersonHours,
    decimal? HourlyRate = null,
    string? Note = null
);

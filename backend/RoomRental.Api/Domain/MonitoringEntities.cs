namespace RoomRental.Api.Domain;

public enum ClientEventType
{
    PageView,
    CtaClick,
    LandingView,
    PricingView,
    AuthAttempt,
    RegistrationSubmit
}

public enum ActorType
{
    Owner,
    Admin,
    System
}

public enum IncidentSeverity
{
    Warning,
    Critical
}

public enum IncidentState
{
    Active,
    Acknowledged,
    Resolved
}

public enum WorkerHealthStatus
{
    Healthy,
    Running,
    Degraded,
    Failed,
    Disabled
}

public enum CacNullReason
{
    MissingCostData,
    MissingValuation,
    ZeroDenominator,
    CohortImmature,
    NoRegistrations
}

public sealed class ClientAnalyticsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string EventId { get; set; }
    public required string BrowserId { get; set; }
    public required string SessionId { get; set; }
    public Guid? OwnerId { get; set; }
    public required string EventType { get; set; }
    public string? EventName { get; set; }
    public required string Path { get; set; }
    public string? ReferrerPath { get; set; }
    public string? CtaId { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public DateTimeOffset ClientTimestamp { get; set; }
    public DateTimeOffset ServerTimestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? RequestFingerprint { get; set; }
}

public sealed class RegistrationAttribution
{
    public Guid OwnerId { get; set; }
    public string? BrowserId { get; set; }
    public string? FirstTouchSource { get; set; }
    public string? FirstTouchMedium { get; set; }
    public string? FirstTouchCampaign { get; set; }
    public string? FirstTouchContent { get; set; }
    public string? FirstTouchTerm { get; set; }
    public string? FirstTouchLandingPage { get; set; }
    public DateTimeOffset? FirstTouchAt { get; set; }
    public string? LastTouchSource { get; set; }
    public string? LastTouchMedium { get; set; }
    public string? LastTouchCampaign { get; set; }
    public string? LastTouchContent { get; set; }
    public string? LastTouchTerm { get; set; }
    public string? LastTouchLandingPage { get; set; }
    public DateTimeOffset? LastTouchAt { get; set; }
    public string VerificationMethod { get; set; } = "Code";
    public bool AutoApproved { get; set; } = true;
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RegistrationLifecycleFact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegistrationAttemptId { get; set; }
    public required string Stage { get; set; } // Started, CodeSent, CodeResent, Verified, Bypassed
    public string VerificationMethod { get; set; } = "Code"; // Code vs Bypass
    public string? BrowserId { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public string? LandingPage { get; set; }
    public string? ReferrerPath { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OwnerMilestone
{
    public Guid OwnerId { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public Guid? FirstActivatedInvoiceId { get; set; }
    public DateTimeOffset? FirstPaidAt { get; set; }
    public long? FirstPaidOrderCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MilestoneOutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string EventType { get; set; } // InvoiceIssued, SubscriptionPaid
    public required string TargetId { get; set; }  // InvoiceId (Guid string) or OrderCode (long string)
    public DateTimeOffset OccurredAt { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Completed, Failed
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class OwnerDailyActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public DateOnly ActivityDate { get; set; }
    public int OwnerActionCount { get; set; }
    public int AdminActionCount { get; set; }
    public int SystemActionCount { get; set; }
    public bool HasOwnerAction { get; set; }
    public string? LastActionType { get; set; }
    public DateTimeOffset FirstActionAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketingCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Channel { get; set; }
    public string? Source { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmSource { get; set; }
    public string? TargetUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketingCampaignCost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public required string Campaign { get; set; }
    public string? Channel { get; set; }
    public string? Source { get; set; }
    public decimal CashVnd { get; set; }
    public decimal PersonHours { get; set; }
    public decimal? HourlyRate { get; set; }
    public string? Note { get; set; }
    public Guid UpdatedBy { get; set; }
    public required string UpdatedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OperationalRouteMinuteBucket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset MinuteTimestamp { get; set; }
    public required string RouteTemplate { get; set; }
    public required string HttpMethod { get; set; }
    public int StatusCode { get; set; }
    public int RequestCount { get; set; }
    public int LatencyLe50ms { get; set; }
    public int LatencyLe100ms { get; set; }
    public int LatencyLe250ms { get; set; }
    public int LatencyLe500ms { get; set; }
    public int LatencyLe1000ms { get; set; }
    public int LatencyLe2000ms { get; set; }
    public int LatencyGt2000ms { get; set; }
    public double TotalLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
}

public sealed class WorkerHeartbeatState
{
    public required string WorkerName { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public string Status { get; set; } = "Healthy";
    public int ConsecFailures { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HostMetricsRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset RecordedAt { get; set; }
    public double? CpuPercent { get; set; }
    public double? MemoryPercent { get; set; }
    public double? DiskPercent { get; set; }
    public int ContainerCount { get; set; }
    public string? ContainerStatusJson { get; set; }
    public string HealthEndpointStatus { get; set; } = "ok";
    public string HostStatus { get; set; } = "Healthy";
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MonitoringIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string IncidentKey { get; set; }
    public required string RuleName { get; set; }
    public string Severity { get; set; } = "Warning";
    public string State { get; set; } = "Active";
    public required string Summary { get; set; }
    public required string Details { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }
    public int NotificationCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OperationalEventCounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset PeriodStart { get; set; }
    public required string Category { get; set; }
    public required string MetricName { get; set; }
    public long Count { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DailyAggregateMetric
{
    public DateOnly Date { get; set; }
    public int TotalSessions { get; set; }
    public int DistinctVisitors { get; set; }
    public int Registrations { get; set; }
    public int ActivatedOwners { get; set; }
    public int ActiveOwnersWau { get; set; }
    public int ActiveOwnersMau { get; set; }
    public decimal SubscriptionRevenueVnd { get; set; }
    public decimal MarketingSpendVnd { get; set; }
    public decimal MarketingHours { get; set; }
    public long TotalRequests { get; set; }
    public long Total5xxErrors { get; set; }
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
}

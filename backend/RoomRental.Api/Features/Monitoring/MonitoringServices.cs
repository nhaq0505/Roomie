using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring;

public sealed class ClientAnalyticsService(AppDbContext db, TimeProvider clock) : IClientAnalyticsService
{
    private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "page_view",
        "cta_click",
        "session_start",
        "landing_view",
        "pricing_view",
        "auth_attempt",
        "registration_submit",
        "custom"
    };

    private static readonly Regex SafeLabelRegex = new(@"^[a-zA-Z0-9_\-\.]{1,64}$", RegexOptions.Compiled);

    public async Task<AnalyticsEventBatchResponse> RecordClientEventsAsync(
        AnalyticsEventBatchRequest request,
        Guid? authenticatedOwnerId,
        string? clientIp,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BrowserId) || string.IsNullOrWhiteSpace(request.SessionId))
            throw new ApiException(StatusCodes.Status400BadRequest, "BrowserId and SessionId are required.");

        var browserId = SanitizeId(request.BrowserId);
        var sessionId = SanitizeId(request.SessionId);
        var now = clock.GetUtcNow();

        var processed = 0;
        var rejected = 0;

        foreach (var evt in request.Events.Take(50)) // Bound batch size to 50
        {
            if (string.IsNullOrWhiteSpace(evt.EventType) || !AllowedEventTypes.Contains(evt.EventType.Trim()))
            {
                rejected++;
                continue;
            }

            var cleanPath = SanitizePath(evt.Path);
            if (string.IsNullOrWhiteSpace(cleanPath))
            {
                rejected++;
                continue;
            }

            var record = new ClientAnalyticsEvent
            {
                BrowserId = browserId,
                SessionId = sessionId,
                OwnerId = authenticatedOwnerId,
                EventType = evt.EventType.Trim().ToLowerInvariant(),
                EventName = Truncate(evt.EventName, 100),
                Path = cleanPath,
                ReferrerPath = SanitizePath(evt.ReferrerPath),
                CtaId = SanitizeLabel(evt.CtaId),
                UtmSource = SanitizeLabel(evt.UtmSource),
                UtmMedium = SanitizeLabel(evt.UtmMedium),
                UtmCampaign = SanitizeLabel(evt.UtmCampaign),
                UtmContent = SanitizeLabel(evt.UtmContent),
                UtmTerm = SanitizeLabel(evt.UtmTerm),
                ClientTimestamp = evt.ClientTimestamp ?? now,
                ServerTimestamp = now
            };

            db.AnalyticsEvents.Add(record);
            processed++;
        }

        if (processed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new AnalyticsEventBatchResponse(processed, rejected);
    }

    private static string SanitizeId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    private static string? SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var clean = path.Trim();
        var qIdx = clean.IndexOf('?');
        if (qIdx >= 0) clean = clean[..qIdx];
        var hashIdx = clean.IndexOf('#');
        if (hashIdx >= 0) clean = clean[..hashIdx];
        if (clean.Length > 256) clean = clean[..256];
        return clean;
    }

    private static string? SanitizeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 64) trimmed = trimmed[..64];
        return SafeLabelRegex.IsMatch(trimmed) ? trimmed : null;
    }

    private static string? Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLen ? trimmed[..maxLen] : trimmed;
    }
}

public sealed class OwnerMilestoneTracker(AppDbContext db, TimeProvider clock) : IOwnerMilestoneTracker
{
    public async Task TrackOwnerRegisteredAsync(
        Guid ownerId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        if (milestone is null)
        {
            milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.OwnerMilestones.Add(milestone);
        }

        if (attribution is not null)
        {
            var existingAttribution = await db.RegistrationAttributions.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (existingAttribution is null)
            {
                var attrRecord = new RegistrationAttribution
                {
                    OwnerId = ownerId,
                    BrowserId = attribution.BrowserId,
                    FirstTouchSource = attribution.UtmSource,
                    FirstTouchMedium = attribution.UtmMedium,
                    FirstTouchCampaign = attribution.UtmCampaign,
                    FirstTouchContent = attribution.UtmContent,
                    FirstTouchTerm = attribution.UtmTerm,
                    FirstTouchLandingPage = attribution.LandingPage,
                    FirstTouchAt = now,
                    LastTouchSource = attribution.UtmSource,
                    LastTouchMedium = attribution.UtmMedium,
                    LastTouchCampaign = attribution.UtmCampaign,
                    LastTouchContent = attribution.UtmContent,
                    LastTouchTerm = attribution.UtmTerm,
                    LastTouchLandingPage = attribution.LandingPage,
                    LastTouchAt = now,
                    VerificationMethod = verificationMethod,
                    AutoApproved = true,
                    RegisteredAt = now
                };
                db.RegistrationAttributions.Add(attrRecord);
            }
        }

        await RecordOwnerActivityAsync(ownerId, ActorType.Owner, "Registered", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        CancellationToken ct)
    {
        if (!string.Equals(kind, "Monthly", StringComparison.OrdinalIgnoreCase))
            return;

        var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        var now = clock.GetUtcNow();
        if (milestone is null)
        {
            milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = now,
                ActivatedAt = now,
                FirstActivatedInvoiceId = invoiceId,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.OwnerMilestones.Add(milestone);
        }
        else if (milestone.ActivatedAt is null)
        {
            milestone.ActivatedAt = now;
            milestone.FirstActivatedInvoiceId = invoiceId;
            milestone.UpdatedAt = now;
        }

        await RecordOwnerActivityAsync(ownerId, ActorType.Owner, "InvoiceIssued", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        CancellationToken ct)
    {
        var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        var now = clock.GetUtcNow();
        if (milestone is null)
        {
            milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = now,
                FirstPaidAt = now,
                FirstPaidOrderCode = orderCode,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.OwnerMilestones.Add(milestone);
        }
        else if (milestone.FirstPaidAt is null)
        {
            milestone.FirstPaidAt = now;
            milestone.FirstPaidOrderCode = orderCode;
            milestone.UpdatedAt = now;
        }

        await RecordOwnerActivityAsync(ownerId, ActorType.Owner, "SubscriptionPaid", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordOwnerActivityAsync(
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);

        var activity = await db.OwnerDailyActivities
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.ActivityDate == today, ct);

        if (activity is null)
        {
            activity = new OwnerDailyActivity
            {
                OwnerId = ownerId,
                ActivityDate = today,
                OwnerActionCount = actorType == ActorType.Owner ? 1 : 0,
                AdminActionCount = actorType == ActorType.Admin ? 1 : 0,
                SystemActionCount = actorType == ActorType.System ? 1 : 0,
                HasOwnerAction = actorType == ActorType.Owner,
                LastActionType = actionType,
                FirstActionAt = now,
                LastActionAt = now
            };
            db.OwnerDailyActivities.Add(activity);
        }
        else
        {
            if (actorType == ActorType.Owner)
            {
                activity.OwnerActionCount++;
                activity.HasOwnerAction = true;
            }
            else if (actorType == ActorType.Admin)
            {
                activity.AdminActionCount++;
            }
            else
            {
                activity.SystemActionCount++;
            }

            activity.LastActionType = actionType;
            activity.LastActionAt = now;
        }
    }
}

public sealed class DefaultGrowthMonitoringQueryService : IGrowthMonitoringQueryService
{
    public Task<AdminMonitoringGrowthResponse> GetGrowthMetricsAsync(MonitoringFilterRequest filter, CancellationToken ct)
    {
        throw new NotImplementedException("Growth monitoring queries are unimplemented in T0; will be implemented in T1.");
    }
}

public sealed class DefaultRevenueMonitoringQueryService(AppDbContext db, TimeProvider clock) : IRevenueMonitoringQueryService
{
    public Task<AdminMonitoringRevenueResponse> GetRevenueMetricsAsync(MonitoringFilterRequest filter, CancellationToken ct)
    {
        throw new NotImplementedException("Revenue monitoring queries are unimplemented in T0; will be implemented in T1.");
    }

    public async Task<IReadOnlyList<AdminCampaignCostItemDto>> GetCampaignCostsAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var query = db.CampaignCosts.AsNoTracking();
        if (fromDate.HasValue) query = query.Where(x => x.Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.Date <= toDate.Value);

        var list = await query.OrderByDescending(x => x.Date).ThenBy(x => x.Campaign).ToListAsync(ct);
        return list.Select(x => new AdminCampaignCostItemDto(
            x.Id,
            x.Date,
            x.Campaign,
            x.Channel,
            x.Source,
            x.CashVnd,
            x.PersonHours,
            x.HourlyRate,
            x.Note,
            x.UpdatedByName,
            x.UpdatedAt
        )).ToList();
    }

    public async Task<AdminCampaignCostItemDto> UpsertCampaignCostAsync(
        UpsertCampaignCostRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var entity = await db.CampaignCosts.FirstOrDefaultAsync(
            x => x.Date == request.Date && x.Campaign == request.Campaign && x.Channel == request.Channel && x.Source == request.Source, ct);

        if (entity is null)
        {
            entity = new MarketingCampaignCost
            {
                Date = request.Date,
                Campaign = request.Campaign,
                Channel = request.Channel,
                Source = request.Source,
                CashVnd = request.CashVnd,
                PersonHours = request.PersonHours,
                HourlyRate = request.HourlyRate,
                Note = request.Note,
                UpdatedBy = adminUserId,
                UpdatedByName = adminUserName,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CampaignCosts.Add(entity);
        }
        else
        {
            entity.CashVnd = request.CashVnd;
            entity.PersonHours = request.PersonHours;
            entity.HourlyRate = request.HourlyRate;
            entity.Note = request.Note;
            entity.UpdatedBy = adminUserId;
            entity.UpdatedByName = adminUserName;
            entity.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return new AdminCampaignCostItemDto(
            entity.Id,
            entity.Date,
            entity.Campaign,
            entity.Channel,
            entity.Source,
            entity.CashVnd,
            entity.PersonHours,
            entity.HourlyRate,
            entity.Note,
            entity.UpdatedByName,
            entity.UpdatedAt
        );
    }
}

public sealed class DefaultOperationsMonitoringService : IOperationsMonitoringService
{
    public Task<AdminMonitoringOperationsResponse> GetOperationsMetricsAsync(CancellationToken ct)
    {
        throw new NotImplementedException("Operations monitoring queries are unimplemented in T0; will be implemented in T3.");
    }

    public Task RecordRouteMetricAsync(string routeTemplate, string httpMethod, int statusCode, double latencyMs, CancellationToken ct)
    {
        // No-op in default foundation; Worker C instruments pipeline
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
            if (status == "Healthy")
            {
                worker.LastSuccessAt = now;
                worker.ConsecFailures = 0;
            }
            else if (status == "Failed")
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
            w.UpdatedAt < now.AddMinutes(-10) // Stale if not updated in 10 minutes
        )).ToList();
    }
}

public sealed class DefaultHostMetricsReader : IHostMetricsReader
{
    public Task<HostMetricsDto?> ReadHostMetricsAsync(CancellationToken ct)
    {
        // Unimplemented in T0; Worker C integrates with Ubuntu host collector mounted JSON
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

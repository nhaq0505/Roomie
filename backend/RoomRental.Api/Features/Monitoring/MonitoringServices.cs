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
        "registration_submit"
    };

    private static readonly Regex SafeLabelRegex = new(@"^[a-zA-Z0-9_\-\.\s\+]{1,64}$", RegexOptions.Compiled);

    public async Task<AnalyticsEventBatchResponse> RecordClientEventsAsync(
        AnalyticsEventBatchRequest request,
        Guid? authenticatedOwnerId,
        string? clientIp,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BrowserId) || string.IsNullOrWhiteSpace(request.SessionId))
            throw new ApiException(StatusCodes.Status400BadRequest, "BrowserId and SessionId are required.");

        if (request.Events == null || request.Events.Count == 0)
            return new AnalyticsEventBatchResponse(0, 0, 0);

        if (request.Events.Count > 100)
            throw new ApiException(StatusCodes.Status400BadRequest, "Batch size exceeds maximum limit of 100 events.");

        var browserId = SanitizeId(request.BrowserId);
        var sessionId = SanitizeId(request.SessionId);
        var now = clock.GetUtcNow();

        var processed = 0;
        var rejected = 0;
        var deduped = 0;

        var batchEvents = request.Events.Take(100).ToList();
        var incomingEventIds = batchEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.EventId))
            .Select(e => SanitizeId(e.EventId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingDbIds = incomingEventIds.Count > 0
            ? await db.AnalyticsEvents
                .Where(x => incomingEventIds.Contains(x.EventId))
                .Select(x => x.EventId)
                .ToHashSetAsync(StringComparer.Ordinal, ct)
            : new HashSet<string>(StringComparer.Ordinal);

        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evt in batchEvents)
        {
            if (string.IsNullOrWhiteSpace(evt.EventId))
            {
                rejected++;
                continue;
            }

            var eventId = SanitizeId(evt.EventId);

            // Deduping: identical retries with same EventId are safely deduped without error
            if (existingDbIds.Contains(eventId) || !seenInBatch.Add(eventId))
            {
                deduped++;
                continue;
            }

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

            var clientTs = evt.ClientTimestamp;
            if (clientTs.HasValue)
            {
                // Validate client timestamp against server-known timing: clamp if in the future or too old
                if (clientTs.Value > now.AddMinutes(5) || clientTs.Value < now.AddDays(-30))
                {
                    clientTs = now;
                }
            }

            var record = new ClientAnalyticsEvent
            {
                EventId = eventId,
                BrowserId = browserId,
                SessionId = sessionId,
                OwnerId = authenticatedOwnerId,
                EventType = evt.EventType.Trim().ToLowerInvariant(),
                EventName = SanitizeLabel(evt.EventName, 64),
                Path = cleanPath,
                ReferrerPath = SanitizePath(evt.ReferrerPath),
                CtaId = SanitizeLabel(evt.CtaId, 64),
                UtmSource = SanitizeLabel(evt.UtmSource, 64),
                UtmMedium = SanitizeLabel(evt.UtmMedium, 64),
                UtmCampaign = SanitizeLabel(evt.UtmCampaign, 64),
                UtmContent = SanitizeLabel(evt.UtmContent, 64),
                UtmTerm = SanitizeLabel(evt.UtmTerm, 64),
                ClientTimestamp = clientTs ?? now,
                ServerTimestamp = now
            };

            db.AnalyticsEvents.Add(record);
            processed++;
        }

        if (processed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new AnalyticsEventBatchResponse(processed, rejected, deduped);
    }

    public static string SanitizeId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    public static string? SanitizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        var trimmed = rawPath.Trim();

        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return null;
            path = uri.AbsolutePath;
        }
        else
        {
            path = trimmed;
        }

        var qIdx = path.IndexOf('?');
        if (qIdx >= 0) path = path[..qIdx];
        var hIdx = path.IndexOf('#');
        if (hIdx >= 0) path = path[..hIdx];

        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!path.StartsWith('/'))
            path = "/" + path;

        // Strip GUIDs first
        path = Regex.Replace(path, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", ":id");
        // Strip reset tokens, auth tokens, challenge tokens (20+ alphanumeric/underscore chars)
        path = Regex.Replace(path, @"\b[0-9a-zA-Z_]{20,}\b", ":token");
        // Deduplicate consecutive slashes
        path = Regex.Replace(path, @"/{2,}", "/");
        if (path.Length > 1 && path.EndsWith('/'))
            path = path[..^1];

        if (path.Length > 128)
            path = path[..128];

        if (!Regex.IsMatch(path, @"^/[a-zA-Z0-9_\-/:.]{0,127}$"))
            return null;

        return path;
    }

    public static string? SanitizeLabel(string? value, int maxLen = 64)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();

        // Reject PII: email addresses or long hex/base64 tokens
        if (trimmed.Contains('@') || trimmed.Contains('%'))
            return null;

        if (trimmed.Length >= 20 && Regex.IsMatch(trimmed, @"^[0-9a-fA-F]{20,}$"))
            return null;

        if (trimmed.Length > maxLen)
            trimmed = trimmed[..maxLen];

        return SafeLabelRegex.IsMatch(trimmed) ? trimmed : null;
    }
}

public sealed class OwnerMilestoneTracker(AppDbContext db, TimeProvider clock) : IOwnerMilestoneTracker
{
    private static DateTimeOffset ValidateOccurredAt(DateTimeOffset? clientTime, DateTimeOffset serverNow)
    {
        if (!clientTime.HasValue) return serverNow;
        var ts = clientTime.Value;
        if (ts > serverNow.AddMinutes(5) || ts < serverNow.AddYears(-2))
            return serverNow;
        return ts;
    }

    public async Task TrackRegistrationStartedAsync(
        Guid attemptId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var touch = attribution?.FirstTouch;
        var fact = new RegistrationLifecycleFact
        {
            RegistrationAttemptId = attemptId,
            Stage = "Started",
            VerificationMethod = verificationMethod,
            BrowserId = ClientAnalyticsService.SanitizeLabel(attribution?.BrowserId),
            UtmSource = ClientAnalyticsService.SanitizeLabel(touch?.Source ?? attribution?.UtmSource),
            UtmMedium = ClientAnalyticsService.SanitizeLabel(touch?.Medium ?? attribution?.UtmMedium),
            UtmCampaign = ClientAnalyticsService.SanitizeLabel(touch?.Campaign ?? attribution?.UtmCampaign),
            UtmContent = ClientAnalyticsService.SanitizeLabel(touch?.Content ?? attribution?.UtmContent),
            UtmTerm = ClientAnalyticsService.SanitizeLabel(touch?.Term ?? attribution?.UtmTerm),
            LandingPage = ClientAnalyticsService.SanitizePath(touch?.LandingPage ?? attribution?.LandingPage),
            ReferrerPath = ClientAnalyticsService.SanitizePath(touch?.Referrer ?? attribution?.Referrer),
            OccurredAt = ValidateOccurredAt(touch?.OccurredAt, now)
        };
        db.RegistrationFacts.Add(fact);
        await db.SaveChangesAsync(ct);
    }

    public async Task TrackRegistrationCompletedAsync(
        Guid attemptId,
        Guid ownerId,
        string stage,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var fact = new RegistrationLifecycleFact
        {
            RegistrationAttemptId = attemptId,
            OwnerId = ownerId,
            Stage = stage,
            VerificationMethod = stage == "Bypassed" ? "Bypass" : "Code",
            OccurredAt = now
        };
        db.RegistrationFacts.Add(fact);
        await db.SaveChangesAsync(ct);
    }

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

        if (attribution is null)
        {
            var startedFact = await db.RegistrationFacts
                .FirstOrDefaultAsync(x => x.RegistrationAttemptId == ownerId && x.Stage == "Started", ct);
            if (startedFact is not null)
            {
                attribution = new RegistrationAttributionDto(
                    startedFact.BrowserId,
                    FirstTouch: new AttributionTouchDto(
                        startedFact.UtmSource,
                        startedFact.UtmMedium,
                        startedFact.UtmCampaign,
                        startedFact.UtmContent,
                        startedFact.UtmTerm,
                        startedFact.LandingPage,
                        startedFact.ReferrerPath,
                        startedFact.OccurredAt
                    ),
                    LastTouch: null
                );
            }
        }

        if (attribution is not null)
        {
            var existingAttribution = await db.RegistrationAttributions.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (existingAttribution is null)
            {
                var first = attribution.FirstTouch;
                var last = attribution.LastTouch;

                var firstSource = ClientAnalyticsService.SanitizeLabel(first?.Source ?? attribution.UtmSource);
                var firstMedium = ClientAnalyticsService.SanitizeLabel(first?.Medium ?? attribution.UtmMedium);
                var firstCampaign = ClientAnalyticsService.SanitizeLabel(first?.Campaign ?? attribution.UtmCampaign);
                var firstContent = ClientAnalyticsService.SanitizeLabel(first?.Content ?? attribution.UtmContent);
                var firstTerm = ClientAnalyticsService.SanitizeLabel(first?.Term ?? attribution.UtmTerm);
                var firstLanding = ClientAnalyticsService.SanitizePath(first?.LandingPage ?? attribution.LandingPage);
                var firstAt = first != null ? ValidateOccurredAt(first.OccurredAt, now) : (firstSource != null ? now : (DateTimeOffset?)null);

                // Independent last-touch non-direct: NEVER duplicate first-touch into last-touch!
                string? lastSource = null;
                string? lastMedium = null;
                string? lastCampaign = null;
                string? lastContent = null;
                string? lastTerm = null;
                string? lastLanding = null;
                DateTimeOffset? lastAt = null;

                if (last != null)
                {
                    lastSource = ClientAnalyticsService.SanitizeLabel(last.Source);
                    lastMedium = ClientAnalyticsService.SanitizeLabel(last.Medium);
                    lastCampaign = ClientAnalyticsService.SanitizeLabel(last.Campaign);
                    lastContent = ClientAnalyticsService.SanitizeLabel(last.Content);
                    lastTerm = ClientAnalyticsService.SanitizeLabel(last.Term);
                    lastLanding = ClientAnalyticsService.SanitizePath(last.LandingPage);
                    lastAt = ValidateOccurredAt(last.OccurredAt, now);
                    if (firstAt.HasValue && lastAt.HasValue && lastAt < firstAt)
                    {
                        lastAt = firstAt;
                    }
                }

                var attrRecord = new RegistrationAttribution
                {
                    OwnerId = ownerId,
                    BrowserId = ClientAnalyticsService.SanitizeLabel(attribution.BrowserId),
                    FirstTouchSource = firstSource,
                    FirstTouchMedium = firstMedium,
                    FirstTouchCampaign = firstCampaign,
                    FirstTouchContent = firstContent,
                    FirstTouchTerm = firstTerm,
                    FirstTouchLandingPage = firstLanding,
                    FirstTouchAt = firstAt,
                    LastTouchSource = lastSource,
                    LastTouchMedium = lastMedium,
                    LastTouchCampaign = lastCampaign,
                    LastTouchContent = lastContent,
                    LastTouchTerm = lastTerm,
                    LastTouchLandingPage = lastLanding,
                    LastTouchAt = lastAt,
                    VerificationMethod = verificationMethod,
                    AutoApproved = true,
                    RegisteredAt = now
                };
                db.RegistrationAttributions.Add(attrRecord);
            }
        }

        var existingFact = await db.RegistrationFacts.AnyAsync(x => x.OwnerId == ownerId, ct);
        if (!existingFact)
        {
            var first = attribution?.FirstTouch;
            db.RegistrationFacts.Add(new RegistrationLifecycleFact
            {
                RegistrationAttemptId = ownerId,
                OwnerId = ownerId,
                Stage = verificationMethod == "Bypass" ? "Bypassed" : "Verified",
                VerificationMethod = verificationMethod,
                BrowserId = ClientAnalyticsService.SanitizeLabel(attribution?.BrowserId),
                UtmSource = ClientAnalyticsService.SanitizeLabel(first?.Source ?? attribution?.UtmSource),
                UtmMedium = ClientAnalyticsService.SanitizeLabel(first?.Medium ?? attribution?.UtmMedium),
                UtmCampaign = ClientAnalyticsService.SanitizeLabel(first?.Campaign ?? attribution?.UtmCampaign),
                UtmContent = ClientAnalyticsService.SanitizeLabel(first?.Content ?? attribution?.UtmContent),
                UtmTerm = ClientAnalyticsService.SanitizeLabel(first?.Term ?? attribution?.UtmTerm),
                LandingPage = ClientAnalyticsService.SanitizePath(first?.LandingPage ?? attribution?.LandingPage),
                ReferrerPath = ClientAnalyticsService.SanitizePath(first?.Referrer ?? attribution?.Referrer),
                OccurredAt = now
            });
        }

        await RecordOwnerActivityAsync(ownerId, ActorType.Owner, "Registered", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        ActorType actor = ActorType.Owner,
        CancellationToken ct = default)
    {
        if (!string.Equals(kind, "Monthly", StringComparison.OrdinalIgnoreCase))
            return;

        var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        var now = clock.GetUtcNow();
        if (milestone is null)
        {
            var actualRegisteredAt = await db.Users
                .Where(u => u.Id == ownerId)
                .Select(u => (DateTimeOffset?)u.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (!actualRegisteredAt.HasValue)
            {
                // Owner does not exist in Users table; cannot add milestone due to FK constraint
                return;
            }

            milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = actualRegisteredAt,
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

        await RecordOwnerActivityAsync(ownerId, actor, "InvoiceIssued", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        ActorType actor = ActorType.Owner,
        CancellationToken ct = default)
    {
        var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        var now = clock.GetUtcNow();
        if (milestone is null)
        {
            var actualRegisteredAt = await db.Users
                .Where(u => u.Id == ownerId)
                .Select(u => (DateTimeOffset?)u.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (!actualRegisteredAt.HasValue)
            {
                // Owner does not exist in Users table; cannot add milestone due to FK constraint
                return;
            }

            milestone = new OwnerMilestone
            {
                OwnerId = ownerId,
                RegisteredAt = actualRegisteredAt,
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

        await RecordOwnerActivityAsync(ownerId, actor, "SubscriptionPaid", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordOwnerActivityAsync(
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct)
    {
        var userExists = await db.Users.AnyAsync(u => u.Id == ownerId, ct);
        if (!userExists) return;

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

    public async Task<IReadOnlyList<MarketingCampaignDto>> GetCampaignsAsync(
        bool? activeOnly,
        string? channel,
        string? source,
        CancellationToken ct)
    {
        var query = db.MarketingCampaigns.AsNoTracking();
        if (activeOnly == true) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(channel)) query = query.Where(x => x.Channel == channel);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(x => x.Source == source);

        var list = await query.OrderBy(x => x.Name).ToListAsync(ct);
        return list.Select(x => new MarketingCampaignDto(
            x.Id,
            x.Code,
            x.Name,
            x.Channel,
            x.Source,
            x.UtmCampaign,
            x.UtmMedium,
            x.UtmSource,
            x.TargetUrl,
            x.IsActive,
            x.CreatedByName,
            x.CreatedAt,
            x.UpdatedAt
        )).ToList();
    }

    public async Task<MarketingCampaignDto> CreateCampaignAsync(
        CreateCampaignRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            throw new ApiException(StatusCodes.Status400BadRequest, "Campaign Code and Name are required.");

        var code = request.Code.Trim().ToLowerInvariant();
        if (await db.MarketingCampaigns.AnyAsync(x => x.Code == code, ct))
            throw new ApiException(StatusCodes.Status409Conflict, "Campaign code already exists.");

        var now = clock.GetUtcNow();
        var campaign = new MarketingCampaign
        {
            Code = code,
            Name = request.Name.Trim(),
            Channel = request.Channel?.Trim(),
            Source = request.Source?.Trim(),
            UtmCampaign = request.UtmCampaign?.Trim() ?? code,
            UtmMedium = request.UtmMedium?.Trim(),
            UtmSource = request.UtmSource?.Trim(),
            TargetUrl = request.TargetUrl?.Trim(),
            IsActive = request.IsActive,
            CreatedBy = adminUserId,
            CreatedByName = adminUserName,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MarketingCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return new MarketingCampaignDto(
            campaign.Id,
            campaign.Code,
            campaign.Name,
            campaign.Channel,
            campaign.Source,
            campaign.UtmCampaign,
            campaign.UtmMedium,
            campaign.UtmSource,
            campaign.TargetUrl,
            campaign.IsActive,
            campaign.CreatedByName,
            campaign.CreatedAt,
            campaign.UpdatedAt
        );
    }

    public async Task<MarketingCampaignDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct)
    {
        var campaign = await db.MarketingCampaigns.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Campaign not found.");

        if (!string.IsNullOrWhiteSpace(request.Name))
            campaign.Name = request.Name.Trim();

        if (request.Channel != null) campaign.Channel = request.Channel.Trim();
        if (request.Source != null) campaign.Source = request.Source.Trim();
        if (request.UtmCampaign != null) campaign.UtmCampaign = request.UtmCampaign.Trim();
        if (request.UtmMedium != null) campaign.UtmMedium = request.UtmMedium.Trim();
        if (request.UtmSource != null) campaign.UtmSource = request.UtmSource.Trim();
        if (request.TargetUrl != null) campaign.TargetUrl = request.TargetUrl.Trim();
        if (request.IsActive.HasValue) campaign.IsActive = request.IsActive.Value;

        campaign.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return new MarketingCampaignDto(
            campaign.Id,
            campaign.Code,
            campaign.Name,
            campaign.Channel,
            campaign.Source,
            campaign.UtmCampaign,
            campaign.UtmMedium,
            campaign.UtmSource,
            campaign.TargetUrl,
            campaign.IsActive,
            campaign.CreatedByName,
            campaign.CreatedAt,
            campaign.UpdatedAt
        );
    }

    public async Task<IReadOnlyList<AdminCampaignCostItemDto>> GetCampaignCostsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? campaign,
        CancellationToken ct)
    {
        var query = db.CampaignCosts.AsNoTracking();
        if (fromDate.HasValue) query = query.Where(x => x.Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.Date <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(campaign)) query = query.Where(x => x.Campaign == campaign);

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

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class OwnerMilestoneTracker : IOwnerMilestoneTracker
{
    private readonly AppDbContext? _directDb;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly TimeProvider _clock;

    public OwnerMilestoneTracker(IServiceScopeFactory scopeFactory, TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public OwnerMilestoneTracker(AppDbContext db, TimeProvider clock, IServiceScopeFactory? scopeFactory = null)
    {
        _directDb = db;
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    private sealed class DbScope(AppDbContext db, IServiceScope? scope) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public ValueTask DisposeAsync()
        {
            scope?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private DbScope GetDbContext()
    {
        if (_directDb != null)
            return new DbScope(_directDb, null);

        var scope = _scopeFactory!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new DbScope(db, scope);
    }

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
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;
            var now = _clock.GetUtcNow();
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
        catch
        {
            // Registration telemetry failure must not block authentication
        }
    }

    public async Task TrackRegistrationCompletedAsync(
        Guid attemptId,
        Guid ownerId,
        string stage,
        CancellationToken ct)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;
            var now = _clock.GetUtcNow();
            var startedFact = await db.RegistrationFacts
                .FirstOrDefaultAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "Started", ct);

            var fact = new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptId,
                OwnerId = ownerId,
                Stage = stage,
                VerificationMethod = stage == "Bypassed" ? "Bypass" : "Code",
                BrowserId = startedFact?.BrowserId,
                UtmSource = startedFact?.UtmSource,
                UtmMedium = startedFact?.UtmMedium,
                UtmCampaign = startedFact?.UtmCampaign,
                UtmContent = startedFact?.UtmContent,
                UtmTerm = startedFact?.UtmTerm,
                LandingPage = startedFact?.LandingPage,
                ReferrerPath = startedFact?.ReferrerPath,
                OccurredAt = now
            };
            if (startedFact != null && startedFact.OwnerId == null)
            {
                startedFact.OwnerId = ownerId;
            }
            db.RegistrationFacts.Add(fact);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Registration telemetry failure must not block authentication
        }
    }

    public async Task TrackRegistrationResentAsync(
        Guid attemptId,
        CancellationToken ct)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;
            var now = _clock.GetUtcNow();
            var startedFact = await db.RegistrationFacts
                .FirstOrDefaultAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "Started", ct);

            var fact = new RegistrationLifecycleFact
            {
                RegistrationAttemptId = attemptId,
                Stage = "CodeResent",
                VerificationMethod = "Code",
                BrowserId = startedFact?.BrowserId,
                UtmSource = startedFact?.UtmSource,
                UtmMedium = startedFact?.UtmMedium,
                UtmCampaign = startedFact?.UtmCampaign,
                UtmContent = startedFact?.UtmContent,
                UtmTerm = startedFact?.UtmTerm,
                LandingPage = startedFact?.LandingPage,
                ReferrerPath = startedFact?.ReferrerPath,
                OwnerId = startedFact?.OwnerId,
                OccurredAt = now
            };
            db.RegistrationFacts.Add(fact);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Registration telemetry failure must not block authentication
        }
    }

    public async Task TrackOwnerRegisteredAsync(
        Guid ownerId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;
            var now = _clock.GetUtcNow();
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
        catch
        {
            // Registration telemetry failure must not block authentication
        }
    }

    public async Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        ActorType actor = ActorType.Owner,
        CancellationToken ct = default)
    {
        try
        {
            if (!string.Equals(kind, "Monthly", StringComparison.OrdinalIgnoreCase))
                return;

            await using var context = GetDbContext();
            var db = context.Db;

            var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            var now = _clock.GetUtcNow();
            if (milestone is null)
            {
                var actualRegisteredAt = await db.Users
                    .Where(u => u.Id == ownerId)
                    .Select(u => (DateTimeOffset?)u.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (actualRegisteredAt.HasValue)
                {
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
                    await db.SaveChangesAsync(ct);
                }
            }
            else if (milestone.ActivatedAt is null)
            {
                milestone.ActivatedAt = now;
                milestone.FirstActivatedInvoiceId = invoiceId;
                milestone.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
            }

            await RecordOwnerActivityInternalAsync(db, ownerId, actor, "InvoiceIssued", ct);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Telemetry failure must not block invoice issuance
        }
    }

    public async Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        ActorType actor = ActorType.System,
        DateTimeOffset? paidAt = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;

            var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            var now = _clock.GetUtcNow();
            var effectivePaidAt = paidAt ?? now;
            if (milestone is null)
            {
                var actualRegisteredAt = await db.Users
                    .Where(u => u.Id == ownerId)
                    .Select(u => (DateTimeOffset?)u.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (actualRegisteredAt.HasValue)
                {
                    milestone = new OwnerMilestone
                    {
                        OwnerId = ownerId,
                        RegisteredAt = actualRegisteredAt,
                        FirstPaidAt = effectivePaidAt,
                        FirstPaidOrderCode = orderCode,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.OwnerMilestones.Add(milestone);
                }
            }
            else if (milestone.FirstPaidAt is null)
            {
                milestone.FirstPaidAt = effectivePaidAt;
                milestone.FirstPaidOrderCode = orderCode;
                milestone.UpdatedAt = now;
            }

            await RecordOwnerActivityInternalAsync(db, ownerId, actor, "SubscriptionPaid", ct);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Payment tracking failure must not block webhook processing
        }
    }

    public async Task RecordOwnerActivityAsync(
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;
            await RecordOwnerActivityInternalAsync(db, ownerId, actorType, actionType, ct);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Activity recording failure must not block user operations
        }
    }

    private async Task RecordOwnerActivityInternalAsync(
        AppDbContext db,
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct)
    {
        var userExists = await db.Users.AnyAsync(u => u.Id == ownerId, ct);
        if (!userExists) return;

        var now = _clock.GetUtcNow();
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

public sealed class DefaultGrowthMonitoringQueryService(AppDbContext db, TimeProvider clock) : IGrowthMonitoringQueryService
{
    internal static bool EvaluateTouchMatches(
        string? source,
        string? medium,
        string? campaign,
        MonitoringFilterRequest filter,
        HashSet<string> campaignChannelCampaigns)
    {
        var hasSource = !string.IsNullOrWhiteSpace(filter.Source);
        var hasChannel = !string.IsNullOrWhiteSpace(filter.Channel);
        var hasCampaign = !string.IsNullOrWhiteSpace(filter.Campaign);

        if (hasSource)
        {
            var isDirectQuery = string.Equals(filter.Source, "(direct)", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(filter.Source, "direct", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(filter.Source, "(none)", StringComparison.OrdinalIgnoreCase);
            if (isDirectQuery)
            {
                if (!string.IsNullOrWhiteSpace(source) &&
                    !string.Equals(source, "(direct)", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, "direct", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, "(none)", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else if (!string.Equals(source, filter.Source, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (hasCampaign && !string.Equals(campaign, filter.Campaign, StringComparison.OrdinalIgnoreCase))
            return false;

        if (hasChannel)
        {
            var matchMedium = string.Equals(medium, filter.Channel, StringComparison.OrdinalIgnoreCase);
            var matchCamp = campaign != null && campaignChannelCampaigns.Contains(campaign);
            if (!matchMedium && !matchCamp)
                return false;
        }

        return true;
    }

    internal static HashSet<Guid>? GetMatchingOwnerIds(
        IEnumerable<RegistrationAttribution> attributions,
        MonitoringFilterRequest filter,
        HashSet<string> campaignChannelCampaigns)
    {
        var hasSource = !string.IsNullOrWhiteSpace(filter.Source);
        var hasChannel = !string.IsNullOrWhiteSpace(filter.Channel);
        var hasCampaign = !string.IsNullOrWhiteSpace(filter.Campaign);

        if (!hasSource && !hasChannel && !hasCampaign)
            return null;

        return attributions
            .Where(a => EvaluateTouchMatches(a.FirstTouchSource, a.FirstTouchMedium, a.FirstTouchCampaign, filter, campaignChannelCampaigns) ||
                        (a.LastTouchSource != null && EvaluateTouchMatches(a.LastTouchSource, a.LastTouchMedium, a.LastTouchCampaign, filter, campaignChannelCampaigns)))
            .Select(a => a.OwnerId)
            .ToHashSet();
    }

    public async Task<AdminMonitoringGrowthResponse> GetGrowthMetricsAsync(MonitoringFilterRequest filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);

        var toDate = filter.ToDate ?? today;
        var fromDate = filter.FromDate ?? toDate.AddDays(-29);
        if (fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        // Clamp both to today (no future dates)
        if (toDate > today)
            toDate = today;
        if (fromDate > today)
            fromDate = today;

        // Clamp historical depth: max 400 days (~13 months)
        var minAllowedDate = today.AddDays(-400);
        if (fromDate < minAllowedDate)
            fromDate = minAllowedDate;
        if (toDate < minAllowedDate)
            toDate = minAllowedDate;

        // Clamp maximum range span to 366 days
        if ((toDate.DayNumber - fromDate.DayNumber) > 365)
            fromDate = toDate.AddDays(-365);

        var startUtc = ApiSupport.StartOfVietnamDate(fromDate);
        var endUtc = ApiSupport.EndOfVietnamDate(toDate);

        var campaignChannels = !string.IsNullOrWhiteSpace(filter.Channel)
            ? (await db.MarketingCampaigns.Where(mc => mc.Channel == filter.Channel).Select(mc => mc.UtmCampaign ?? mc.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var attributions = await db.RegistrationAttributions.AsNoTracking().Take(50_000).ToListAsync(ct);
        var matchingOwnerIds = GetMatchingOwnerIds(attributions, filter, campaignChannels);
        var attrMap = attributions.ToDictionary(a => a.OwnerId);

        var rawRetentionCutoffDate = today.AddDays(-89);
        var hasRawCoverageMissing = fromDate < rawRetentionCutoffDate;
        var hasDimensionFilter = !string.IsNullOrWhiteSpace(filter.Source) ||
                                 !string.IsNullOrWhiteSpace(filter.Channel) ||
                                 !string.IsNullOrWhiteSpace(filter.Campaign);

        // 1. Client Analytics Events
        var eventsQuery = db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ServerTimestamp >= startUtc && e.ServerTimestamp < endUtc);

        if (!string.IsNullOrWhiteSpace(filter.Source))
            eventsQuery = eventsQuery.Where(e => e.UtmSource == filter.Source);
        if (!string.IsNullOrWhiteSpace(filter.Campaign))
            eventsQuery = eventsQuery.Where(e => e.UtmCampaign == filter.Campaign);
        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            eventsQuery = eventsQuery.Where(e => e.UtmMedium == filter.Channel || (e.UtmCampaign != null && campaignChannels.Contains(e.UtmCampaign)));
        }

        var events = await eventsQuery.Take(50_000).ToListAsync(ct);

        // 2. Daily Aggregates (pre-fetch bounded)
        var dailyAggregates = await db.DailyAggregateMetrics.AsNoTracking()
            .Where(a => a.Date >= fromDate && a.Date <= toDate)
            .Take(1000)
            .ToDictionaryAsync(a => a.Date, ct);

        // Summary TotalSessions & UniqueVisitors
        int? totalSessions;
        int? uniqueVisitors;

        if (!hasRawCoverageMissing)
        {
            totalSessions = events.Select(e => e.SessionId).Distinct().Count();
            uniqueVisitors = events.Select(e => e.BrowserId).Distinct().Count();
        }
        else
        {
            // Missing raw coverage: period distinct visitors cannot sum daily distinct as period distinct
            uniqueVisitors = null;

            if (hasDimensionFilter)
            {
                // DailyAggregateMetrics does not store dimensions
                totalSessions = null;
            }
            else
            {
                // Unfiltered: can sum daily sessions if aggregates are complete
                var canSum = true;
                var sumSessions = 0;
                for (var d = fromDate; d <= toDate; d = d.AddDays(1))
                {
                    if (d >= rawRetentionCutoffDate)
                    {
                        var dayStartUtc = ApiSupport.StartOfVietnamDate(d);
                        var dayEndUtc = ApiSupport.EndOfVietnamDate(d);
                        sumSessions += events
                            .Where(e => e.ServerTimestamp >= dayStartUtc && e.ServerTimestamp < dayEndUtc)
                            .Select(e => e.SessionId)
                            .Distinct()
                            .Count();
                    }
                    else
                    {
                        if (dailyAggregates.TryGetValue(d, out var agg))
                        {
                            sumSessions += agg.TotalSessions;
                        }
                        else
                        {
                            canSum = false;
                            break;
                        }
                    }
                }
                totalSessions = canSum ? sumSessions : null;
            }
        }

        // 3. Owner Milestones & Registrations in date range
        var milestonesQuery = db.OwnerMilestones.AsNoTracking()
            .Where(m => m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc);

        if (matchingOwnerIds != null)
        {
            milestonesQuery = milestonesQuery.Where(m => matchingOwnerIds.Contains(m.OwnerId));
        }

        var registeredMilestones = await milestonesQuery.Take(50_000).ToListAsync(ct);
        var registrations = registeredMilestones.Count;

        // Activations in date range
        var activationsQuery = db.OwnerMilestones.AsNoTracking()
            .Where(m => m.ActivatedAt >= startUtc && m.ActivatedAt < endUtc);

        if (matchingOwnerIds != null)
        {
            activationsQuery = activationsQuery.Where(m => matchingOwnerIds.Contains(m.OwnerId));
        }
        var activatedOwners = await activationsQuery.CountAsync(ct);

        // Current WAU (last 7 Vietnam days) and MAU (last 30 Vietnam days)
        var wauStart = today.AddDays(-6);
        var wauQuery = db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= wauStart && a.ActivityDate <= today && a.HasOwnerAction);
        if (matchingOwnerIds != null)
            wauQuery = wauQuery.Where(a => matchingOwnerIds.Contains(a.OwnerId));

        var currentWau = await wauQuery
            .Select(a => a.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var mauStart = today.AddDays(-29);
        var mauQuery = db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= mauStart && a.ActivityDate <= today && a.HasOwnerAction);
        if (matchingOwnerIds != null)
            mauQuery = mauQuery.Where(a => matchingOwnerIds.Contains(a.OwnerId));

        var currentMau = await mauQuery
            .Select(a => a.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var summary = new GrowthSummaryDto(
            totalSessions,
            uniqueVisitors,
            registrations,
            activatedOwners,
            currentWau,
            currentMau
        );

        // 4. Sources
        var sourceEventGroups = events
            .GroupBy(e => (Source: e.UtmSource ?? "(direct)", Medium: e.UtmMedium ?? "(none)"))
            .ToDictionary(g => g.Key, g => g.Select(x => x.SessionId).Distinct().Count());

        var sourceRegGroups = registeredMilestones
            .GroupBy(m => {
                if (attrMap.TryGetValue(m.OwnerId, out var attr))
                {
                    var s = attr.FirstTouchSource ?? attr.LastTouchSource ?? "(direct)";
                    var med = attr.FirstTouchMedium ?? attr.LastTouchMedium ?? "(none)";
                    return (Source: s, Medium: med);
                }
                return (Source: "(direct)", Medium: "(none)");
            })
            .ToDictionary(g => g.Key, g => (Registrations: g.Count(), Activated: g.Count(x => x.ActivatedAt != null)));

        var allKeys = sourceEventGroups.Keys.Union(sourceRegGroups.Keys).ToList();
        var sources = allKeys.Select(k => {
            sourceEventGroups.TryGetValue(k, out var sCount);
            sourceRegGroups.TryGetValue(k, out var regInfo);
            int? reportedSessions = hasRawCoverageMissing ? null : sCount;
            return new TrafficSourceStatsDto(
                k.Source,
                k.Medium == "(none)" ? null : k.Medium,
                reportedSessions,
                regInfo.Registrations,
                regInfo.Activated
            );
        }).OrderByDescending(s => s.Sessions ?? 0).ThenByDescending(s => s.Registrations).ToList();

        // 5. Top CTAs
        var topCtas = (toDate < rawRetentionCutoffDate)
            ? new List<CtaPerformanceDto>()
            : events
                .Where(e => string.Equals(e.EventType, "cta_click", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.CtaId))
                .GroupBy(e => (CtaId: e.CtaId!, Path: e.Path))
                .Select(g => new CtaPerformanceDto(g.Key.CtaId, g.Key.Path, g.Count()))
                .OrderByDescending(c => c.Clicks)
                .Take(10)
                .ToList();

        // 6. Cohort Maturity
        // 7-day cohort: registrations that occurred at least 7 full Vietnam days ago
        var activationCutoffUtc = ApiSupport.StartOfVietnamDate(today.AddDays(-6));
        var activationEligible = registeredMilestones
            .Where(m => m.RegisteredAt.HasValue && m.RegisteredAt.Value < activationCutoffUtc)
            .ToList();
        var activationCohortMaturedCount = activationEligible.Count;
        double? activationRate7Day = null;
        if (activationCohortMaturedCount > 0)
        {
            var activatedWithin7 = activationEligible.Count(m =>
                m.ActivatedAt.HasValue && m.RegisteredAt.HasValue &&
                m.ActivatedAt.Value <= m.RegisteredAt.Value.AddDays(7));
            activationRate7Day = Math.Round((double)activatedWithin7 / activationCohortMaturedCount * 100.0, 1);
        }

        // 30-day cohort: registrations that occurred at least 30 full Vietnam days ago
        var paidCutoffUtc = ApiSupport.StartOfVietnamDate(today.AddDays(-29));
        var paidEligible = registeredMilestones
            .Where(m => m.RegisteredAt.HasValue && m.RegisteredAt.Value < paidCutoffUtc)
            .ToList();
        var paidCohortMaturedCount = paidEligible.Count;
        double? firstPaidRate30Day = null;
        if (paidCohortMaturedCount > 0)
        {
            var paidWithin30 = paidEligible.Count(m =>
                m.FirstPaidAt.HasValue && m.RegisteredAt.HasValue &&
                m.FirstPaidAt.Value <= m.RegisteredAt.Value.AddDays(30));
            firstPaidRate30Day = Math.Round((double)paidWithin30 / paidCohortMaturedCount * 100.0, 1);
        }

        var cohortMaturity = new CohortMaturityDto(
            activationRate7Day,
            activationCohortMaturedCount,
            firstPaidRate30Day,
            paidCohortMaturedCount
        );

        // 7. Retention Matrix
        var retentionRows = new List<CohortRetentionRowDto>();
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var allActivatedMilestones = await db.OwnerMilestones.AsNoTracking()
            .Where(m => m.ActivatedAt != null)
            .Take(50_000)
            .ToListAsync(ct);

        var allActivities = await db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.HasOwnerAction)
            .Take(50_000)
            .ToListAsync(ct);

        for (var i = 5; i >= 0; i--)
        {
            var cohortMonth = currentMonthStart.AddMonths(-i);
            var cohortPeriodStr = cohortMonth.ToString("yyyy-MM");

            var cohortOwners = allActivatedMilestones
                .Where(m => {
                    var actDate = ApiSupport.VietnamDate(m.ActivatedAt!.Value);
                    return actDate.Year == cohortMonth.Year && actDate.Month == cohortMonth.Month;
                })
                .Select(m => m.OwnerId)
                .Distinct();

            if (matchingOwnerIds != null)
                cohortOwners = cohortOwners.Where(id => matchingOwnerIds.Contains(id));

            var cohortOwnersSet = cohortOwners.ToHashSet();
            if (cohortOwnersSet.Count == 0)
                continue;

            var percentages = new List<double?>();
            for (var mOffset = 0; mOffset <= 3; mOffset++)
            {
                var targetMonth = cohortMonth.AddMonths(mOffset);
                if (mOffset == 0)
                {
                    percentages.Add(100.0);
                }
                else if (targetMonth >= currentMonthStart)
                {
                    // Target month is current ongoing (incomplete) month or future month -> null, not mature zero!
                    percentages.Add(null);
                }
                else
                {
                    var activeCount = allActivities
                        .Where(a => a.ActivityDate.Year == targetMonth.Year &&
                                    a.ActivityDate.Month == targetMonth.Month &&
                                    cohortOwnersSet.Contains(a.OwnerId))
                        .Select(a => a.OwnerId)
                        .Distinct()
                        .Count();

                    var pct = Math.Round((double)activeCount / cohortOwnersSet.Count * 100.0, 1);
                    percentages.Add(pct);
                }
            }

            retentionRows.Add(new CohortRetentionRowDto(
                cohortPeriodStr,
                cohortOwnersSet.Count,
                percentages
            ));
        }

        // 8. Daily Series & Aggregates fallback for 90d+ pruned telemetry
        var dailyPoints = new List<DailyGrowthPointDto>();
        var allDaysActivities = await db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= fromDate && a.ActivityDate <= toDate && a.HasOwnerAction)
            .Take(50_000)
            .ToListAsync(ct);

        var allPeriodMilestones = await db.OwnerMilestones.AsNoTracking()
            .Where(m => (m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc) || (m.ActivatedAt >= startUtc && m.ActivatedAt < endUtc))
            .Take(50_000)
            .ToListAsync(ct);

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var dayStartUtc = ApiSupport.StartOfVietnamDate(d);
            var dayEndUtc = ApiSupport.EndOfVietnamDate(d);

            int? daySessions;
            if (d >= rawRetentionCutoffDate)
            {
                // Raw coverage is present for this date.
                // A zero count here is a REAL ZERO, never fallback on aggregate!
                daySessions = events
                    .Where(e => e.ServerTimestamp >= dayStartUtc && e.ServerTimestamp < dayEndUtc)
                    .Select(e => e.SessionId)
                    .Distinct()
                    .Count();
            }
            else
            {
                // Date is older than 90 days: raw coverage is missing!
                if (hasDimensionFilter)
                {
                    // DailyAggregateMetric has no dimension breakdown:
                    daySessions = null;
                }
                else
                {
                    // Unfiltered: dimensions match!
                    if (dailyAggregates.TryGetValue(d, out var agg))
                    {
                        daySessions = agg.TotalSessions;
                    }
                    else
                    {
                        daySessions = null;
                    }
                }
            }

            var dayRegs = allPeriodMilestones
                .Count(m => m.RegisteredAt >= dayStartUtc && m.RegisteredAt < dayEndUtc && (matchingOwnerIds == null || matchingOwnerIds.Contains(m.OwnerId)));

            var dayActs = allPeriodMilestones
                .Count(m => m.ActivatedAt >= dayStartUtc && m.ActivatedAt < dayEndUtc && (matchingOwnerIds == null || matchingOwnerIds.Contains(m.OwnerId)));

            var dayActiveOwners = allDaysActivities
                .Where(a => a.ActivityDate == d && (matchingOwnerIds == null || matchingOwnerIds.Contains(a.OwnerId)))
                .Select(a => a.OwnerId)
                .Distinct()
                .Count();

            dailyPoints.Add(new DailyGrowthPointDto(
                d,
                daySessions,
                dayRegs,
                dayActs,
                dayActiveOwners
            ));
        }

        // 9. Funnel facts (Started, CodeResent, Verified, Bypassed) with attribution scoping
        var factsQuery = db.RegistrationFacts.AsNoTracking()
            .Where(x => x.OccurredAt >= startUtc && x.OccurredAt < endUtc);

        var rawFacts = await factsQuery.Take(50_000).ToListAsync(ct);

        var attemptAttributions = rawFacts
            .Where(f => !string.IsNullOrWhiteSpace(f.UtmSource) || !string.IsNullOrWhiteSpace(f.UtmCampaign) || !string.IsNullOrWhiteSpace(f.UtmMedium))
            .GroupBy(f => f.RegistrationAttemptId)
            .ToDictionary(g => g.Key, g => g.First());

        bool FactMatchesFilter(RegistrationLifecycleFact fact)
        {
            if (!hasDimensionFilter)
                return true;

            if (fact.OwnerId.HasValue && matchingOwnerIds != null)
            {
                return matchingOwnerIds.Contains(fact.OwnerId.Value);
            }

            var src = fact.UtmSource;
            var med = fact.UtmMedium;
            var cmp = fact.UtmCampaign;

            if (string.IsNullOrWhiteSpace(src) && string.IsNullOrWhiteSpace(cmp) &&
                attemptAttributions.TryGetValue(fact.RegistrationAttemptId, out var attemptFact))
            {
                src = attemptFact.UtmSource;
                med = attemptFact.UtmMedium;
                cmp = attemptFact.UtmCampaign;
            }

            return EvaluateTouchMatches(src, med, cmp, filter, campaignChannels);
        }

        var matchingFacts = rawFacts.Where(FactMatchesFilter).ToList();
        var funnelStarted = matchingFacts.Count(x => x.Stage == "Started");
        var funnelResent = matchingFacts.Count(x => x.Stage == "CodeResent");
        var funnelVerified = matchingFacts.Count(x => x.Stage == "Verified");
        var funnelBypassed = matchingFacts.Count(x => x.Stage == "Bypassed");

        // Explicit unknown attempt coverage
        var unknownAttemptCount = rawFacts
            .Where(x => x.Stage == "Started" && string.IsNullOrWhiteSpace(x.UtmSource) && string.IsNullOrWhiteSpace(x.UtmCampaign))
            .Select(x => x.RegistrationAttemptId)
            .Distinct()
            .Count();

        var funnel = new RegistrationFunnelDto(funnelStarted, funnelResent, funnelVerified, funnelBypassed, unknownAttemptCount);

        return new AdminMonitoringGrowthResponse(
            summary,
            sources,
            topCtas,
            retentionRows,
            cohortMaturity,
            dailyPoints,
            funnel
        );
    }
}

public sealed class DefaultRevenueMonitoringQueryService(AppDbContext db, TimeProvider clock) : IRevenueMonitoringQueryService
{
    public async Task<AdminMonitoringRevenueResponse> GetRevenueMetricsAsync(MonitoringFilterRequest filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);

        var toDate = filter.ToDate ?? today;
        var fromDate = filter.FromDate ?? toDate.AddDays(-29);
        if (fromDate > toDate)
            (fromDate, toDate) = (toDate, fromDate);

        // Clamp both to today (no future dates)
        if (toDate > today)
            toDate = today;
        if (fromDate > today)
            fromDate = today;

        // Clamp historical depth: max 400 days (~13 months)
        var minAllowedDate = today.AddDays(-400);
        if (fromDate < minAllowedDate)
            fromDate = minAllowedDate;
        if (toDate < minAllowedDate)
            toDate = minAllowedDate;

        // Clamp maximum range span to 366 days
        if ((toDate.DayNumber - fromDate.DayNumber) > 365)
            fromDate = toDate.AddDays(-365);

        var startUtc = ApiSupport.StartOfVietnamDate(fromDate);
        var endUtc = ApiSupport.EndOfVietnamDate(toDate);

        var campaignChannels = !string.IsNullOrWhiteSpace(filter.Channel)
            ? (await db.MarketingCampaigns.Where(mc => mc.Channel == filter.Channel).Select(mc => mc.UtmCampaign ?? mc.Code).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var attributions = await db.RegistrationAttributions.AsNoTracking().Take(50_000).ToListAsync(ct);
        var matchingOwnerIds = DefaultGrowthMonitoringQueryService.GetMatchingOwnerIds(attributions, filter, campaignChannels);

        // 1. Subscription Orders (Confirmed Paid Subscription Only)
        var ordersQuery = db.PayOsAttempts.AsNoTracking()
            .Where(x => x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid && x.PaidAt >= startUtc && x.PaidAt < endUtc);

        if (matchingOwnerIds != null)
        {
            ordersQuery = ordersQuery.Where(o => matchingOwnerIds.Contains(o.OwnerId));
        }

        var attempts = await ordersQuery.OrderByDescending(x => x.PaidAt).Take(50_000).ToListAsync(ct);
        var subscriptionOrders = attempts.Select(a => new SubscriptionOrderRevenueDto(
            a.OrderCode,
            a.OwnerId,
            (int)a.Amount,
            a.SubscriptionDays,
            a.PaidAt!.Value
        )).ToList();

        // 2. Campaign Costs
        var costsQuery = db.CampaignCosts.AsNoTracking()
            .Where(c => c.Date >= fromDate && c.Date <= toDate);

        if (!string.IsNullOrWhiteSpace(filter.Campaign))
            costsQuery = costsQuery.Where(c => c.Campaign == filter.Campaign);
        if (!string.IsNullOrWhiteSpace(filter.Channel))
            costsQuery = costsQuery.Where(c => c.Channel == filter.Channel);
        if (!string.IsNullOrWhiteSpace(filter.Source))
            costsQuery = costsQuery.Where(c => c.Source == filter.Source);

        var costs = await costsQuery.Take(10_000).ToListAsync(ct);

        var campaignCosts = costs
            .GroupBy(c => (c.Campaign, Channel: c.Channel ?? "(none)"))
            .Select(g => {
                var cash = g.Sum(x => x.CashVnd);
                var hours = g.Sum(x => x.PersonHours);
                var hasMissingRate = g.Any(x => x.PersonHours > 0 && x.HourlyRate == null);
                decimal? imputed = hasMissingRate
                    ? null
                    : g.Sum(x => x.PersonHours * (x.HourlyRate ?? 0m));
                var total = cash + (imputed ?? 0m);
                return new CampaignCostSummaryDto(
                    g.Key.Campaign,
                    g.Key.Channel == "(none)" ? null : g.Key.Channel,
                    cash,
                    hours,
                    imputed,
                    total
                );
            })
            .OrderByDescending(c => c.TotalCostVnd)
            .ToList();

        var totalMarketingSpendVnd = campaignCosts.Sum(c => c.TotalCostVnd);
        var totalMarketingHours = campaignCosts.Sum(c => c.PersonHours);
        var totalSubscriptionRevenueVnd = subscriptionOrders.Sum(o => (decimal)o.AmountVnd);
        var paidSubscriptionCount = subscriptionOrders.Count;

        // 3. CAC30 Calculation with explicit null reasons
        Cac30MetricDto cac30;
        if (costs.Count == 0)
        {
            cac30 = new Cac30MetricDto(null, false, "MissingCostData");
        }
        else if (costs.Any(c => c.PersonHours > 0 && c.HourlyRate == null))
        {
            cac30 = new Cac30MetricDto(null, false, "MissingValuation");
        }
        else
        {
            var cohortOwnersQuery = db.OwnerMilestones.AsNoTracking()
                .Where(m => m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc);

            if (matchingOwnerIds != null)
            {
                cohortOwnersQuery = cohortOwnersQuery.Where(m => matchingOwnerIds.Contains(m.OwnerId));
            }

            var cohortOwners = await cohortOwnersQuery.ToListAsync(ct);
            if (cohortOwners.Count == 0)
            {
                cac30 = new Cac30MetricDto(null, false, "NoRegistrations");
            }
            else if (toDate > today.AddDays(-30))
            {
                cac30 = new Cac30MetricDto(null, false, "CohortImmature");
            }
            else
            {
                var firstPaidWithin30 = cohortOwners.Count(m =>
                    m.FirstPaidAt.HasValue && m.RegisteredAt.HasValue &&
                    m.FirstPaidAt.Value <= m.RegisteredAt.Value.AddDays(30));

                if (firstPaidWithin30 == 0)
                {
                    cac30 = new Cac30MetricDto(null, false, "ZeroDenominator");
                }
                else
                {
                    var cacVal = Math.Round(totalMarketingSpendVnd / firstPaidWithin30, 2);
                    cac30 = new Cac30MetricDto(cacVal, true, null);
                }
            }
        }

        var summary = new RevenueSummaryDto(
            totalSubscriptionRevenueVnd,
            paidSubscriptionCount,
            totalMarketingSpendVnd,
            totalMarketingHours,
            cac30
        );

        // 4. Daily Series
        var dailyRevenue = new List<DailyRevenuePointDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var dayStartUtc = ApiSupport.StartOfVietnamDate(d);
            var dayEndUtc = ApiSupport.EndOfVietnamDate(d);

            var subRev = attempts
                .Where(a => a.PaidAt >= dayStartUtc && a.PaidAt < dayEndUtc)
                .Sum(a => a.Amount);

            var dayCosts = costs.Where(c => c.Date == d).ToList();
            var spend = dayCosts.Sum(c => c.CashVnd + (c.PersonHours > 0 && c.HourlyRate != null ? c.PersonHours * c.HourlyRate.Value : 0m));

            dailyRevenue.Add(new DailyRevenuePointDto(d, subRev, spend));
        }

        return new AdminMonitoringRevenueResponse(
            summary,
            subscriptionOrders,
            campaignCosts,
            dailyRevenue
        );
    }

    public string BuildTrackingUrl(
        string targetUrl,
        string? source,
        string? medium,
        string? campaign,
        string? term = null,
        string? content = null)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            return string.Empty;

        var isAbsolute = targetUrl.Contains("://");
        var baseUri = isAbsolute ? new Uri(targetUrl) : new Uri("https://roomie.io.vn" + (targetUrl.StartsWith('/') ? targetUrl : "/" + targetUrl));

        var query = System.Web.HttpUtility.ParseQueryString(baseUri.Query);
        if (!string.IsNullOrWhiteSpace(source)) query["utm_source"] = source.Trim();
        if (!string.IsNullOrWhiteSpace(medium)) query["utm_medium"] = medium.Trim();
        if (!string.IsNullOrWhiteSpace(campaign)) query["utm_campaign"] = campaign.Trim();
        if (!string.IsNullOrWhiteSpace(term)) query["utm_term"] = term.Trim();
        if (!string.IsNullOrWhiteSpace(content)) query["utm_content"] = content.Trim();

        var queryString = query.ToString();
        var queryPart = string.IsNullOrEmpty(queryString) ? "" : "?" + queryString;

        if (isAbsolute)
        {
            return $"{baseUri.Scheme}://{baseUri.Authority}{baseUri.AbsolutePath}{queryPart}";
        }

        return $"{baseUri.AbsolutePath}{queryPart}";
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
            x.UpdatedAt,
            BuildTrackingUrl(x.TargetUrl ?? "/dang-ky", x.UtmSource, x.UtmMedium, x.UtmCampaign)
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
            campaign.UpdatedAt,
            BuildTrackingUrl(campaign.TargetUrl ?? "/dang-ky", campaign.UtmSource, campaign.UtmMedium, campaign.UtmCampaign)
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
            campaign.UpdatedAt,
            BuildTrackingUrl(campaign.TargetUrl ?? "/dang-ky", campaign.UtmSource, campaign.UtmMedium, campaign.UtmCampaign)
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

public sealed class DefaultMonitoringMaintenanceService(AppDbContext db, TimeProvider clock) : IMonitoringMaintenanceService
{
    public async Task<int> PruneExpiredTelemetryAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ApiSupport.VietnamDate(now);
        var pruned = 0;

        // 1. Raw Web Events (90 days)
        var rawCutoff = now.AddDays(-90);
        var oldEvents = await db.AnalyticsEvents
            .Where(e => e.ServerTimestamp < rawCutoff)
            .ToListAsync(ct);
        if (oldEvents.Count > 0)
        {
            db.AnalyticsEvents.RemoveRange(oldEvents);
            pruned += oldEvents.Count;
        }

        // 2. Owner Daily Activities (13 months)
        var activityCutoff = today.AddMonths(-13);
        var oldActivities = await db.OwnerDailyActivities
            .Where(a => a.ActivityDate < activityCutoff)
            .ToListAsync(ct);
        if (oldActivities.Count > 0)
        {
            db.OwnerDailyActivities.RemoveRange(oldActivities);
            pruned += oldActivities.Count;
        }

        // 3. Daily Aggregate Metrics (13 months)
        var oldAggregates = await db.DailyAggregateMetrics
            .Where(a => a.Date < activityCutoff)
            .ToListAsync(ct);
        if (oldAggregates.Count > 0)
        {
            db.DailyAggregateMetrics.RemoveRange(oldAggregates);
            pruned += oldAggregates.Count;
        }

        if (pruned > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return pruned;
    }

    public async Task<DailyAggregateMetric> RollupDailyAggregateAsync(DateOnly date, CancellationToken ct)
    {
        var startUtc = ApiSupport.StartOfVietnamDate(date);
        var endUtc = ApiSupport.EndOfVietnamDate(date);

        var daySessions = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ServerTimestamp >= startUtc && e.ServerTimestamp < endUtc)
            .Select(e => e.SessionId)
            .Distinct()
            .CountAsync(ct);

        var dayVisitors = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ServerTimestamp >= startUtc && e.ServerTimestamp < endUtc)
            .Select(e => e.BrowserId)
            .Distinct()
            .CountAsync(ct);

        var registrations = await db.OwnerMilestones
            .CountAsync(x => x.RegisteredAt >= startUtc && x.RegisteredAt < endUtc, ct);

        var activations = await db.OwnerMilestones
            .CountAsync(x => x.ActivatedAt >= startUtc && x.ActivatedAt < endUtc, ct);

        var wauStart = date.AddDays(-6);
        var activeOwnersWau = await db.OwnerDailyActivities.AsNoTracking()
            .Where(x => x.ActivityDate >= wauStart && x.ActivityDate <= date && x.HasOwnerAction)
            .Select(x => x.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var mauStart = date.AddDays(-29);
        var activeOwnersMau = await db.OwnerDailyActivities.AsNoTracking()
            .Where(x => x.ActivityDate >= mauStart && x.ActivityDate <= date && x.HasOwnerAction)
            .Select(x => x.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var subRevenue = await db.PayOsAttempts
            .Where(x => x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid && x.PaidAt >= startUtc && x.PaidAt < endUtc)
            .SumAsync(x => (decimal)x.Amount, ct);

        var costs = await db.CampaignCosts
            .Where(x => x.Date == date)
            .ToListAsync(ct);

        var marketingSpend = costs.Sum(x => x.CashVnd + (x.PersonHours > 0 && x.HourlyRate != null ? x.PersonHours * x.HourlyRate.Value : 0m));
        var marketingHours = costs.Sum(x => x.PersonHours);

        var aggregate = await db.DailyAggregateMetrics.SingleOrDefaultAsync(x => x.Date == date, ct);
        if (aggregate is null)
        {
            aggregate = new DailyAggregateMetric
            {
                Date = date,
                TotalSessions = daySessions,
                DistinctVisitors = dayVisitors,
                Registrations = registrations,
                ActivatedOwners = activations,
                ActiveOwnersWau = activeOwnersWau,
                ActiveOwnersMau = activeOwnersMau,
                SubscriptionRevenueVnd = subRevenue,
                MarketingSpendVnd = marketingSpend,
                MarketingHours = marketingHours,
                TotalRequests = 0,
                Total5xxErrors = 0,
                CalculatedAt = clock.GetUtcNow()
            };
            db.DailyAggregateMetrics.Add(aggregate);
        }
        else
        {
            aggregate.TotalSessions = daySessions;
            aggregate.DistinctVisitors = dayVisitors;
            aggregate.Registrations = registrations;
            aggregate.ActivatedOwners = activations;
            aggregate.ActiveOwnersWau = activeOwnersWau;
            aggregate.ActiveOwnersMau = activeOwnersMau;
            aggregate.SubscriptionRevenueVnd = subRevenue;
            aggregate.MarketingSpendVnd = marketingSpend;
            aggregate.MarketingHours = marketingHours;
            aggregate.CalculatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return aggregate;
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

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
        DateTimeOffset? issuedAt = null,
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
            var effectiveIssuedAt = issuedAt ?? now;
            var isNewMilestone = false;

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
                        ActivatedAt = effectiveIssuedAt,
                        FirstActivatedInvoiceId = invoiceId,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.OwnerMilestones.Add(milestone);
                    isNewMilestone = true;
                }
            }
            else if (milestone.ActivatedAt is null)
            {
                milestone.ActivatedAt = effectiveIssuedAt;
                milestone.FirstActivatedInvoiceId = invoiceId;
                milestone.UpdatedAt = now;
                isNewMilestone = true;
            }

            if (isNewMilestone)
            {
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
            var isNewMilestone = false;

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
                    isNewMilestone = true;
                }
            }
            else if (milestone.FirstPaidAt is null)
            {
                milestone.FirstPaidAt = effectivePaidAt;
                milestone.FirstPaidOrderCode = orderCode;
                milestone.UpdatedAt = now;
                isNewMilestone = true;
            }

            if (isNewMilestone)
            {
                await RecordOwnerActivityInternalAsync(db, ownerId, actor, "SubscriptionPaid", ct);
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Payment tracking failure must not block webhook processing
        }
    }

    public async Task EnsureSubscriptionPaidMilestoneAsync(Guid ownerId, CancellationToken ct = default)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;

            var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (milestone?.FirstPaidAt != null)
                return;

            var earliestPaid = await db.PayOsAttempts.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid && x.PaidAt != null)
                .OrderBy(x => x.PaidAt)
                .FirstOrDefaultAsync(ct);

            if (earliestPaid is not null && earliestPaid.PaidAt.HasValue)
            {
                await TrackSubscriptionPaidAsync(ownerId, earliestPaid.OrderCode, (int)earliestPaid.Amount, ActorType.System, earliestPaid.PaidAt.Value, ct);
            }
        }
        catch
        {
            // Milestone repair must not crash callers
        }
    }

    public async Task EnsureInvoiceIssuedMilestoneAsync(Guid ownerId, CancellationToken ct = default)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;

            var milestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (milestone?.ActivatedAt != null)
                return;

            var earliestInvoice = await db.Invoices.AsNoTracking()
                .Where(x => x.OwnerId == ownerId && x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Issued)
                .OrderBy(x => x.IssuedAt)
                .FirstOrDefaultAsync(ct);

            if (earliestInvoice is not null)
            {
                // Must filter for genuine publish transition, avoiding draft edit revisions created before issuance
                var publishRevision = await db.InvoiceRevisions.AsNoTracking()
                    .Where(r => r.InvoiceId == earliestInvoice.Id && EF.Functions.Like(r.Reason, "%publish%"))
                    .OrderBy(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                // Provable actor: Owner only if provable owner publish revision exists, otherwise System
                var actor = (publishRevision != null && publishRevision.ActorId == ownerId)
                    ? ActorType.Owner
                    : ActorType.System;

                var transitionTime = publishRevision?.CreatedAt ?? earliestInvoice.IssuedAt;
                await TrackInvoiceIssuedAsync(ownerId, earliestInvoice.Id, earliestInvoice.Kind, actor, transitionTime, ct);
            }
        }
        catch
        {
            // Milestone repair must not crash callers
        }
    }

    private static string SanitizeErrorCode(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => "ERR_CONCURRENCY_CONFLICT",
        DbUpdateException => "ERR_DB_UPDATE",
        TimeoutException or OperationCanceledException => "ERR_TIMEOUT",
        InvalidOperationException => "ERR_INVALID_OP",
        _ => "ERR_TELEMETRY_FAILED"
    };

    public async Task<int> ProcessPendingOutboxEventsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var context = GetDbContext();
            var db = context.Db;

            var pendingEvents = await db.MilestoneOutboxEvents
                .Where(x => x.Status == "Pending")
                .OrderBy(x => x.OccurredAt)
                .Take(100)
                .ToListAsync(ct);

            if (pendingEvents.Count == 0)
                return 0;

            var processed = 0;
            var now = _clock.GetUtcNow();
            foreach (var ev in pendingEvents)
            {
                try
                {
                    var milestoneRecorded = false;
                    if (ev.EventType == "SubscriptionPaid")
                    {
                        var parts = ev.TargetId?.Split(':') ?? [];
                        var codeStr = parts.Length > 0 ? parts[0] : ev.TargetId;
                        if (long.TryParse(codeStr, out var orderCode))
                        {
                            var actor = parts.Length > 1 && Enum.TryParse<ActorType>(parts[1], out var parsedActor) ? parsedActor : ActorType.System;
                            await TrackSubscriptionPaidAsync(ev.OwnerId, orderCode, 0, actor, ev.OccurredAt, ct);
                        }

                        // Authoritative durable verification: milestone must actually be persisted in DB
                        milestoneRecorded = await db.OwnerMilestones.AnyAsync(
                            m => m.OwnerId == ev.OwnerId && m.FirstPaidAt != null, ct);
                    }
                    else if (ev.EventType == "InvoiceIssued")
                    {
                        var parts = ev.TargetId?.Split(':') ?? [];
                        var idStr = parts.Length > 0 ? parts[0] : ev.TargetId;
                        if (Guid.TryParse(idStr, out var invoiceId))
                        {
                            var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == invoiceId, ct);
                            if (invoice != null && string.Equals(invoice.Kind, "Monthly", StringComparison.OrdinalIgnoreCase) && invoice.LifecycleState == InvoiceLifecycleState.Issued)
                            {
                                var actor = parts.Length > 1 && Enum.TryParse<ActorType>(parts[1], out var parsedActor) ? parsedActor : ActorType.System;
                                await TrackInvoiceIssuedAsync(ev.OwnerId, invoice.Id, invoice.Kind, actor, ev.OccurredAt, ct);
                            }
                        }

                        // Authoritative durable verification: milestone must actually be persisted in DB
                        milestoneRecorded = await db.OwnerMilestones.AnyAsync(
                            m => m.OwnerId == ev.OwnerId && m.ActivatedAt != null, ct);
                    }

                    if (milestoneRecorded)
                    {
                        ev.Status = "Completed";
                        ev.ProcessedAt = now;
                        ev.LastError = null;
                        processed++;
                    }
                    else
                    {
                        ev.RetryCount++;
                        ev.LastError = "ERR_MILESTONE_NOT_RECORDED";
                        if (ev.RetryCount >= 10)
                            ev.Status = "Failed";
                    }
                }
                catch (Exception ex)
                {
                    ev.RetryCount++;
                    ev.LastError = SanitizeErrorCode(ex);
                    if (ev.RetryCount >= 10)
                        ev.Status = "Failed";
                }
            }

            await db.SaveChangesAsync(ct);
            return processed;
        }
        catch
        {
            return 0;
        }
    }

    public async Task RepairMissingMilestonesAsync(CancellationToken ct = default)
    {
        try
        {
            await ProcessPendingOutboxEventsAsync(ct);

            await using var context = GetDbContext();
            var db = context.Db;

            var unmilestonedPaidOwners = await db.PayOsAttempts.AsNoTracking()
                .Where(a => a.Kind == "Subscription" && a.Status == PaymentAttemptStatus.Paid && a.PaidAt != null)
                .Where(a => !db.OwnerMilestones.Any(m => m.OwnerId == a.OwnerId && m.FirstPaidAt != null))
                .Select(a => a.OwnerId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var ownerId in unmilestonedPaidOwners)
            {
                await EnsureSubscriptionPaidMilestoneAsync(ownerId, ct);
            }

            var unmilestonedActivatedOwners = await db.Invoices.AsNoTracking()
                .Where(i => i.Kind == "Monthly" && i.LifecycleState == InvoiceLifecycleState.Issued)
                .Where(i => !db.OwnerMilestones.Any(m => m.OwnerId == i.OwnerId && m.ActivatedAt != null))
                .Select(i => i.OwnerId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var ownerId in unmilestonedActivatedOwners)
            {
                await EnsureInvoiceIssuedMilestoneAsync(ownerId, ct);
            }
        }
        catch
        {
            // Repair must not fail startup or background workers
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

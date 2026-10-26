using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring;


public sealed class DefaultGrowthMonitoringQueryService(AppDbContext db, TimeProvider clock) : IGrowthMonitoringQueryService
{
    public static bool EvaluateTouchMatches(
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

    public static IQueryable<Guid>? GetMatchingOwnerIdsQuery(
        AppDbContext db,
        MonitoringFilterRequest filter,
        HashSet<string> campaignChannelCampaigns)
    {
        var hasSource = !string.IsNullOrWhiteSpace(filter.Source);
        var hasChannel = !string.IsNullOrWhiteSpace(filter.Channel);
        var hasCampaign = !string.IsNullOrWhiteSpace(filter.Campaign);

        if (!hasSource && !hasChannel && !hasCampaign)
            return null;

        var query = db.RegistrationAttributions.AsNoTracking();

        if (hasSource)
        {
            var isDirectQuery = string.Equals(filter.Source, "(direct)", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(filter.Source, "direct", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(filter.Source, "(none)", StringComparison.OrdinalIgnoreCase);
            if (isDirectQuery)
            {
                query = query.Where(a =>
                    (a.FirstTouchSource == null || a.FirstTouchSource == "(direct)" || a.FirstTouchSource == "direct" || a.FirstTouchSource == "(none)") ||
                    (a.LastTouchSource != null && (a.LastTouchSource == "(direct)" || a.LastTouchSource == "direct" || a.LastTouchSource == "(none)")));
            }
            else
            {
                var sourceLower = filter.Source!.ToLower();
                query = query.Where(a =>
                    (a.FirstTouchSource != null && a.FirstTouchSource.ToLower() == sourceLower) ||
                    (a.LastTouchSource != null && a.LastTouchSource.ToLower() == sourceLower));
            }
        }

        if (hasCampaign)
        {
            var campaignLower = filter.Campaign!.ToLower();
            query = query.Where(a =>
                (a.FirstTouchCampaign != null && a.FirstTouchCampaign.ToLower() == campaignLower) ||
                (a.LastTouchCampaign != null && a.LastTouchCampaign.ToLower() == campaignLower));
        }

        if (hasChannel)
        {
            var channelFilter = filter.Channel!.ToLower();
            if (campaignChannelCampaigns.Count > 0)
            {
                var campList = campaignChannelCampaigns.Select(c => c.ToLower()).ToList();
                query = query.Where(a =>
                    (a.FirstTouchMedium != null && a.FirstTouchMedium.ToLower() == channelFilter) ||
                    (a.LastTouchMedium != null && a.LastTouchMedium.ToLower() == channelFilter) ||
                    (a.FirstTouchCampaign != null && campList.Contains(a.FirstTouchCampaign.ToLower())) ||
                    (a.LastTouchCampaign != null && campList.Contains(a.LastTouchCampaign.ToLower())));
            }
            else
            {
                query = query.Where(a =>
                    (a.FirstTouchMedium != null && a.FirstTouchMedium.ToLower() == channelFilter) ||
                    (a.LastTouchMedium != null && a.LastTouchMedium.ToLower() == channelFilter));
            }
        }

        return query.Select(a => a.OwnerId);
    }

    public static async Task<HashSet<Guid>?> GetMatchingOwnerIdsAsync(
        AppDbContext db,
        MonitoringFilterRequest filter,
        HashSet<string> campaignChannelCampaigns,
        CancellationToken ct)
    {
        var query = GetMatchingOwnerIdsQuery(db, filter, campaignChannelCampaigns);
        if (query is null) return null;
        var list = await query.ToListAsync(ct);
        return list.ToHashSet();
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

        var matchingOwnerIdsQuery = GetMatchingOwnerIdsQuery(db, filter, campaignChannels);

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

        var events = await eventsQuery.Take(50_001).ToListAsync(ct);
        var isEventsTruncated = events.Count > 50_000;
        if (isEventsTruncated)
            events.RemoveAt(50_000);

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
            totalSessions = await eventsQuery.Select(e => e.SessionId).Distinct().CountAsync(ct);
            uniqueVisitors = await eventsQuery.Select(e => e.BrowserId).Distinct().CountAsync(ct);
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
                // If single day, daily aggregate TotalSessions is the exact count for that day
                if (fromDate == toDate && dailyAggregates.TryGetValue(fromDate, out var singleAgg))
                {
                    totalSessions = singleAgg.TotalSessions;
                }
                else
                {
                    // Multi-day period crossing midnight with pruned raw coverage:
                    // 30-min idle sessions cross midnight, so daily distinct session sums
                    // mathematically overestimate true period distinct sessions.
                    // Must report null (unavailable/unknown) rather than an imprecise sum.
                    totalSessions = null;
                }
            }
        }

        // 3. Owner Milestones & Registrations in date range
        var milestonesQuery = db.OwnerMilestones.AsNoTracking()
            .Where(m => m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc);

        if (matchingOwnerIdsQuery != null)
        {
            milestonesQuery = milestonesQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));
        }

        var registrations = await milestonesQuery.CountAsync(ct);
        var registeredMilestones = await milestonesQuery.Take(50_001).ToListAsync(ct);
        var isMilestonesTruncated = registeredMilestones.Count > 50_000;
        if (isMilestonesTruncated)
            registeredMilestones.RemoveAt(50_000);

        // Activations in date range
        var activationsQuery = db.OwnerMilestones.AsNoTracking()
            .Where(m => m.ActivatedAt >= startUtc && m.ActivatedAt < endUtc);

        if (matchingOwnerIdsQuery != null)
        {
            activationsQuery = activationsQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));
        }
        var activatedOwners = await activationsQuery.CountAsync(ct);

        // Current WAU (last 7 Vietnam days) and MAU (last 30 Vietnam days)
        var wauStart = today.AddDays(-6);
        var wauQuery = db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= wauStart && a.ActivityDate <= today && a.HasOwnerAction);
        if (matchingOwnerIdsQuery != null)
            wauQuery = wauQuery.Where(a => matchingOwnerIdsQuery.Contains(a.OwnerId));

        var currentWau = await wauQuery
            .Select(a => a.OwnerId)
            .Distinct()
            .CountAsync(ct);

        var mauStart = today.AddDays(-29);
        var mauQuery = db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= mauStart && a.ActivityDate <= today && a.HasOwnerAction);
        if (matchingOwnerIdsQuery != null)
            mauQuery = mauQuery.Where(a => matchingOwnerIdsQuery.Contains(a.OwnerId));

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
        var sourceEvents = events;
        var sourceRegisteredMilestones = registeredMilestones;
        var sourceHasRawCoverageMissing = hasRawCoverageMissing;
        var sourceIsEventsTruncated = isEventsTruncated;

        if (!string.IsNullOrWhiteSpace(filter.SourcesWindow))
        {
            var win = filter.SourcesWindow.Trim().ToLowerInvariant();
            if (win is "1d" or "7d" or "all")
            {
                DateTimeOffset? srcStartUtc = win switch
                {
                    "1d" => ApiSupport.StartOfVietnamDate(today),
                    "7d" => ApiSupport.StartOfVietnamDate(today.AddDays(-6)),
                    _ => null
                };
                var srcEndUtc = ApiSupport.EndOfVietnamDate(today);

                var srcEventsQuery = db.AnalyticsEvents.AsNoTracking().AsQueryable();
                if (srcStartUtc.HasValue)
                {
                    srcEventsQuery = srcEventsQuery.Where(e => e.ServerTimestamp >= srcStartUtc.Value && e.ServerTimestamp < srcEndUtc);
                }
                else
                {
                    srcEventsQuery = srcEventsQuery.Where(e => e.ServerTimestamp < srcEndUtc);
                }

                if (!string.IsNullOrWhiteSpace(filter.Source))
                    srcEventsQuery = srcEventsQuery.Where(e => e.UtmSource == filter.Source);
                if (!string.IsNullOrWhiteSpace(filter.Campaign))
                    srcEventsQuery = srcEventsQuery.Where(e => e.UtmCampaign == filter.Campaign);
                if (!string.IsNullOrWhiteSpace(filter.Channel))
                {
                    srcEventsQuery = srcEventsQuery.Where(e => e.UtmMedium == filter.Channel || (e.UtmCampaign != null && campaignChannels.Contains(e.UtmCampaign)));
                }

                sourceEvents = await srcEventsQuery.Take(50_001).ToListAsync(ct);
                sourceIsEventsTruncated = sourceEvents.Count > 50_000;
                if (sourceIsEventsTruncated)
                    sourceEvents.RemoveAt(50_000);
                sourceHasRawCoverageMissing = false;

                var srcMilestonesQuery = db.OwnerMilestones.AsNoTracking().AsQueryable();
                if (srcStartUtc.HasValue)
                {
                    srcMilestonesQuery = srcMilestonesQuery.Where(m => m.RegisteredAt >= srcStartUtc.Value && m.RegisteredAt < srcEndUtc);
                }
                else
                {
                    srcMilestonesQuery = srcMilestonesQuery.Where(m => m.RegisteredAt != null && m.RegisteredAt < srcEndUtc);
                }

                if (matchingOwnerIdsQuery != null)
                {
                    srcMilestonesQuery = srcMilestonesQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));
                }

                sourceRegisteredMilestones = await srcMilestonesQuery.Take(50_001).ToListAsync(ct);
                if (sourceRegisteredMilestones.Count > 50_000)
                    sourceRegisteredMilestones.RemoveAt(50_000);
            }
        }

        var sourceEventGroups = sourceEvents
            .GroupBy(e => (Source: e.UtmSource ?? "(direct)", Medium: e.UtmMedium ?? "(none)"))
            .ToDictionary(g => g.Key, g => g.Select(x => x.SessionId).Distinct().Count());

        var regOwnerIds = sourceRegisteredMilestones.Select(m => m.OwnerId).ToList();
        var attributions = regOwnerIds.Count > 0
            ? await db.RegistrationAttributions.AsNoTracking().Where(a => regOwnerIds.Contains(a.OwnerId)).ToListAsync(ct)
            : [];
        var attrMap = attributions.ToDictionary(a => a.OwnerId);

        var sourceRegGroups = sourceRegisteredMilestones
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
            int? reportedSessions = (sourceHasRawCoverageMissing || sourceIsEventsTruncated) ? null : sCount;
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
        var actEligibleMilestones = await milestonesQuery
            .Where(m => m.RegisteredAt != null && m.RegisteredAt < activationCutoffUtc)
            .Select(m => new { m.RegisteredAt, m.ActivatedAt })
            .ToListAsync(ct);

        var activationCohortMaturedCount = actEligibleMilestones.Count;
        double? activationRate7Day = null;
        if (activationCohortMaturedCount > 0)
        {
            var activatedWithin7 = actEligibleMilestones.Count(m =>
                m.ActivatedAt.HasValue && m.RegisteredAt.HasValue &&
                m.ActivatedAt.Value <= m.RegisteredAt.Value.AddDays(7));
            activationRate7Day = Math.Round((double)activatedWithin7 / activationCohortMaturedCount * 100.0, 1);
        }

        // 30-day cohort: registrations that occurred at least 30 full Vietnam days ago
        var paidCutoffUtc = ApiSupport.StartOfVietnamDate(today.AddDays(-29));
        var paidEligibleMilestones = await milestonesQuery
            .Where(m => m.RegisteredAt != null && m.RegisteredAt < paidCutoffUtc)
            .Select(m => new { m.RegisteredAt, m.FirstPaidAt })
            .ToListAsync(ct);

        var paidCohortMaturedCount = paidEligibleMilestones.Count;
        double? firstPaidRate30Day = null;
        if (paidCohortMaturedCount > 0)
        {
            var paidWithin30 = paidEligibleMilestones.Count(m =>
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

        for (var i = 5; i >= 0; i--)
        {
            var cohortMonth = currentMonthStart.AddMonths(-i);
            var cohortPeriodStr = cohortMonth.ToString("yyyy-MM");
            var cohortStartUtc = ApiSupport.StartOfVietnamDate(cohortMonth);
            var cohortEndUtc = ApiSupport.EndOfVietnamDate(new DateOnly(cohortMonth.Year, cohortMonth.Month, DateTime.DaysInMonth(cohortMonth.Year, cohortMonth.Month)));

            var cohortOwnersQuery = db.OwnerMilestones.AsNoTracking()
                .Where(m => m.ActivatedAt >= cohortStartUtc && m.ActivatedAt < cohortEndUtc);
            if (matchingOwnerIdsQuery != null)
                cohortOwnersQuery = cohortOwnersQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));

            var cohortOwnerIdsQuery = cohortOwnersQuery.Select(m => m.OwnerId);
            var cohortOwnerCount = await cohortOwnerIdsQuery.Distinct().CountAsync(ct);
            if (cohortOwnerCount == 0)
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
                    var targetStart = new DateOnly(targetMonth.Year, targetMonth.Month, 1);
                    var targetEnd = new DateOnly(targetMonth.Year, targetMonth.Month, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));

                    var activeCount = await db.OwnerDailyActivities.AsNoTracking()
                        .Where(a => a.ActivityDate >= targetStart && a.ActivityDate <= targetEnd && a.HasOwnerAction && cohortOwnerIdsQuery.Contains(a.OwnerId))
                        .Select(a => a.OwnerId)
                        .Distinct()
                        .CountAsync(ct);

                    var pct = Math.Round((double)activeCount / cohortOwnerCount * 100.0, 1);
                    percentages.Add(pct);
                }
            }

            retentionRows.Add(new CohortRetentionRowDto(
                cohortPeriodStr,
                cohortOwnerCount,
                percentages
            ));
        }

        // 8. Daily Series & Aggregates fallback for 90d+ pruned telemetry
        var dailyPoints = new List<DailyGrowthPointDto>();
        var activeDaysQuery = db.OwnerDailyActivities.AsNoTracking()
            .Where(a => a.ActivityDate >= fromDate && a.ActivityDate <= toDate && a.HasOwnerAction);
        if (matchingOwnerIdsQuery != null)
            activeDaysQuery = activeDaysQuery.Where(a => matchingOwnerIdsQuery.Contains(a.OwnerId));

        var dailyActiveCounts = await activeDaysQuery
            .GroupBy(a => a.ActivityDate)
            .Select(g => new { Date = g.Key, Count = g.Select(x => x.OwnerId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count, ct);

        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var dayStartUtc = ApiSupport.StartOfVietnamDate(d);
            var dayEndUtc = ApiSupport.EndOfVietnamDate(d);

            int? daySessions;
            if (d >= rawRetentionCutoffDate)
            {
                // Raw coverage is present for this date.
                // A zero count here is a REAL ZERO, never fallback on aggregate!
                // If the raw events query was truncated by safety cap, expose null rather than partial counts
                daySessions = isEventsTruncated
                    ? null
                    : events
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

            var dayRegs = await milestonesQuery
                .Where(m => m.RegisteredAt >= dayStartUtc && m.RegisteredAt < dayEndUtc)
                .CountAsync(ct);

            var dayActs = await activationsQuery
                .Where(m => m.ActivatedAt >= dayStartUtc && m.ActivatedAt < dayEndUtc)
                .CountAsync(ct);

            var dayActiveOwners = dailyActiveCounts.GetValueOrDefault(d, 0);

            dailyPoints.Add(new DailyGrowthPointDto(
                d,
                daySessions,
                dayRegs,
                dayActs,
                dayActiveOwners
            ));
        }

        // 9. Funnel facts (Started, CodeResent, Verified, Bypassed) with attribution scoping
        var factsBaseQuery = db.RegistrationFacts.AsNoTracking()
            .Where(x => x.OccurredAt >= startUtc && x.OccurredAt < endUtc);

        int funnelStarted, funnelResent, funnelVerified, funnelBypassed;
        if (!hasDimensionFilter)
        {
            funnelStarted = await factsBaseQuery.Where(x => x.Stage == "Started").CountAsync(ct);
            funnelResent = await factsBaseQuery.Where(x => x.Stage == "CodeResent").CountAsync(ct);
            funnelVerified = await factsBaseQuery.Where(x => x.Stage == "Verified").CountAsync(ct);
            funnelBypassed = await factsBaseQuery.Where(x => x.Stage == "Bypassed").CountAsync(ct);
        }
        else
        {
            var matchingAttemptsQuery = db.RegistrationFacts.AsNoTracking().Where(f => f.Stage == "Started");
            if (!string.IsNullOrWhiteSpace(filter.Source))
            {
                var isDirect = string.Equals(filter.Source, "(direct)", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(filter.Source, "direct", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(filter.Source, "(none)", StringComparison.OrdinalIgnoreCase);
                if (isDirect)
                    matchingAttemptsQuery = matchingAttemptsQuery.Where(f => f.UtmSource == null || f.UtmSource == "(direct)" || f.UtmSource == "direct" || f.UtmSource == "(none)");
                else
                    matchingAttemptsQuery = matchingAttemptsQuery.Where(f => f.UtmSource != null && f.UtmSource.ToLower() == filter.Source!.ToLower());
            }
            if (!string.IsNullOrWhiteSpace(filter.Campaign))
                matchingAttemptsQuery = matchingAttemptsQuery.Where(f => f.UtmCampaign != null && f.UtmCampaign.ToLower() == filter.Campaign!.ToLower());
            if (!string.IsNullOrWhiteSpace(filter.Channel))
            {
                var channelFilter = filter.Channel!.ToLower();
                if (campaignChannels.Count > 0)
                {
                    var campList = campaignChannels.Select(c => c.ToLower()).ToList();
                    matchingAttemptsQuery = matchingAttemptsQuery.Where(f =>
                        (f.UtmMedium != null && f.UtmMedium.ToLower() == channelFilter) ||
                        (f.UtmCampaign != null && campList.Contains(f.UtmCampaign.ToLower())));
                }
                else
                {
                    matchingAttemptsQuery = matchingAttemptsQuery.Where(f => f.UtmMedium != null && f.UtmMedium.ToLower() == channelFilter);
                }
            }

            var matchingAttemptIds = matchingAttemptsQuery.Select(a => a.RegistrationAttemptId);

            var filteredFacts = matchingOwnerIdsQuery != null
                ? factsBaseQuery.Where(f =>
                    (f.OwnerId.HasValue && matchingOwnerIdsQuery.Contains(f.OwnerId.Value)) ||
                    matchingAttemptIds.Contains(f.RegistrationAttemptId))
                : factsBaseQuery.Where(f => matchingAttemptIds.Contains(f.RegistrationAttemptId));

            funnelStarted = await filteredFacts.Where(x => x.Stage == "Started").CountAsync(ct);
            funnelResent = await filteredFacts.Where(x => x.Stage == "CodeResent").CountAsync(ct);
            funnelVerified = await filteredFacts.Where(x => x.Stage == "Verified").CountAsync(ct);
            funnelBypassed = await filteredFacts.Where(x => x.Stage == "Bypassed").CountAsync(ct);
        }

        var unknownAttemptCount = await factsBaseQuery
            .Where(x => x.Stage == "Started" && (x.UtmSource == null || x.UtmSource == "") && (x.UtmCampaign == null || x.UtmCampaign == ""))
            .Select(x => x.RegistrationAttemptId)
            .Distinct()
            .CountAsync(ct);

        var funnel = new RegistrationFunnelDto(funnelStarted, funnelResent, funnelVerified, funnelBypassed, unknownAttemptCount);

        return new AdminMonitoringGrowthResponse(
            summary,
            sources,
            topCtas,
            retentionRows,
            cohortMaturity,
            dailyPoints,
            funnel,
            EffectiveFromDate: fromDate,
            EffectiveToDate: toDate
        );
    }
}

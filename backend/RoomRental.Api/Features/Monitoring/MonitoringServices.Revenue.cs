using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Features.Monitoring;


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

        var matchingOwnerIdsQuery = DefaultGrowthMonitoringQueryService.GetMatchingOwnerIdsQuery(db, filter, campaignChannels);

        // 1. Subscription Orders (Confirmed Paid Subscription Only)
        var ordersQuery = db.PayOsAttempts.AsNoTracking()
            .Where(x => x.Kind == "Subscription" && x.Status == PaymentAttemptStatus.Paid && x.PaidAt >= startUtc && x.PaidAt < endUtc);

        if (matchingOwnerIdsQuery != null)
        {
            ordersQuery = ordersQuery.Where(o => matchingOwnerIdsQuery.Contains(o.OwnerId));
        }

        var totalSubscriptionRevenueVnd = await ordersQuery.SumAsync(o => (decimal?)o.Amount, ct) ?? 0m;
        var paidSubscriptionCount = await ordersQuery.CountAsync(ct);

        var attempts = await ordersQuery.OrderByDescending(x => x.PaidAt).Take(1000).ToListAsync(ct);
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

        var groupedCosts = await costsQuery
            .GroupBy(c => new { c.Campaign, Channel = c.Channel ?? "(none)" })
            .Select(g => new {
                g.Key.Campaign,
                g.Key.Channel,
                Cash = g.Sum(x => x.CashVnd),
                Hours = g.Sum(x => x.PersonHours),
                HasMissingRate = g.Any(x => x.PersonHours > 0 && x.HourlyRate == null),
                Imputed = g.Sum(x => x.PersonHours * (x.HourlyRate ?? 0m))
            })
            .ToListAsync(ct);

        var campaignCosts = groupedCosts
            .Select(g => {
                var total = g.Cash + (g.HasMissingRate ? 0m : g.Imputed);
                return new CampaignCostSummaryDto(
                    g.Campaign,
                    g.Channel == "(none)" ? null : g.Channel,
                    g.Cash,
                    g.Hours,
                    g.HasMissingRate ? null : g.Imputed,
                    total
                );
            })
            .OrderByDescending(c => c.TotalCostVnd)
            .ToList();

        var totalMarketingSpendVnd = campaignCosts.Sum(c => c.TotalCostVnd);
        var totalMarketingHours = campaignCosts.Sum(c => c.PersonHours);

        // 3. CAC30 Calculation with explicit null reasons
        var hasAnyCosts = await costsQuery.AnyAsync(ct);
        var hasMissingValuation = await costsQuery.AnyAsync(c => c.PersonHours > 0 && c.HourlyRate == null, ct);

        Cac30MetricDto cac30;
        if (!hasAnyCosts)
        {
            cac30 = new Cac30MetricDto(null, false, "MissingCostData");
        }
        else if (hasMissingValuation)
        {
            cac30 = new Cac30MetricDto(null, false, "MissingValuation");
        }
        else
        {
            var cohortOwnersQuery = db.OwnerMilestones.AsNoTracking()
                .Where(m => m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc);

            if (matchingOwnerIdsQuery != null)
            {
                cohortOwnersQuery = cohortOwnersQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));
            }

            var cohortRegisteredCount = await cohortOwnersQuery.CountAsync(ct);
            if (cohortRegisteredCount == 0)
            {
                cac30 = new Cac30MetricDto(null, false, "NoRegistrations");
            }
            else if (toDate > today.AddDays(-30))
            {
                cac30 = new Cac30MetricDto(null, false, "CohortImmature");
            }
            else
            {
                var paidMilestones = await cohortOwnersQuery
                    .Where(m => m.FirstPaidAt != null && m.RegisteredAt != null)
                    .Select(m => new { m.RegisteredAt, m.FirstPaidAt })
                    .ToListAsync(ct);

                var firstPaidWithin30 = paidMilestones.Count(m =>
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
        var dailyCostsDict = await costsQuery
            .GroupBy(c => c.Date)
            .Select(g => new {
                Date = g.Key,
                Spend = g.Sum(x => x.CashVnd + (x.PersonHours > 0 && x.HourlyRate != null ? x.PersonHours * x.HourlyRate.Value : 0m))
            })
            .ToDictionaryAsync(x => x.Date, x => x.Spend, ct);

        var dailyRevenue = new List<DailyRevenuePointDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            var dayStartUtc = ApiSupport.StartOfVietnamDate(d);
            var dayEndUtc = ApiSupport.EndOfVietnamDate(d);

            var subRev = await ordersQuery
                .Where(a => a.PaidAt >= dayStartUtc && a.PaidAt < dayEndUtc)
                .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

            var spend = dailyCostsDict.GetValueOrDefault(d, 0m);

            dailyRevenue.Add(new DailyRevenuePointDto(d, subRev, spend));
        }

        // 5. Campaign ROAS / ROI Breakdown
        var allCampaignMetadata = await db.MarketingCampaigns.AsNoTracking().ToListAsync(ct);

        var roasAttributions = await (from m in db.OwnerMilestones.AsNoTracking()
                                      join a in db.RegistrationAttributions.AsNoTracking() on m.OwnerId equals a.OwnerId into attrJoin
                                      from a in attrJoin.DefaultIfEmpty()
                                      where (m.RegisteredAt >= startUtc && m.RegisteredAt < endUtc) ||
                                            (m.ActivatedAt >= startUtc && m.ActivatedAt < endUtc)
                                      select new
                                      {
                                          m.OwnerId,
                                          m.RegisteredAt,
                                          m.ActivatedAt,
                                          Campaign = a != null ? (a.LastTouchCampaign ?? a.FirstTouchCampaign) : null,
                                          Source = a != null ? (a.LastTouchSource ?? a.FirstTouchSource) : null
                                      }).ToListAsync(ct);

        var orderOwnerIds = attempts.Select(a => a.OwnerId).Distinct().ToList();
        var orderOwnerAttributions = orderOwnerIds.Count > 0
            ? await db.RegistrationAttributions.AsNoTracking()
                .Where(a => orderOwnerIds.Contains(a.OwnerId))
                .ToDictionaryAsync(
                    a => a.OwnerId,
                    a => a.LastTouchCampaign ?? a.FirstTouchCampaign,
                    ct)
            : new Dictionary<Guid, string?>();

        var campaignKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in allCampaignMetadata)
        {
            var key = !string.IsNullOrWhiteSpace(meta.UtmCampaign) ? meta.UtmCampaign! : meta.Code;
            if (!string.IsNullOrWhiteSpace(key)) campaignKeys.Add(key);
        }
        foreach (var cost in campaignCosts)
        {
            if (!string.IsNullOrWhiteSpace(cost.Campaign)) campaignKeys.Add(cost.Campaign);
        }
        foreach (var row in roasAttributions)
        {
            if (!string.IsNullOrWhiteSpace(row.Campaign)) campaignKeys.Add(row.Campaign!);
        }
        foreach (var camp in orderOwnerAttributions.Values)
        {
            if (!string.IsNullOrWhiteSpace(camp)) campaignKeys.Add(camp!);
        }

        if (!string.IsNullOrWhiteSpace(filter.Campaign))
        {
            campaignKeys.RemoveWhere(k => !string.Equals(k, filter.Campaign, StringComparison.OrdinalIgnoreCase));
        }

        var campaignRoas = new List<CampaignRoasRowDto>();
        foreach (var campKey in campaignKeys)
        {
            var meta = allCampaignMetadata.FirstOrDefault(m =>
                string.Equals(m.UtmCampaign, campKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.Code, campKey, StringComparison.OrdinalIgnoreCase));

            var costItems = campaignCosts.Where(c => string.Equals(c.Campaign, campKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var channel = meta?.Channel ?? costItems.Select(c => c.Channel).FirstOrDefault(ch => !string.IsNullOrWhiteSpace(ch));
            if (!string.IsNullOrWhiteSpace(filter.Channel) && !string.Equals(channel, filter.Channel, StringComparison.OrdinalIgnoreCase))
                continue;

            var source = meta?.Source ?? meta?.UtmSource;
            var spend = costItems.Sum(c => c.TotalCostVnd);
            var regs = roasAttributions.Count(r =>
                string.Equals(r.Campaign, campKey, StringComparison.OrdinalIgnoreCase) &&
                r.RegisteredAt >= startUtc && r.RegisteredAt < endUtc);
            var acts = roasAttributions.Count(r =>
                string.Equals(r.Campaign, campKey, StringComparison.OrdinalIgnoreCase) &&
                r.ActivatedAt >= startUtc && r.ActivatedAt < endUtc);

            var campOrders = attempts.Where(a =>
                orderOwnerAttributions.TryGetValue(a.OwnerId, out var c) &&
                string.Equals(c, campKey, StringComparison.OrdinalIgnoreCase)).ToList();

            var paidOrders = campOrders.Count;
            var rev = campOrders.Sum(a => (decimal)a.Amount);
            var cplVnd = regs > 0 && spend > 0 ? Math.Round(spend / regs, 2) : (decimal?)null;
            var roasPercent = spend > 0 ? Math.Round((rev / spend) * 100m, 2) : (decimal?)null;

            campaignRoas.Add(new CampaignRoasRowDto(
                campKey,
                meta?.Name ?? campKey,
                channel,
                source,
                meta?.PromoCode,
                spend,
                null,
                regs,
                acts,
                paidOrders,
                rev,
                cplVnd,
                roasPercent));
        }

        campaignRoas = campaignRoas
            .OrderByDescending(x => x.RevenueVnd)
            .ThenByDescending(x => x.TotalCostVnd)
            .ThenByDescending(x => x.Registrations)
            .ThenBy(x => x.Campaign)
            .ToList();

        return new AdminMonitoringRevenueResponse(
            summary,
            subscriptionOrders,
            campaignCosts,
            dailyRevenue,
            EffectiveFromDate: fromDate,
            EffectiveToDate: toDate,
            CampaignRoas: campaignRoas
        );
    }

    public string BuildTrackingUrl(
        string targetUrl,
        string? source,
        string? medium,
        string? campaign,
        string? term = null,
        string? content = null,
        string? promoCode = null)
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
        if (!string.IsNullOrWhiteSpace(promoCode)) query["promo"] = promoCode.Trim().ToUpperInvariant();

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
            BuildTrackingUrl(x.TargetUrl ?? "/dang-ky", x.UtmSource, x.UtmMedium, x.UtmCampaign, promoCode: x.PromoCode),
            x.PromoCode
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

        var promoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant();
        if (promoCode is { Length: > 32 })
            throw new ApiException(StatusCodes.Status400BadRequest, "PromoCode must be 32 characters or fewer.");

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
            PromoCode = promoCode,
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
            BuildTrackingUrl(campaign.TargetUrl ?? "/dang-ky", campaign.UtmSource, campaign.UtmMedium, campaign.UtmCampaign, promoCode: campaign.PromoCode),
            campaign.PromoCode
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
        if (request.PromoCode != null)
        {
            var promoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant();
            if (promoCode is { Length: > 32 })
                throw new ApiException(StatusCodes.Status400BadRequest, "PromoCode must be 32 characters or fewer.");
            campaign.PromoCode = promoCode;
        }
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
            BuildTrackingUrl(campaign.TargetUrl ?? "/dang-ky", campaign.UtmSource, campaign.UtmMedium, campaign.UtmCampaign, promoCode: campaign.PromoCode),
            campaign.PromoCode
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

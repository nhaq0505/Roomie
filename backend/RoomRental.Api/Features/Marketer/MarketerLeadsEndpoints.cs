using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class MarketerLeadsEndpoints
{
    public static void MapMarketerLeadsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/marketer/leads")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Marketer"));

        group.MapGet("", async (
            string? segment,
            string? status,
            string? leadStatus,
            string? source,
            string? campaign,
            string? q,
            string? search,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var allLeads = await LoadAllOwnerLeadsAsync(db, now, ct);

            var stuckCount = allLeads.Count(x => MatchesSegment(x.Lead, x.LastActivityDate, "StuckOnboarding", now));
            var freeNearLimitCount = allLeads.Count(x => MatchesSegment(x.Lead, x.LastActivityDate, "FreeNearLimit", now));
            var proExpiringCount = allLeads.Count(x => MatchesSegment(x.Lead, x.LastActivityDate, "ProExpiringSoon", now));
            var inactiveCount = allLeads.Count(x => MatchesSegment(x.Lead, x.LastActivityDate, "InactiveOwners", now));
            var activeProCount = allLeads.Count(x => MatchesSegment(x.Lead, x.LastActivityDate, "ActivePro", now));

            var summary = new MarketerLeadSummaryCountsDto(
                allLeads.Count,
                stuckCount,
                freeNearLimitCount,
                proExpiringCount,
                inactiveCount,
                activeProCount);

            IEnumerable<(MarketerLeadItemDto Lead, DateOnly? LastActivityDate)> filtered = allLeads;

            if (!string.IsNullOrWhiteSpace(segment) && !string.Equals(segment, "All", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x => MatchesSegment(x.Lead, x.LastActivityDate, segment.Trim(), now));
            }

            var effectiveStatus = !string.IsNullOrWhiteSpace(leadStatus) ? leadStatus : status;
            if (!string.IsNullOrWhiteSpace(effectiveStatus) && !string.Equals(effectiveStatus, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<OwnerLeadStatus>(effectiveStatus.Trim(), ignoreCase: true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
                    throw new ApiException(StatusCodes.Status400BadRequest, "Trạng thái Lead không hợp lệ.");
                filtered = filtered.Where(x => x.Lead.LeadStatus == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "All", StringComparison.OrdinalIgnoreCase))
            {
                var srcTerm = source.Trim();
                filtered = filtered.Where(x => x.Lead.UtmSource != null && string.Equals(x.Lead.UtmSource, srcTerm, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(campaign) && !string.Equals(campaign, "All", StringComparison.OrdinalIgnoreCase))
            {
                var campTerm = campaign.Trim();
                filtered = filtered.Where(x => x.Lead.UtmCampaign != null && string.Equals(x.Lead.UtmCampaign, campTerm, StringComparison.OrdinalIgnoreCase));
            }

            var effectiveSearch = !string.IsNullOrWhiteSpace(search) ? search : q;
            if (!string.IsNullOrWhiteSpace(effectiveSearch))
            {
                var term = effectiveSearch.Trim().ToLowerInvariant();
                filtered = filtered.Where(x =>
                    x.Lead.Name.ToLowerInvariant().Contains(term) ||
                    x.Lead.Email.ToLowerInvariant().Contains(term) ||
                    (x.Lead.Phone != null && x.Lead.Phone.ToLowerInvariant().Contains(term)) ||
                    (x.Lead.UtmCampaign != null && x.Lead.UtmCampaign.ToLowerInvariant().Contains(term)) ||
                    (x.Lead.UtmSource != null && x.Lead.UtmSource.ToLowerInvariant().Contains(term)) ||
                    (x.Lead.Tags != null && x.Lead.Tags.ToLowerInvariant().Contains(term)));
            }

            var items = filtered
                .Select(x => x.Lead)
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.OwnerId)
                .ToList();

            return Results.Ok(new MarketerLeadsResponse(summary, items));
        });

        group.MapGet("/{ownerId:guid}", async (
            Guid ownerId,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var allLeads = await LoadAllOwnerLeadsAsync(db, now, ct, ownerId);
            if (allLeads.Count == 0)
                throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chủ trọ.");

            var notes = await db.OwnerMarketingNotes.AsNoTracking()
                .Where(n => n.OwnerId == ownerId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new MarketerLeadNoteDto(
                    n.Id,
                    n.OwnerId,
                    n.AuthorUserId,
                    n.AuthorName,
                    n.Content,
                    n.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new MarketerLeadDetailDto(allLeads[0].Lead, notes));
        });

        group.MapGet("/{ownerId:guid}/notes", async (
            Guid ownerId,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var ownerExists = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == ownerId && u.Role == UserRole.Owner, ct);
            if (!ownerExists)
                throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chủ trọ.");

            var notes = await db.OwnerMarketingNotes.AsNoTracking()
                .Where(n => n.OwnerId == ownerId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new MarketerLeadNoteDto(
                    n.Id,
                    n.OwnerId,
                    n.AuthorUserId,
                    n.AuthorName,
                    n.Content,
                    n.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(notes);
        });

        group.MapPut("/{ownerId:guid}/profile", async (
            Guid ownerId,
            UpdateMarketerLeadProfileRequest request,
            HttpContext context,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (!Enum.IsDefined(request.LeadStatus))
                throw new ApiException(StatusCodes.Status400BadRequest, "Trạng thái chăm sóc không hợp lệ.");

            var ownerExists = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == ownerId && u.Role == UserRole.Owner, ct);
            if (!ownerExists)
                throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chủ trọ.");

            var rawTags = new List<string>();
            if (request.Tags is { } tagsElem)
            {
                if (tagsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in tagsElem.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                            rawTags.AddRange(s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    }
                }
                else if (tagsElem.ValueKind == JsonValueKind.String && tagsElem.GetString() is { } str)
                {
                    rawTags.AddRange(str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }

            var tagsList = rawTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var joinedTags = string.Join(", ", tagsList);
            if (joinedTags.Length > 500)
                throw new ApiException(StatusCodes.Status400BadRequest, "Danh sách nhãn (tags) tối đa 500 ký tự.");

            var now = clock.GetUtcNow();
            var actorId = ApiSupport.UserId(context);

            var profile = await db.OwnerMarketingProfiles.SingleOrDefaultAsync(p => p.OwnerId == ownerId, ct);
            if (profile is null)
            {
                profile = new OwnerMarketingProfile
                {
                    OwnerId = ownerId,
                    LeadStatus = request.LeadStatus,
                    Tags = joinedTags,
                    NextFollowUpAt = request.NextFollowUpAt,
                    LastContactedAt = request.MarkContactedNow || request.LeadStatus == OwnerLeadStatus.Contacted ? now : null,
                    AssignedMarketerId = actorId,
                    UpdatedAt = now
                };
                db.OwnerMarketingProfiles.Add(profile);
            }
            else
            {
                profile.LeadStatus = request.LeadStatus;
                profile.Tags = joinedTags;
                profile.NextFollowUpAt = request.NextFollowUpAt;
                if (request.MarkContactedNow || (request.LeadStatus == OwnerLeadStatus.Contacted && profile.LastContactedAt == null))
                {
                    profile.LastContactedAt = now;
                }
                profile.AssignedMarketerId ??= actorId;
                profile.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);

            var updated = await LoadAllOwnerLeadsAsync(db, now, ct, ownerId);
            return Results.Ok(updated[0].Lead);
        });

        group.MapPost("/{ownerId:guid}/notes", async (
            Guid ownerId,
            CreateMarketerLeadNoteRequest request,
            HttpContext context,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var content = request.Content?.Trim();
            if (string.IsNullOrWhiteSpace(content) || content.Length > 2000)
                throw new ApiException(StatusCodes.Status400BadRequest, "Nội dung ghi chú phải từ 1 đến 2000 ký tự.");

            var ownerExists = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == ownerId && u.Role == UserRole.Owner, ct);
            if (!ownerExists)
                throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chủ trọ.");

            var now = clock.GetUtcNow();
            var actorId = ApiSupport.UserId(context);
            var actorName = context.User.Identity?.Name ?? "Marketer";

            var note = new OwnerMarketingNote
            {
                OwnerId = ownerId,
                AuthorUserId = actorId,
                AuthorName = actorName,
                Content = content,
                CreatedAt = now
            };
            db.OwnerMarketingNotes.Add(note);

            var profile = await db.OwnerMarketingProfiles.SingleOrDefaultAsync(p => p.OwnerId == ownerId, ct);
            if (profile is null)
            {
                profile = new OwnerMarketingProfile
                {
                    OwnerId = ownerId,
                    LeadStatus = OwnerLeadStatus.Contacted,
                    LastContactedAt = now,
                    AssignedMarketerId = actorId,
                    UpdatedAt = now
                };
                db.OwnerMarketingProfiles.Add(profile);
            }
            else
            {
                profile.LastContactedAt = now;
                profile.AssignedMarketerId ??= actorId;
                profile.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/marketer/leads/{ownerId}/notes/{note.Id}", new MarketerLeadNoteDto(
                note.Id,
                note.OwnerId,
                note.AuthorUserId,
                note.AuthorName,
                note.Content,
                note.CreatedAt));
        });
    }

    public static bool MatchesSegment(MarketerLeadItemDto lead, DateOnly? lastActivityDate, string segment, DateTimeOffset now)
    {
        var today = ApiSupport.VietnamDate(now);
        return segment switch
        {
            "StuckOnboarding" => !lead.IsActivated || lead.RoomCount == 0,
            "FreeNearLimit" => !lead.IsPro && lead.RoomCount >= 4,
            "ProExpiringSoon" => lead.IsPro && lead.SubscriptionExpiresAt.HasValue &&
                                 lead.SubscriptionExpiresAt.Value > now &&
                                 lead.SubscriptionExpiresAt.Value <= now.AddDays(14),
            "InactiveOwners" => !lastActivityDate.HasValue || lastActivityDate.Value < today.AddDays(-14),
            "ActivePro" => lead.IsPro && (!lead.SubscriptionExpiresAt.HasValue || lead.SubscriptionExpiresAt.Value > now.AddDays(14)),
            "AllFreeOwners" => !lead.IsPro,
            _ => true
        };
    }

    public static async Task<List<(MarketerLeadItemDto Lead, DateOnly? LastActivityDate)>> LoadAllOwnerLeadsAsync(
        AppDbContext db,
        DateTimeOffset now,
        CancellationToken ct,
        Guid? filterOwnerId = null)
    {
        var ownersQuery = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Owner);
        if (filterOwnerId.HasValue)
        {
            ownersQuery = ownersQuery.Where(u => u.Id == filterOwnerId.Value);
        }

        // Select ONLY non-sensitive fields: never select CitizenId, PermanentAddress, Bank/payOS fields, etc.
        var owners = await ownersQuery
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Status,
                u.CreatedAt,
                u.SubscriptionTier,
                u.SubscriptionExpiresAt
            })
            .ToListAsync(ct);

        if (owners.Count == 0)
            return [];

        var ownerIds = owners.Select(x => x.Id).ToList();

        var buildingCounts = await db.Buildings.AsNoTracking()
            .Where(b => ownerIds.Contains(b.OwnerId) && b.ArchivedAt == null)
            .GroupBy(b => b.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, ct);

        var roomCounts = await (from r in db.Rooms.AsNoTracking()
                                join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                                where ownerIds.Contains(b.OwnerId) && r.ArchivedAt == null && b.ArchivedAt == null
                                group r by b.OwnerId into g
                                select new { OwnerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, ct);

        var milestones = await db.OwnerMilestones.AsNoTracking()
            .Where(m => ownerIds.Contains(m.OwnerId))
            .Select(m => new { m.OwnerId, m.ActivatedAt })
            .ToDictionaryAsync(x => x.OwnerId, x => x.ActivatedAt, ct);

        var attributions = await db.RegistrationAttributions.AsNoTracking()
            .Where(a => ownerIds.Contains(a.OwnerId))
            .Select(a => new
            {
                a.OwnerId,
                Source = a.LastTouchSource ?? a.FirstTouchSource,
                Medium = a.LastTouchMedium ?? a.FirstTouchMedium,
                Campaign = a.LastTouchCampaign ?? a.FirstTouchCampaign
            })
            .ToDictionaryAsync(x => x.OwnerId, x => x, ct);

        var redemptionsRaw = await (from r in db.PromotionRedemptions.AsNoTracking()
                                    join p in db.PromotionCodes.AsNoTracking() on r.PromotionCodeId equals p.Id
                                    where ownerIds.Contains(r.UserId)
                                    orderby r.RedeemedAt descending
                                    select new { r.UserId, p.Code }).ToListAsync(ct);

        var redemptionsByOwner = redemptionsRaw
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Code).Distinct().ToList());

        var profiles = await (from p in db.OwnerMarketingProfiles.AsNoTracking()
                              join m in db.Users.AsNoTracking() on p.AssignedMarketerId equals m.Id into mGroup
                              from m in mGroup.DefaultIfEmpty()
                              where ownerIds.Contains(p.OwnerId)
                              select new
                              {
                                  p.OwnerId,
                                  p.LeadStatus,
                                  p.Tags,
                                  p.NextFollowUpAt,
                                  p.LastContactedAt,
                                  AssignedMarketerName = m != null ? m.Name : null
                              })
            .ToDictionaryAsync(x => x.OwnerId, x => x, ct);

        var noteCounts = await db.OwnerMarketingNotes.AsNoTracking()
            .Where(n => ownerIds.Contains(n.OwnerId))
            .GroupBy(n => n.OwnerId)
            .Select(g => new { OwnerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, ct);

        var lastActivities = await db.OwnerDailyActivities.AsNoTracking()
            .Where(a => ownerIds.Contains(a.OwnerId) && a.HasOwnerAction)
            .GroupBy(a => a.OwnerId)
            .Select(g => new { OwnerId = g.Key, LastDate = g.Max(x => (DateOnly?)x.ActivityDate) })
            .ToDictionaryAsync(x => x.OwnerId, x => x.LastDate, ct);

        var result = new List<(MarketerLeadItemDto Lead, DateOnly? LastActivityDate)>(owners.Count);
        foreach (var o in owners)
        {
            var isPro = o.SubscriptionTier == SubscriptionTier.Pro &&
                        (o.SubscriptionExpiresAt is null || o.SubscriptionExpiresAt > now);
            var maxRooms = isPro ? int.MaxValue : 5;
            var bCount = buildingCounts.GetValueOrDefault(o.Id, 0);
            var rCount = roomCounts.GetValueOrDefault(o.Id, 0);
            var activatedAt = milestones.GetValueOrDefault(o.Id, null);
            var attr = attributions.GetValueOrDefault(o.Id);
            var codes = redemptionsByOwner.GetValueOrDefault(o.Id, []);
            var prof = profiles.GetValueOrDefault(o.Id);
            var nCount = noteCounts.GetValueOrDefault(o.Id, 0);
            var lastAct = lastActivities.GetValueOrDefault(o.Id, null);

            var tags = prof is not null && !string.IsNullOrWhiteSpace(prof.Tags)
                ? prof.Tags
                : null;
            DateTimeOffset? lastActiveAt = lastAct.HasValue
                ? new DateTimeOffset(lastAct.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7))
                : null;

            var dto = new MarketerLeadItemDto(
                o.Id,
                o.Name,
                o.Email,
                o.Phone,
                o.Status,
                o.CreatedAt,
                bCount,
                rCount,
                maxRooms,
                activatedAt.HasValue,
                activatedAt,
                o.SubscriptionTier,
                isPro,
                o.SubscriptionExpiresAt,
                attr?.Source,
                attr?.Medium,
                attr?.Campaign,
                codes,
                prof?.LeadStatus ?? OwnerLeadStatus.New,
                tags,
                prof?.NextFollowUpAt,
                prof?.LastContactedAt,
                prof?.AssignedMarketerName,
                nCount,
                lastActiveAt);

            result.Add((dto, lastAct));
        }

        return result;
    }
}

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class MarketerContentEndpoints
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    public static void MapMarketerContentEndpoints(this WebApplication app)
    {
        var marketerGroup = app.MapGroup("/api/marketer")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Marketer"));

        // --- CMS Guide Articles (Marketer/Admin) ---
        marketerGroup.MapGet("/articles", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.MarketingArticles.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => ToArticleDto(x))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        marketerGroup.MapPost("/articles", async (
            UpsertMarketingArticleRequest request,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var slug = ValidateAndNormalizeSlug(request.Slug);
            ValidateArticleRequest(request);

            if (await db.MarketingArticles.AnyAsync(x => x.Slug == slug, ct))
                throw new ApiException(StatusCodes.Status409Conflict, "Đường dẫn bài viết (slug) này đã tồn tại.");

            var now = clock.GetUtcNow();
            var resolvedStatus = ResolveArticleStatus(request);
            var resolvedReadTime = ResolveReadTimeLabel(request);
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : "Dùng thử miễn phí 5 phòng");
            var resolvedCtaUrl = !string.IsNullOrWhiteSpace(request.CtaTargetUrl) ? request.CtaTargetUrl.Trim() : (!string.IsNullOrWhiteSpace(request.CtaUrl) ? request.CtaUrl.Trim() : "/register");
            var article = new MarketingArticle
            {
                Slug = slug,
                Title = request.Title.Trim(),
                Category = request.Category.Trim(),
                ReadTimeLabel = resolvedReadTime,
                Summary = request.Summary.Trim(),
                ContentMarkdown = request.ContentMarkdown.Trim(),
                SeoTitle = string.IsNullOrWhiteSpace(request.SeoTitle) ? request.Title.Trim() : request.SeoTitle.Trim(),
                SeoDescription = string.IsNullOrWhiteSpace(request.SeoDescription) ? request.Summary.Trim() : request.SeoDescription.Trim(),
                CtaLabel = resolvedCtaLabel,
                CtaTargetUrl = resolvedCtaUrl,
                Status = resolvedStatus,
                PublishedAt = resolvedStatus == MarketingArticleStatus.Published ? now : null,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.MarketingArticles.Add(article);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/marketer/articles/{article.Id}", ToArticleDto(article));
        });

        marketerGroup.MapPut("/articles/{id:guid}", async (
            Guid id,
            UpsertMarketingArticleRequest request,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var article = await db.MarketingArticles.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy bài viết.");

            var slug = ValidateAndNormalizeSlug(request.Slug);
            ValidateArticleRequest(request);

            if (!string.Equals(article.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
                await db.MarketingArticles.AnyAsync(x => x.Slug == slug && x.Id != id, ct))
            {
                throw new ApiException(StatusCodes.Status409Conflict, "Đường dẫn bài viết (slug) này đã tồn tại.");
            }

            var now = clock.GetUtcNow();
            var resolvedStatus = ResolveArticleStatus(request);
            var resolvedReadTime = ResolveReadTimeLabel(request);
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : "Dùng thử miễn phí 5 phòng");
            var resolvedCtaUrl = !string.IsNullOrWhiteSpace(request.CtaTargetUrl) ? request.CtaTargetUrl.Trim() : (!string.IsNullOrWhiteSpace(request.CtaUrl) ? request.CtaUrl.Trim() : "/register");
            article.Slug = slug;
            article.Title = request.Title.Trim();
            article.Category = request.Category.Trim();
            article.ReadTimeLabel = resolvedReadTime;
            article.Summary = request.Summary.Trim();
            article.ContentMarkdown = request.ContentMarkdown.Trim();
            article.SeoTitle = string.IsNullOrWhiteSpace(request.SeoTitle) ? request.Title.Trim() : request.SeoTitle.Trim();
            article.SeoDescription = string.IsNullOrWhiteSpace(request.SeoDescription) ? request.Summary.Trim() : request.SeoDescription.Trim();
            article.CtaLabel = resolvedCtaLabel;
            article.CtaTargetUrl = resolvedCtaUrl;
            if (resolvedStatus == MarketingArticleStatus.Published && article.PublishedAt is null)
            {
                article.PublishedAt = now;
            }
            article.Status = resolvedStatus;
            article.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToArticleDto(article));
        });

        marketerGroup.MapDelete("/articles/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var article = await db.MarketingArticles.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy bài viết.");

            db.MarketingArticles.Remove(article);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // --- Public Guide Articles ---
        app.MapGet("/api/public/guides", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.MarketingArticles.AsNoTracking()
                .Where(x => x.Status == MarketingArticleStatus.Published)
                .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
                .Select(x => new PublicGuideArticleSummaryDto(
                    x.Slug,
                    x.Title,
                    x.Category,
                    x.ReadTimeLabel,
                    x.Summary,
                    x.PublishedAt))
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        app.MapGet("/api/public/guides/{slug}", async (string slug, AppDbContext db, CancellationToken ct) =>
        {
            var normalized = slug.Trim().ToLowerInvariant();
            var article = await db.MarketingArticles
                .SingleOrDefaultAsync(x => x.Slug == normalized && x.Status == MarketingArticleStatus.Published, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy bài viết hướng dẫn.");

            article.ViewCount++;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new PublicGuideArticleDetailDto(
                article.Slug,
                article.Title,
                article.Category,
                article.ReadTimeLabel,
                article.Summary,
                article.ContentMarkdown,
                article.SeoTitle,
                article.SeoDescription,
                article.CtaLabel,
                article.CtaTargetUrl,
                article.PublishedAt));
        });

        app.MapPost("/api/public/guides/{slug}/cta-click", async (string slug, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var normalized = slug.Trim().ToLowerInvariant();
            var article = await db.MarketingArticles
                .SingleOrDefaultAsync(x => x.Slug == normalized && x.Status == MarketingArticleStatus.Published, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy bài viết hướng dẫn.");

            article.CtaClickCount++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, ctaClickCount = article.CtaClickCount });
        });

        // --- In-App & Landing Banners (Marketer/Admin) ---
        marketerGroup.MapGet("/banners", async (AppDbContext db, CancellationToken ct) =>
        {
            var banners = await db.MarketingBanners.AsNoTracking()
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => ToBannerDto(x))
                .ToListAsync(ct);
            return Results.Ok(banners);
        });

        marketerGroup.MapPost("/banners", async (
            UpsertMarketingBannerRequest request,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            ValidateBannerRequest(request);
            var now = clock.GetUtcNow();
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);
            var resolvedVariant = !string.IsNullOrWhiteSpace(request.Variant) ? request.Variant.Trim() : (!string.IsNullOrWhiteSpace(request.ThemeColor) ? request.ThemeColor.Trim() : "teal");
            var resolvedPromoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant();
            var resolvedTargetSegment = string.IsNullOrWhiteSpace(request.TargetSegment) ? "All" : request.TargetSegment.Trim();
            var banner = new MarketingBanner
            {
                Title = request.Title.Trim(),
                Message = request.Message.Trim(),
                CtaLabel = resolvedCtaLabel,
                CtaUrl = string.IsNullOrWhiteSpace(request.CtaUrl) ? null : request.CtaUrl.Trim(),
                PromoCode = resolvedPromoCode,
                Placement = request.Placement,
                TargetSegment = resolvedTargetSegment,
                Variant = resolvedVariant,
                IsActive = request.IsActive,
                StartsAt = request.StartsAt,
                EndsAt = request.EndsAt,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.MarketingBanners.Add(banner);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/marketer/banners/{banner.Id}", ToBannerDto(banner));
        });

        marketerGroup.MapPut("/banners/{id:guid}", async (
            Guid id,
            UpsertMarketingBannerRequest request,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var banner = await db.MarketingBanners.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy banner.");

            ValidateBannerRequest(request);
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);
            var resolvedVariant = !string.IsNullOrWhiteSpace(request.Variant) ? request.Variant.Trim() : (!string.IsNullOrWhiteSpace(request.ThemeColor) ? request.ThemeColor.Trim() : "teal");
            var resolvedPromoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant();
            var resolvedTargetSegment = string.IsNullOrWhiteSpace(request.TargetSegment) ? "All" : request.TargetSegment.Trim();
            banner.Title = request.Title.Trim();
            banner.Message = request.Message.Trim();
            banner.CtaLabel = resolvedCtaLabel;
            banner.CtaUrl = string.IsNullOrWhiteSpace(request.CtaUrl) ? null : request.CtaUrl.Trim();
            banner.PromoCode = resolvedPromoCode;
            banner.Placement = request.Placement;
            banner.TargetSegment = resolvedTargetSegment;
            banner.Variant = resolvedVariant;
            banner.IsActive = request.IsActive;
            banner.StartsAt = request.StartsAt;
            banner.EndsAt = request.EndsAt;
            banner.UpdatedAt = clock.GetUtcNow();

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToBannerDto(banner));
        });

        marketerGroup.MapDelete("/banners/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var banner = await db.MarketingBanners.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy banner.");

            db.MarketingBanners.Remove(banner);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // --- Public / Active Banners ---
        app.MapGet("/api/banners/active", async (
            string? placement,
            HttpContext context,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var parsedPlacement = MarketingBannerPlacement.OwnerDashboard;
            if (!string.IsNullOrWhiteSpace(placement))
            {
                if (!Enum.TryParse<MarketingBannerPlacement>(placement.Trim(), ignoreCase: true, out parsedPlacement) ||
                    !Enum.IsDefined(parsedPlacement))
                {
                    throw new ApiException(StatusCodes.Status400BadRequest, "Vị trí hiển thị banner không hợp lệ.");
                }
            }

            var now = clock.GetUtcNow();
            var candidates = await db.MarketingBanners
                .Where(b => (b.Placement == parsedPlacement || b.Placement == MarketingBannerPlacement.Both) &&
                            b.IsActive &&
                            (!b.StartsAt.HasValue || b.StartsAt <= now) &&
                            (!b.EndsAt.HasValue || b.EndsAt >= now))
                .OrderByDescending(b => b.UpdatedAt)
                .ToListAsync(ct);

            if (parsedPlacement == MarketingBannerPlacement.OwnerDashboard &&
                context.User.Identity?.IsAuthenticated == true &&
                Guid.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
            {
                var owner = await db.Users.AsNoTracking()
                    .SingleOrDefaultAsync(u => u.Id == currentUserId && u.Role == UserRole.Owner, ct);
                if (owner is not null)
                {
                    var isEffectivePro = owner.SubscriptionTier == SubscriptionTier.Pro &&
                                         (!owner.SubscriptionExpiresAt.HasValue || owner.SubscriptionExpiresAt.Value > now);
                    var isProExpiringSoon = isEffectivePro &&
                                            owner.SubscriptionExpiresAt.HasValue &&
                                            owner.SubscriptionExpiresAt.Value <= now.AddDays(14);
                    var activeRoomCount = await (from r in db.Rooms.AsNoTracking()
                                                 join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                                                 where b.OwnerId == owner.Id && r.ArchivedAt == null && b.ArchivedAt == null
                                                 select r.Id).CountAsync(ct);

                    candidates = candidates
                        .Where(b => MatchesOwnerBannerSegment(b.TargetSegment, isEffectivePro, isProExpiringSoon, activeRoomCount))
                        .ToList();
                }
            }

            var activeBanners = candidates.Take(3).ToList();

            if (activeBanners.Count > 0)
            {
                foreach (var b in activeBanners)
                {
                    b.ImpressionCount++;
                }
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(activeBanners.Select(ToBannerDto).ToList());
        });

        app.MapPost("/api/banners/{id:guid}/click", async (
            Guid id,
            HttpContext context,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var banner = await db.MarketingBanners.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy banner.");

            banner.ClickCount++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, clickCount = banner.ClickCount });
        });
    }

    private static MarketingArticleStatus ResolveArticleStatus(UpsertMarketingArticleRequest request)
    {
        if (request.Status.HasValue) return request.Status.Value;
        if (request.IsPublished.HasValue) return request.IsPublished.Value ? MarketingArticleStatus.Published : MarketingArticleStatus.Draft;
        return MarketingArticleStatus.Published;
    }

    private static string ResolveReadTimeLabel(UpsertMarketingArticleRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ReadTimeLabel)) return request.ReadTimeLabel.Trim();
        if (request.ReadingTimeMinutes.HasValue && request.ReadingTimeMinutes.Value > 0) return $"{request.ReadingTimeMinutes.Value} phút đọc";
        return "5 phút đọc";
    }

    private static string ValidateAndNormalizeSlug(string? rawSlug)
    {
        var slug = rawSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 160 || !SlugRegex().IsMatch(slug))
            throw new ApiException(StatusCodes.Status400BadRequest, "Slug bài viết chỉ được chứa chữ thường, số và dấu gạch ngang (ví dụ: cach-quan-ly-nha-tro).");
        return slug;
    }

    private static void ValidateArticleRequest(UpsertMarketingArticleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 250)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tiêu đề bài viết phải từ 1 đến 250 ký tự.");
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 100)
            throw new ApiException(StatusCodes.Status400BadRequest, "Chuyên mục bài viết phải từ 1 đến 100 ký tự.");
        if (string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length > 600)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tóm tắt bài viết phải từ 1 đến 600 ký tự.");
        if (string.IsNullOrWhiteSpace(request.ContentMarkdown))
            throw new ApiException(StatusCodes.Status400BadRequest, "Nội dung bài viết không được để trống.");
        var status = ResolveArticleStatus(request);
        if (!Enum.IsDefined(status))
            throw new ApiException(StatusCodes.Status400BadRequest, "Trạng thái bài viết không hợp lệ.");
    }

    private static void ValidateBannerRequest(UpsertMarketingBannerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tiêu đề banner phải từ 1 đến 200 ký tự.");
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length > 600)
            throw new ApiException(StatusCodes.Status400BadRequest, "Nội dung thông điệp banner phải từ 1 đến 600 ký tự.");
        if (!Enum.IsDefined(request.Placement))
            throw new ApiException(StatusCodes.Status400BadRequest, "Vị trí hiển thị banner không hợp lệ.");
        if (request.StartsAt.HasValue && request.EndsAt.HasValue && request.StartsAt > request.EndsAt)
            throw new ApiException(StatusCodes.Status400BadRequest, "Thời gian bắt đầu không được lớn hơn thời gian kết thúc.");
    }

    private static MarketingArticleDto ToArticleDto(MarketingArticle x) => new(
        x.Id,
        x.Slug,
        x.Title,
        x.Category,
        x.ReadTimeLabel,
        x.Summary,
        x.ContentMarkdown,
        x.SeoTitle,
        x.SeoDescription,
        x.CtaLabel,
        x.CtaTargetUrl,
        x.Status,
        x.ViewCount,
        x.CtaClickCount,
        x.PublishedAt,
        x.CreatedAt,
        x.UpdatedAt);

    private static bool MatchesOwnerBannerSegment(
        string? targetSegment,
        bool isEffectivePro,
        bool isProExpiringSoon,
        int activeRoomCount)
    {
        var seg = string.IsNullOrWhiteSpace(targetSegment) ? "All" : targetSegment.Trim();
        if (seg.Equals("All", StringComparison.OrdinalIgnoreCase))
            return true;
        if (seg.Equals("FreeOnly", StringComparison.OrdinalIgnoreCase) ||
            seg.Equals("FreeOwnersOnly", StringComparison.OrdinalIgnoreCase) ||
            seg.Equals("AllFreeOwners", StringComparison.OrdinalIgnoreCase))
            return !isEffectivePro;
        if (seg.Equals("NearRoomLimit", StringComparison.OrdinalIgnoreCase) ||
            seg.Equals("FreeNearLimit", StringComparison.OrdinalIgnoreCase))
            return !isEffectivePro && activeRoomCount >= 4;
        if (seg.Equals("ProExpiringSoon", StringComparison.OrdinalIgnoreCase) ||
            seg.Equals("ExpiringProOwners", StringComparison.OrdinalIgnoreCase))
            return isProExpiringSoon;
        return true;
    }

    private static MarketingBannerDto ToBannerDto(MarketingBanner x) => new(
        x.Id,
        x.Title,
        x.Message,
        x.CtaLabel,
        x.CtaUrl,
        x.PromoCode,
        x.Placement,
        string.IsNullOrWhiteSpace(x.TargetSegment) ? "All" : x.TargetSegment,
        x.Variant,
        x.IsActive,
        x.StartsAt,
        x.EndsAt,
        x.ImpressionCount,
        x.ClickCount,
        x.CreatedAt,
        x.UpdatedAt);
}

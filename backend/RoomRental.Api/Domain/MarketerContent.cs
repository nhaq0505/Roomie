using System.Text.Json.Serialization;

namespace RoomRental.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MarketingArticleStatus
{
    Draft = 0,
    Published = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MarketingBannerPlacement
{
    LandingTop = 0,
    OwnerDashboard = 1,
    Both = 2
}

public sealed class MarketingArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ReadTimeLabel { get; set; } = "5 phút đọc";
    public string Summary { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public string SeoTitle { get; set; } = string.Empty;
    public string SeoDescription { get; set; } = string.Empty;
    public string CtaLabel { get; set; } = "Dùng thử miễn phí 5 phòng";
    public string CtaTargetUrl { get; set; } = "/register";
    public MarketingArticleStatus Status { get; set; } = MarketingArticleStatus.Draft;
    public int ViewCount { get; set; }
    public int CtaClickCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketingBanner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public string? PromoCode { get; set; }
    public MarketingBannerPlacement Placement { get; set; } = MarketingBannerPlacement.OwnerDashboard;
    public string TargetSegment { get; set; } = "All";
    public string Variant { get; set; } = "promo";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int ImpressionCount { get; set; }
    public int ClickCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record MarketingArticleDto(
    Guid Id,
    string Slug,
    string Title,
    string Category,
    string ReadTimeLabel,
    string Summary,
    string ContentMarkdown,
    string SeoTitle,
    string SeoDescription,
    string CtaLabel,
    string CtaTargetUrl,
    MarketingArticleStatus Status,
    int ViewCount,
    int CtaClickCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public int ReadingTimeMinutes => int.TryParse(new string(ReadTimeLabel.Where(char.IsDigit).ToArray()), out var m) && m > 0 ? m : 5;
    public string CtaText => CtaLabel;
    public string CtaUrl => CtaTargetUrl;
    public bool IsPublished => Status == MarketingArticleStatus.Published;
    public string AuthorName => "Roomie Marketing";
}

public sealed record UpsertMarketingArticleRequest(
    string Slug,
    string Title,
    string Category,
    string? ReadTimeLabel,
    string Summary,
    string ContentMarkdown,
    string? SeoTitle,
    string? SeoDescription,
    string? CtaLabel,
    string? CtaTargetUrl,
    MarketingArticleStatus? Status = null,
    int? ReadingTimeMinutes = null,
    string? CtaText = null,
    string? CtaUrl = null,
    bool? IsPublished = null);

public sealed record PublicGuideArticleSummaryDto(
    string Slug,
    string Title,
    string Category,
    string ReadTimeLabel,
    string Summary,
    DateTimeOffset? PublishedAt);

public sealed record PublicGuideArticleDetailDto(
    string Slug,
    string Title,
    string Category,
    string ReadTimeLabel,
    string Summary,
    string ContentMarkdown,
    string SeoTitle,
    string SeoDescription,
    string CtaLabel,
    string CtaTargetUrl,
    DateTimeOffset? PublishedAt)
{
    public int ReadingTimeMinutes => int.TryParse(new string(ReadTimeLabel.Where(char.IsDigit).ToArray()), out var m) && m > 0 ? m : 5;
    public string CtaText => CtaLabel;
    public string CtaUrl => CtaTargetUrl;
    public string AuthorName => "Roomie Marketing";
}

public sealed record MarketingBannerDto(
    Guid Id,
    string Title,
    string Message,
    string? CtaLabel,
    string? CtaUrl,
    string? PromoCode,
    MarketingBannerPlacement Placement,
    string TargetSegment,
    string Variant,
    bool IsActive,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int ImpressionCount,
    int ClickCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string? CtaText => CtaLabel;
    public string ThemeColor => Variant;
    public int ImpressionsCount => ImpressionCount;
    public int ClicksCount => ClickCount;
}

public sealed record UpsertMarketingBannerRequest(
    string Title,
    string Message,
    string? CtaLabel,
    string? CtaUrl,
    MarketingBannerPlacement Placement,
    string? Variant,
    bool IsActive,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? CtaText = null,
    string? PromoCode = null,
    string? TargetSegment = null,
    string? ThemeColor = null);

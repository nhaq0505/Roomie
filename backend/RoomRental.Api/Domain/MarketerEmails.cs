using System.Text.Json.Serialization;

namespace RoomRental.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MarketingEmailCampaignStatus
{
    Draft = 0,
    Completed = 1,
    Sending = 2,
    Failed = 3
}

public sealed class MarketingEmailCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string TargetSegment { get; set; } = "StuckOnboarding";
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string? PromoCode { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public MarketingEmailCampaignStatus Status { get; set; } = MarketingEmailCampaignStatus.Draft;
    public int TotalRecipients { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketingEmailRecipientLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public Guid OwnerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = "Sent";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public MarketingEmailCampaign? Campaign { get; set; }
    public AppUser? Owner { get; set; }
}

public sealed record MarketingEmailCampaignDto(
    Guid Id,
    string Name,
    string TargetSegment,
    string Subject,
    string BodyHtml,
    string? PromoCode,
    string? CtaLabel,
    string? CtaUrl,
    MarketingEmailCampaignStatus Status,
    int TotalRecipients,
    int SentCount,
    int FailedCount,
    string CreatedByName,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public Guid CampaignId => Id;
    public string Segment => TargetSegment;
    public string? CtaText => CtaLabel;
    public int RecipientCount => TotalRecipients;
}

public sealed record UpsertMarketingEmailCampaignRequest(
    string Name,
    string? TargetSegment,
    string Subject,
    string BodyHtml,
    string? PromoCode,
    string? CtaLabel,
    string? CtaUrl,
    string? Segment = null,
    string? CtaText = null);

public sealed record SendMarketingTestEmailRequest(string? TestEmail = null, string? ToEmail = null);

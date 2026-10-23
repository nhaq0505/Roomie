using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomRental.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OwnerLeadStatus
{
    New = 0,
    Contacted = 1,
    Onboarded = 2,
    Nurture = 3,
    Churned = 4,
    Interested = 5,
    Converted = 6
}

public sealed class OwnerMarketingProfile
{
    public Guid OwnerId { get; set; }
    public OwnerLeadStatus LeadStatus { get; set; } = OwnerLeadStatus.New;
    public string Tags { get; set; } = string.Empty;
    public DateTimeOffset? NextFollowUpAt { get; set; }
    public DateTimeOffset? LastContactedAt { get; set; }
    public Guid? AssignedMarketerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? Owner { get; set; }
    public AppUser? AssignedMarketer { get; set; }
}

public sealed class OwnerMarketingNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? Owner { get; set; }
    public AppUser? AuthorUser { get; set; }
}

public sealed record MarketerLeadSummaryCountsDto(
    int TotalOwners,
    int StuckOnboardingCount,
    int FreeNearLimitCount,
    int ProExpiringSoonCount,
    int InactiveOwnersCount,
    int ActiveProCount)
{
    public int All => TotalOwners;
    public int StuckOnboarding => StuckOnboardingCount;
    public int FreeNearLimit => FreeNearLimitCount;
    public int ProExpiringSoon => ProExpiringSoonCount;
    public int InactiveOwners => InactiveOwnersCount;
    public int ActivePro => ActiveProCount;
}

public sealed record MarketerLeadItemDto(
    Guid OwnerId,
    string Name,
    string Email,
    string? Phone,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerStatus Status,
    DateTimeOffset CreatedAt,
    int BuildingCount,
    int RoomCount,
    int MaxRooms,
    bool IsActivated,
    DateTimeOffset? ActivatedAt,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SubscriptionTier SubscriptionTier,
    bool IsPro,
    DateTimeOffset? SubscriptionExpiresAt,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    IReadOnlyList<string> RedeemedPromoCodes,
    OwnerLeadStatus LeadStatus,
    string? Tags,
    DateTimeOffset? NextFollowUpAt,
    DateTimeOffset? LastContactedAt,
    string? AssignedMarketerName,
    int NoteCount,
    DateTimeOffset? LastActiveAt = null)
{
    public int NotesCount => NoteCount;
}

public sealed record MarketerLeadsResponse(
    MarketerLeadSummaryCountsDto Summary,
    IReadOnlyList<MarketerLeadItemDto> Items)
{
    public int Total => Items.Count;
    public int Page => 1;
    public int PageSize => Math.Max(20, Items.Count);
    public MarketerLeadSummaryCountsDto SegmentCounts => Summary;
}

public sealed record MarketerLeadNoteDto(
    Guid Id,
    Guid OwnerId,
    Guid AuthorUserId,
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt)
{
    public Guid AuthorId => AuthorUserId;
}

public sealed record MarketerLeadDetailDto(
    MarketerLeadItemDto Lead,
    IReadOnlyList<MarketerLeadNoteDto> Notes);

public sealed record UpdateMarketerLeadProfileRequest(
    OwnerLeadStatus LeadStatus,
    JsonElement? Tags,
    DateTimeOffset? NextFollowUpAt,
    bool MarkContactedNow = false);

public sealed record CreateMarketerLeadNoteRequest(string Content);

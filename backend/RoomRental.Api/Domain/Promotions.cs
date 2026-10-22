namespace RoomRental.Api.Domain;

public sealed class SystemPromotion
{
    public string Id { get; set; } = "welcome";
    public string Name { get; set; } = "Tặng Pro người đăng ký mới";
    public bool IsEnabled { get; set; } = true;
    public int RewardMonths { get; set; } = 3;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromotionCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public int RewardMonths { get; set; } = 1;
    public int? MaxRedemptions { get; set; }
    public int RedeemedCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromotionRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromotionCodeId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset RedeemedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record WelcomePromotionDto(
    string Id,
    string Name,
    bool IsEnabled,
    int RewardMonths,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt
);

public sealed record WelcomePromotionUpdateRequest(
    bool IsEnabled,
    int RewardMonths,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt
);

public sealed record PromotionCodeDto(
    Guid Id,
    string Code,
    string? Description,
    int RewardMonths,
    int? MaxRedemptions,
    int RedeemedCount,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt
);

public sealed record PromotionCodeCreateRequest(
    string Code,
    string? Description,
    int RewardMonths,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    bool IsActive = true
);

public sealed record PromotionCodeUpdateRequest(
    string? Description,
    int RewardMonths,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    bool IsActive
);

public sealed record AdminPromotionsOverviewDto(
    WelcomePromotionDto Welcome,
    IReadOnlyList<PromotionCodeDto> Codes
);

public sealed record RedeemPromotionCodeRequest(
    string Code
);

public sealed record RedeemPromotionResultDto(
    bool Success,
    string Message,
    string SubscriptionTier,
    DateTimeOffset? SubscriptionExpiresAt,
    int AddedMonths
);

namespace RoomRental.Api.Domain;

public sealed record UserDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    string Status,
    string SubscriptionTier = "Free",
    DateTimeOffset? SubscriptionExpiresAt = null,
    int MaxRooms = 5,
    bool MfaEnabled = false,
    bool MfaRequired = false,
    string? Phone = null,
    string? CitizenId = null,
    DateOnly? DateOfBirth = null,
    DateOnly? CitizenIdIssuedDate = null,
    string? CitizenIdIssuedPlace = null,
    string? PermanentAddress = null);
public sealed record UpdateProfileRequest(
    string Name,
    string? Phone = null,
    string? CitizenId = null,
    DateOnly? DateOfBirth = null,
    DateOnly? CitizenIdIssuedDate = null,
    string? CitizenIdIssuedPlace = null,
    string? PermanentAddress = null);
public sealed record OwnerAdminDto(Guid Id, string Name, string Email, string? Phone, string Status, string? Reason, DateTimeOffset CreatedAt, string SubscriptionTier = "Free", DateTimeOffset? SubscriptionExpiresAt = null, bool IsPro = false, int RoomCount = 0, int MaxRooms = 5);
public sealed record MarketerAdminDto(Guid Id, string Name, string Email, string? Phone, string Status, string? Reason, DateTimeOffset CreatedAt);
public sealed record CreateMarketerRequest(string Name, string Email, string Password, string? Phone = null);
public sealed record AdminSubscriptionRequest(string Tier, int? Days = null, DateTimeOffset? ExpiresAt = null, string? Reason = null);
public sealed record SubscriptionPackageDto(string Id, string Name, int Days, decimal Price, string Description);
public sealed record SubscriptionStatusDto(string Tier, bool IsPro, DateTimeOffset? ExpiresAt, int RoomCount, int MaxRooms, IReadOnlyList<SubscriptionPackageDto> AvailablePackages);
public sealed record CreateSubscriptionCheckoutRequest(string? PackageId = null);
public sealed record RegisterRequest(string Name, string Email, string Password, string? Phone, RegistrationAttributionDto? Attribution = null, string? PromoCode = null);
public sealed record RegistrationVerificationDto(bool VerificationRequired, string ChallengeToken, string Email, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);
public sealed record VerifyRegistrationRequest(string ChallengeToken, string Code);
public sealed record ResendRegistrationRequest(string ChallengeToken);
// Code is accepted only for compatibility with older clients and is ignored.
public sealed record LoginRequest(string Email, string Password, string? Code = null, bool RememberMe = false);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? ConfirmPassword = null);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword, string ConfirmPassword);
public sealed record AdminResetPasswordRequest(string CurrentPassword);
public sealed record OccupancyUpdateRequest(
    Guid OccupancyId,
    string TenantName,
    string? Phone,
    int People,
    string? TenantCitizenId = null,
    IReadOnlyList<CoResidentDto>? CoResidents = null,
    DateOnly? TenantDateOfBirth = null,
    string? TenantGender = null,
    DateOnly? TenantCitizenIdIssuedDate = null,
    string? TenantCitizenIdIssuedPlace = null,
    string? TenantPermanentAddress = null,
    string? TenantEmail = null,
    DateOnly? ContractEndDate = null,
    int? PaymentDueDay = null,
    decimal? RoomAreaSqm = null,
    string? HandoverAssets = null,
    string? AdditionalTerms = null,
    string? HouseholdHeadRelation = null,
    decimal? InitialReading = null,
    decimal? InitialWaterReading = null);
public sealed record OwnerStatusRequest(string Status, string? Reason);
public sealed record BuildingRequest(string Name, string? Address);
public sealed record BuildingDto(Guid Id, string Name, string? Address);
public sealed record RoomCreateRequest(Guid BuildingId, string Name, decimal Rent, decimal ElectricityRate, decimal WaterFee, bool WaterPerPerson, decimal ServiceFee, decimal InitialReading = 0, bool WaterByMeter = false, decimal InitialWaterReading = 0);
public sealed record RoomUpdateRequest(string Name, decimal Rent, decimal ElectricityRate, decimal WaterFee, bool WaterPerPerson, decimal ServiceFee, bool WaterByMeter = false, decimal? InitialWaterReading = null);
public sealed record OccupancyRequest(
    string TenantName,
    string? Phone,
    int People,
    DateOnly StartDate,
    decimal DepositAmount = 0,
    decimal? InitialReading = null,
    string? TenantCitizenId = null,
    IReadOnlyList<CoResidentDto>? CoResidents = null,
    DateOnly? TenantDateOfBirth = null,
    string? TenantGender = null,
    DateOnly? TenantCitizenIdIssuedDate = null,
    string? TenantCitizenIdIssuedPlace = null,
    string? TenantPermanentAddress = null,
    string? TenantEmail = null,
    DateOnly? ContractEndDate = null,
    int? PaymentDueDay = null,
    decimal? RoomAreaSqm = null,
    string? HandoverAssets = null,
    string? AdditionalTerms = null,
    string? HouseholdHeadRelation = null,
    decimal? InitialWaterReading = null);
public sealed record OccupancyDto(
    Guid Id,
    string TenantName,
    string? Phone,
    int People,
    DateOnly StartDate,
    string? Token,
    string? Url,
    Guid? DepositInvoiceId,
    decimal DepositRequired,
    decimal DepositPaid,
    decimal DepositHeld,
    string? TenantCitizenId = null,
    IReadOnlyList<CoResidentDto>? CoResidents = null,
    DateOnly? TenantDateOfBirth = null,
    string? TenantGender = null,
    DateOnly? TenantCitizenIdIssuedDate = null,
    string? TenantCitizenIdIssuedPlace = null,
    string? TenantPermanentAddress = null,
    string? TenantEmail = null,
    DateOnly? ContractEndDate = null,
    int PaymentDueDay = 5,
    decimal? RoomAreaSqm = null,
    string? HandoverAssets = null,
    string? AdditionalTerms = null,
    string? HouseholdHeadRelation = null,
    DateTimeOffset? TenantSelfCheckinAt = null,
    bool HasTenantIdentityPhoto = false,
    string? TenantIdentityPhotoFileName = null);
public sealed record RoomDto(Guid Id, Guid BuildingId, string BuildingName, string Name, decimal Rent, decimal ElectricityRate, decimal WaterFee, bool WaterPerPerson, decimal ServiceFee, decimal InitialReading, decimal PreviousReading, OccupancyDto? Occupancy, bool WaterByMeter = false, decimal InitialWaterReading = 0, decimal PreviousWaterReading = 0);
public sealed record RotateLinkDto(string Token, string Url);
public sealed record ReadingRequest(Guid RoomId, string Period, decimal CurrentReading, decimal? CurrentWaterReading = null);
public sealed record ReadingDto(Guid Id, Guid RoomId, Guid OccupancyId, string RoomName, string BuildingName, string Period, decimal PreviousReading, decimal CurrentReading, decimal Consumption, DateTimeOffset RecordedAt, InvoiceDto? Invoice = null, string LifecycleState = "Issued", bool WaterByMeter = false, decimal? PreviousWaterReading = null, decimal? CurrentWaterReading = null, decimal? WaterConsumption = null);
public sealed record ReadingPreviewDto(Guid RoomId, Guid OccupancyId, string Period, DateOnly BillingStartDate, DateOnly BillingEndDate,
    int BillableDays, int MonthDays, decimal SuggestedRent, decimal PreviousReading, decimal CurrentReading, decimal Consumption,
    decimal ElectricityRate, decimal SuggestedElectricity, decimal WaterFee, bool WaterPerPerson, decimal SuggestedWater,
    decimal ServiceFee, decimal SuggestedService, decimal SuggestedTotal, bool WaterByMeter = false, decimal? PreviousWaterReading = null, decimal? CurrentWaterReading = null, decimal? WaterConsumption = null);
public sealed record GenerateInvoiceRequest(string Period);
public sealed record InvoiceDto(Guid Id, Guid RoomId, string RoomName, string BuildingName, string Period, string TenantName, decimal PreviousReading, decimal CurrentReading, decimal Consumption, decimal Rent, decimal ElectricityAmount, decimal WaterAmount, decimal ServiceAmount, decimal Total, decimal Paid, decimal Outstanding, string Status, DateOnly DueDate, DateTimeOffset IssuedAt, string Kind, Guid OccupancyId, decimal DepositHeld, IReadOnlyList<DepositSettlement> DepositSettlements, string? Description, bool IsFinalMeterReading,
    string LifecycleState, long Version, Guid BuildingId, decimal? MonthlyRentRate, decimal? ElectricityRate, decimal? WaterRate, bool? WaterPerPerson, decimal? ServiceRate,
    DateOnly? BillingStartDate, DateOnly? BillingEndDate, int? BillableDays, int? MonthDays,
    bool HasPendingPayment = false, bool CanCorrectMeter = false, string? MeterCorrectionBlockedReason = null, string? TenantToken = null, bool HasMonthlyOverlap = false,
    bool? WaterByMeter = null, decimal? PreviousWaterReading = null, decimal? CurrentWaterReading = null, decimal? WaterConsumption = null);
public sealed record ReceiptRequest(decimal Amount, string? Note, string? IdempotencyKey, long? ExpectedVersion = null);
public sealed record PublishInvoiceRequest(Guid RequestId, long ExpectedVersion, decimal Rent, decimal ElectricityAmount, decimal WaterAmount, decimal ServiceAmount, string? Reason = null);
public sealed record InvoiceCorrectionRequest(Guid RequestId, long ExpectedVersion, string Reason, decimal? Rent = null, decimal? ElectricityAmount = null, decimal? WaterAmount = null, decimal? ServiceAmount = null, decimal? CurrentReading = null, DateOnly? DueDate = null, string? Description = null, decimal? Amount = null, decimal? CurrentWaterReading = null);
public sealed record InvoiceCorrectionDto(Guid Id, int Revision, Guid RequestId, string Reason, Guid ActorId, string ActorName, DateTimeOffset CreatedAt, long Version, string BeforeJson, string AfterJson);
public sealed record PaymentSettingsRequest(string ClientId, string ApiKey, string ChecksumKey);
public sealed record PaymentSettingsDto(bool Configured, string? ClientId, string? WebhookUrl,
    string WebhookStatus, string? WebhookVerifiedAt, string? WebhookError)
{
    public string ConfirmationMode { get; init; } = "Manual";
    public ManualBankDto? ManualBank { get; init; }
}
public sealed record PaymentCheckoutDto(string CheckoutUrl, string? QrCode, int Amount, long OrderCode,
    string? AccountName, string? AccountNumber, string? BankBin, string? Description, string Status, long? ExpiresAt);

public sealed record DepositSettlementRequest(decimal Amount, string Kind, string Reason, string IdempotencyKey, Guid? TargetInvoiceId, long? ExpectedVersion = null, long? TargetExpectedVersion = null);

public sealed record SupplementalInvoiceRequest(Guid RequestId, Guid OccupancyId, string Description, decimal Amount, DateOnly DueDate);

public sealed record CompleteMoveoutRequest(Guid OccupancyId, Guid RequestId, decimal RefundAmount,
    decimal ExpectedHeld, long? ExpectedDepositVersion);

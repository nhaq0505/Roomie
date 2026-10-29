namespace RoomRental.Api.Domain;

public enum PaymentConfirmationMode { Manual, Automatic }

public enum UserRole { Admin, Owner, Marketer }
public enum OwnerStatus { Pending, Approved, Rejected, Suspended }
public enum SubscriptionTier { Free, Pro }
public enum InvoiceStatus { Unpaid, Partial, Paid }
public enum InvoiceLifecycleState { Draft, Issued }
public enum PaymentAttemptStatus { Pending, Paid, Failed }

public sealed class AppUser
{
    public const int FreeTierMaxRooms = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? CitizenId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? CitizenIdIssuedDate { get; set; }
    public string? CitizenIdIssuedPlace { get; set; }
    public string? PermanentAddress { get; set; }
    public PaymentConfirmationMode PaymentConfirmationMode { get; set; } = PaymentConfirmationMode.Manual;
    public string? ManualBankBin { get; set; }
    public string? ManualAccountNumber { get; set; }
    public string? ManualAccountName { get; set; }
    public required string PasswordHash { get; set; }
    // Legacy MFA storage retained to preserve the deployed schema; authentication ignores it.
    public string? ProtectedMfaSecret { get; set; }
    public string? PendingMfaSecret { get; set; }
    public DateTimeOffset? PendingMfaExpiresAt { get; set; }
    public bool MfaEnabled { get; set; }
    public long MfaLastStep { get; set; } = -1;
    public string? MfaRecoveryHashes { get; set; }
    public string? PasswordResetHash { get; set; }
    public DateTimeOffset? PasswordResetExpiresAt { get; set; }
    public UserRole Role { get; set; } = UserRole.Owner;
    public OwnerStatus Status { get; set; } = OwnerStatus.Approved;
    public string? StatusReason { get; set; }
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsPro => SubscriptionTier == SubscriptionTier.Pro && (SubscriptionExpiresAt == null || SubscriptionExpiresAt > DateTimeOffset.UtcNow);
    public int MaxRooms => IsPro ? int.MaxValue : FreeTierMaxRooms;
}

// No login-capable user exists until this email challenge has been consumed.
public sealed class RegistrationVerification
{
    public required string Email { get; set; }
    public string ChallengeHash { get; set; } = "";
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string? PendingPromoCode { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CodeExpiresAt { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
    public DateTimeOffset WindowStartedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int SendCount { get; set; }
    public int FailedAttempts { get; set; }
}

public sealed class AuthSession
{
    public required string Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class Building
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? PoliceStationName { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedBy { get; set; }
    public Guid? ArchiveBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BuildingId { get; set; }
    public required string Name { get; set; }
    public decimal Rent { get; set; }
    public decimal ElectricityRate { get; set; }
    public decimal WaterFee { get; set; }
    public bool WaterPerPerson { get; set; }
    public bool WaterByMeter { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal InitialReading { get; set; }
    public decimal InitialWaterReading { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedBy { get; set; }
    public Guid? ArchiveBatchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Occupancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public required string TenantName { get; set; }
    public string? Phone { get; set; }
    public int People { get; set; } = 1;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? TenantCitizenId { get; set; }
    public DateOnly? TenantDateOfBirth { get; set; }
    public string? TenantGender { get; set; }
    public DateOnly? TenantCitizenIdIssuedDate { get; set; }
    public string? TenantCitizenIdIssuedPlace { get; set; }
    public string? TenantPermanentAddress { get; set; }
    public string? TenantEmail { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public int PaymentDueDay { get; set; } = 5;
    public decimal? RoomAreaSqm { get; set; }
    public string? HandoverAssets { get; set; }
    public string? AdditionalTerms { get; set; }
    public string? HouseholdHeadRelation { get; set; }
    public string? CoResidentsJson { get; set; }
    public bool IsActive { get; set; } = true;
    public string? TenantTokenHash { get; set; }
    public string? ProtectedTenantToken { get; set; }
    public DateTimeOffset? TenantSelfCheckinAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MonthlyReading
{
    public int? OccupantsCount { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public Guid OccupancyId { get; set; }
    public required string Period { get; set; }
    public decimal PreviousReading { get; set; }
    public decimal CurrentReading { get; set; }
    public decimal Consumption { get; set; }
    public decimal? PreviousWaterReading { get; set; }
    public decimal? CurrentWaterReading { get; set; }
    public decimal? WaterConsumption { get; set; }
    public decimal? MonthlyRentRate { get; set; }
    public decimal? ElectricityRate { get; set; }
    public decimal? WaterRate { get; set; }
    public bool? WaterPerPerson { get; set; }
    public bool? WaterByMeter { get; set; }
    public decimal? ServiceRate { get; set; }
    public DateOnly? BillingStartDate { get; set; }
    public DateOnly? BillingEndDate { get; set; }
    public int? BillableDays { get; set; }
    public int? MonthDays { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DepositSettlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public required string Kind { get; set; }
    public required string Reason { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid? TargetInvoiceId { get; set; }
    public required string RecordedAt { get; set; }
}

public sealed class MoveoutCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid OwnerId { get; set; }
    public Guid RoomId { get; set; }
    public Guid OccupancyId { get; set; }
    public Guid ActorId { get; set; }
    public required string RequestFingerprint { get; set; }
    public decimal HeldBeforeSettlement { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal RetainedAmount { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Invoice
{
    public string Kind { get; set; } = "Monthly";
    public string? Description { get; set; }
    public bool IsFinalMeterReading { get; set; }
    public InvoiceLifecycleState LifecycleState { get; set; } = InvoiceLifecycleState.Issued;
    public long Version { get; set; } = 1;
    public decimal? MonthlyRentRate { get; set; }
    public decimal? ElectricityRate { get; set; }
    public decimal? WaterRate { get; set; }
    public bool? WaterPerPerson { get; set; }
    public bool? WaterByMeter { get; set; }
    public decimal? ServiceRate { get; set; }
    public DateOnly? BillingStartDate { get; set; }
    public DateOnly? BillingEndDate { get; set; }
    public int? BillableDays { get; set; }
    public int? MonthDays { get; set; }
    public List<DepositSettlement> DepositSettlements { get; set; } = [];

    public Guid? PaymentReservationId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid RoomId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid OccupancyId { get; set; }
    public required string RoomName { get; set; }
    public required string BuildingName { get; set; }
    public required string Period { get; set; }
    public required string TenantName { get; set; }
    public decimal PreviousReading { get; set; }
    public decimal CurrentReading { get; set; }
    public decimal Consumption { get; set; }
    public decimal? PreviousWaterReading { get; set; }
    public decimal? CurrentWaterReading { get; set; }
    public decimal? WaterConsumption { get; set; }
    public decimal Rent { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ServiceAmount { get; set; }
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InvoiceRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public int Revision { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestFingerprint { get; set; }
    public required string Reason { get; set; }
    public Guid ActorId { get; set; }
    public required string ActorName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; }
    public required string BeforeJson { get; set; }
    public required string AfterJson { get; set; }
}

public sealed class PaymentReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }
    public required string Source { get; set; }
    public string? ExternalReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PaymentWebhookRegistration
{
    public Guid OwnerId { get; set; }
    public required string SettingsVersion { get; set; }
    public required string WebhookUrl { get; set; }
    public required string Status { get; set; }
    public string? VerifiedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class OwnerPaymentSettings
{
    public Guid OwnerId { get; set; }
    public required string ClientId { get; set; }
    public required string ProtectedApiKey { get; set; }
    public required string ProtectedChecksumKey { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PayOsAttempt
{
    public string? InvoiceAllocationsJson { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid InvoiceId { get; set; }
    public string Kind { get; set; } = "Invoice";
    public int? SubscriptionDays { get; set; }
    public long OrderCode { get; set; }
    public int Amount { get; set; }
    public string? PaymentLinkId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? QrCode { get; set; }
    public string? AccountName { get; set; }
    public string? AccountNumber { get; set; }
    public string? BankBin { get; set; }
    public string? Description { get; set; }
    public string? ProviderStatus { get; set; }
    public long? ExpiresAt { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
}

public sealed class ProcessedWebhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string Reference { get; set; }
    public long OrderCode { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LoginThrottle
{
    public required string Key { get; set; }
    public DateTimeOffset WindowStartedAt { get; set; }
    public int Attempts { get; set; }
}

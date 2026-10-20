namespace RoomRental.Api.Domain;

public sealed record ManualBankDto(string BankBin, string AccountNumber, string AccountName);
public sealed record PaymentPreferencesRequest(string ConfirmationMode, string? BankBin, string? AccountNumber, string? AccountName);
public sealed record InvoiceAllocation(Guid InvoiceId, decimal Amount);
public sealed record CreateManualPaymentRequest(Guid? InvoiceId);
public sealed record ConfirmManualPaymentRequest(decimal Amount);
public sealed class ManualPaymentRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid OccupancyId { get; set; }
    public required string AllocationsJson { get; set; }
    public decimal Amount { get; set; }
    public required string BankBin { get; set; }
    public required string AccountNumber { get; set; }
    public required string AccountName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(1);
    public DateTimeOffset? ConfirmedAt { get; set; }
}

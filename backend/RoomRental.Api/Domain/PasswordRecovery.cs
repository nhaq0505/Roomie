namespace RoomRental.Api.Domain;

public sealed class PasswordRecovery
{
    public required string Email { get; set; }
    public Guid? UserId { get; set; }
    public string PasswordBinding { get; set; } = "";
    public string ChallengeHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public string? ProtectedCode { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSentAt { get; set; }
    public DateTimeOffset WindowStartedAt { get; set; }
    public int SendCount { get; set; }
    public int FailedAttempts { get; set; }
    public string? ResetHash { get; set; }
    public DateTimeOffset? ResetExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public bool NotificationPending { get; set; }
}

public sealed record ForgotPasswordRequest(string Email);
public sealed record PasswordRecoveryChallengeDto(string ChallengeToken, string Email, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);
public sealed record VerifyPasswordRecoveryRequest(string ChallengeToken, string Code);
public sealed record PasswordRecoveryGrantDto(string ResetToken, DateTimeOffset ExpiresAt);
public sealed record CompletePasswordRecoveryRequest(string ResetToken, string NewPassword, string ConfirmPassword);

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class PasswordRecoveryService(AppDbContext db, IPasswordRecoveryEmailSender sender,
    IDataProtectionProvider protection, IPasswordHasher<AppUser> hasher, TimeProvider clock)
{
    private const string Invalid = "Mã khôi phục không hợp lệ hoặc đã hết hạn.";
    internal static string Binding(AppUser user) => PasswordSecurity.HashToken(user.PasswordHash + ":" + user.PasswordResetHash);
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);

    public async Task<PasswordRecoveryChallengeDto> StartAsync(string? email, CancellationToken ct)
    {
        email = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
            throw new ApiException(400, "Vui lòng nhập địa chỉ email hợp lệ.");
        sender.EnsureConfigured(); // Configuration errors are identical for all email addresses.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var now = clock.GetUtcNow();
        var row = await db.PasswordRecoveries.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (row is not null && row.LastSentAt.AddSeconds(60) > now)
            throw new ApiException(429, "Vui lòng chờ 60 giây trước khi gửi lại mã.");
        if (row is not null && row.WindowStartedAt.AddHours(1) > now && row.SendCount >= 5)
            throw new ApiException(429, "Đã đạt giới hạn gửi mã. Vui lòng thử lại sau một giờ.");
        if (row is null) { row = new PasswordRecovery { Email = email }; db.PasswordRecoveries.Add(row); }
        if (row.WindowStartedAt.AddHours(1) <= now) { row.WindowStartedAt = now; row.SendCount = 0; }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, ct);
        var token = Token();
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        row.UserId = user?.Id;
        row.PasswordBinding = user is null ? Token() : Binding(user);
        row.ChallengeHash = PasswordSecurity.HashToken(token);
        row.CodeHash = PasswordSecurity.HashToken("password-recovery:" + token + ":" + code);
        // Delivery is queued for both real and unknown accounts, keeping SMTP out of response timing.
        row.ProtectedCode = protection.CreateProtector("RoomRental.PasswordRecovery.Delivery.v1").Protect(code);
        row.ExpiresAt = now.AddMinutes(10);
        row.LastSentAt = now;
        row.SendCount++;
        row.FailedAttempts = 0;
        row.ResetHash = null;
        row.ResetExpiresAt = null;
        row.ConsumedAt = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(token, email, row.ExpiresAt, now.AddSeconds(60));
    }

    public async Task<PasswordRecoveryGrantDto> VerifyAsync(string token, string code, CancellationToken ct)
    {
        if (!ValidToken(token)) throw new ApiException(400, Invalid);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = PasswordSecurity.HashToken(token);
        var row = await db.PasswordRecoveries.SingleOrDefaultAsync(x => x.ChallengeHash == hash, ct);
        var now = clock.GetUtcNow();
        if (row is null || row.ConsumedAt is not null || row.ResetHash is not null || row.ExpiresAt <= now || row.FailedAttempts >= 5)
            throw new ApiException(400, Invalid);
        if (code is not { Length: 6 } || code.Any(x => x is < '0' or > '9') ||
            !PasswordSecurity.MatchesToken("password-recovery:" + token + ":" + code, row.CodeHash))
        {
            row.FailedAttempts++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new ApiException(400, Invalid);
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == row.UserId && x.Email == row.Email, ct);
        if (user is null || Binding(user) != row.PasswordBinding) throw new ApiException(400, Invalid);
        var grant = Token();
        row.ResetHash = PasswordSecurity.HashToken(grant);
        row.ResetExpiresAt = now.AddMinutes(10);
        row.CodeHash = "";
        row.ProtectedCode = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(grant, row.ResetExpiresAt.Value);
    }

    public async Task CompleteAsync(string token, string password, string confirmation, CancellationToken ct)
    {
        PasswordSecurity.ValidateNewPassword(password, confirmation);
        if (!ValidToken(token)) throw new ApiException(400, Invalid);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = PasswordSecurity.HashToken(token);
        var row = await db.PasswordRecoveries.SingleOrDefaultAsync(x => x.ResetHash == hash, ct);
        if (row is null || row.ConsumedAt is not null || row.ResetExpiresAt is null || row.ResetExpiresAt <= clock.GetUtcNow())
            throw new ApiException(400, Invalid);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == row.UserId && x.Email == row.Email, ct);
        if (user is null || Binding(user) != row.PasswordBinding) throw new ApiException(400, Invalid);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed)
            throw new ApiException(400, "Mật khẩu mới phải khác mật khẩu hiện tại.");
        user.PasswordHash = hasher.HashPassword(user, password);
        user.PasswordResetHash = null;
        user.PasswordResetExpiresAt = null;
        row.ConsumedAt = clock.GetUtcNow();
        row.CodeHash = "";
        row.ProtectedCode = null;
        row.ResetHash = null;
        row.ResetExpiresAt = null;
        row.NotificationPending = true;
        await PasswordSecurity.RevokeSessionsAsync(db, user.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

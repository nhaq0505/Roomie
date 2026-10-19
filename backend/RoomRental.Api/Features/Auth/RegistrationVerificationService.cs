using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class RegistrationVerificationService(AppDbContext db, IRegistrationEmailSender sender, TimeProvider clock)
{
    private const string InvalidCode = "Mã xác nhận không hợp lệ hoặc đã hết hạn.";
    private const string TooManyAttempts = "Bạn đã nhập sai mã quá nhiều lần. Vui lòng gửi lại mã.";
    private const string SendLimit = "Đã đạt giới hạn gửi mã. Vui lòng thử lại sau một giờ.";
    private const string Cooldown = "Vui lòng chờ 60 giây trước khi gửi lại mã.";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Matches(string value, string expected) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(Hash(value)), Encoding.ASCII.GetBytes(expected));

    public async Task<RegistrationVerificationDto> StartAsync(AppUser proposedUser, CancellationToken ct)
    {
        sender.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var now = clock.GetUtcNow();
        if (await db.Users.AnyAsync(x => x.Email == proposedUser.Email, ct))
            throw new ApiException(409, "Email này đã được đăng ký.");
        var pending = await db.RegistrationVerifications.SingleOrDefaultAsync(x => x.Email == proposedUser.Email, ct);
        if (pending is not null) CheckDeliveryLimit(pending, now);
        else
        {
            pending = new RegistrationVerification { Email = proposedUser.Email };
            db.RegistrationVerifications.Add(pending);
        }
        var previous = db.Entry(pending).State == EntityState.Added ? null : db.Entry(pending).CurrentValues.Clone();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        pending.ChallengeHash = Hash(token);
        pending.UserId = proposedUser.Id;
        pending.Name = proposedUser.Name;
        pending.Phone = proposedUser.Phone;
        pending.PasswordHash = proposedUser.PasswordHash;
        pending.ExpiresAt = now.AddMinutes(30);
        pending.ConsumedAt = null;
        var code = PrepareCode(pending, token, now);
        // Commit a reservation before SMTP: no database locks are held during delivery.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await DeliverAsync(pending, code, previous, ct);
        return ToDto(pending, token);
    }

    public async Task<RegistrationVerificationDto> ResendAsync(string token, CancellationToken ct)
    {
        sender.EnsureConfigured();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var pending = await FindAsync(token, ct);
        var now = clock.GetUtcNow();
        if (pending is null || pending.ConsumedAt is not null || pending.ExpiresAt <= now)
            throw new ApiException(400, InvalidCode);
        CheckDeliveryLimit(pending, now);
        var previous = db.Entry(pending).CurrentValues.Clone();
        var code = PrepareCode(pending, token, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await transaction.DisposeAsync();
        await DeliverAsync(pending, code, previous, ct);
        return ToDto(pending, token);
    }

    public async Task<AppUser> VerifyAsync(string token, string code, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var pending = await FindAsync(token, ct);
        var now = clock.GetUtcNow();
        if (pending is null || pending.ConsumedAt is not null || pending.ExpiresAt <= now || pending.CodeExpiresAt <= now)
            throw new ApiException(400, InvalidCode);
        if (pending.FailedAttempts >= 5) throw new ApiException(400, TooManyAttempts);
        if (code is null || code.Length != 6 || code.Any(x => x is < '0' or > '9') || !Matches(token + ":" + code, pending.CodeHash))
        {
            pending.FailedAttempts++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct); // Failed attempts must survive the error response.
            throw new ApiException(400, pending.FailedAttempts >= 5 ? TooManyAttempts : InvalidCode);
        }
        var user = new AppUser
        {
            Id = pending.UserId, Name = pending.Name, Email = pending.Email, Phone = pending.Phone,
            PasswordHash = pending.PasswordHash, Role = UserRole.Owner, Status = OwnerStatus.Approved,
            SubscriptionTier = SubscriptionTier.Free, CreatedAt = now
        };
        db.Users.Add(user);
        pending.ConsumedAt = now;
        pending.PasswordHash = "";
        pending.CodeHash = "";
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return user;
    }

    private async Task DeliverAsync(RegistrationVerification pending, string code,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues? previous, CancellationToken ct)
    {
        var email = pending.Email;
        var challenge = pending.ChallengeHash;
        var codeHash = pending.CodeHash;
        try { await sender.SendAsync(email, code, ct); }
        catch
        {
            // Compensate only our unchanged reservation. A concurrent verify/re-registration wins.
            db.ChangeTracker.Clear();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cleanup.Token);
            var current = await db.RegistrationVerifications.SingleOrDefaultAsync(x => x.Email == email, cleanup.Token);
            if (current is not null && current.ChallengeHash == challenge && current.CodeHash == codeHash && current.ConsumedAt is null)
            {
                if (previous is null) db.RegistrationVerifications.Remove(current);
                else db.Entry(current).CurrentValues.SetValues(previous);
                await db.SaveChangesAsync(cleanup.Token);
            }
            await transaction.CommitAsync(cleanup.Token);
            throw;
        }
    }

    private Task<RegistrationVerification?> FindAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 || token.Any(x => !Uri.IsHexDigit(x)))
            return Task.FromResult<RegistrationVerification?>(null);
        var hash = Hash(token);
        return db.RegistrationVerifications.SingleOrDefaultAsync(x => x.ChallengeHash == hash, ct);
    }

    private static void CheckDeliveryLimit(RegistrationVerification pending, DateTimeOffset now)
    {
        if (pending.LastSentAt.AddSeconds(60) > now) throw new ApiException(429, Cooldown);
        if (pending.WindowStartedAt.AddHours(1) > now && pending.SendCount >= 5) throw new ApiException(429, SendLimit);
    }

    private static string PrepareCode(RegistrationVerification pending, string token, DateTimeOffset now)
    {
        if (pending.WindowStartedAt.AddHours(1) <= now) { pending.WindowStartedAt = now; pending.SendCount = 0; }
        string code;
        do { code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture); }
        while (Hash(token + ":" + code) == pending.CodeHash);
        pending.CodeHash = Hash(token + ":" + code);
        pending.CodeExpiresAt = now.AddMinutes(10) < pending.ExpiresAt ? now.AddMinutes(10) : pending.ExpiresAt;
        pending.LastSentAt = now;
        pending.FailedAttempts = 0;
        pending.SendCount++;
        return code;
    }

    private static RegistrationVerificationDto ToDto(RegistrationVerification pending, string token) =>
        new(true, token, pending.Email, pending.CodeExpiresAt, pending.LastSentAt.AddSeconds(60));
}

using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public static class AccountLoginLimiter
{
    public static async Task ReserveAsync(AppDbContext db, string email, int limit, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var key = PasswordSecurity.HashToken(email);
        var now = DateTimeOffset.UtcNow;
        var row = await db.LoginThrottles.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) { row = new LoginThrottle { Key = key, WindowStartedAt = now }; db.LoginThrottles.Add(row); }
        if (row.WindowStartedAt.AddMinutes(1) <= now) { row.WindowStartedAt = now; row.Attempts = 0; }
        if (row.Attempts >= limit) throw new ApiException(429, "Đã thử đăng nhập quá nhiều lần. Vui lòng thử lại sau một phút.");
        row.Attempts++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

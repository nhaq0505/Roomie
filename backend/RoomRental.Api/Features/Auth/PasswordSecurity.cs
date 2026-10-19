using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public static class PasswordSecurity
{
    public static void ValidateNewPassword(string? password, string? confirmation)
    {
        if (!IsValidNewPassword(password))
            throw new ApiException(400, "Mật khẩu mới cần từ 9 đến 256 ký tự.");
        if (password != confirmation) throw new ApiException(400, "Xác nhận mật khẩu không khớp.");
    }

    public static bool IsValidNewPassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length is >= 9 and <= 256;

    public static void VerifyCurrentPassword(AppUser user, string? password, IPasswordHasher<AppUser> hasher)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256 ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            throw new ApiException(400, "Mật khẩu hiện tại không chính xác.");
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool MatchesToken(string token, string hash) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(HashToken(token)), Encoding.ASCII.GetBytes(hash));

    public static async Task RevokeSessionsAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var sessions = await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;
    }
}

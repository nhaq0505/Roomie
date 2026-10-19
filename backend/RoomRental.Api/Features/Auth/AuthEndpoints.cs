using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/auth");

        routes.MapPost("/register", async (RegisterRequest request, AppDbContext db, IPasswordHasher<AppUser> hasher, RegistrationVerificationService verification, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var name = request.Name?.Trim();
            var email = request.Email?.Trim().ToLowerInvariant();
            var phone = request.Phone?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập họ tên tối đa 160 ký tự.");
            if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !email.Contains('@') || !System.Net.Mail.MailAddress.TryCreate(email, out _))
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập địa chỉ email hợp lệ.");
            PasswordSecurity.ValidateNewPassword(request.Password, request.Password);
            if (await db.Users.AnyAsync(user => user.Email == email, cancellationToken))
                throw new ApiException(StatusCodes.Status409Conflict, "Email này đã được đăng ký.");

            var user = new AppUser
            {
                Name = name,
                Email = email,
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone[..Math.Min(phone.Length, 60)],
                PasswordHash = "",
                Role = UserRole.Owner,
                Status = OwnerStatus.Approved,
                SubscriptionTier = SubscriptionTier.Free,
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password!);
            if (configuration.GetValue("EmailVerification:Enabled", true))
                return Results.Json(await verification.StartAsync(user, cancellationToken), statusCode: 202);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created("/api/auth/me", ToDto(user));
        }).RequireRateLimiting("auth");

        routes.MapPost("/verify-registration", async (VerifyRegistrationRequest request,
            RegistrationVerificationService verification, CancellationToken ct) =>
        {
            var user = await verification.VerifyAsync(request.ChallengeToken, request.Code, ct);
            return Results.Created("/api/auth/me", ToDto(user));
        }).RequireRateLimiting("auth");

        routes.MapPost("/resend-registration", async (ResendRegistrationRequest request,
            RegistrationVerificationService verification, CancellationToken ct) =>
            Results.Ok(await verification.ResendAsync(request.ChallengeToken, ct))).RequireRateLimiting("auth");

        routes.MapPost("/login", async (LoginRequest request, HttpContext context, AppDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken cancellationToken) =>
        {
            var normalizedEmail = request.Email?.Trim().ToLowerInvariant() ?? "";
            if (normalizedEmail.Length > 320 || request.Password?.Length > 256) throw new ApiException(400, "Thông tin đăng nhập không hợp lệ.");
            await AccountLoginLimiter.ReserveAsync(db, normalizedEmail, app.Environment.IsDevelopment() ? 1000 : 10, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var email = request.Email?.Trim().ToLowerInvariant();
            var user = string.IsNullOrWhiteSpace(email)
                ? null
                : await db.Users.SingleOrDefaultAsync(row => row.Email == email, cancellationToken);
            var verificationResult = user is null || string.IsNullOrWhiteSpace(request.Password)
                ? PasswordVerificationResult.Failed
                : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
                throw new ApiException(StatusCodes.Status401Unauthorized, "Email hoặc mật khẩu không chính xác.");
            if (user is null) throw new ApiException(StatusCodes.Status401Unauthorized, "Email hoặc mật khẩu không chính xác.");
            if (user.Status is OwnerStatus.Rejected or OwnerStatus.Suspended)
                throw new ApiException(StatusCodes.Status403Forbidden, user.StatusReason ?? "Tài khoản này hiện không thể đăng nhập.");

            if (user.PasswordResetHash is not null)
                throw new ApiException(403, "Tài khoản cần đặt lại mật khẩu bằng mã do quản trị viên cấp.");
            var now = DateTimeOffset.UtcNow;
            var session = new AuthSession
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = user.Id,
                CreatedAt = now,
                ExpiresAt = now.AddHours(12)
            };
            db.AuthSessions.Add(session);
            if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = hasher.HashPassword(user, request.Password!);
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role.ToString()),
                new("sid", session.Id)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = session.ExpiresAt
            });
            return Results.Ok(ToDto(user));
        }).RequireRateLimiting("auth");

        routes.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext context,
            AppDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            await AccountLoginLimiter.ReserveAsync(db, "password-change:" + ApiSupport.UserId(context), app.Environment.IsDevelopment() ? 1000 : 10, ct);
            PasswordSecurity.ValidateNewPassword(request.NewPassword, request.ConfirmPassword ?? request.NewPassword);
            if (request.ConfirmPassword is not null && request.NewPassword != request.ConfirmPassword)
                throw new ApiException(400, "Xác nhận mật khẩu không khớp.");
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var user = await db.Users.SingleAsync(x => x.Id == ApiSupport.UserId(context), ct);
            PasswordSecurity.VerifyCurrentPassword(user, request.CurrentPassword, hasher);
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword) != PasswordVerificationResult.Failed)
                throw new ApiException(400, "Mật khẩu mới phải khác mật khẩu hiện tại.");
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.PasswordResetHash = null;
            user.PasswordResetExpiresAt = null;
            var sessionId = context.User.FindFirst("sid")?.Value;
            var now = DateTimeOffset.UtcNow;
            await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null && x.Id != sessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, (DateTimeOffset?)now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { success = true });
        }).RequireAuthorization().RequireRateLimiting("auth");

        routes.MapPost("/reset-password", async (ResetPasswordRequest request, HttpContext context,
            AppDbContext db, IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            PasswordSecurity.ValidateNewPassword(request.NewPassword, request.ConfirmPassword);
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length != 64)
                throw new ApiException(400, "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
            var email = request.Email?.Trim().ToLowerInvariant();
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email && x.Role == UserRole.Owner, ct);
            if (user?.PasswordResetHash is null || user.PasswordResetExpiresAt is null ||
                user.PasswordResetExpiresAt <= DateTimeOffset.UtcNow ||
                !PasswordSecurity.MatchesToken(request.Token, user.PasswordResetHash))
                throw new ApiException(400, "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.PasswordResetHash = null;
            user.PasswordResetExpiresAt = null;
            await PasswordSecurity.RevokeSessionsAsync(db, user.Id, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new { success = true });
        }).RequireRateLimiting("auth");

        routes.MapGet("/me", async (HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var userId = ApiSupport.UserId(context);
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Please sign in.");
            return Results.Ok(ToDto(user));
        }).RequireAuthorization();

        routes.MapPost("/logout", async (HttpContext context, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var sessionId = context.User.FindFirst("sid")?.Value;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var session = await db.AuthSessions.SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken);
                if (session is not null && session.RevokedAt is null)
                {
                    session.RevokedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { success = true });
        }).RequireAuthorization();
    }

    // Keep legacy response flags false even when old MFA data remains in storage.
    public static UserDto ToDto(AppUser user) => new(
        user.Id,
        user.Name,
        user.Email,
        user.Role.ToString(),
        user.Status.ToString(),
        user.SubscriptionTier.ToString(),
        user.SubscriptionExpiresAt,
        user.MaxRooms, false, false
    );
}

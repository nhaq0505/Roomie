using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;

namespace RoomRental.Api.Services;

public sealed class RequestSecurityMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionId = context.User.FindFirstValue("sid");
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var valid = false;
            OwnerStatus? persistedStatus = null;
            DateTimeOffset sessionCreatedAt = default;
            DateTimeOffset sessionExpiresAt = default;
            var now = (context.RequestServices?.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            var hasUserId = Guid.TryParse(userIdText, out var parsedUserId);
            if (hasUserId && !string.IsNullOrWhiteSpace(sessionId))
            {
                var userId = parsedUserId;
                var sessionUser = await (from session in db.AuthSessions.AsNoTracking()
                                         join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                                         where session.Id == sessionId && session.UserId == userId &&
                                               session.RevokedAt == null && session.ExpiresAt > now
                                         select new { user.Role, user.Status, session.CreatedAt, session.ExpiresAt }).SingleOrDefaultAsync(context.RequestAborted);
                persistedStatus = sessionUser?.Status;
                if (sessionUser is not null)
                {
                    sessionCreatedAt = sessionUser.CreatedAt;
                    sessionExpiresAt = sessionUser.ExpiresAt;
                }
                valid = sessionUser is not null && sessionUser.Status is not (OwnerStatus.Rejected or OwnerStatus.Suspended) &&
                        ((sessionUser.Role == UserRole.Admin && sessionUser.Status == OwnerStatus.Approved && context.User.IsInRole("Admin")) ||
                         (sessionUser.Role == UserRole.Marketer && sessionUser.Status == OwnerStatus.Approved && context.User.IsInRole("Marketer")) ||
                         (sessionUser.Role == UserRole.Owner && context.User.IsInRole("Owner")));
            }

            if (!valid)
            {
                if (!context.Response.HasStarted && context.RequestServices?.GetService<IAuthenticationService>() is not null)
                {
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "Phiên đăng nhập đã hết hạn hoặc không còn hiệu lực." });
                return;
            }

            if (context.User.IsInRole("Owner") && persistedStatus != OwnerStatus.Approved)
            {
                if (!context.Request.Path.StartsWithSegments("/api/auth"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { message = "Tài khoản đang chờ quản trị viên phê duyệt." });
                    return;
                }
            }

            if (MutatingMethods.Contains(context.Request.Method) &&
                !context.Request.Path.StartsWithSegments("/api/payments/webhook") &&
                !ApiSupport.IsSameOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { message = "Nguồn gửi yêu cầu không hợp lệ." });
                return;
            }

            var rmClaim = context.User.FindFirstValue("rm");
            var isRememberMe = rmClaim is not null
                ? rmClaim == "1"
                : (sessionExpiresAt - sessionCreatedAt) > TimeSpan.FromDays(1);
            var lifetime = isRememberMe ? AuthEndpoints.RememberMeSessionLifetime : AuthEndpoints.DefaultSessionLifetime;
            if ((sessionExpiresAt - now) <= TimeSpan.FromTicks(lifetime.Ticks / 2) &&
                ShouldSlideSession(context.Request.Path))
            {
                var refreshedExpiresAt = now.Add(lifetime);
                await db.AuthSessions
                    .Where(s => s.Id == sessionId && s.UserId == parsedUserId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, refreshedExpiresAt), context.RequestAborted);
                if (!context.Response.HasStarted && context.RequestServices?.GetService<IAuthenticationService>() is not null)
                {
                    await context.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        context.User,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            AllowRefresh = true,
                            ExpiresUtc = refreshedExpiresAt
                        });
                }
            }
        }

        await next(context);

        if (context.User.Identity?.IsAuthenticated == true &&
            MutatingMethods.Contains(context.Request.Method))
        {
            var userIdText = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdText, out var userId))
            {
                context.RequestServices?.GetService<IMemoryCache>()?.Remove($"dashboard:{userId}");
            }
        }
    }

    private static bool ShouldSlideSession(PathString path) =>
        !path.StartsWithSegments("/api/auth") ||
        path.StartsWithSegments("/api/auth/me");
}

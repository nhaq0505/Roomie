using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

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
            if (Guid.TryParse(userIdText, out var userId) && !string.IsNullOrWhiteSpace(sessionId))
            {
                var now = DateTimeOffset.UtcNow;
                var sessionUser = await (from session in db.AuthSessions.AsNoTracking()
                                         join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                                         where session.Id == sessionId && session.UserId == userId &&
                                               session.RevokedAt == null && session.ExpiresAt > now
                                         select new { user.Role, user.Status }).SingleOrDefaultAsync(context.RequestAborted);
                persistedStatus = sessionUser?.Status;
                valid = sessionUser is not null && sessionUser.Status is not (OwnerStatus.Rejected or OwnerStatus.Suspended) &&
                        ((sessionUser.Role == UserRole.Admin && sessionUser.Status == OwnerStatus.Approved && context.User.IsInRole("Admin")) ||
                         (sessionUser.Role == UserRole.Owner && context.User.IsInRole("Owner")));
            }

            if (!valid)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
        }

        await next(context);
    }
}

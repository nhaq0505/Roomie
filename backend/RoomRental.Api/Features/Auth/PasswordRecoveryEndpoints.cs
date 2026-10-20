using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class PasswordRecoveryEndpoints
{
    public static void MapPasswordRecoveryEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        routes.MapPost("/forgot-password", async (ForgotPasswordRequest request, PasswordRecoveryService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(await service.StartAsync(request.Email, ct), statusCode: 202);
        });
        routes.MapPost("/verify-password-recovery", async (VerifyPasswordRecoveryRequest request, PasswordRecoveryService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.VerifyAsync(request.ChallengeToken, request.Code, ct));
        });
        routes.MapPost("/complete-password-recovery", async (CompletePasswordRecoveryRequest request, PasswordRecoveryService service, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await service.CompleteAsync(request.ResetToken, request.NewPassword, request.ConfirmPassword, ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { success = true });
        });
    }
}

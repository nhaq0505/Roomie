using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class OperationalHardeningTests
{
    public static async Task RunAsync()
    {
        foreach (var enabled in new[] { false, true })
        {
            var dto = RoomRental.Api.Endpoints.AuthEndpoints.ToDto(new AppUser { Name = "Fixture", Email = "fixture@example.invalid", PasswordHash = "fixture", MfaEnabled = enabled, Role = UserRole.Admin });
            if (dto.MfaEnabled || dto.MfaRequired) throw new Exception("Legacy MFA flags must always be false");
        }
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        foreach (var enabled in new[] { false, true })
        {
            var admin = new AppUser { Name = "Fixture", Email = $"admin-{enabled}@example.invalid", PasswordHash = "fixture", Role = UserRole.Admin, MfaEnabled = enabled };
            var session = new AuthSession { Id = Guid.NewGuid().ToString("N"), UserId = admin.Id, ExpiresAt = now.AddHours(1) };
            db.Users.Add(admin); db.AuthSessions.Add(session); await db.SaveChangesAsync();
            var context = new DefaultHttpContext();
            context.Request.Method = "GET"; context.Request.Path = "/api/admin/owners";
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Role, "Admin"), new Claim("sid", session.Id)
            }, "fixture"));
            var reached = false;
            await new RequestSecurityMiddleware(_ => { reached = true; return Task.CompletedTask; }).InvokeAsync(context, db);
            if (!reached) throw new Exception("Admin business routes still gated by legacy MFA state");
        }

        db.RegistrationVerifications.AddRange(new RegistrationVerification {Email="expired@example.invalid",ChallengeHash="expired",ExpiresAt=now.AddDays(-8),PasswordHash="sensitive"},
            new RegistrationVerification {Email="active@example.invalid",ChallengeHash="active",ExpiresAt=now.AddMinutes(20)},
            new RegistrationVerification {Email="cooldown@example.invalid",ChallengeHash="cooldown",ExpiresAt=now.AddMinutes(-40),WindowStartedAt=now});
        await db.SaveChangesAsync();
        await DataMaintenanceService.CleanAsync(db,now,default);
        if (await db.RegistrationVerifications.CountAsync()!=2) throw new Exception("Maintenance retention removed active/cooldown rows");
        for(int i=0;i<10;i++) await AccountLoginLimiter.ReserveAsync(db,"account@example.invalid",10,default);
        try { await AccountLoginLimiter.ReserveAsync(db,"account@example.invalid",10,default); throw new Exception("Account limit missing"); }
        catch(ApiException e) when(e.StatusCode==429) { }
        if (ApiSupport.SafePath("/api/tenant/secret/invoices") .Contains("secret")) throw new Exception("Token leaked");
        Console.WriteLine("PASS: MFA compatibility flags; independent retention; persisted account throttling; tenant log redaction");
    }
}

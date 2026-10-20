using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;

namespace RoomRental.Api.Services;

// Persisted, encrypted delivery reservations keep SMTP timing out of the public API.
// Claim before sending: failures/restarts never send twice automatically; users can resend after cooldown.
public sealed class PasswordRecoveryEmailWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<PasswordRecoveryEmailWorker> logger) : BackgroundService
{
    public static async Task DeliverPendingAsync(AppDbContext db, IPasswordRecoveryEmailSender sender,
        IDataProtectionProvider protection, TimeProvider clock, CancellationToken ct)
    {
        var pending = await db.PasswordRecoveries.AsNoTracking().Where(x => x.ProtectedCode != null)
            .OrderBy(x => x.LastSentAt).Take(20).ToListAsync(ct);
        foreach (var row in pending)
        {
            var claimed = await db.PasswordRecoveries.Where(x => x.Email == row.Email && x.ChallengeHash == row.ChallengeHash && x.ProtectedCode == row.ProtectedCode)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProtectedCode, (string?)null), ct);
            if (claimed == 0 || row.ExpiresAt <= clock.GetUtcNow() || row.ConsumedAt is not null || row.ResetHash is not null) continue;
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.UserId && x.Email == row.Email, ct);
            if (user is null || PasswordRecoveryService.Binding(user) != row.PasswordBinding) continue;
            var code = protection.CreateProtector("RoomRental.PasswordRecovery.Delivery.v1").Unprotect(row.ProtectedCode!);
            await sender.SendRecoveryCodeAsync(row.Email, code, ct);
        }
        var notifications = await db.PasswordRecoveries.AsNoTracking().Where(x => x.NotificationPending).Take(20).ToListAsync(ct);
        foreach (var row in notifications)
        {
            var claimed = await db.PasswordRecoveries.Where(x => x.Email == row.Email && x.NotificationPending)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NotificationPending, false), ct);
            if (claimed > 0) await sender.SendPasswordChangedAsync(row.Email, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var tracker = scope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("recovery-email", isEnabled: true, status: "Running", errorMessage: null, stoppingToken);

                await DeliverPendingAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IPasswordRecoveryEmailSender>(),
                    scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), clock, stoppingToken);

                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("recovery-email", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // No recipient, challenge, OTP, SMTP response or connection string in logs.
                logger.LogWarning("Password recovery delivery failed; check email service availability.");
                try
                {
                    using var errScope = scopes.CreateScope();
                    var tracker = errScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                    if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("recovery-email", isEnabled: true, status: "Failed", errorMessage: ex.Message, stoppingToken);
                }
                catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

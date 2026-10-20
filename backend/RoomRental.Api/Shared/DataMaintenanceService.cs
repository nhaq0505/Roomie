using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;

namespace RoomRental.Api.Services;

// Independent of billing flags. Small batches bound transaction and memory use.
public sealed class DataMaintenanceService(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<DataMaintenanceService> logger) : BackgroundService
{
    public static async Task<int> CleanAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-7);
        var pending = await db.RegistrationVerifications.Where(x => x.ExpiresAt < cutoff)
            .OrderBy(x => x.ExpiresAt).Take(200).ToListAsync(ct);
        db.RegistrationVerifications.RemoveRange(pending);
        var recoveries = await db.PasswordRecoveries.Where(x => x.ExpiresAt < cutoff)
            .OrderBy(x => x.ExpiresAt).Take(200).ToListAsync(ct);
        db.PasswordRecoveries.RemoveRange(recoveries);
        var sessions = await db.AuthSessions.Where(x => x.ExpiresAt < cutoff)
            .OrderBy(x => x.ExpiresAt).Take(200).ToListAsync(ct);
        db.AuthSessions.RemoveRange(sessions);
        var throttles = await db.LoginThrottles.Where(x => x.WindowStartedAt < cutoff).Take(200).ToListAsync(ct);
        db.LoginThrottles.RemoveRange(throttles);
        await db.SaveChangesAsync(ct);
        return pending.Count + sessions.Count;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var tracker = scope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("cleanup", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);

                await CleanAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock.GetUtcNow(), stoppingToken);

                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("cleanup", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Data maintenance failed; retrying on next interval.");
                try
                {
                    using var errScope = scopes.CreateScope();
                    var tracker = errScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                    if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("cleanup", isEnabled: true, status: "Failed", errorMessage: ex.Message, stoppingToken);
                }
                catch { }
            }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

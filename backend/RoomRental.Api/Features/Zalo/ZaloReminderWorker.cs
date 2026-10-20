using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class ZaloReminderWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ZaloReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var listScope = scopes.CreateScope();
                var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owners = await db.ZaloSettings.AsNoTracking().Select(x => x.OwnerId).ToListAsync(stoppingToken);
                foreach (var owner in owners)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        var ownerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var reminders = scope.ServiceProvider.GetRequiredService<ZaloReminderService>();
                        var approved = await ownerDb.Users.AnyAsync(x => x.Id == owner && x.Role == UserRole.Owner && x.Status == OwnerStatus.Approved, stoppingToken);
                        if (!approved) continue;
                        await reminders.QueueScheduledAsync(owner, stoppingToken);
                        ownerDb.ChangeTracker.Clear();
                        await reminders.DispatchAsync(owner, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { logger.LogWarning("Zalo reminder cycle failed for owner {OwnerId} ({ErrorType}).", owner, ex.GetType().Name); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Zalo reminder worker unavailable ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

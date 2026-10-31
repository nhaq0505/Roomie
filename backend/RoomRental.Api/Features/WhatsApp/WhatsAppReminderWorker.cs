using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class WhatsAppReminderWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WhatsAppReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var listScope = scopes.CreateScope();
                var tracker = listScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("whatsapp", isEnabled: true, status: "Running", errorMessage: null, stoppingToken);

                var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Only Approved owners are eligible; filter in one query instead of a lookup per owner.
                var owners = await (from s in db.WhatsAppSettings.AsNoTracking()
                                    join u in db.Users.AsNoTracking() on s.OwnerId equals u.Id
                                    where u.Role == UserRole.Owner && u.Status == OwnerStatus.Approved
                                    select s.OwnerId).ToListAsync(stoppingToken);
                foreach (var owner in owners)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        var ownerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var reminders = scope.ServiceProvider.GetRequiredService<WhatsAppReminderService>();
                        await reminders.QueueScheduledAsync(owner, stoppingToken);
                        ownerDb.ChangeTracker.Clear();
                        await reminders.DispatchAsync(owner, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { logger.LogWarning("WhatsApp reminder cycle failed for owner {OwnerId} ({ErrorType}).", owner, ex.GetType().Name); }
                }

                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("whatsapp", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("WhatsApp reminder worker unavailable ({ErrorType}).", ex.GetType().Name);
                try
                {
                    using var errScope = scopes.CreateScope();
                    var tracker = errScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                    if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("whatsapp", isEnabled: true, status: "Failed", errorMessage: ex.GetType().Name, stoppingToken);
                }
                catch { }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

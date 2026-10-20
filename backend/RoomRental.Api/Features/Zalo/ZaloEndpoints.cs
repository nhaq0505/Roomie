using System.Data;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public sealed record ZaloSettingsRequest(bool ScheduleEnabled, int DayOfMonth, int Hour, int Minute,
    string BankBin, string BankAccount, string BankAccountName);

public static class ZaloEndpoints
{
    public static void MapZaloEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/zalo").RequireAuthorization(p => p.RequireRole("Owner"));
        routes.MapGet("/settings", async (HttpContext context, AppDbContext db, IZaloGateway gateway, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var settings = await db.ZaloSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct) ?? new OwnerZaloSettings { OwnerId = ownerId };
            return Results.Ok(new { settings.ScheduleEnabled, settings.DayOfMonth, settings.Hour, settings.Minute,
                settings.BankBin, settings.BankAccount, settings.BankAccountName, gatewayEnabled = gateway.Enabled });
        });
        routes.MapPut("/settings", async (ZaloSettingsRequest request, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            if (request.DayOfMonth is < 1 or > 31 || request.Hour is < 7 or > 20 || request.Minute is < 0 or > 59)
                throw new ApiException(400, "Choose a monthly day from 1 to 31 and a time between 07:00 and 20:59.");
            var ownerId = ApiSupport.OwnerId(context);
            var bank = new OwnerZaloSettings { BankBin = request.BankBin?.Trim() ?? "", BankAccount = request.BankAccount?.Trim() ?? "", BankAccountName = request.BankAccountName?.Trim() ?? "" };
            if (!ZaloReminderService.BankReady(bank)) throw new ApiException(400, "Enter a six-digit bank BIN, a valid account number and the account holder name.");
            var settings = await db.ZaloSettings.SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (settings is null) { settings = new() { OwnerId = ownerId }; db.ZaloSettings.Add(settings); }
            settings.ScheduleEnabled = request.ScheduleEnabled;
            settings.DayOfMonth = request.DayOfMonth;
            settings.Hour = request.Hour;
            settings.Minute = request.Minute;
            settings.BankBin = bank.BankBin;
            settings.BankAccount = bank.BankAccount;
            settings.BankAccountName = bank.BankAccountName;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        routes.MapGet("/connection", async (HttpContext context, IZaloGateway gateway, CancellationToken ct) =>
        {
            try { return Results.Ok(await gateway.ConnectionAsync(ApiSupport.OwnerId(context), "status", ct)); }
            catch (Exception ex) when (ex is HttpRequestException or ApiException or TaskCanceledException && !ct.IsCancellationRequested)
            { return Results.Ok(new ZaloConnection("Unavailable")); }
        });
        routes.MapPost("/connect", async (HttpContext context, IZaloGateway gateway, CancellationToken ct) =>
            Results.Ok(await gateway.ConnectionAsync(ApiSupport.OwnerId(context), "connect", ct))).RequireRateLimiting("tenant-payment");
        routes.MapPost("/disconnect", async (HttpContext context, AppDbContext db, IZaloGateway gateway, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            // Pause schedule and cancel queued work before disconnecting the gateway.
            await db.ZaloSettings.Where(x => x.OwnerId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ScheduleEnabled, false), ct);
            await db.ZaloReminders.Where(x => x.OwnerId == ownerId && x.Status == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Skipped").SetProperty(x => x.Detail, "DisconnectedByOwner"), ct);
            return Results.Ok(await gateway.ConnectionAsync(ownerId, "disconnect", ct));
        });
        routes.MapPost("/queue", async (HttpContext context, AppDbContext db, IZaloGateway gateway, ZaloReminderService reminders, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var settings = await db.ZaloSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
            if (settings is null || !ZaloReminderService.BankReady(settings)) throw new ApiException(409, "Save the receiving bank account before queuing reminders.");
            if ((await gateway.ConnectionAsync(ownerId, "status", ct)).Status != "Connected") throw new ApiException(409, "Connect Zalo before queuing reminders.");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // Serialize manual and scheduled enqueue for this owner with a harmless row update.
            await db.ZaloSettings.Where(x => x.OwnerId == ownerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DayOfMonth, x => x.DayOfMonth), ct);
            var queued = await reminders.QueueAsync(ownerId, ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { queued });
        }).RequireRateLimiting("tenant-payment");
        routes.MapGet("/history", async (HttpContext context, int? page, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var current = Math.Clamp(page ?? 1, 1, 100000);
            var query = db.ZaloReminders.AsNoTracking().Where(x => x.OwnerId == ownerId);
            var total = await query.CountAsync(ct);
            var rows = await (from job in query join invoice in db.Invoices on job.InvoiceId equals invoice.Id
                orderby job.QueuedAtMs descending, job.InvoiceId
                select new { job.InvoiceId, invoice.RoomName, invoice.Period, job.Status, job.Detail,
                    job.QueuedAtMs, job.LastAttemptAtMs, job.SentAtMs }).Skip((current - 1) * 20).Take(20).ToListAsync(ct);
            return Results.Ok(new { items = rows, total, page = current, pageSize = 20 });
        });
    }
}

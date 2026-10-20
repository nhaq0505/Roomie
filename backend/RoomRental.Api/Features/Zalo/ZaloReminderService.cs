using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed partial class ZaloReminderService(AppDbContext db, IZaloGateway gateway, TimeProvider clock,
    TenantTokenService tokens, PaymentWebhookService payments)
{
    public const long CooldownMs = 20 * 60 * 60 * 1000;
    public static DateTimeOffset VietnamTime(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(7));
    public static bool InSendWindow(DateTimeOffset utc) => VietnamTime(utc).Hour is >= 7 and < 21;
    public static bool ScheduleDue(OwnerZaloSettings s, DateTimeOffset utc)
    {
        var local = VietnamTime(utc);
        var day = Math.Min(s.DayOfMonth, DateTime.DaysInMonth(local.Year, local.Month));
        return s.ScheduleEnabled && s.LastScheduledPeriod != local.ToString("yyyy-MM") &&
               local >= new DateTimeOffset(local.Year, local.Month, day, s.Hour, s.Minute, 0, local.Offset);
    }
    public static string? NormalizePhone(string? input)
    {
        var phone = Regex.Replace(input ?? "", @"[\s().-]", "");
        if (phone.StartsWith("+84")) phone = "0" + phone[3..];
        else if (phone.StartsWith("84") && phone.Length == 11) phone = "0" + phone[2..];
        return PhonePattern().IsMatch(phone) ? phone : null;
    }
    [GeneratedRegex(@"^0[35789]\d{8}$")]
    private static partial Regex PhonePattern();
    private string? ValidToken(Occupancy occupancy)
    {
        var token = tokens.Unprotect(occupancy);
        return token is not null && TenantTokenService.Hash(token) == occupancy.TenantTokenHash ? token : null;
    }

    public async Task<int> QueueScheduledAsync(Guid ownerId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // Lock the same owner row used by manual enqueue before reading the monthly marker.
        await db.ZaloSettings.Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DayOfMonth, x => x.DayOfMonth), ct);
        var settings = await db.ZaloSettings.SingleAsync(x => x.OwnerId == ownerId, ct);
        var added = 0;
        if (ScheduleDue(settings, clock.GetUtcNow()) && await tokens.CanShareAsync(ownerId, payments, ct))
        {
            added = await QueueAsync(ownerId, ct);
            settings.LastScheduledPeriod = VietnamTime(clock.GetUtcNow()).ToString("yyyy-MM");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return added;
    }

    public async Task<int> QueueAsync(Guid ownerId, CancellationToken ct)
    {
        if (!await tokens.CanShareAsync(ownerId, payments, ct))
            throw new ApiException(409, "Configure payment settings before sharing tenant links.");
        // Serializable scope also encloses the scheduled-period marker in the worker.
        var candidates = await (from i in db.Invoices.AsNoTracking()
            join o in db.Occupancies.AsNoTracking() on i.OccupancyId equals o.Id
            where i.OwnerId == ownerId && i.LifecycleState == InvoiceLifecycleState.Issued && i.Total > i.Paid && o.IsActive
            select new { i.Id, Occupancy = o }).ToListAsync(ct);
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var added = 0;
        foreach (var invoice in candidates.Where(x => NormalizePhone(x.Occupancy.Phone) != null && ValidToken(x.Occupancy) != null))
        {
            var row = await db.ZaloReminders.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, ct);
            if (row is null)
            {
                db.ZaloReminders.Add(new() { InvoiceId = invoice.Id, OwnerId = ownerId, QueuedAtMs = now });
                added++;
            }
            else if (row.Status != "Pending" && row.Status != "Sending" &&
                     (row.LastAttemptAtMs == 0 || now - row.LastAttemptAtMs >= CooldownMs))
            {
                row.Status = "Pending";
                row.AttemptId = Guid.NewGuid();
                row.Detail = null;
                row.SentAtMs = null;
                row.QueuedAtMs = now;
                added++;
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task DispatchAsync(Guid ownerId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (!gateway.Enabled || !InSendWindow(clock.GetUtcNow())) return;
        // A database lease serializes sends per owner across API instances, not just in this process.
        var claimed = await db.ZaloSettings.Where(x => x.OwnerId == ownerId && x.DispatchLeaseUntilMs <= now && x.NextSendAtMs <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, now + 120_000), ct);
        if (claimed == 0) return;
        try
        {
            // An interrupted submission may have reached Zalo. Require explicit requeue after the cooldown.
            await db.ZaloReminders.Where(x => x.OwnerId == ownerId && x.Status == "Sending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Unknown").SetProperty(x => x.Detail, "DeliveryUnknown"), ct);
            if (!await tokens.CanShareAsync(ownerId, payments, ct) || !await db.Users.AnyAsync(x => x.Id == ownerId && x.Role == UserRole.Owner && x.Status == OwnerStatus.Approved, ct)) return;
            if ((await gateway.ConnectionAsync(ownerId, "status", ct)).Status != "Connected") return;
            var job = await db.ZaloReminders.Where(x => x.OwnerId == ownerId && x.Status == "Pending")
                .OrderBy(x => x.QueuedAtMs).ThenBy(x => x.InvoiceId).FirstOrDefaultAsync(ct);
            if (job is null) return;
            var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.InvoiceId && x.OwnerId == ownerId, ct);
            var occupancy = invoice is null ? null : await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invoice.OccupancyId && x.IsActive, ct);
            var phone = NormalizePhone(occupancy?.Phone);
            if (invoice is null || invoice.LifecycleState != InvoiceLifecycleState.Issued || invoice.Total <= invoice.Paid || phone is null)
            {
                job.Status = "Skipped";
                job.Detail = "InvoiceIneligible";
                await db.SaveChangesAsync(ct);
                return;
            }
            if (job.LastAttemptAtMs != 0 && now - job.LastAttemptAtMs < CooldownMs) return;
            if (!InSendWindow(clock.GetUtcNow())) return;
            var token = ValidToken(occupancy!);
            if (token is null)
            {
                job.Status = "Skipped";
                job.Detail = "TenantLinkUnavailable";
                await db.SaveChangesAsync(ct);
                return;
            }
            var link = tokens.Url(token);
            var remaining = (long)ApiSupport.RoundVnd(invoice.Total - invoice.Paid);
            var money = remaining.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
            var description = string.IsNullOrWhiteSpace(invoice.Description) ? invoice.Kind == "Deposit" ? "Tiền cọc" : "Tiền phòng và dịch vụ" : invoice.Description;
            var message = string.Create(CultureInfo.GetCultureInfo("vi-VN"), $"Roomie • Hóa đơn {invoice.Period}\n{invoice.BuildingName} – Phòng {invoice.RoomName}\nNgười thuê: {invoice.TenantName}\nNội dung hóa đơn: {description}\nTiền phòng: {invoice.Rent:N0} đ; điện: {invoice.ElectricityAmount:N0} đ; nước: {invoice.WaterAmount:N0} đ; dịch vụ: {invoice.ServiceAmount:N0} đ\nTổng: {invoice.Total:N0} đ; đã trả: {invoice.Paid:N0} đ\nCòn thanh toán: {money} đ\nHạn thanh toán: {invoice.DueDate:dd/MM/yyyy}\nXem hóa đơn: {link}\nBạn có thể trả lời chủ trọ tại đây.");
            var previousAttemptAtMs = job.LastAttemptAtMs;
            job.Status = "Sending";
            job.LastAttemptAtMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
            await db.SaveChangesAsync(ct); // Persist before external side effects.
            try
            {
                var result = await gateway.SendAsync(ownerId, new(job.AttemptId, phone, message), ct);
                job.Status = result.Status is "Sent" or "Failed" ? result.Status : result.Status is "Disconnected" or "Deferred" ? "Pending" : "Unknown";
                if (job.Status == "Pending") job.LastAttemptAtMs = previousAttemptAtMs; // Provider confirms no submission took place.
                job.Detail = job.Status == "Sent" || result.Status == "Deferred" ? null : job.Status == "Failed" ? "RecipientRejected" : result.Status == "Disconnected" ? "ReconnectRequired" : "DeliveryUnknown";
                if (job.Status == "Sent") job.SentAtMs = clock.GetUtcNow().ToUnixTimeMilliseconds();
            }
            catch (Exception) when (!ct.IsCancellationRequested) { job.Status = "Unknown"; job.Detail = "DeliveryUnknown"; }
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            // The five-second interval starts after completion, even if Zalo was slow.
            await db.ZaloSettings.Where(x => x.OwnerId == ownerId && x.DispatchLeaseUntilMs == now + 120_000)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, 0L)
                    .SetProperty(x => x.NextSendAtMs, clock.GetUtcNow().ToUnixTimeMilliseconds() + 5_000), CancellationToken.None);
        }
    }
}

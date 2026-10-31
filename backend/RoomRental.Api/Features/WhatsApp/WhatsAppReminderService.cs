using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed partial class WhatsAppReminderService(AppDbContext db, IWhatsAppGateway gateway, TimeProvider clock,
    TenantTokenService tokens, PaymentWebhookService payments)
{
    public const long CooldownMs = ZaloReminderService.CooldownMs;
    public static DateTimeOffset VietnamTime(DateTimeOffset utc) => ZaloReminderService.VietnamTime(utc);
    public static bool InSendWindow(DateTimeOffset utc) => ZaloReminderService.InSendWindow(utc);
    public static bool ScheduleDue(OwnerWhatsAppSettings s, DateTimeOffset utc)
    {
        var local = VietnamTime(utc);
        var day = Math.Min(s.DayOfMonth, DateTime.DaysInMonth(local.Year, local.Month));
        return s.ScheduleEnabled && s.LastScheduledPeriod != local.ToString("yyyy-MM") &&
               local >= new DateTimeOffset(local.Year, local.Month, day, s.Hour, s.Minute, 0, local.Offset);
    }
    // Vietnamese mobile numbers (+84 / 0...) are routed exclusively to Zalo;
    // international E.164 numbers (non-+84) default to WhatsApp.
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (ZaloReminderService.NormalizePhone(input) is not null) return null;
        var phone = Regex.Replace(input.Trim(), @"[\s().-]", "");
        if (phone.StartsWith("00")) phone = "+" + phone[2..];
        if (phone.StartsWith("+84") || phone.StartsWith("0")) return null;
        return InternationalPhonePattern().IsMatch(phone) ? phone : null;
    }
    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex InternationalPhonePattern();

    public static string BuildMessage(Invoice invoice, string link)
    {
        var description = string.IsNullOrWhiteSpace(invoice.Description)
            ? invoice.Kind == "Deposit" ? "Security deposit" : "Room rent and utilities"
            : invoice.Description;
        var culture = CultureInfo.GetCultureInfo("en-US");

        var hasBreakdown = invoice.Kind == "Monthly" ||
            invoice.Rent > 0 || invoice.ElectricityAmount > 0 || invoice.WaterAmount > 0 || invoice.ServiceAmount > 0;

        var remainingLine = invoice.Paid > 0 && invoice.Total > invoice.Paid
            ? string.Create(culture, $"\nRemaining balance: {(invoice.Total - invoice.Paid):N0} VND;")
            : "";

        if (hasBreakdown)
        {
            return string.Create(culture, $"""
                Roomie • Invoice {invoice.Period}

                {invoice.BuildingName} – Room {invoice.RoomName}
                Tenant: {invoice.TenantName}
                Description: {description}
                Rent: {invoice.Rent:N0} VND;
                Electricity: {invoice.ElectricityAmount:N0} VND; Water: {invoice.WaterAmount:N0} VND;
                Services: {invoice.ServiceAmount:N0} VND

                Total: {invoice.Total:N0} VND;{remainingLine}

                Pay here: {link}
                """);
        }

        return string.Create(culture, $"""
            Roomie • Invoice {invoice.Period}

            {invoice.BuildingName} – Room {invoice.RoomName}
            Tenant: {invoice.TenantName}
            Description: {description}

            Total: {invoice.Total:N0} VND;{remainingLine}

            Pay here: {link}
            """);
    }

    private string? ValidToken(Occupancy occupancy)
    {
        var token = tokens.Unprotect(occupancy);
        return token is not null && TenantTokenService.Hash(token) == occupancy.TenantTokenHash ? token : null;
    }

    // Called only on creation/publication; caller saves queue and invoice in the same transaction.
    // Never sweep old invoices or requeue an existing reminder automatically.
    public async Task StageIssuedAsync(Invoice invoice, CancellationToken ct)
    {
        if (!gateway.Enabled || invoice.LifecycleState != InvoiceLifecycleState.Issued || invoice.Total <= invoice.Paid) return;
        var occupancy = db.Occupancies.Local.SingleOrDefault(x => x.Id == invoice.OccupancyId)
            ?? await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invoice.OccupancyId, ct);
        if (occupancy is null || !occupancy.IsActive || NormalizePhone(occupancy.Phone) is null || ValidToken(occupancy) is null) return;
        if (!await db.Users.AnyAsync(x => x.Id == invoice.OwnerId && x.Role == UserRole.Owner && x.Status == OwnerStatus.Approved, ct) ||
            !await tokens.CanShareAsync(invoice.OwnerId, payments, ct)) return;
        if (db.WhatsAppReminders.Local.Any(x => x.InvoiceId == invoice.Id) ||
            await db.WhatsAppReminders.AnyAsync(x => x.InvoiceId == invoice.Id, ct)) return;
        if (!db.WhatsAppSettings.Local.Any(x => x.OwnerId == invoice.OwnerId) &&
            !await db.WhatsAppSettings.AnyAsync(x => x.OwnerId == invoice.OwnerId, ct))
            db.WhatsAppSettings.Add(new() { OwnerId = invoice.OwnerId });
        db.WhatsAppReminders.Add(new() { InvoiceId = invoice.Id, OwnerId = invoice.OwnerId,
            QueuedAtMs = clock.GetUtcNow().ToUnixTimeMilliseconds() });
    }

    public async Task<int> QueueScheduledAsync(Guid ownerId, CancellationToken ct)
    {
        // Cheap unlocked pre-check: the worker calls this every few seconds per owner, and
        // opening a Serializable transaction + row lock (a write) when nothing is due creates
        // constant WAL/dead tuples. The locked path below re-checks before queuing.
        var snapshot = await db.WhatsAppSettings.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerId == ownerId, ct);
        if (snapshot is null || !ScheduleDue(snapshot, clock.GetUtcNow())) return 0;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        // Lock the same owner row used by manual enqueue before reading the monthly marker.
        await db.WhatsAppSettings.Where(x => x.OwnerId == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DayOfMonth, x => x.DayOfMonth), ct);
        var settings = await db.WhatsAppSettings.SingleAsync(x => x.OwnerId == ownerId, ct);
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
        if (!db.WhatsAppSettings.Local.Any(x => x.OwnerId == ownerId) &&
            !await db.WhatsAppSettings.AnyAsync(x => x.OwnerId == ownerId, ct))
            db.WhatsAppSettings.Add(new() { OwnerId = ownerId });
        // Serializable scope also encloses the scheduled-period marker in the worker.
        var candidates = await (from i in db.Invoices.AsNoTracking()
            join o in db.Occupancies.AsNoTracking() on i.OccupancyId equals o.Id
            where i.OwnerId == ownerId && i.LifecycleState == InvoiceLifecycleState.Issued && i.Total > i.Paid && o.IsActive
            select new { i.Id, Occupancy = o }).ToListAsync(ct);
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var added = 0;
        var eligible = candidates.Where(x => NormalizePhone(x.Occupancy.Phone) != null && ValidToken(x.Occupancy) != null).ToList();
        var invoiceIds = eligible.Select(x => x.Id).ToList();
        var existingRows = invoiceIds.Count == 0
            ? new Dictionary<Guid, WhatsAppReminder>()
            : await db.WhatsAppReminders.Where(x => invoiceIds.Contains(x.InvoiceId)).ToDictionaryAsync(x => x.InvoiceId, ct);
        foreach (var invoice in eligible)
        {
            if (!existingRows.TryGetValue(invoice.Id, out var row))
            {
                db.WhatsAppReminders.Add(new() { InvoiceId = invoice.Id, OwnerId = ownerId, QueuedAtMs = now });
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
        // Skip the lease write, payment/approval checks and gateway status call when there is
        // nothing to send. "Sending" rows still need the lease path to be marked Unknown.
        if (!await db.WhatsAppReminders.AsNoTracking().AnyAsync(x => x.OwnerId == ownerId && (x.Status == "Pending" || x.Status == "Sending"), ct)) return;
        // A database lease serializes sends per owner across API instances, not just in this process.
        var claimed = await db.WhatsAppSettings.Where(x => x.OwnerId == ownerId && x.DispatchLeaseUntilMs <= now && x.NextSendAtMs <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, now + 120_000), ct);
        if (claimed == 0) return;
        try
        {
            // An interrupted submission may have reached WhatsApp. Require explicit requeue after the cooldown.
            await db.WhatsAppReminders.Where(x => x.OwnerId == ownerId && x.Status == "Sending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Unknown").SetProperty(x => x.Detail, "DeliveryUnknown"), ct);
            if (!await tokens.CanShareAsync(ownerId, payments, ct) || !await db.Users.AnyAsync(x => x.Id == ownerId && x.Role == UserRole.Owner && x.Status == OwnerStatus.Approved, ct)) return;
            if ((await gateway.ConnectionAsync(ownerId, "status", ct)).Status != "Connected") return;
            var job = await db.WhatsAppReminders.Where(x => x.OwnerId == ownerId && x.Status == "Pending")
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
            var message = BuildMessage(invoice, link);
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
            // The five-second interval starts after completion, even if WhatsApp was slow.
            await db.WhatsAppSettings.Where(x => x.OwnerId == ownerId && x.DispatchLeaseUntilMs == now + 120_000)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, 0L)
                    .SetProperty(x => x.NextSendAtMs, clock.GetUtcNow().ToUnixTimeMilliseconds() + 5_000), CancellationToken.None);
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

var clock = new FakeClock(DateTimeOffset.Parse("2026-10-20T01:00:00Z")); // 08:00 Vietnam
using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var postgres = Environment.GetEnvironmentVariable("ZALO_TEST_POSTGRES");
if (postgres is not null && (!postgres.Contains("Database=roomie_qa_zalo") || !postgres.Contains("Host=127.0.0.1;")))
    throw new InvalidOperationException("Use an isolated loopback roomie_qa_zalo database only.");
if (postgres is not null)
{
    await using var migrations = new PostgresAppDbContext(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql(postgres).Options);
    await migrations.Database.MigrateAsync();
}
var options = postgres is null ? new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options :
    new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres).Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();
var gateway = new FakeGateway();
var protection = new EphemeralDataProtectionProvider();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["PublicBaseUrl"] = "https://roomie.example" }).Build();
var tokens = new TenantTokenService(protection, config);
var payments = new PaymentWebhookService(db, protection, new NoHttp(), config);
var service = new ZaloReminderService(db, gateway, clock, tokens, payments);
var checks = 0;
void Assert(bool yes, string name) { if (!yes) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }

Assert(!ZaloReminderService.InSendWindow(DateTimeOffset.Parse("2026-10-19T23:59:59Z")), "before 07:00 blocked");
Assert(ZaloReminderService.InSendWindow(DateTimeOffset.Parse("2026-10-20T00:00:00Z")), "07:00 allowed");
Assert(ZaloReminderService.InSendWindow(DateTimeOffset.Parse("2026-10-20T13:59:59Z")), "20:59:59 allowed");
Assert(!ZaloReminderService.InSendWindow(DateTimeOffset.Parse("2026-10-20T14:00:00Z")), "21:00 blocked");
Assert(ZaloReminderService.NormalizePhone("+84 912 345 678") == "0912345678" && ZaloReminderService.NormalizePhone("84912345678") == "0912345678", "Vietnam phone normalization");
Assert(ZaloReminderService.NormalizePhone("123") == null && ZaloReminderService.NormalizePhone("19001234") == null, "invalid phones rejected");
var schedule = new OwnerZaloSettings { ScheduleEnabled = true, DayOfMonth = 31, Hour = 8, Minute = 30 };
Assert(!ZaloReminderService.ScheduleDue(schedule, DateTimeOffset.Parse("2028-02-29T01:29:59Z")), "schedule respects minute in leap month");
Assert(ZaloReminderService.ScheduleDue(schedule, DateTimeOffset.Parse("2028-02-29T01:30:00Z")), "day 31 clamps to leap-month last day");
schedule.LastScheduledPeriod = "2028-02";
Assert(!ZaloReminderService.ScheduleDue(schedule, DateTimeOffset.Parse("2028-02-29T02:00:00Z")), "schedule once per month");
schedule.LastScheduledPeriod = "";
Assert(ZaloReminderService.ScheduleDue(schedule, DateTimeOffset.Parse("2027-02-28T01:30:00Z")), "non-leap month clamps to day 28");
schedule.ScheduleEnabled = false;
Assert(!ZaloReminderService.ScheduleDue(schedule, clock.GetUtcNow()), "disabled schedule does not run");

var owner = new AppUser { Name = "Fixture Owner", Email = "zalo@example.invalid", PasswordHash = "fixture", Role = UserRole.Owner, Status = OwnerStatus.Approved,
    PaymentConfirmationMode = PaymentConfirmationMode.Manual, ManualBankBin = "970436", ManualAccountNumber = "123456789", ManualAccountName = "FIXTURE" };
var other = new AppUser { Name = "Other Owner", Email = "other@example.invalid", PasswordHash = "fixture", Role = UserRole.Owner, Status = OwnerStatus.Approved,
    PaymentConfirmationMode = PaymentConfirmationMode.Manual, ManualBankBin = "970436", ManualAccountNumber = "123456789", ManualAccountName = "FIXTURE" };
db.Users.AddRange(owner, other);
var setting = new OwnerZaloSettings { OwnerId = owner.Id };
db.ZaloSettings.Add(setting);
await db.SaveChangesAsync();
Invoice AddInvoice(string room, Guid ownerId, string? phone = "0912345678", decimal paid = 0, bool active = true, bool draft = false)
{
    var occupancy = new Occupancy { RoomId = Guid.NewGuid(), TenantName = "Tenant", Phone = phone, IsActive = active, StartDate = new(2026, 1, 1) };
    var token = tokens.Create();
    occupancy.TenantTokenHash = token.Hash; occupancy.ProtectedTenantToken = token.ProtectedToken;
    db.Occupancies.Add(occupancy);
    var invoice = new Invoice { OwnerId = ownerId, RoomId = occupancy.RoomId, BuildingId = Guid.NewGuid(), OccupancyId = occupancy.Id,
        RoomName = room, BuildingName = "Building", TenantName = "Tenant", Period = "2026-10", Total = 100000, Paid = paid,
        Rent = 100000, DueDate = new(2026, 10, 25), LifecycleState = draft ? InvoiceLifecycleState.Draft : InvoiceLifecycleState.Issued };
    db.Invoices.Add(invoice); return invoice;
}
var invoice = AddInvoice("101", owner.Id, paid: 25000);
AddInvoice("invalid", owner.Id, phone: "123");
AddInvoice("paid", owner.Id, paid: 100000);
AddInvoice("inactive", owner.Id, active: false);
AddInvoice("draft", owner.Id, draft: true);
AddInvoice("other", other.Id);
await db.SaveChangesAsync();
Assert(await service.QueueAsync(owner.Id, default) == 1, "queue filters draft, paid, invalid phone, inactive and other owner");
Assert(await service.QueueAsync(owner.Id, default) == 0, "double enqueue does not duplicate");
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == 1 && gateway.Sends[0].Message.Contains("75.000") && gateway.Sends[0].Phone == "0912345678", "send remaining amount and normalized number");
var originalToken = tokens.Unprotect(await db.Occupancies.SingleAsync(x => x.Id == invoice.OccupancyId));
Assert(gateway.Sends[0].Message.Contains($"Xem hóa đơn: https://roomie.example/t/{originalToken}\n") &&
    !gateway.Sends[0].Message.Contains("/checkout") && !gateway.Sends[0].Message.Contains("/invoices/") &&
    !gateway.Sends[0].Message.Contains("STK:") && !gateway.Sends[0].Message.Contains("Quét QR"), "existing tenant overview link, no direct checkout, bank or QR");
db.ChangeTracker.Clear();
Assert((await db.ZaloReminders.SingleAsync()).Status == "Sent", "successful send persisted");
Assert(await service.QueueAsync(owner.Id, default) == 0, "sent invoice blocked for 20 hours");
clock.Advance(TimeSpan.FromHours(20).Subtract(TimeSpan.FromMilliseconds(1)));
Assert(await service.QueueAsync(owner.Id, default) == 0, "20-hour boundary minus one ms blocked");
clock.Advance(TimeSpan.FromMilliseconds(1));
Assert(await service.QueueAsync(owner.Id, default) == 1, "20-hour boundary permits explicit requeue");
var job = await db.ZaloReminders.SingleAsync();
clock.Set(DateTimeOffset.Parse("2026-10-21T01:00:00Z"));
var current = await db.Invoices.SingleAsync(x => x.Id == invoice.Id); current.Paid = current.Total;
await db.SaveChangesAsync();
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == 1 && job.Status == "Skipped", "payment received while queued stops reminder");
current.Paid = 0; await db.SaveChangesAsync();
await service.QueueAsync(owner.Id, default);
clock.Advance(TimeSpan.FromSeconds(5));
gateway.Connection = "Disconnected";
await service.DispatchAsync(owner.Id, default);
Assert(job.Status == "Pending" && gateway.Sends.Count == 1, "lost session retains queued work");
gateway.Connection = "Connected";
clock.Advance(TimeSpan.FromSeconds(5));
gateway.Result = "Disconnected";
var oldAttempt = job.LastAttemptAtMs;
await service.DispatchAsync(owner.Id, default);
Assert(job.Status == "Pending" && job.LastAttemptAtMs == oldAttempt, "disconnect before submission permits resume without cooldown");
gateway.Result = "Deferred";
clock.Advance(TimeSpan.FromSeconds(5));
await service.DispatchAsync(owner.Id, default);
Assert(job.Status == "Pending", "gateway window or spacing deferral retains pending work");
gateway.Result = "Unknown";
clock.Advance(TimeSpan.FromSeconds(5));
await service.DispatchAsync(owner.Id, default);
Assert(job.Status == "Unknown" && job.Detail == "DeliveryUnknown", "ambiguous delivery never reported as sent");
Assert(await service.QueueAsync(owner.Id, default) == 0, "unknown delivery protected by cooldown");
var before = gateway.Sends.Count;
clock.Advance(TimeSpan.FromSeconds(5));
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == before, "unknown deliveries are never automatically retried");
job.Status = "Sending"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
clock.Advance(TimeSpan.FromSeconds(5));
await service.DispatchAsync(owner.Id, default);
Assert((await db.ZaloReminders.SingleAsync()).Status == "Unknown", "interrupted sending state recovers conservatively");
clock.Set(DateTimeOffset.Parse("2026-10-22T01:00:00Z"));
await service.QueueAsync(owner.Id, default);
job = await db.ZaloReminders.SingleAsync();
var lease = clock.GetUtcNow().ToUnixTimeMilliseconds() + 60000;
await db.ZaloSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, lease));
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == before && job.Status == "Pending", "database lease prevents concurrent worker send");
await db.ZaloSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchLeaseUntilMs, 0L));
clock.Set(DateTimeOffset.Parse("2026-10-22T14:00:00Z"));
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == before && job.Status == "Pending", "queued work waits outside send window");
clock.Set(DateTimeOffset.Parse("2026-10-23T01:00:00Z"));
await db.Users.Where(x => x.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OwnerStatus.Suspended));
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == before, "suspended owner cannot send in background");
await db.Users.Where(x => x.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OwnerStatus.Approved));
clock.Advance(TimeSpan.FromSeconds(5));
gateway.ThrowOnSend = true;
await service.DispatchAsync(owner.Id, default);
Assert(job.Status == "Unknown", "transport failure has unknown delivery outcome");

db.ChangeTracker.Clear();
var monthlySettings = await db.ZaloSettings.SingleAsync();
monthlySettings.ScheduleEnabled = true; monthlySettings.DayOfMonth = 23; monthlySettings.Hour = 8; monthlySettings.Minute = 0;
var scheduledInvoice = AddInvoice("Scheduled", owner.Id);
await db.SaveChangesAsync();
Assert(await service.QueueScheduledAsync(owner.Id, default) == 1, "monthly run queues an issued outstanding invoice");
Assert(monthlySettings.LastScheduledPeriod == "2026-10" && await db.ZaloReminders.AnyAsync(x => x.InvoiceId == scheduledInvoice.Id), "scheduled marker and queue commit together");
Assert(await service.QueueScheduledAsync(owner.Id, default) == 0, "repeated worker tick does not rerun the monthly schedule");
monthlySettings.ScheduleEnabled = false; monthlySettings.LastScheduledPeriod = "";
AddInvoice("DisabledSchedule", owner.Id); await db.SaveChangesAsync();
Assert(await service.QueueScheduledAsync(owner.Id, default) == 0 && monthlySettings.LastScheduledPeriod == "", "disabled schedule retains no run marker or new queue item");

// Current token is read at dispatch time; never reuse a revoked link captured at enqueue.
clock.Set(DateTimeOffset.Parse("2026-10-24T01:00:00Z"));
gateway.ThrowOnSend = false; gateway.Result = "Sent";
await db.ZaloReminders.ExecuteDeleteAsync();
await db.Invoices.ExecuteUpdateAsync(s => s.SetProperty(x => x.Paid, x => x.Total));
var rotatedInvoice = AddInvoice("Rotate", owner.Id);
await db.SaveChangesAsync();
Assert(await service.QueueAsync(owner.Id, default) == 1, "invoice with a valid tenant link queues without Zalo bank settings");
var resident = await db.Occupancies.SingleAsync(x => x.Id == rotatedInvoice.OccupancyId);
var rotated = tokens.Create(); resident.TenantTokenHash = rotated.Hash; resident.ProtectedTenantToken = rotated.ProtectedToken;
await db.SaveChangesAsync();
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Last().Message.Contains($"Xem hóa đơn: {tokens.Url(rotated.Token)}\n") && !gateway.Sends.Last().Message.Contains("/checkout"), "dispatch uses current rotated tenant link");
clock.Set(DateTimeOffset.Parse("2026-10-25T01:00:00Z"));
await service.QueueAsync(owner.Id, default);
resident.TenantTokenHash = "revoked"; await db.SaveChangesAsync();
var sentBeforeRevocation = gateway.Sends.Count;
await service.DispatchAsync(owner.Id, default);
Assert(gateway.Sends.Count == sentBeforeRevocation && (await db.ZaloReminders.SingleAsync()).Detail == "TenantLinkUnavailable", "revoked token skipped at send time");
Assert(await service.QueueAsync(owner.Id, default) == 0, "invalid tenant token excluded from queue");
await db.Users.Where(x => x.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ManualBankBin, (string?)null));
try { await service.QueueAsync(owner.Id, default); throw new Exception("payment sharing guard missing"); }
catch (ApiException) { Assert(true, "existing tenant-link payment setup rule remains enforced"); }

// Automatic enqueue is part of the billing transaction, without contacting Zalo.
db.ChangeTracker.Clear();
await db.Users.Where(x => x.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ManualBankBin, "970436"));
await db.ZaloReminders.ExecuteDeleteAsync(); await db.ZaloSettings.ExecuteDeleteAsync();
db.ChangeTracker.Clear();
var autoBilling = new InvoiceService(db, service);
var oldUnsent = AddInvoice("Older invoice", owner.Id); await db.SaveChangesAsync();
var autoInvoice = AddInvoice("Automatic", owner.Id);
gateway.Connection = "Disconnected";
var sendsBeforeAuto = gateway.Sends.Count;
await autoBilling.StageZaloReminderAsync(autoInvoice, default);
await autoBilling.StageZaloReminderAsync(autoInvoice, default);
await db.SaveChangesAsync();
Assert(await db.ZaloReminders.CountAsync() == 1 && await db.ZaloSettings.CountAsync() == 1, "first issued invoice creates queue and owner lease without Zalo bank configuration");
Assert(gateway.Sends.Count == sendsBeforeAuto && (await db.ZaloReminders.SingleAsync()).Status == "Pending", "invoice creation queues while disconnected without sending synchronously");
Assert(!await db.ZaloReminders.AnyAsync(x => x.InvoiceId == oldUnsent.Id), "new-invoice event never sweeps historical invoices");
var existingAuto = await db.ZaloReminders.SingleAsync(); existingAuto.Status = "Unknown"; await db.SaveChangesAsync();
clock.Advance(TimeSpan.FromDays(2));
await autoBilling.StageZaloReminderAsync(autoInvoice, default); await db.SaveChangesAsync();
Assert(await db.ZaloReminders.CountAsync() == 1 && existingAuto.Status == "Unknown", "replayed creation never retries an existing unknown reminder even after cooldown");
var draftAuto = AddInvoice("DraftAuto", owner.Id, draft: true);
await autoBilling.StageZaloReminderAsync(draftAuto, default); await db.SaveChangesAsync();
Assert(!await db.ZaloReminders.AnyAsync(x => x.InvoiceId == draftAuto.Id), "draft is not automatically queued");
draftAuto.LifecycleState = InvoiceLifecycleState.Issued;
await autoBilling.StageZaloReminderAsync(draftAuto, default); await db.SaveChangesAsync();
Assert(await db.ZaloReminders.AnyAsync(x => x.InvoiceId == draftAuto.Id), "publishing a draft queues its first reminder");
var excludedAuto = new[] { AddInvoice("BadPhoneAuto", owner.Id, phone: "invalid"), AddInvoice("PaidAuto", owner.Id, paid: 100000), AddInvoice("InactiveAuto", owner.Id, active: false), AddInvoice("NoTokenAuto", owner.Id) };
db.Occupancies.Local.Single(x => x.Id == excludedAuto[3].OccupancyId).ProtectedTenantToken = null;
foreach (var candidate in excludedAuto) await autoBilling.StageZaloReminderAsync(candidate, default);
await db.SaveChangesAsync();
Assert(await db.ZaloReminders.CountAsync() == 2, "automatic enqueue excludes invalid phone, paid, inactive and missing tenant link");
gateway.Enabled = false;
var disabledAuto = AddInvoice("DisabledAuto", owner.Id);
await autoBilling.StageZaloReminderAsync(disabledAuto, default); await db.SaveChangesAsync();
Assert(!await db.ZaloReminders.AnyAsync(x => x.InvoiceId == disabledAuto.Id), "disabled gateway leaves billing operational without automatic queue");
gateway.Enabled = true;
Guid rolledBackId;
await using (var tx = await db.Database.BeginTransactionAsync())
{
    var rollbackInvoice = AddInvoice("RollbackAuto", owner.Id); rolledBackId = rollbackInvoice.Id;
    await autoBilling.StageZaloReminderAsync(rollbackInvoice, default); await db.SaveChangesAsync();
    Assert(await db.ZaloReminders.AnyAsync(x => x.InvoiceId == rolledBackId), "queue is written within invoice transaction");
    await tx.RollbackAsync();
}
db.ChangeTracker.Clear();
Assert(!await db.Invoices.AnyAsync(x => x.Id == rolledBackId) && !await db.ZaloReminders.AnyAsync(x => x.InvoiceId == rolledBackId), "rollback removes both invoice and automatic reminder");
// Exercise the real monthly generation path rather than just its staging helper.
var autoBuilding = new Building { OwnerId = owner.Id, Name = "Auto fixture" };
var autoRoom = new Room { BuildingId = autoBuilding.Id, Name = "Auto monthly" };
var autoToken = tokens.Create();
var autoOccupancy = new Occupancy { RoomId = autoRoom.Id, TenantName = "Fixture", Phone = "0912345678", StartDate = new(2026, 1, 1), TenantTokenHash = autoToken.Hash, ProtectedTenantToken = autoToken.ProtectedToken };
db.Buildings.Add(autoBuilding); db.Rooms.Add(autoRoom); db.Occupancies.Add(autoOccupancy);
db.Readings.Add(new() { RoomId = autoRoom.Id, OccupancyId = autoOccupancy.Id, Period = "2026-11", CurrentReading = 10, Consumption = 10,
    MonthlyRentRate = 100000, ElectricityRate = 1000, WaterRate = 0, WaterPerPerson = false, ServiceRate = 0,
    BillingStartDate = new(2026, 11, 1), BillingEndDate = new(2026, 11, 30), BillableDays = 30, MonthDays = 30 });
await db.SaveChangesAsync();
var generatedAuto = await autoBilling.GenerateForOwnerAsync(owner.Id, "2026-11");
var monthlyAuto = await db.Invoices.SingleAsync(x => x.OccupancyId == autoOccupancy.Id);
Assert(generatedAuto.Created == 1 && await db.ZaloReminders.AnyAsync(x => x.InvoiceId == monthlyAuto.Id), "monthly billing automatically commits its reminder");
Assert((await autoBilling.GenerateForOwnerAsync(owner.Id, "2026-11")).Created == 0 && await db.ZaloReminders.CountAsync(x => x.InvoiceId == monthlyAuto.Id) == 1, "repeated monthly billing does not duplicate reminder");

// Both generated migration sets must build and SQLite migration must preserve existing business data.
var migratedPath = Path.Combine(Path.GetTempPath(), $"roomie-zalo-migration-{Guid.NewGuid():N}.db");
try
{
    var migrationOptions = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite($"Data Source={migratedPath}").Options;
    await using var migrated = new SqliteAppDbContext(migrationOptions);
    await migrated.Database.MigrateAsync();
    migrated.Users.Add(new() { Name = "Keep", Email = "keep@example.invalid", PasswordHash = "fixture" });
    await migrated.SaveChangesAsync();
    await migrated.Database.MigrateAsync();
    Assert(await migrated.Users.CountAsync() == 1 && await migrated.ZaloReminders.CountAsync() == 0, "SQLite migrations and rerun preserve data");
}
finally { SqliteConnection.ClearAllPools(); File.Delete(migratedPath); }
Console.WriteLine($"PASS: {checks} Zalo assertions; no external provider calls.");

sealed class FakeClock(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset value = initial;
    public override DateTimeOffset GetUtcNow() => value;
    public void Advance(TimeSpan span) => value += span;
    public void Set(DateTimeOffset instant) => value = instant;
}
sealed class FakeGateway : IZaloGateway
{
    public bool Enabled { get; set; } = true;
    public string Connection = "Connected";
    public string Result = "Sent";
    public bool ThrowOnSend;
    public List<ZaloSendRequest> Sends = [];
    public Task<ZaloConnection> ConnectionAsync(Guid ownerId, string action, CancellationToken ct) => Task.FromResult(new ZaloConnection(Connection));
    public Task<ZaloSendResult> SendAsync(Guid ownerId, ZaloSendRequest request, CancellationToken ct)
    {
        Sends.Add(request);
        if (ThrowOnSend) throw new HttpRequestException("fixture timeout");
        return Task.FromResult(new ZaloSendResult(Result));
    }
}

sealed class NoHttp : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new Exception("No provider calls allowed");
}

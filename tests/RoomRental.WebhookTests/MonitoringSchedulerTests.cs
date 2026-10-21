using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Features.Monitoring.Operations;

namespace RoomRental.WebhookTests;

public static class MonitoringSchedulerTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        await TestSchedulingAsync();
        await TestDisabledAndInvalidConfigurationAsync();
        await TestRealMaintenanceAsync();
        await TestDisconnectedDatabaseAsync();
    }

    private static ServiceProvider Services(TimeProvider clock, MonitoringScheduleOptions schedule, Probe probe)
    {
        var services = new ServiceCollection();
        services.AddLogging(x => x.ClearProviders());
        services.AddSingleton(clock);
        services.AddMonitoringCore(new ConfigurationBuilder().Build());
        services.AddMonitoringOperations(new ConfigurationBuilder().Build());
        services.AddSingleton<IOptions<MonitoringScheduleOptions>>(Options.Create(schedule));
        services.AddScoped<IIncidentService>(_ => new IncidentStub(probe));
        services.AddScoped<IMonitoringMaintenanceService>(_ => new MaintenanceStub(probe));
        return services.BuildServiceProvider();
    }

    private static MonitoringBackgroundWorker Worker(ServiceProvider services) =>
        services.GetServices<IHostedService>().OfType<MonitoringBackgroundWorker>().Single();

    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(5, timeout.Token);
    }

    private static async Task TestSchedulingAsync()
    {
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-21T17:30:00Z")); // Oct 22 Vietnam
        var probe = new Probe { FailIncidentOnce = true, FailRollupOnce = true };
        await using var services = Services(clock, new(), probe);
        var worker = Worker(services);
        await worker.StartAsync(default);
        await UntilAsync(() => clock.TimerCount == 2);
        var tracker = services.GetRequiredService<IWorkerTelemetryTracker>();
        Assert(tracker.GetStatuses().All(x => x.Status == "Failed"), "Scheduler reports both startup failures without stopping host");
        Assert(probe.Prunes == 0, "Failed rollup never prunes raw telemetry");
        clock.Advance(TimeSpan.FromSeconds(60));
        await UntilAsync(() => probe.IncidentCalls == 2 && clock.TimerCount == 2);
        Assert(probe.RollupCalls == 1, "Incident retry does not wait for hourly maintenance");
        clock.Advance(TimeSpan.FromSeconds(3540));
        await UntilAsync(() => probe.Prunes == 1 && clock.TimerCount == 2);
        Assert(probe.Dates.TakeLast(7).SequenceEqual(Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 10, 15).AddDays(i))),
            "Maintenance recomputes seven closed Vietnam dates before pruning");
        Assert(tracker.GetStatuses().All(x => x.Status == "Healthy"), "Successful retries recover worker health");
        Assert(probe.MaxConcurrentIncidents == 1, "Incident cycles never overlap");
        await worker.StopAsync(default);
        Assert(clock.TimerCount == 0, "Shutdown cancels both scheduled waits");

        // A blocked maintenance call cannot block probes and receives host cancellation.
        var blocked = new Probe { BlockMaintenance = true };
        await using var blockedServices = Services(clock, new(), blocked);
        var blockedWorker = Worker(blockedServices);
        await blockedWorker.StartAsync(default);
        await UntilAsync(() => blocked.RollupCalls == 1 && clock.TimerCount == 1);
        clock.Advance(TimeSpan.FromMinutes(1));
        await UntilAsync(() => blocked.IncidentCalls == 2 && clock.TimerCount == 1);
        Assert(blocked.Prunes == 0, "Probes continue while maintenance is blocked");
        await blockedWorker.StopAsync(default);
        Assert(blocked.MaintenanceCancelled, "Shutdown cancels in-flight maintenance without recording failure");
    }

    private static async Task TestDisabledAndInvalidConfigurationAsync()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var probe = new Probe();
        await using var services = Services(clock, new() { IncidentChecksEnabled = false, MaintenanceEnabled = false }, probe);
        var worker = Worker(services);
        await worker.StartAsync(default);
        await UntilAsync(() => services.GetRequiredService<IWorkerTelemetryTracker>().GetStatuses().Count == 2);
        Assert(probe.IncidentCalls == 0 && probe.RollupCalls == 0 && clock.TimerCount == 0,
            "Disabled scheduler performs no work and schedules no timers");
        Assert(services.GetRequiredService<IWorkerTelemetryTracker>().GetStatuses().All(x => !x.IsEnabled && !x.IsStale),
            "Disabled workers stay visible without false stale alarms");
        await worker.StopAsync(default);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Monitoring:Schedule:IncidentIntervalSeconds"] = "0",
            ["Monitoring:Schedule:RollupDays"] = "90"
        }).Build();
        var registrations = new ServiceCollection().AddMonitoringOperations(config);
        using var invalid = registrations.BuildServiceProvider();
        var rejected = false;
        try { _ = invalid.GetRequiredService<IOptions<MonitoringScheduleOptions>>().Value; }
        catch (OptionsValidationException) { rejected = true; }
        Assert(rejected, "Invalid schedule and rollup outside raw retention are rejected");
    }

    private static async Task TestRealMaintenanceAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new ManualClock(DateTimeOffset.Parse("2026-10-21T18:00:00Z"));
        var services = new ServiceCollection();
        services.AddLogging(x => x.ClearProviders());
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddMonitoringCore(new ConfigurationBuilder().Build());
        services.AddMonitoringOperations(new ConfigurationBuilder().Build());
        services.Configure<MonitoringScheduleOptions>(o => o.IncidentChecksEnabled = false);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        var worker = Worker(provider);
        await worker.StartAsync(default);
        await UntilAsync(() => clock.TimerCount == 1);
        Assert(provider.GetRequiredService<IWorkerTelemetryTracker>().GetStatuses().Single(x => x.WorkerName == "monitoring-maintenance").Status == "Healthy",
            "DI scheduler executes actual SQLite rollup and retention services");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.DailyAggregateMetrics.CountAsync() == 7, "Real maintenance creates seven daily aggregates");
        }
        clock.Advance(TimeSpan.FromHours(1));
        await UntilAsync(() => clock.TimerCount == 1);
        await using (var scope = provider.CreateAsyncScope())
            Assert(await scope.ServiceProvider.GetRequiredService<AppDbContext>().DailyAggregateMetrics.CountAsync() == 7,
                "Repeated maintenance updates existing aggregates without duplication");
        await worker.StopAsync(default);
    }

    private static async Task TestDisconnectedDatabaseAsync()
    {
        IncidentMonitoringService.ResetDbProbeHistory();
        var dbPath = Path.Combine(Path.GetTempPath(), $"roomie-monitoring-{Guid.NewGuid():N}.db");
        try
        {
            await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-21T12:00:00Z"));
            var notifier = new RecordingNotifier();
            var service = new IncidentMonitoringService(db, new RouteLatencyAggregator(clock), new EmptyHostReader(),
                new WorkerTelemetryTracker(clock), notifier, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<IncidentMonitoringService>.Instance);
            service.DbProbeOverride = _ => Task.FromResult(false);
            // Dispose the underlying connection: any attempted DB access now throws.
            await connection.DisposeAsync();
            await service.TriggerIncidentCheckAsync(default);
            await service.TriggerIncidentCheckAsync(default);
            clock.Advance(TimeSpan.FromMinutes(1));
            await service.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 0, "Rapid duplicate probes cannot confirm an outage");
            clock.Advance(TimeSpan.FromMinutes(1));
            notifier.Fail = true;
            await service.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 0, "Failed outage SMTP is not counted as sent");
            notifier.Fail = false;
            await service.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.SequenceEqual(new[] { false }), "DB outage alert sends without any working DB connection");
            await service.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 1, "In-memory outage cooldown suppresses duplicate alerts");
            clock.Advance(TimeSpan.FromHours(1));
            await service.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 2, "DB outage reminder sends after one hour");

            // Reconnect using a new scoped context, preserving singleton/process outage state.
            await using var restored = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await restored.OpenAsync();
            await using var restoredDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(restored).Options);
            var recovered = new IncidentMonitoringService(restoredDb, new RouteLatencyAggregator(clock), new EmptyHostReader(),
                new WorkerTelemetryTracker(clock), notifier, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<IncidentMonitoringService>.Instance);
            notifier.Fail = true;
            await recovered.TriggerIncidentCheckAsync(default);
            var incident = await restoredDb.MonitoringIncidents.SingleAsync();
            Assert(incident.State == "Resolved" && incident.NotificationCount == 2 && incident.Details.Contains("[PENDING_RECOVERY_RETRY]"),
                "Reconnect persists outage history and pending recovery despite SMTP failure");
            notifier.Fail = false;
            await recovered.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 3 && notifier.Messages.Last() && !incident.Details.Contains("[PENDING_RECOVERY_RETRY]"),
                "Pending DB recovery notice retries successfully on the next cycle");
            await recovered.TriggerIncidentCheckAsync(default);
            Assert(notifier.Messages.Count == 3, "Recovery is not sent again on the next healthy cycle");
        }
        finally
        {
            IncidentMonitoringService.ResetDbProbeHistory();
            File.Delete(dbPath);
        }
    }

    private sealed class Probe
    {
        public int IncidentCalls, RollupCalls, Prunes, ActiveIncidents, MaxConcurrentIncidents;
        public bool FailIncidentOnce, FailRollupOnce, BlockMaintenance, MaintenanceCancelled;
        public List<DateOnly> Dates = [];
    }
    private sealed class IncidentStub(Probe p) : IIncidentService
    {
        public async Task TriggerIncidentCheckAsync(CancellationToken ct)
        {
            p.MaxConcurrentIncidents = Math.Max(p.MaxConcurrentIncidents, ++p.ActiveIncidents);
            try
            {
                p.IncidentCalls++;
                await Task.Yield();
                if (p.FailIncidentOnce) { p.FailIncidentOnce = false; throw new Exception("fixture"); }
            }
            finally { p.ActiveIncidents--; }
        }
        public Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IncidentDto> UpdateIncidentStateAsync(Guid id, UpdateIncidentRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class MaintenanceStub(Probe p) : IMonitoringMaintenanceService
    {
        public Task<int> PruneExpiredTelemetryAsync(CancellationToken ct) { p.Prunes++; return Task.FromResult(0); }
        public async Task<DailyAggregateMetric> RollupDailyAggregateAsync(DateOnly date, CancellationToken ct)
        {
            p.RollupCalls++;
            p.Dates.Add(date);
            if (p.FailRollupOnce) { p.FailRollupOnce = false; throw new Exception("fixture"); }
            if (p.BlockMaintenance)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { p.MaintenanceCancelled = true; throw; }
            }
            return new DailyAggregateMetric { Date = date };
        }
    }
    private sealed class RecordingNotifier : IIncidentNotifier
    {
        public bool Fail;
        public List<bool> Messages = [];
        public Task SendIncidentAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct)
        {
            if (Fail) throw new IOException("fixture SMTP");
            Messages.Add(isRecovery);
            return Task.CompletedTask;
        }
    }
    private sealed class EmptyHostReader : ILinuxHostCollectorMetricsReader
    {
        public Task<HostCollectorMetrics?> ReadHostMetricsAsync(CancellationToken ct = default) => Task.FromResult<HostCollectorMetrics?>(null);
    }

    // A controllable timer clock exercises the actual BackgroundService loops without wall-clock sleeps.
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
        public int TimerCount { get { lock (gate) return timers.Count; } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                var timer = new ManualTimer(this, callback, state, now + dueTime);
                timers.Add(timer);
                return timer;
            }
        }
        public void Advance(TimeSpan delta)
        {
            ManualTimer[] due;
            lock (gate)
            {
                now += delta;
                due = timers.Where(t => t.Due <= now).ToArray();
                foreach (var timer in due) timers.Remove(timer);
            }
            foreach (var timer in due) timer.Callback(timer.State);
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            public DateTimeOffset Due = due;
            public TimerCallback Callback = callback;
            public object? State = state;
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() { lock (clock.gate) clock.timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

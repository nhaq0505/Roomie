using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Features.Monitoring.Operations;
using RoomRental.Api.Services;

namespace RoomRental.WebhookTests;

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    public TestTimeProvider(DateTimeOffset initial) => _utcNow = initial;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    public void SetUtcNow(DateTimeOffset time) => _utcNow = time;
}

public static class OperationsMonitoringTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("Running OperationsMonitoringTests...");
        await TestLinuxHostCollectorMetricsReaderAsync();
        await TestRouteLatencyAggregatorAsync();
        await TestWorkerTelemetryTrackerAsync();
        await TestIncidentEngineAndAlertsAsync();
        await TestSmtpIncidentNotifierAsync();
        await TestOperationsAndIncidentsHttpEndpointsAsync();
    }

    private static async Task TestLinuxHostCollectorMetricsReaderAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "monitoring_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var metricsFile = Path.Combine(tempDir, "host-metrics.json");

        try
        {
            var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
            var options = Options.Create(new HostMetricsOptions
            {
                HostMetricsPath = metricsFile,
                StalenessThresholdSeconds = 180.0
            });
            var reader = new LinuxHostCollectorMetricsReader(options, fakeClock, NullLogger<LinuxHostCollectorMetricsReader>.Instance);

            // Test 1: Missing file returns null
            var missingResult = await reader.ReadHostMetricsAsync();
            Assert(missingResult == null, "Host reader returns null when file is missing");

            // Test 2: Valid JSON within freshness threshold matching real Python collector output
            var validJson = """
            {
                "version": "1.0",
                "generated_at": "2026-10-20T15:59:00Z",
                "collector": { "version": "1.1.0", "status": "healthy", "duration_ms": 120 },
                "host": {
                    "hostname": "test-vps",
                    "cpu": { "percent": 42.5, "status": "healthy" },
                    "memory": { "percent": 68.0 },
                    "disk": { "percent": 35.2 }
                },
                "containers": {
                    "app": { "running": true, "status": "running", "health": "healthy" },
                    "db": { "running": true, "status": "running", "health": "healthy" }
                },
                "services": {
                    "api_health": { "status": "healthy" },
                    "backup_watchdog": {
                        "status": "healthy",
                        "last_success_at": "2026-10-20T15:00:00Z",
                        "age_seconds": 3600,
                        "threshold_seconds": 97200
                    }
                },
                "incidents": {
                    "active": [
                        {
                            "id": "inc_cpu_1729418400",
                            "target": "cpu",
                            "severity": "warning",
                            "title": "CPU sustained high",
                            "consecutive_samples": 10,
                            "current_value": "88%",
                            "opened_at": "2026-10-20T15:50:00Z"
                        }
                    ],
                    "recent_history": [
                        {
                            "id": "inc_disk_1729415000",
                            "target": "disk",
                            "severity": "warning",
                            "title": "Disk sustained high",
                            "resolved_at": "2026-10-20T15:30:00Z",
                            "duration_seconds": 900,
                            "resolved_value": "72%"
                        }
                    ]
                }
            }
            """;
            await File.WriteAllTextAsync(metricsFile, validJson);

            var validResult = await reader.ReadHostMetricsAsync();
            Assert(validResult != null, "Host reader parsed valid JSON");
            Assert(validResult!.CpuPercent == 42.5, "CpuPercent parsed correctly");
            Assert(validResult.MemoryPercent == 68.0, "MemoryPercent parsed correctly");
            Assert(validResult.DiskPercent == 35.2, "DiskPercent parsed correctly");
            Assert(validResult.ContainerCount == 2, "ContainerCount parsed correctly");
            Assert(validResult.HealthEndpointStatus == "healthy", "HealthEndpointStatus parsed correctly");
            Assert(validResult.HostStatus == "healthy", "HostStatus parsed correctly");
            Assert(!validResult.IsStale, "Metric generated 60s ago is not stale (< 180s)");
            Assert(validResult.ActiveIncidents?.Count == 1, "Active incidents list parsed");
            Assert(validResult.ActiveIncidents?[0].Id == "inc_cpu_1729418400", "Active incident stable ID matches");
            Assert(validResult.RecentHistory?.Count == 1, "Recent history list parsed");
            Assert(validResult.RecentHistory?[0].Id == "inc_disk_1729415000", "Recent history stable ID matches");
            Assert(validResult.BackupWatchdog != null && validResult.BackupWatchdog.ThresholdSeconds == 97200.0, "Backup watchdog threshold is 27h (97200s)");

            // Test 3: Stale JSON (> 180s)
            fakeClock.Advance(TimeSpan.FromMinutes(5)); // now 16:05:00, sample at 15:59:00 (360s old)
            var staleResult = await reader.ReadHostMetricsAsync();
            Assert(staleResult != null && staleResult.IsStale, "Metric older than 180s is marked stale");

            // Test 4: Nullable fields preserve nulls without fabricating 0.0 or 0 count
            var nullableJson = """
            {
                "version": "1.0",
                "generated_at": "2026-10-20T16:04:30Z",
                "host": {
                    "cpu": { "percent": null, "status": "unknown" },
                    "memory": {},
                    "disk": null
                },
                "containers": {},
                "services": {
                    "backup_watchdog": {
                        "status": "healthy"
                    }
                }
            }
            """;
            await File.WriteAllTextAsync(metricsFile, nullableJson);

            var nullResult = await reader.ReadHostMetricsAsync();
            Assert(nullResult != null, "Host reader parsed nullable JSON");
            Assert(nullResult!.CpuPercent == null, "Nullable CPU preserved as null");
            Assert(nullResult.MemoryPercent == null, "Missing RAM preserved as null");
            Assert(nullResult.DiskPercent == null, "Null disk preserved as null");
            Assert(nullResult.ContainerCount == null, "Container count is null (not invented 0) for empty object");
            Assert(nullResult.BackupWatchdog?.ThresholdSeconds == 97200.0, "Omitted watchdog threshold defaults to 27h (97200s)");

            // Test 5: Out of range percentages rejected as null
            var outOfRangeJson = """
            {
                "version": "1.0",
                "generated_at": "2026-10-20T16:04:30Z",
                "host": {
                    "cpu": { "percent": 150.0 },
                    "memory": { "percent": -10.0 }
                }
            }
            """;
            await File.WriteAllTextAsync(metricsFile, outOfRangeJson);
            var rangeResult = await reader.ReadHostMetricsAsync();
            Assert(rangeResult?.CpuPercent == null, "CPU percent > 100 rejected as null");
            Assert(rangeResult?.MemoryPercent == null, "Memory percent < 0 rejected as null");

            // Test 6: Future timestamp > 2m rejected
            var futureJson = """
            {
                "version": "1.0",
                "generated_at": "2026-10-20T16:20:00Z"
            }
            """;
            await File.WriteAllTextAsync(metricsFile, futureJson);
            var futureResult = await reader.ReadHostMetricsAsync();
            Assert(futureResult != null && futureResult.IsStale && futureResult.RecordedAt == null, "Future timestamp marked invalid/stale");

            // Test 7: Corrupt JSON returns null without crashing
            await File.WriteAllTextAsync(metricsFile, "{ corrupt json !!!");
            var corruptResult = await reader.ReadHostMetricsAsync();
            Assert(corruptResult == null, "Host reader returns null for malformed JSON");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static Task TestRouteLatencyAggregatorAsync()
    {
        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var aggregator = new RouteLatencyAggregator(fakeClock);

        // Test 1: Route template normalization
        Assert(RouteLatencyAggregator.NormalizeRoute("/api/invoices/12345") == "/api/invoices/{id}", "Normalized numeric ID");
        Assert(RouteLatencyAggregator.NormalizeRoute("/api/users/a3b683b5-245c-4d37-8ec9-232a93902f23") == "/api/users/{id}", "Normalized GUID ID");
        Assert(RouteLatencyAggregator.NormalizeRoute("/api/invoices/123/items?page=1") == "/api/invoices/{id}/items", "Normalized nested path with query");

        // Test 2: Latency recording and P95 calculation
        // Record 100 requests for /api/test:
        // 90 requests at 20ms
        for (int i = 0; i < 90; i++)
        {
            aggregator.Record("/api/test", "GET", 200, 20.0);
        }
        // 5 requests at 80ms
        for (int i = 0; i < 5; i++)
        {
            aggregator.Record("/api/test", "GET", 200, 80.0);
        }
        // 5 requests at 300ms (with 5xx status code)
        for (int i = 0; i < 5; i++)
        {
            aggregator.Record("/api/test", "GET", 500, 300.0);
        }

        var snapshots = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        Assert(snapshots.Count == 1, "Exactly one route recorded");
        var routeSnap = snapshots[0];
        Assert(routeSnap.RequestCount == 100, "100 total requests");
        Assert(routeSnap.Status5xxCount == 5, "5 5xx requests");
        Assert(routeSnap.MaxLatencyMs == 300.0, "Max latency is 300ms");
        Assert(routeSnap.P95LatencyMs != null && routeSnap.P95LatencyMs.Value >= 50.0 && routeSnap.P95LatencyMs.Value <= 100.0,
            $"P95 latency correctly interpolated in 50-100ms bucket ({routeSnap.P95LatencyMs}ms)");

        // Test 3: Overview snapshot
        var overview = aggregator.GetOverviewSnapshot(TimeSpan.FromHours(1), dbConnected: true, dbStatus: "Healthy");
        Assert(overview.TotalRequestsLastHour == 100, "Total requests last hour is 100");
        Assert(overview.Api5xxRatePercent == 5.0, "5xx rate is 5.0%");
        Assert(overview.OverallStatus == "Degraded", "Status is Degraded due to 5% 5xx rate");

        // Test 4: Sliding window expiry
        fakeClock.Advance(TimeSpan.FromMinutes(70)); // Advance past 1h
        var expiredSnapshots = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        Assert(expiredSnapshots.Count == 0, "Requests older than 1h expired from 1h window");

        var expiredOverview = aggregator.GetOverviewSnapshot(TimeSpan.FromHours(1), dbConnected: true);
        Assert(expiredOverview.TotalRequestsLastHour == 0, "0 requests after window expiry");
        Assert(expiredOverview.OverallStatus == "Healthy", "Status healthy after old traffic expired");

        return Task.CompletedTask;
    }

    private static async Task TestWorkerTelemetryTrackerAsync()
    {
        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var tracker = new WorkerTelemetryTracker(fakeClock);

        // Test 1: Record successful execution
        await tracker.ExecuteTrackedAsync("invoice-sweep", async ct =>
        {
            await Task.Delay(1, ct);
        });

        var statuses = tracker.GetStatuses();
        Assert(statuses.Count == 1, "Tracked 1 worker");
        Assert(statuses[0].WorkerName == "invoice-sweep", "Worker name matches");
        Assert(statuses[0].Status == "Healthy", "Worker status is Healthy");
        Assert(statuses[0].ConsecutiveFailures == 0, "0 consecutive failures on success");
        Assert(!statuses[0].IsStale, "Worker is not stale");

        // Test 2: Record failure
        try
        {
            await tracker.ExecuteTrackedAsync("reconcile", _ => throw new InvalidOperationException("payOS timeout"));
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        statuses = tracker.GetStatuses();
        var reconcileStatus = statuses.Single(s => s.WorkerName == "reconcile");
        Assert(reconcileStatus.Status == "Failed", "Failed worker has status Failed");
        Assert(reconcileStatus.ConsecutiveFailures == 1, "1 consecutive failure recorded");
        Assert(reconcileStatus.LastErrorMessage == "payOS timeout", "Error message recorded");

        // Test 3: Per-worker SLA staleness test
        // Advance 15 minutes: reconcile (SLA 10m) should be stale, but invoice-sweep (SLA 26h) should NOT be stale!
        fakeClock.Advance(TimeSpan.FromMinutes(15));
        statuses = tracker.GetStatuses();
        var rStatus15m = statuses.Single(s => s.WorkerName == "reconcile");
        var iStatus15m = statuses.Single(s => s.WorkerName == "invoice-sweep");
        Assert(rStatus15m.IsStale, "Reconcile worker is stale after 15m (> 10m SLA)");
        Assert(!iStatus15m.IsStale, "Invoice sweep worker is NOT stale after 15m (within 26h SLA)");

        // Test 4: Disabled worker is never stale
        tracker.RecordHeartbeat("disabled-worker", isEnabled: false, status: "Healthy");
        fakeClock.Advance(TimeSpan.FromHours(50));
        statuses = tracker.GetStatuses();
        var dStatus = statuses.Single(s => s.WorkerName == "disabled-worker");
        Assert(!dStatus.IsStale, "Disabled worker is never marked stale");

        // Test 5: Invoice sweep becomes stale after exceeding 26h SLA
        var iStatus50h = statuses.Single(s => s.WorkerName == "invoice-sweep");
        Assert(iStatus50h.IsStale, "Invoice sweep worker is stale after exceeding 26h SLA");
    }

    private static async Task TestIncidentEngineAndAlertsAsync()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"incidents_test_{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={tempDbPath}");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var aggregator = new RouteLatencyAggregator(fakeClock);
        var fakeNotifier = new FakeAlertNotifier();
        var fakeHostReader = new FakeHostReader();
        var workerTracker = new WorkerTelemetryTracker(fakeClock);

        var incidentService = new IncidentMonitoringService(
            db,
            aggregator,
            fakeHostReader,
            workerTracker,
            fakeNotifier,
            fakeClock,
            NullLogger<IncidentMonitoringService>.Instance
        );

        // --- Scenario 1: 5xx Rate Rule (Exact: Sustained 5min, Min 100 req, > 5.0% threshold) ---
        // Step 1.1: 90 requests with 10 errors (rate = 11.1%), but TotalRequests < 100 -> NO incident opened!
        for (int i = 0; i < 80; i++) aggregator.Record("/api/test", "GET", 200, 25.0);
        for (int i = 0; i < 10; i++) aggregator.Record("/api/test", "GET", 500, 50.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        var incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(incidentsResp.ActiveIncidentCount == 0, "No incident triggered when 5-min requests < 100");
        Assert(fakeNotifier.Alerts.Count == 0, "No alert email sent when requests < 100");

        // Step 1.2: Add 20 more requests (5 errors) -> Total 110 requests, 15 errors = 13.6% > 5% -> Triggers High 5xx incident!
        for (int i = 0; i < 15; i++) aggregator.Record("/api/test", "GET", 200, 25.0);
        for (int i = 0; i < 5; i++) aggregator.Record("/api/test", "GET", 500, 50.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(incidentsResp.ActiveIncidentCount == 1, "Incident triggered when requests >= 100 and 5xx rate > 5%");
        var active5xx = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(active5xx.Severity == "Critical", "Severity is Critical for 5xx rate >= 10%");
        Assert(active5xx.State == "Active", "Incident state is Active");
        Assert(fakeNotifier.Alerts.Count == 1, "Sent 1 initial alert email");
        Assert(!fakeNotifier.Alerts[0].IsRecovery, "Alert is not a recovery notice");
        Assert(active5xx.NotificationCount == 1, "Notification count is 1");

        // Step 1.3: 1-hour Cooldown suppresses repeat notification
        fakeClock.Advance(TimeSpan.FromMinutes(20));
        await incidentService.TriggerIncidentCheckAsync(default);
        Assert(fakeNotifier.Alerts.Count == 1, "No duplicate alert sent within 1-hour cooldown");

        // Step 1.4: Traffic drops below 100 requests in 5-minute window -> Unknown state PRESERVES active incident (NO auto-resolve!)
        fakeClock.Advance(TimeSpan.FromMinutes(10));
        for (int i = 0; i < 5; i++) aggregator.Record("/api/test", "GET", 200, 25.0);
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var preservedInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(preservedInc.State == "Active", "Incident remains Active during low-traffic unknown window");

        // Step 1.5: Cooldown expires after 65 minutes -> Sends reminder notification
        fakeClock.Advance(TimeSpan.FromMinutes(40)); // now 70 min since initial notification
        for (int i = 0; i < 90; i++) aggregator.Record("/api/test", "GET", 200, 25.0);
        for (int i = 0; i < 20; i++) aggregator.Record("/api/test", "GET", 500, 50.0);
        await incidentService.TriggerIncidentCheckAsync(default);
        Assert(fakeNotifier.Alerts.Count == 2, "Reminder alert sent after 1 hour cooldown");
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var remindedInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(remindedInc.NotificationCount == 2, "Notification count incremented to 2");

        // Step 1.6: Explicit healthy traffic (120 requests, 0 errors) -> Auto-resolves and sends recovery notice!
        fakeClock.Advance(TimeSpan.FromMinutes(6));
        for (int i = 0; i < 120; i++) aggregator.Record("/api/test", "GET", 200, 25.0);
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var resolved5xx = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(resolved5xx.State == "Resolved", "Incident auto-resolved when condition explicitly healthy");
        Assert(resolved5xx.ResolvedAt != null, "ResolvedAt timestamp recorded");
        Assert(fakeNotifier.Alerts.Count == 3, "Sent recovery notification email");
        Assert(fakeNotifier.Alerts[2].IsRecovery, "Third notification is recovery notice");

        // --- Scenario 2: API P95 Latency Rule (Exact: Sustained 10min, Min 100 req, > 2000ms threshold) ---
        fakeClock.Advance(TimeSpan.FromMinutes(15));
        for (int i = 0; i < 105; i++) aggregator.Record("/api/slow", "GET", 200, 2500.0);
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var p95Inc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_p95_latency");
        Assert(p95Inc.State == "Active", "High P95 incident triggered for latency > 2000ms");
        Assert(p95Inc.Severity == "Critical", "P95 severity is Critical");

        // Auto-resolve P95 when latency normalizes
        fakeClock.Advance(TimeSpan.FromMinutes(11));
        for (int i = 0; i < 110; i++) aggregator.Record("/api/slow", "GET", 200, 50.0);
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var resolvedP95 = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_p95_latency");
        Assert(resolvedP95.State == "Resolved", "High P95 incident auto-resolved with fast requests");

        // --- Scenario 3: Database Connectivity Rule (Exact: 3 consecutive probes >= 1 min apart) ---
        IncidentMonitoringService.ResetDbProbeHistory();
        incidentService.DbProbeOverride = _ => Task.FromResult(false);

        // Probe 1 at T+0: Does NOT trigger yet (needs 3 consecutive probes)
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(!incidentsResp.Incidents.Any(i => i.IncidentKey == "database_disconnected" && i.State != "Resolved"),
            "1 probe failure does not trigger database incident immediately");

        // Probe 2 at T+1m: Does NOT trigger yet
        fakeClock.Advance(TimeSpan.FromMinutes(1));
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(!incidentsResp.Incidents.Any(i => i.IncidentKey == "database_disconnected" && i.State != "Resolved"),
            "2 probe failures do not trigger database incident yet");

        // Probe 3 at T+2.5m: 3 consecutive probes >= 1 min apart -> TRIGGERS database_disconnected!
        fakeClock.Advance(TimeSpan.FromMinutes(1.5));
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var dbInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "database_disconnected");
        Assert(dbInc.State == "Active", "Database incident triggered after 3 consecutive failures >= 1m apart");
        Assert(dbInc.Severity == "Critical", "Database incident severity is Critical");

        // Reconnect and probe succeeds -> Auto-resolves!
        incidentService.DbProbeOverride = _ => Task.FromResult(true);
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var resolvedDbInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "database_disconnected");
        Assert(resolvedDbInc.State == "Resolved", "Database incident auto-resolved after connection restored");
        incidentService.DbProbeOverride = null;

        // --- Scenario 4: Worker Telemetry & SLAs ---
        workerTracker.RecordHeartbeat("zalo", isEnabled: true, status: "Healthy");
        workerTracker.RecordHeartbeat("recovery-email", isEnabled: true, status: "Healthy");
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(!incidentsResp.Incidents.Any(i => i.IncidentKey.StartsWith("worker_stalled") && i.State != "Resolved"),
            "Healthy workers have no active incidents");

        // Trigger worker failure
        workerTracker.RecordFailure("zalo", "Zalo token expired");
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var zaloInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "worker_stalled_zalo");
        Assert(zaloInc.State == "Active", "Worker failure triggers worker_stalled incident");

        // Worker recovers
        workerTracker.RecordSuccess("zalo");
        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var resolvedZalo = incidentsResp.Incidents.Single(i => i.IncidentKey == "worker_stalled_zalo");
        Assert(resolvedZalo.State == "Resolved", "Worker incident auto-resolves when worker recovers");

        // --- Scenario 5: Notification Failure & Retry Queue ---
        fakeNotifier.ShouldThrow = true; // Simulate SMTP failure
        fakeClock.Advance(TimeSpan.FromMinutes(20));
        workerTracker.RecordFailure("recovery-email", "SMTP timeout");
        await incidentService.TriggerIncidentCheckAsync(default);

        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var emailInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "worker_stalled_recovery-email");
        Assert(emailInc.State == "Active", "Incident created even when SMTP throws");
        Assert(emailInc.LastNotifiedAt == null, "LastNotifiedAt remains null when SMTP delivery fails");
        Assert(emailInc.NotificationCount == 0, "NotificationCount remains 0 when SMTP delivery fails");

        // Next cycle: SMTP server is back online
        fakeNotifier.ShouldThrow = false;
        fakeClock.Advance(TimeSpan.FromMinutes(1));
        await incidentService.TriggerIncidentCheckAsync(default);

        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var retriedInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "worker_stalled_recovery-email");
        Assert(retriedInc.LastNotifiedAt != null, "LastNotifiedAt updated after successful retry");
        Assert(retriedInc.NotificationCount == 1, "NotificationCount incremented after successful retry");
    }

    private static async Task TestSmtpIncidentNotifierAsync()
    {
        var fakeEmailSender = new FakeEmailSender();
        var options = Options.Create(new IncidentNotificationOptions
        {
            AlertEmailTo = "ops-team@rehouse.vn",
            AlertEmailFrom = "alerts@rehouse.vn",
            Enabled = true
        });

        var notifier = new SmtpIncidentNotifier(options, NullLogger<SmtpIncidentNotifier>.Instance, fakeEmailSender);

        var sampleIncident = new MonitoringIncident
        {
            Id = Guid.NewGuid(),
            IncidentKey = "host_disk_high",
            RuleName = "High Host Disk Utilization",
            Severity = "Warning",
            State = "Active",
            Summary = "Host Disk usage is 89.5% (>= 85% threshold)",
            Details = "Disk percent: 89.5%",
            StartedAt = DateTimeOffset.Parse("2026-10-20T16:00:00Z"),
            AcknowledgedAt = null,
            ResolvedAt = null,
            LastNotifiedAt = DateTimeOffset.Parse("2026-10-20T16:00:00Z"),
            NotificationCount = 1
        };

        // Test alert email
        await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default);
        Assert(fakeEmailSender.SentEmails.Count == 1, "Sent 1 alert email");
        var sentAlert = fakeEmailSender.SentEmails[0];
        Assert(sentAlert.To == "ops-team@rehouse.vn", "Sent to configured ops email");
        Assert(sentAlert.Subject.Contains("[ALERT - WARNING] High Host Disk Utilization"), "Subject formatted with severity");
        Assert(sentAlert.Body.Contains("Note: SMTP acceptance by server does not guarantee final mailbox delivery"),
            "Body includes required SMTP delivery disclaimer");

        // Test recovery email
        sampleIncident.ResolvedAt = DateTimeOffset.Parse("2026-10-20T16:30:00Z");
        await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: true, default);
        Assert(fakeEmailSender.SentEmails.Count == 2, "Sent recovery email");
        var sentRecovery = fakeEmailSender.SentEmails[1];
        Assert(sentRecovery.Subject.Contains("[RESOLVED] High Host Disk Utilization"), "Subject formatted with [RESOLVED]");
    }

    private static async Task TestOperationsAndIncidentsHttpEndpointsAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddMonitoringCore(builder.Configuration);
        builder.Services.AddMonitoringOperations(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("fixture-ops").AddScheme<AuthenticationSchemeOptions, OpsTestFixtureAuth>("fixture-ops", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("analytics",
            _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseMiddleware<RouteLatencyMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMonitoringEndpoints();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Seed worker heartbeats for all 6 workers
        var workerTracker = scope.ServiceProvider.GetRequiredService<IWorkerStateTracker>();
        await workerTracker.RecordWorkerHeartbeatAsync("invoice-sweep", isEnabled: true, status: "Healthy", errorMessage: null, default);
        await workerTracker.RecordWorkerHeartbeatAsync("reconcile", isEnabled: true, status: "Healthy", errorMessage: null, default);
        await workerTracker.RecordWorkerHeartbeatAsync("recovery-email", isEnabled: true, status: "Healthy", errorMessage: null, default);
        await workerTracker.RecordWorkerHeartbeatAsync("zalo", isEnabled: true, status: "Healthy", errorMessage: null, default);
        await workerTracker.RecordWorkerHeartbeatAsync("cleanup", isEnabled: true, status: "Healthy", errorMessage: null, default);
        await workerTracker.RecordWorkerHeartbeatAsync("ops-aggregation", isEnabled: true, status: "Healthy", errorMessage: null, default);

        var adminClient = app.GetTestClient();
        adminClient.BaseAddress = new Uri("http://localhost");
        adminClient.DefaultRequestHeaders.Add("X-Fixture-Role", "Admin");

        // 1. GET /api/admin/monitoring/operations returns 200 OK
        var opsResp = await adminClient.GetAsync("/api/admin/monitoring/operations");
        Assert(opsResp.StatusCode == HttpStatusCode.OK, "Admin operations endpoint returns 200 OK with T3 implemented");

        var opsData = await opsResp.Content.ReadFromJsonAsync<AdminMonitoringOperationsResponse>();
        Assert(opsData != null, "Operations response parsed successfully");
        Assert(opsData!.Overview.OverallStatus == "Healthy", "Default overview status is Healthy");
        Assert(opsData.Overview.Window == "1h", "Default window is 1h");
        Assert(opsData.Workers.Any(w => w.WorkerName == "backup-watchdog"), "backup-watchdog surfaced as WorkerStatusDto");
        Assert(opsData.Workers.Count(w => w.WorkerName != "backup-watchdog") == 6, "All 6 application workers tracked");
        Assert(opsData.Workers.Where(w => w.WorkerName != "backup-watchdog").All(w => w.Status == "Healthy"), "All 6 application workers healthy");
        Assert(opsData.Workers.Single(w => w.WorkerName == "backup-watchdog").Status == "Unknown", "Missing collector surfaces backup-watchdog as Unknown");
        Assert(opsData.Queues != null, "Operational queues stats present");

        // 2. Query with custom window
        var ops24hResp = await adminClient.GetAsync("/api/admin/monitoring/operations?window=24h");
        Assert(ops24hResp.StatusCode == HttpStatusCode.OK, "Operations with ?window=24h returns 200 OK");
        var ops24hData = await ops24hResp.Content.ReadFromJsonAsync<AdminMonitoringOperationsResponse>();
        Assert(ops24hData!.Overview.Window == "24h", "Window correctly set to 24h");

        // 3. GET /api/admin/monitoring/incidents
        var testIncident = new MonitoringIncident
        {
            IncidentKey = "manual_test_alert",
            RuleName = "Manual Verification Alert",
            Severity = "Critical",
            State = "Active",
            Summary = "Testing incident endpoint response",
            Details = "Automated test suite verification",
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.MonitoringIncidents.Add(testIncident);
        await db.SaveChangesAsync();

        var incidentsResp = await adminClient.GetAsync("/api/admin/monitoring/incidents");
        Assert(incidentsResp.StatusCode == HttpStatusCode.OK, "Admin incidents endpoint returns 200 OK");
        var incidentsData = await incidentsResp.Content.ReadFromJsonAsync<AdminMonitoringIncidentsResponse>();
        Assert(incidentsData != null, "Incidents response parsed");
        Assert(incidentsData!.ActiveIncidentCount == 1, "Active incident count is 1");
        Assert(incidentsData.Incidents.Count == 1, "Returned 1 incident item");
        Assert(incidentsData.Incidents[0].IncidentKey == "manual_test_alert", "Incident key matches");

        // 4. PATCH /api/admin/monitoring/incidents/{id} (Acknowledge)
        var ackRequest = new UpdateIncidentRequest("Acknowledged", "Investigating root cause");
        var ackResp = await adminClient.PatchAsJsonAsync($"/api/admin/monitoring/incidents/{testIncident.Id}", ackRequest);
        if (ackResp.StatusCode != HttpStatusCode.OK)
        {
            var err = await ackResp.Content.ReadAsStringAsync();
            throw new Exception($"ackResp failed with {ackResp.StatusCode}: {err}");
        }
        Assert(ackResp.StatusCode == HttpStatusCode.OK, "Update incident state returned 200 OK");
        var updatedIncident = await ackResp.Content.ReadFromJsonAsync<IncidentDto>();
        Assert(updatedIncident != null && updatedIncident.State == "Acknowledged", "Incident transitioned to Acknowledged");
        Assert(updatedIncident!.Details.Contains("Investigating root cause"), "Admin note persisted to incident details");

        // 5. PATCH /api/admin/monitoring/incidents/{id} (Resolve)
        var resolveRequest = new UpdateIncidentRequest("Resolved", "Service restored");
        var resolveResp = await adminClient.PatchAsJsonAsync($"/api/admin/monitoring/incidents/{testIncident.Id}", resolveRequest);
        Assert(resolveResp.StatusCode == HttpStatusCode.OK, "Resolve incident returned 200 OK");
        var resolvedIncident = await resolveResp.Content.ReadFromJsonAsync<IncidentDto>();
        Assert(resolvedIncident != null && resolvedIncident.State == "Resolved", "Incident transitioned to Resolved");
        Assert(resolvedIncident!.ResolvedAt != null, "ResolvedAt timestamp recorded");
    }

    private sealed class OpsTestFixtureAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var userId = role == "Admin" ? AdminUserId : OwnerUserId;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Name, role == "Admin" ? "Test Admin" : "Test Owner")
            };
            var identity = new ClaimsIdentity(claims, "fixture-ops");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "fixture-ops");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeAlertNotifier : IIncidentNotifier
    {
        public bool ShouldThrow { get; set; }
        public List<(MonitoringIncident Incident, bool IsRecovery)> Alerts { get; } = new();

        public Task SendIncidentAlertAsync(MonitoringIncident incident, bool isRecovery, CancellationToken ct)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("SMTP connection refused");
            }

            Alerts.Add((incident, isRecovery));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostReader : ILinuxHostCollectorMetricsReader, IHostMetricsReader
    {
        public HostCollectorMetrics? Metrics { get; set; }
        public Task<HostCollectorMetrics?> ReadHostMetricsAsync(CancellationToken ct = default) => Task.FromResult(Metrics);
        Task<HostMetricsDto?> IHostMetricsReader.ReadHostMetricsAsync(CancellationToken ct)
        {
            if (Metrics == null) return Task.FromResult<HostMetricsDto?>(null);
            return Task.FromResult<HostMetricsDto?>(new HostMetricsDto(
                Metrics.CpuPercent,
                Metrics.MemoryPercent,
                Metrics.DiskPercent,
                Metrics.ContainerCount,
                Metrics.HealthEndpointStatus,
                Metrics.HostStatus,
                Metrics.RecordedAt,
                Metrics.IsStale
            ));
        }
    }

    private sealed class FakeEmailSender : IAlertEmailSender
    {
        public List<(string To, string Subject, string Body)> SentEmails { get; } = new();

        public Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            SentEmails.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }
}

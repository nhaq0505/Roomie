using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Features.Monitoring.Operations;
using RoomRental.Api.Services;
using RoomRental.Tests;

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
        await TestProductionDiAlertTransportAsync();
        await TestRouteLatencyMiddlewareRealHttpPipelineAsync();
        TestWorkerStartVsSuccessRegressions();
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

        var histogram = aggregator.GetWindowSummary(TimeSpan.FromHours(1)).HistogramBuckets;
        Assert(histogram.Select(b => b.Count).SequenceEqual(new[] { 90, 5, 0, 5, 0, 0, 0 }),
            "Histogram exposes exclusive latency bucket counts");
        fakeClock.Advance(TimeSpan.FromMinutes(1));
        var flushed = aggregator.DrainCompletedMinuteBuckets(fakeClock.GetUtcNow().ToUnixTimeSeconds() / 60);
        Assert(flushed.Sum(b => b.RequestCount) == 100, "Minute flush writes all recorded requests");
        Assert(aggregator.DrainCompletedMinuteBuckets(fakeClock.GetUtcNow().ToUnixTimeSeconds() / 60).Count == 0,
            "Completed minute is not flushed twice");
        Assert(aggregator.GetWindowSummary(TimeSpan.FromHours(1)).TotalRequests == 100,
            "Minute flush preserves the reporting window");

        // Test 4: Pruning expired requests
        fakeClock.Advance(TimeSpan.FromMinutes(185));
        var prunedOverview = aggregator.GetOverviewSnapshot(TimeSpan.FromHours(1), dbConnected: true, dbStatus: "Healthy");
        Assert(prunedOverview.TotalRequestsLastHour == 0, "Requests older than 1h expired from 1h window");
        Assert(prunedOverview.Api5xxRatePercent == null, "Api5xxRatePercent is null when 0 requests");
        Assert(prunedOverview.OverallStatus == "Healthy", "Status healthy after old traffic expired");

        aggregator.Record("/api/current", "GET", 200, 2500);
        Assert(aggregator.GetWindowSummary(TimeSpan.FromHours(24)).TotalRequests == 101,
            "24h window includes traffic older than one hour");
        fakeClock.Advance(TimeSpan.FromHours(23));
        aggregator.Record("/api/current", "GET", 200, 20);
        Assert(aggregator.GetWindowSummary(TimeSpan.FromHours(24)).TotalRequests == 2,
            "24h window expires old traffic after pruning");
        aggregator.Reset();
        Assert(aggregator.GetWindowSummary(TimeSpan.FromHours(24)).HistogramBuckets.Count == 0,
            "Empty window exposes no fabricated histogram");

        return Task.CompletedTask;
    }

    private static Task TestWorkerTelemetryTrackerAsync()
    {
        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var tracker = new WorkerTelemetryTracker(fakeClock);

        // Test 1: Record successful heartbeat
        tracker.RecordHeartbeat("invoice-sweep", isEnabled: true, status: "Healthy");
        var statuses = tracker.GetStatuses();
        Assert(statuses.Count == 1, "Tracked 1 worker");
        Assert(statuses[0].WorkerName == "invoice-sweep", "Worker name matches");
        Assert(statuses[0].Status == "Healthy", "Worker status is Healthy");
        Assert(statuses[0].ConsecutiveFailures == 0, "0 consecutive failures on success");
        Assert(!statuses[0].IsStale, "Worker is not stale");

        // Test 2: Record worker failure
        tracker.RecordHeartbeat("invoice-sweep", isEnabled: true, status: "Failed", errorMessage: "SMTP timeout");
        statuses = tracker.GetStatuses();
        Assert(statuses[0].Status == "Failed", "Failed worker has status Failed");
        Assert(statuses[0].ConsecutiveFailures == 1, "1 consecutive failure recorded");
        Assert(statuses[0].LastErrorMessage == "SMTP timeout", "Error message recorded");

        // Test 3: SLA staleness evaluation
        tracker.RecordHeartbeat("reconcile", isEnabled: true, status: "Healthy"); // reconcile SLA is 10m
        fakeClock.Advance(TimeSpan.FromMinutes(15));
        statuses = tracker.GetStatuses();
        var reconcileStatus = statuses.Single(s => s.WorkerName == "reconcile");
        Assert(reconcileStatus.IsStale, "Reconcile worker is stale after 15m (> 10m SLA)");

        var sweepStatus = statuses.Single(s => s.WorkerName == "invoice-sweep"); // invoice-sweep SLA is 26h
        Assert(!sweepStatus.IsStale, "Invoice sweep worker is NOT stale after 15m (within 26h SLA)");

        // Test 4: Disabled worker is never stale
        tracker.RecordHeartbeat("reconcile", isEnabled: false, status: "Healthy");
        statuses = tracker.GetStatuses();
        reconcileStatus = statuses.Single(s => s.WorkerName == "reconcile");
        Assert(!reconcileStatus.IsStale, "Disabled worker is never marked stale");

        // Test 5: Invoice sweep exceeds 26h SLA
        fakeClock.Advance(TimeSpan.FromHours(27));
        statuses = tracker.GetStatuses();
        sweepStatus = statuses.Single(s => s.WorkerName == "invoice-sweep");
        Assert(sweepStatus.IsStale, "Invoice sweep worker is stale after exceeding 26h SLA");

        return Task.CompletedTask;
    }

    private static async Task TestIncidentEngineAndAlertsAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var routeAggregator = new RouteLatencyAggregator(fakeClock);
        var hostReader = new FakeHostReader();
        var workerTracker = new WorkerTelemetryTracker(fakeClock);
        var fakeNotifier = new FakeAlertNotifier();

        var incidentService = new IncidentMonitoringService(
            db,
            routeAggregator,
            hostReader,
            workerTracker,
            fakeNotifier,
            fakeClock,
            NullLogger<IncidentMonitoringService>.Instance
        );

        // --- Scenario 1: 5xx Error Rate Rule (Exact: > 5.0% over 5-minute window with min 100 requests) ---
        // Traffic < 100 requests (99 requests, 10 5xx): NO incident should be triggered!
        for (int i = 0; i < 89; i++) routeAggregator.Record("/api/test", "GET", 200, 20.0);
        for (int i = 0; i < 10; i++) routeAggregator.Record("/api/test", "GET", 500, 50.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        var incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(incidentsResp.ActiveIncidentCount == 0, "No incident triggered when 5-min requests < 100");
        Assert(fakeNotifier.Alerts.Count == 0, "No alert email sent when requests < 100");

        // Add 1 more 5xx request -> 100 total requests, 11 5xx (11.0% rate) -> Critical incident!
        routeAggregator.Record("/api/test", "GET", 500, 50.0);
        await incidentService.TriggerIncidentCheckAsync(default);

        incidentsResp = await incidentService.GetIncidentsAsync(default);
        Assert(incidentsResp.ActiveIncidentCount == 1, "Incident triggered when requests >= 100 and 5xx rate > 5%");
        var activeInc = incidentsResp.Incidents[0];
        Assert(activeInc.Severity == "Critical", "Severity is Critical for 5xx rate >= 10%");
        Assert(activeInc.State == "Active", "Incident state is Active");
        Assert(fakeNotifier.Alerts.Count == 1, "Sent 1 initial alert email");
        Assert(!fakeNotifier.Alerts[0].IsRecovery, "Alert is not a recovery notice");
        Assert(activeInc.NotificationCount == 1, "Notification count is 1");

        // Repeat check immediately -> cooldown suppresses duplicate alert
        await incidentService.TriggerIncidentCheckAsync(default);
        Assert(fakeNotifier.Alerts.Count == 1, "No duplicate alert sent within 1-hour cooldown");

        // Unknown state protection: low traffic window (< 100 requests) does NOT auto-resolve active incident!
        fakeClock.Advance(TimeSpan.FromMinutes(10));
        // Feed only 10 requests (all 200) -> traffic too low to prove healthy
        for (int i = 0; i < 10; i++) routeAggregator.Record("/api/test", "GET", 200, 20.0);
        await incidentService.TriggerIncidentCheckAsync(default);

        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var persistentInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(persistentInc.State == "Active", "Incident remains Active during low-traffic unknown window");

        // Advance past 1-hour cooldown with continued high 5xx -> reminder email sent!
        fakeClock.Advance(TimeSpan.FromMinutes(55));
        for (int i = 0; i < 90; i++) routeAggregator.Record("/api/test", "GET", 200, 20.0);
        for (int i = 0; i < 10; i++) routeAggregator.Record("/api/test", "GET", 500, 50.0);
        await incidentService.TriggerIncidentCheckAsync(default);

        Assert(fakeNotifier.Alerts.Count == 2, "Reminder alert sent after 1 hour cooldown");
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        persistentInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(persistentInc.NotificationCount == 2, "Notification count incremented to 2");

        // Proven healthy: 100 requests with 0 5xx -> auto-resolves!
        fakeClock.Advance(TimeSpan.FromMinutes(6));
        for (int i = 0; i < 100; i++) routeAggregator.Record("/api/test", "GET", 200, 20.0);
        await incidentService.TriggerIncidentCheckAsync(default);

        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var resolvedInc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_5xx_rate");
        Assert(resolvedInc.State == "Resolved", "Incident auto-resolved when condition explicitly healthy");
        Assert(resolvedInc.ResolvedAt != null, "ResolvedAt timestamp recorded");
        Assert(fakeNotifier.Alerts.Count == 3, "Sent recovery notification email");
        Assert(fakeNotifier.Alerts[2].IsRecovery, "Third notification is recovery notice");

        // --- Scenario 2: High P95 Latency Rule (Exact: > 2000ms over 10-minute window with min 100 requests) ---
        fakeClock.Advance(TimeSpan.FromMinutes(10));
        // Record 100 requests where P95 > 2000ms (90 at 100ms, 10 at 2500ms)
        for (int i = 0; i < 90; i++) routeAggregator.Record("/api/slow", "GET", 200, 100.0);
        for (int i = 0; i < 10; i++) routeAggregator.Record("/api/slow", "GET", 200, 2500.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        incidentsResp = await incidentService.GetIncidentsAsync(default);
        var p95Inc = incidentsResp.Incidents.Single(i => i.IncidentKey == "high_p95_latency");
        Assert(p95Inc.State == "Active", "High P95 incident triggered for latency > 2000ms");
        Assert(p95Inc.Severity == "Critical", "P95 severity is Critical");

        // Auto-resolve P95 with 100 fast requests
        fakeClock.Advance(TimeSpan.FromMinutes(11));
        for (int i = 0; i < 100; i++) routeAggregator.Record("/api/slow", "GET", 200, 50.0);
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

        var notifier = new SmtpIncidentNotifier(options, fakeEmailSender, NullLogger<SmtpIncidentNotifier>.Instance);

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

        // Test alert email success
        await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default);
        Assert(fakeEmailSender.SentEmails.Count == 1, "Sent 1 alert email");
        var sentAlert = fakeEmailSender.SentEmails[0];
        Assert(sentAlert.To == "ops-team@rehouse.vn", "Sent to configured ops email");
        Assert(sentAlert.Subject.Contains("[ALERT - WARNING] High Host Disk Utilization"), "Subject formatted with severity");
        Assert(sentAlert.Body.Contains("Note: SMTP acceptance by server does not guarantee final mailbox delivery"),
            "Body includes required SMTP delivery disclaimer");

        // Test recovery email success
        sampleIncident.ResolvedAt = DateTimeOffset.Parse("2026-10-20T16:30:00Z");
        await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: true, default);
        Assert(fakeEmailSender.SentEmails.Count == 2, "Sent recovery email");
        var sentRecovery = fakeEmailSender.SentEmails[1];
        Assert(sentRecovery.Subject.Contains("[RESOLVED] High Host Disk Utilization"), "Subject formatted with [RESOLVED]");

        // Test missing recipient throws InvalidOperationException (never silently succeeds)
        var noRecipientOpts = Options.Create(new IncidentNotificationOptions { AlertEmailTo = null, Enabled = true });
        var noRecipientNotifier = new SmtpIncidentNotifier(noRecipientOpts, fakeEmailSender, NullLogger<SmtpIncidentNotifier>.Instance);
        bool threwNoRecipient = false;
        try { await noRecipientNotifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default); }
        catch (InvalidOperationException) { threwNoRecipient = true; }
        Assert(threwNoRecipient, "Missing recipient throws InvalidOperationException (does not silently succeed)");

        // Test disabled alerting throws InvalidOperationException (never silently succeeds)
        var disabledOpts = Options.Create(new IncidentNotificationOptions { AlertEmailTo = "ops@rehouse.vn", Enabled = false });
        var disabledNotifier = new SmtpIncidentNotifier(disabledOpts, fakeEmailSender, NullLogger<SmtpIncidentNotifier>.Instance);
        bool threwDisabled = false;
        try { await disabledNotifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default); }
        catch (InvalidOperationException) { threwDisabled = true; }
        Assert(threwDisabled, "Disabled alerting throws InvalidOperationException (does not silently succeed)");

        // Deployment image recipient works when an empty Compose value overrides the section.
        var previousRecipient = Environment.GetEnvironmentVariable("MONITORING_ALERT_TO");
        try
        {
            Environment.SetEnvironmentVariable("MONITORING_ALERT_TO", "build-fixture@example.invalid");
            var fallbackNotifier = new SmtpIncidentNotifier(
                Options.Create(new IncidentNotificationOptions { AlertEmailTo = " ", Enabled = true }),
                fakeEmailSender, NullLogger<SmtpIncidentNotifier>.Instance);
            await fallbackNotifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default);
            Assert(fakeEmailSender.SentEmails.Last().To == "build-fixture@example.invalid",
                "Empty Compose recipient falls back to deployment image recipient");
            await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default);
            Assert(fakeEmailSender.SentEmails.Last().To == "ops-team@rehouse.vn",
                "Explicit runtime recipient overrides deployment image recipient");
        }
        finally { Environment.SetEnvironmentVariable("MONITORING_ALERT_TO", previousRecipient); }

        // Test transport failure propagates out (never silently succeeds)
        fakeEmailSender.ShouldThrow = true;
        bool threwTransport = false;
        try { await notifier.SendIncidentAlertAsync(sampleIncident, isRecovery: false, default); }
        catch (InvalidOperationException) { threwTransport = true; }
        Assert(threwTransport, "Transport failure propagates exception and is not counted as sent");
        fakeEmailSender.ShouldThrow = false;
    }

    private static async Task TestProductionDiAlertTransportAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);

        var fakeSink = new FakeEmailSender();
        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(fixture.ConnectionString));
        builder.Services.AddSingleton<TimeProvider>(fakeClock);
        builder.Services.AddMonitoringCore(builder.Configuration);
        builder.Services.AddMonitoringOperations(builder.Configuration);

        // Override IAlertEmailSender with our test sink to verify DI wireup
        builder.Services.RemoveAll<IAlertEmailSender>();
        builder.Services.AddSingleton<IAlertEmailSender>(fakeSink);

        // Configure options with dedicated monitoring recipient
        builder.Services.Configure<IncidentNotificationOptions>(opts =>
        {
            opts.AlertEmailTo = "ops-team@rehouse.vn";
            opts.AlertEmailFrom = "monitoring@rehouse.vn";
            opts.Enabled = true;
        });

        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var incidentService = scope.ServiceProvider.GetRequiredService<IIncidentService>();
        var aggregator = scope.ServiceProvider.GetRequiredService<IRouteLatencyAggregator>();

        // 1. Initial incident trigger with working transport
        // Feed 100 requests with 10 5xx to trigger high_5xx_rate (10% rate)
        for (int i = 0; i < 90; i++) aggregator.Record("/api/invoices", "GET", 200, 25.0);
        for (int i = 0; i < 10; i++) aggregator.Record("/api/invoices", "GET", 500, 50.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        Assert(fakeSink.SentEmails.Count == 1, "DI-wired fake transport received initial alert email");
        var initialSent = fakeSink.SentEmails[0];
        Assert(initialSent.To == "ops-team@rehouse.vn", "Alert sent to dedicated monitoring recipient");
        Assert(initialSent.Subject.Contains("[ALERT - CRITICAL]"), "Subject formatted with severity");

        var dbInc = await db.MonitoringIncidents.SingleAsync(i => i.IncidentKey == "high_5xx_rate");
        Assert(dbInc.LastNotifiedAt != null, "LastNotifiedAt persisted after successful delivery");
        Assert(dbInc.NotificationCount == 1, "NotificationCount is 1 after initial delivery");

        // 2. Reminder alert with transport failure
        fakeClock.Advance(TimeSpan.FromHours(1.1)); // Exceed 1h cooldown
        for (int i = 0; i < 90; i++) aggregator.Record("/api/invoices", "GET", 200, 25.0);
        for (int i = 0; i < 10; i++) aggregator.Record("/api/invoices", "GET", 500, 50.0);
        fakeSink.ShouldThrow = true; // Transport goes down

        await incidentService.TriggerIncidentCheckAsync(default);
        // Delivery failed, NotificationCount must NOT advance, LastNotifiedAt must NOT change
        db.ChangeTracker.Clear();
        var failedInc = await db.MonitoringIncidents.SingleAsync(i => i.IncidentKey == "high_5xx_rate");
        Assert(failedInc.NotificationCount == 1, "NotificationCount not incremented when transport fails");
        Assert(failedInc.LastNotifiedAt == dbInc.LastNotifiedAt, "LastNotifiedAt not updated when transport fails");

        // 3. Transport recovers: reminder delivered on next cycle
        fakeSink.ShouldThrow = false;
        fakeClock.Advance(TimeSpan.FromMinutes(2));
        for (int i = 0; i < 90; i++) aggregator.Record("/api/invoices", "GET", 200, 25.0);
        for (int i = 0; i < 10; i++) aggregator.Record("/api/invoices", "GET", 500, 50.0);

        await incidentService.TriggerIncidentCheckAsync(default);
        db.ChangeTracker.Clear();
        var retriedInc = await db.MonitoringIncidents.SingleAsync(i => i.IncidentKey == "high_5xx_rate");
        Assert(retriedInc.NotificationCount == 2, "NotificationCount incremented to 2 after transport recovery");
        Assert(retriedInc.LastNotifiedAt > dbInc.LastNotifiedAt, "LastNotifiedAt advanced after successful reminder delivery");
        Assert(fakeSink.SentEmails.Count == 2, "Fake sink received reminder alert");

        // 4. Recovery notice with transport failure and restart retry
        // Clear 5xx by recording 100 fast 200s in new minute
        fakeClock.Advance(TimeSpan.FromMinutes(10));
        for (int i = 0; i < 100; i++) aggregator.Record("/api/invoices", "GET", 200, 20.0);

        fakeSink.ShouldThrow = true; // Transport fails during recovery notice
        await incidentService.TriggerIncidentCheckAsync(default);

        db.ChangeTracker.Clear();
        var resolvedInc = await db.MonitoringIncidents.SingleAsync(i => i.IncidentKey == "high_5xx_rate");
        Assert(resolvedInc.State == "Resolved", "Incident resolved even when recovery notice SMTP throws");
        Assert(resolvedInc.Details.Contains("[PENDING_RECOVERY_RETRY]"), "Recovery notice flagged for retry in DB");

        // Simulate application restart with restored transport
        fakeSink.ShouldThrow = false;
        using var restartScope = app.Services.CreateScope();
        var restartedIncidentService = restartScope.ServiceProvider.GetRequiredService<IIncidentService>();
        var restartedDb = restartScope.ServiceProvider.GetRequiredService<AppDbContext>();

        await restartedIncidentService.TriggerIncidentCheckAsync(default);
        restartedDb.ChangeTracker.Clear();
        var finalizedInc = await restartedDb.MonitoringIncidents.SingleAsync(i => i.IncidentKey == "high_5xx_rate");
        Assert(!finalizedInc.Details.Contains("[PENDING_RECOVERY_RETRY]"), "Pending recovery notice retry completed and cleared");
        Assert(fakeSink.SentEmails.Count == 3, "Fake sink received recovery email upon retry");
        Assert(fakeSink.SentEmails[2].Subject.Contains("[RESOLVED]"), "Recovery email subject formatted with [RESOLVED]");
    }

    private static async Task TestRouteLatencyMiddlewareRealHttpPipelineAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var aggregator = new RouteLatencyAggregator(fakeClock);

        builder.Services.AddSingleton<TimeProvider>(fakeClock);
        builder.Services.AddSingleton<IRouteLatencyAggregator>(aggregator);
        builder.Services.AddAuthentication("test-auth").AddScheme<AuthenticationSchemeOptions, OpsTestFixtureAuth>("test-auth", _ => { });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseMiddleware<RouteLatencyMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/api/test-ok", () => Results.Ok(new { status = "success" }));
        app.MapGet("/api/test-api-exception-400", IResult () => throw new ApiException(400, "Invalid request payload"));
        app.MapGet("/api/test-api-exception-422", IResult () => throw new ApiException(422, "Unprocessable entity"));
        app.MapGet("/api/test-unhandled-500", IResult () => throw new InvalidOperationException("Unhandled database connection crash"));
        app.MapGet("/api/test-auth-challenge", [Microsoft.AspNetCore.Authorization.Authorize] () => Results.Ok(new { secret = 123 }));
        app.MapGet("/api/test-admin-forbidden", [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")] () => Results.Ok(new { admin = true }));

        await app.StartAsync();

        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");

        // 1. Success 200
        var respOk = await client.GetAsync("/api/test-ok");
        Assert(respOk.StatusCode == HttpStatusCode.OK, "Real HTTP pipeline returned 200 OK");
        var snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var okSnap = snaps.Single(s => s.RouteTemplate == "/api/test-ok");
        Assert(okSnap.RequestCount == 1, "/api/test-ok recorded 1 request");
        Assert(okSnap.Status5xxCount == 0, "/api/test-ok recorded 0 5xx");

        // 2. ApiException 400
        var resp400 = await client.GetAsync("/api/test-api-exception-400");
        Assert(resp400.StatusCode == HttpStatusCode.BadRequest, "Real HTTP pipeline returned 400 BadRequest");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap400 = snaps.Single(s => s.RouteTemplate == "/api/test-api-exception-400");
        Assert(snap400.RequestCount == 1, "/api/test-api-exception-400 recorded 1 request");
        Assert(snap400.Status5xxCount == 0, "/api/test-api-exception-400 recorded 0 5xx (correctly treated as 4xx, not 500)");

        // 3. ApiException 422
        var resp422 = await client.GetAsync("/api/test-api-exception-422");
        Assert(resp422.StatusCode == HttpStatusCode.UnprocessableEntity, "Real HTTP pipeline returned 422 UnprocessableEntity");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap422 = snaps.Single(s => s.RouteTemplate == "/api/test-api-exception-422");
        Assert(snap422.RequestCount == 1, "/api/test-api-exception-422 recorded 1 request");

        // 4. Unhandled 500 (Crucial regression test: MUST record 500, NEVER 200!)
        var resp500 = await client.GetAsync("/api/test-unhandled-500");
        Assert(resp500.StatusCode == HttpStatusCode.InternalServerError, "Real HTTP pipeline returned 500 InternalServerError");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap500 = snaps.Single(s => s.RouteTemplate == "/api/test-unhandled-500");
        Assert(snap500.RequestCount == 1, "/api/test-unhandled-500 recorded 1 request");
        Assert(snap500.Status5xxCount == 1, "/api/test-unhandled-500 recorded 5xx count == 1 (NOT 200!)");

        // 5. Auth Challenge 401
        var resp401 = await client.GetAsync("/api/test-auth-challenge");
        Assert(resp401.StatusCode == HttpStatusCode.Unauthorized, "Real HTTP pipeline returned 401 Unauthorized");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap401 = snaps.Single(s => s.RouteTemplate == "/api/test-auth-challenge");
        Assert(snap401.RequestCount == 1, "/api/test-auth-challenge recorded 1 request");
        Assert(snap401.Status5xxCount == 0, "/api/test-auth-challenge recorded 0 5xx");

        // 6. Forbidden 403 (Owner trying to access Admin endpoint)
        var forbiddenReq = new HttpRequestMessage(HttpMethod.Get, "/api/test-admin-forbidden");
        forbiddenReq.Headers.Add("X-Fixture-Role", "Owner");
        var resp403 = await client.SendAsync(forbiddenReq);
        Assert(resp403.StatusCode == HttpStatusCode.Forbidden, "Real HTTP pipeline returned 403 Forbidden");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap403 = snaps.Single(s => s.RouteTemplate == "/api/test-admin-forbidden");
        Assert(snap403.RequestCount == 1, "/api/test-admin-forbidden recorded 1 request");

        // 7. Not Found 404
        var resp404 = await client.GetAsync("/api/totally-nonexistent-path");
        Assert(resp404.StatusCode == HttpStatusCode.NotFound, "Real HTTP pipeline returned 404 NotFound");
        snaps = aggregator.GetRouteSnapshots(TimeSpan.FromHours(1));
        var snap404 = snaps.Single(s => s.RouteTemplate == "/api/totally-nonexistent-path");
        Assert(snap404.RequestCount == 1, "/api/totally-nonexistent-path recorded 1 request");
        Assert(snap404.Status5xxCount == 0, "/api/totally-nonexistent-path recorded 0 5xx");

        // 8. Drain buckets and verify actual recorded status codes for each pipeline request
        fakeClock.Advance(TimeSpan.FromMinutes(2));
        var buckets = aggregator.DrainCompletedMinuteBuckets(fakeClock.GetUtcNow().ToUnixTimeSeconds() / 60);

        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-ok" && b.StatusCode == 200), "Recorded status 200 for /api/test-ok");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-api-exception-400" && b.StatusCode == 400), "Recorded status 400 for /api/test-api-exception-400");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-api-exception-422" && b.StatusCode == 422), "Recorded status 422 for /api/test-api-exception-422");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-unhandled-500" && b.StatusCode == 500), "Recorded status 500 for /api/test-unhandled-500 (CRUCIAL: 500 recorded, NOT 200)");
        Assert(!buckets.Any(b => b.RouteTemplate == "/api/test-unhandled-500" && b.StatusCode == 200), "Never recorded 200 for unhandled 500 exception");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-auth-challenge" && b.StatusCode == 401), "Recorded status 401 for /api/test-auth-challenge");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/test-admin-forbidden" && b.StatusCode == 403), "Recorded status 403 for /api/test-admin-forbidden");
        Assert(buckets.Any(b => b.RouteTemplate == "/api/totally-nonexistent-path" && b.StatusCode == 404), "Recorded status 404 for /api/totally-nonexistent-path");
    }

    private static void TestWorkerStartVsSuccessRegressions()
    {
        var fakeClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var tracker = new WorkerTelemetryTracker(fakeClock);

        // 1. Worker starts: status is Running, LastStartedAt updated, LastSuccessAt remains null
        tracker.RecordStart("recovery-email");
        var statuses = tracker.GetStatuses();
        var emailWorker = statuses.Single(w => w.WorkerName == "recovery-email");
        Assert(emailWorker.Status == "Running", "Worker start updates Status to Running");
        Assert(emailWorker.LastStartedAt == fakeClock.GetUtcNow(), "Worker start updates LastStartedAt");
        Assert(emailWorker.LastSuccessAt == null, "Worker start does NOT modify LastSuccessAt (remains null)");
        Assert(!emailWorker.IsStale, "Worker is not stale within startup grace period");

        // 2. Worker completes successfully: status is Healthy, LastSuccessAt updated
        fakeClock.Advance(TimeSpan.FromSeconds(30));
        tracker.RecordSuccess("recovery-email");
        statuses = tracker.GetStatuses();
        emailWorker = statuses.Single(w => w.WorkerName == "recovery-email");
        Assert(emailWorker.Status == "Healthy", "Worker completion updates Status to Healthy");
        Assert(emailWorker.LastSuccessAt == fakeClock.GetUtcNow(), "Worker completion updates LastSuccessAt");
        Assert(emailWorker.ConsecutiveFailures == 0, "Worker completion clears ConsecutiveFailures");

        // 3. Worker starts next cycle: LastSuccessAt must NOT be modified
        fakeClock.Advance(TimeSpan.FromMinutes(2));
        var previousSuccess = emailWorker.LastSuccessAt;
        tracker.RecordStart("recovery-email");
        statuses = tracker.GetStatuses();
        emailWorker = statuses.Single(w => w.WorkerName == "recovery-email");
        Assert(emailWorker.Status == "Running", "Next cycle start updates Status to Running");
        Assert(emailWorker.LastStartedAt == fakeClock.GetUtcNow(), "Next cycle start updates LastStartedAt");
        Assert(emailWorker.LastSuccessAt == previousSuccess, "Next cycle start preserves previous LastSuccessAt");

        // 4. Worker fails: status is Failed, LastErrorAt updated, LastSuccessAt preserved
        fakeClock.Advance(TimeSpan.FromSeconds(10));
        tracker.RecordFailure("recovery-email", "Connection reset");
        statuses = tracker.GetStatuses();
        emailWorker = statuses.Single(w => w.WorkerName == "recovery-email");
        Assert(emailWorker.Status == "Failed", "Worker failure updates Status to Failed");
        Assert(emailWorker.LastErrorAt == fakeClock.GetUtcNow(), "Worker failure updates LastErrorAt");
        Assert(emailWorker.LastErrorMessage == "Connection reset", "Worker failure records error message");
        Assert(emailWorker.ConsecutiveFailures == 1, "Worker failure increments ConsecutiveFailures");
        Assert(emailWorker.LastSuccessAt == previousSuccess, "Worker failure does NOT modify LastSuccessAt");

        // 5. Per-worker SLA verification for all 6 workers
        Assert(WorkerTelemetryTracker.GetWorkerSla("recovery-email") == TimeSpan.FromMinutes(5), "recovery-email SLA is 5m");
        Assert(WorkerTelemetryTracker.GetWorkerSla("zalo") == TimeSpan.FromMinutes(5), "zalo SLA is 5m");
        Assert(WorkerTelemetryTracker.GetWorkerSla("reconcile") == TimeSpan.FromMinutes(10), "reconcile SLA is 10m");
        Assert(WorkerTelemetryTracker.GetWorkerSla("cleanup") == TimeSpan.FromMinutes(20), "cleanup SLA is 20m");
        Assert(WorkerTelemetryTracker.GetWorkerSla("ops-aggregation") == TimeSpan.FromHours(2), "ops-aggregation SLA is 2h");
        Assert(WorkerTelemetryTracker.GetWorkerSla("invoice-sweep") == TimeSpan.FromHours(26), "invoice-sweep SLA is 26h");

        // Test SLA staleness boundaries for each worker
        var workerNames = new[] { "recovery-email", "zalo", "reconcile", "cleanup", "ops-aggregation", "invoice-sweep" };
        foreach (var name in workerNames)
        {
            var sla = WorkerTelemetryTracker.GetWorkerSla(name);
            tracker.RecordSuccess(name);

            // Just before SLA
            fakeClock.Advance(sla - TimeSpan.FromMinutes(1));
            statuses = tracker.GetStatuses();
            var w = statuses.Single(x => x.WorkerName == name);
            Assert(!w.IsStale, $"{name} is NOT stale just before SLA ({sla.TotalMinutes}m)");

            // Exceed SLA
            fakeClock.Advance(TimeSpan.FromMinutes(2));
            statuses = tracker.GetStatuses();
            w = statuses.Single(x => x.WorkerName == name);
            Assert(w.IsStale, $"{name} is marked stale after exceeding SLA");
        }

        // 6. Disabled worker is never marked stale
        tracker.RecordHeartbeat("recovery-email", isEnabled: false, status: "Healthy");
        fakeClock.Advance(TimeSpan.FromDays(7));
        statuses = tracker.GetStatuses();
        emailWorker = statuses.Single(w => w.WorkerName == "recovery-email");
        Assert(!emailWorker.IsStale, "Disabled worker is NEVER marked stale even after 7 days");

        // 7. Startup grace period (10 minutes)
        var freshClock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-20T16:00:00Z"));
        var freshTracker = new WorkerTelemetryTracker(freshClock);
        freshTracker.RecordStart("cleanup"); // never succeeded yet

        freshClock.Advance(TimeSpan.FromMinutes(9));
        statuses = freshTracker.GetStatuses();
        var cleanupWorker = statuses.Single(w => w.WorkerName == "cleanup");
        Assert(!cleanupWorker.IsStale, "Worker without success is within startup grace period at 9m");

        freshClock.Advance(TimeSpan.FromMinutes(2)); // now at 11m
        statuses = freshTracker.GetStatuses();
        cleanupWorker = statuses.Single(w => w.WorkerName == "cleanup");
        Assert(cleanupWorker.IsStale, "Worker without success exceeds 10m startup grace period and becomes stale");
    }

    private static async Task TestOperationsAndIncidentsHttpEndpointsAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        builder.Services.AddMonitoringCore(builder.Configuration);
        builder.Services.AddMonitoringOperations(builder.Configuration);
        // Endpoint fixture controls incident state explicitly; scheduler has its own integration tests.
        builder.Services.Configure<MonitoringScheduleOptions>(o =>
        {
            o.IncidentChecksEnabled = false;
            o.MaintenanceEnabled = false;
        });
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

        app.Services.GetRequiredService<IRouteLatencyAggregator>().Record("/api/histogram-fixture", "GET", 200, 2500);

        // 1. GET /api/admin/monitoring/operations returns 200 OK
        var opsResp = await adminClient.GetAsync("/api/admin/monitoring/operations");
        Assert(opsResp.StatusCode == HttpStatusCode.OK, "Admin operations endpoint returns 200 OK with T3 implemented");

        var opsData = await opsResp.Content.ReadFromJsonAsync<AdminMonitoringOperationsResponse>();
        Assert(opsData != null, "Operations response parsed successfully");
        Assert(opsData!.Overview.OverallStatus == "Healthy", "Default overview status is Healthy");
        Assert(opsData.Overview.Window == "1h", "Default window is 1h");
        Assert(opsData.Workers.Any(w => w.WorkerName == "backup-watchdog"), "backup-watchdog surfaced as WorkerStatusDto");
        Assert(opsData.Workers.Count(w => w.WorkerName != "backup-watchdog") == 8, "All 8 application workers tracked, including disabled schedules");
        Assert(opsData.Workers.Where(w => w.WorkerName != "backup-watchdog" && w.IsEnabled).All(w => w.Status == "Healthy"), "All enabled application workers healthy");
        Assert(opsData.Workers.Single(w => w.WorkerName == "backup-watchdog").Status == "Unknown", "Missing collector surfaces backup-watchdog as Unknown");
        Assert(opsData.Queues != null, "Operational queues stats present");
        Assert(opsData.HistogramBuckets?.Count == 7 && opsData.HistogramBuckets[^1].Count == 1 && opsData.HistogramBuckets[^1].IsViolating,
            "Operations endpoint serializes histogram counts and slow-request marker");

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
        var ackData = await ackResp.Content.ReadFromJsonAsync<IncidentDto>();
        Assert(ackData!.State == "Acknowledged", "Incident transitioned to Acknowledged");
        Assert(ackData.Details.Contains("Investigating root cause"), "Admin note persisted to incident details");

        // 5. PATCH /api/admin/monitoring/incidents/{id} (Resolve)
        var resolveRequest = new UpdateIncidentRequest("Resolved", "Issue mitigated by operations");
        var resolveResp = await adminClient.PatchAsJsonAsync($"/api/admin/monitoring/incidents/{testIncident.Id}", resolveRequest);
        Assert(resolveResp.StatusCode == HttpStatusCode.OK, "Resolve incident returned 200 OK");
        var resolveData = await resolveResp.Content.ReadFromJsonAsync<IncidentDto>();
        Assert(resolveData!.State == "Resolved", "Incident transitioned to Resolved");
        Assert(resolveData.ResolvedAt != null, "ResolvedAt timestamp recorded");
    }

    private sealed class OpsTestFixtureAuth : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public OpsTestFixtureAuth(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Context.Request.Headers["X-Fixture-Role"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var userId = role == "Admin" ? Guid.Parse("11111111-1111-1111-1111-111111111111") : Guid.Parse("22222222-2222-2222-2222-222222222222");
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
        public bool ShouldThrow { get; set; }
        public List<(string To, string Subject, string Body)> SentEmails { get; } = new();

        public Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("SMTP connection refused: connection timed out");
            }
            SentEmails.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }
}

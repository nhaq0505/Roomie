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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Services;

internal static class MonitoringFoundationTests
{
    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly Guid OwnerUserId = Guid.NewGuid();

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("ASSERTION FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync()
    {
        await TestMigrationsAndTablesAsync();
        await TestClientAnalyticsServiceAsync();
        await TestOwnerMilestoneTrackerAsync();
        await TestCampaignCostsServiceAsync();
        await TestWorkerHeartbeatTrackerAsync();
        await TestMonitoringHttpEndpointsAsync();
    }

    private static async Task TestMigrationsAndTablesAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteAppDbContext(options);
        await db.Database.MigrateAsync();

        var expectedTables = new[]
        {
            "AnalyticsEvents",
            "RegistrationAttributions",
            "OwnerMilestones",
            "OwnerDailyActivities",
            "CampaignCosts",
            "RouteMinuteBuckets",
            "WorkerHeartbeats",
            "HostMetrics",
            "MonitoringIncidents",
            "OperationalCounters",
            "DailyAggregateMetrics"
        };

        foreach (var table in expectedTables)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            cmd.Parameters.AddWithValue("$name", table);
            var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
            Assert(exists, $"Migration created table {table}");
        }
    }

    private static async Task TestClientAnalyticsServiceAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = new ClientAnalyticsService(db, TimeProvider.System);

        var request = new AnalyticsEventBatchRequest(
            BrowserId: "browser-test-123",
            SessionId: "session-test-456",
            Events: new[]
            {
                new AnalyticsEventItemDto(
                    EventType: "page_view",
                    EventName: "Home Landing",
                    Path: "/pricing?token=secret123#ref",
                    ReferrerPath: "https://google.com/search?q=rent#top",
                    CtaId: "btn_signup_hero",
                    UtmSource: "google_search",
                    UtmMedium: "cpc",
                    UtmCampaign: "fall_2026",
                    UtmContent: "ad_1",
                    UtmTerm: "room rental software"
                ),
                new AnalyticsEventItemDto(
                    EventType: "unsupported_event_type",
                    EventName: "Bad Event",
                    Path: "/test"
                ),
                new AnalyticsEventItemDto(
                    EventType: "cta_click",
                    EventName: null,
                    Path: "/register",
                    CtaId: "btn-start-now"
                )
            }
        );

        var result = await service.RecordClientEventsAsync(request, null, "127.0.0.1", default);
        Assert(result.ProcessedCount == 2, "Analytics service accepted 2 valid allowlisted events");
        Assert(result.RejectedCount == 1, "Analytics service rejected 1 unallowlisted event");

        var events = await db.AnalyticsEvents.OrderBy(x => x.EventType).ToListAsync();
        Assert(events.Count == 2, "Persisted exactly 2 client analytics events");

        var ctaClick = events.First(x => x.EventType == "cta_click");
        Assert(ctaClick.CtaId == "btn-start-now", "CtaId preserved for cta_click");
        Assert(ctaClick.Path == "/register", "Path cleaned for cta_click");

        var pageView = events.First(x => x.EventType == "page_view");
        Assert(!pageView.Path.Contains("token=secret123") && !pageView.Path.Contains("#ref"), "Path query and hash stripped from event");
        Assert(pageView.Path == "/pricing", "Path stripped cleanly to /pricing");
        Assert(pageView.UtmSource == "google_search", "UTM source recorded");
        Assert(pageView.ServerTimestamp <= DateTimeOffset.UtcNow, "ServerTimestamp set authoritatively");
    }

    private static async Task TestOwnerMilestoneTrackerAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "tracker@test.example",
            Name = "Tracker Owner",
            PasswordHash = "fixture"
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var tracker = new OwnerMilestoneTracker(db, TimeProvider.System);

        // 1. Track registration with attribution
        var attribution = new RegistrationAttributionDto(
            BrowserId: "b-12345",
            UtmSource: "facebook",
            UtmMedium: "social",
            UtmCampaign: "spring_launch",
            LandingPage: "/landing",
            Referrer: "https://facebook.com"
        );
        await tracker.TrackOwnerRegisteredAsync(owner.Id, attribution, "Code", default);

        var milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.RegisteredAt <= DateTimeOffset.UtcNow, "Owner milestone recorded registration time");
        Assert(milestone.ActivatedAt == null, "Owner not activated initially");

        var attr = await db.RegistrationAttributions.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(attr.FirstTouchSource == "facebook", "First touch source recorded");
        Assert(attr.VerificationMethod == "Code", "Verification method recorded as Code");

        // 2. Track Deposit invoice issuance - should NOT activate
        var depositInvoiceId = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, depositInvoiceId, "Deposit", default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == null, "Deposit invoice does NOT activate owner");

        // 3. Track First Monthly invoice issuance - should activate once
        var monthlyInvoice1 = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, monthlyInvoice1, "Monthly", default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt != null && milestone.FirstActivatedInvoiceId == monthlyInvoice1, "First Monthly invoice activates owner");
        var originalActivatedAt = milestone.ActivatedAt!.Value;

        // 4. Second Monthly invoice issuance - must not overwrite activation
        var monthlyInvoice2 = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, monthlyInvoice2, "Monthly", default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == originalActivatedAt && milestone.FirstActivatedInvoiceId == monthlyInvoice1, "Second Monthly invoice preserves first activation milestone");

        // 5. Subscription Paid milestone
        await tracker.TrackSubscriptionPaidAsync(owner.Id, 998877, 299000, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.FirstPaidAt != null && milestone.FirstPaidOrderCode == 998877, "Subscription payment records first paid milestone");

        // 6. Owner daily activity actor isolation
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.Owner, "RoomCreated", default);
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.Admin, "AdminAudit", default);
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.System, "InvoiceSweep", default);

        var today = ApiSupport.VietnamDate(DateTimeOffset.UtcNow);
        var dailyActivity = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(dailyActivity.HasOwnerAction, "Daily activity marked HasOwnerAction = true");
        Assert(dailyActivity.OwnerActionCount >= 1, "OwnerActionCount tracked distinctly");
        Assert(dailyActivity.AdminActionCount == 1, "AdminActionCount tracked distinctly");
        Assert(dailyActivity.SystemActionCount == 1, "SystemActionCount tracked distinctly");
    }

    private static async Task TestCampaignCostsServiceAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var service = new DefaultRevenueMonitoringQueryService(db, TimeProvider.System);

        var adminId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);

        var upsertReq = new UpsertCampaignCostRequest(
            Date: date,
            Campaign: "october_facebook",
            Channel: "facebook",
            Source: "fb_feed",
            CashVnd: 500000m,
            PersonHours: 4.5m,
            HourlyRate: 150000m,
            Note: "Initial campaign push"
        );

        var saved = await service.UpsertCampaignCostAsync(upsertReq, adminId, "TestAdmin", default);
        Assert(saved.CashVnd == 500000m && saved.PersonHours == 4.5m, "Campaign cost saved successfully");

        // Update with new values
        var updateReq = upsertReq with { CashVnd = 750000m, PersonHours = 6.0m };
        var updated = await service.UpsertCampaignCostAsync(updateReq, adminId, "TestAdmin", default);
        Assert(updated.Id == saved.Id && updated.CashVnd == 750000m && updated.PersonHours == 6.0m, "Campaign cost upsert updated existing record without duplicate");

        var costs = await service.GetCampaignCostsAsync(date, date, default);
        Assert(costs.Count == 1 && costs[0].CashVnd == 750000m, "GetCampaignCostsAsync retrieved filtered cost");
    }

    private static async Task TestWorkerHeartbeatTrackerAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var tracker = new DefaultWorkerStateTracker(db, TimeProvider.System);

        await tracker.RecordWorkerHeartbeatAsync("zalo-reminder", true, "Healthy", null, default);
        await tracker.RecordWorkerHeartbeatAsync("payos-reconcile", true, "Failed", "Network timeout to provider", default);

        var list = await tracker.GetWorkerStatusesAsync(default);
        Assert(list.Count == 2, "Tracked 2 workers");

        var zalo = list.First(x => x.WorkerName == "zalo-reminder");
        Assert(zalo.Status == "Healthy" && zalo.IsEnabled && !zalo.IsStale, "Zalo worker status healthy and not stale");

        var payos = list.First(x => x.WorkerName == "payos-reconcile");
        Assert(payos.Status == "Failed" && payos.LastErrorMessage == "Network timeout to provider", "PayOs worker recorded error message");
    }

    private static async Task TestMonitoringHttpEndpointsAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddMonitoringCore(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, MonitoringFixtureAuth>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("analytics",
            _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));

        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMonitoringEndpoints();
        await app.StartAsync();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");

        // 1. Client analytics endpoint rejects foreign origin
        var payload = new AnalyticsEventBatchRequest(
            BrowserId: "bid-123",
            SessionId: "sid-123",
            Events: new[] { new AnalyticsEventItemDto("page_view", "Home", "/home") }
        );

        var foreignReq = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events")
        {
            Content = JsonContent.Create(payload)
        };
        foreignReq.Headers.Add("Origin", "https://malicious.evil.com");
        var foreignResp = await client.SendAsync(foreignReq);
        Assert(foreignResp.StatusCode == HttpStatusCode.Forbidden, "Analytics events rejects mismatched origin with 403 Forbidden");

        // 2. Client analytics endpoint accepts same-origin
        var sameOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events")
        {
            Content = JsonContent.Create(payload)
        };
        sameOriginReq.Headers.Add("Origin", "http://localhost");
        var sameOriginResp = await client.SendAsync(sameOriginReq);
        Assert(sameOriginResp.StatusCode == HttpStatusCode.OK, "Analytics events accepts same-origin with 200 OK");

        // 3. Admin endpoints require authentication
        var unauthResp = await client.GetAsync("/api/admin/monitoring/growth");
        Assert(unauthResp.StatusCode == HttpStatusCode.Unauthorized, "Admin growth endpoint requires authentication (401)");

        // 4. Admin endpoints require Admin role (Owner gets 403)
        var ownerClient = app.GetTestClient();
        ownerClient.DefaultRequestHeaders.Add("X-Fixture-Role", "Owner");
        var ownerResp = await ownerClient.GetAsync("/api/admin/monitoring/growth");
        Assert(ownerResp.StatusCode == HttpStatusCode.Forbidden, "Owner role gets 403 Forbidden for admin monitoring");

        // 5. Admin role receives explicit 501 Not Implemented contract for unimplemented endpoints
        var adminClient = app.GetTestClient();
        adminClient.DefaultRequestHeaders.Add("X-Fixture-Role", "Admin");
        var adminGrowthResp = await adminClient.GetAsync("/api/admin/monitoring/growth");
        Assert(adminGrowthResp.StatusCode == HttpStatusCode.NotImplemented, "Admin growth endpoint returns explicit 501 Not Implemented in T0 foundation");

        var adminOpsResp = await adminClient.GetAsync("/api/admin/monitoring/operations");
        Assert(adminOpsResp.StatusCode == HttpStatusCode.NotImplemented, "Admin operations endpoint returns explicit 501 Not Implemented in T0 foundation");

        // 6. Admin campaign costs endpoint returns 200 OK
        var campaignsResp = await adminClient.GetAsync("/api/admin/monitoring/campaigns");
        Assert(campaignsResp.StatusCode == HttpStatusCode.OK, "Admin campaigns endpoint returns 200 OK");
    }

    private sealed class MonitoringFixtureAuth(
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
                new Claim("sid", "fixture-session-id")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

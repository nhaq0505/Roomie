using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
using RoomRental.Tests;

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
        await TestOwnerMilestoneAndFunnelTrackerAsync();
        await TestCampaignMetadataAndCostsServiceAsync();
        await TestNullableHostMetricsAndRetentionAsync();
        await TestWorkerHeartbeatTrackerAsync();
        await TestMonitoringHttpEndpointsAsync();
    }

    private static async Task TestMigrationsAndTablesAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(migrate: true);
        await using var db = fixture.CreatePostgresDbContext();
        await using var connection = fixture.OpenConnection();

        var expectedTables = new[]
        {
            "AnalyticsEvents",
            "RegistrationAttributions",
            "RegistrationFacts",
            "OwnerMilestones",
            "OwnerDailyActivities",
            "MarketingCampaigns",
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
            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name";
            cmd.Parameters.AddWithValue("name", table);
            var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
            Assert(exists, $"Migration created table {table}");
        }

        // Verify EventId unique index on AnalyticsEvents
        await using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_AnalyticsEvents_EventId'";
        var indexSql = Convert.ToString(await indexCmd.ExecuteScalarAsync()) ?? "";
        Assert(indexSql.Contains("UNIQUE"), "EventId has unique index on AnalyticsEvents");

        // Verify Postgres model snapshot has no pending changes
        Assert(!db.Database.HasPendingModelChanges(), "Postgres migration snapshot has no pending model changes");
    }

    private static async Task TestClientAnalyticsServiceAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

        var service = new ClientAnalyticsService(db, TimeProvider.System);

        var event1Id = "evt-click-1";
        var event2Id = "evt-click-2";
        var badEventId = "evt-bad-1";

        var request1 = new AnalyticsEventBatchRequest(
            BrowserId: "browser-test-123",
            SessionId: "session-test-456",
            Events: new[]
            {
                new AnalyticsEventItemDto(
                    EventId: event1Id,
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
                    EventId: badEventId,
                    EventType: "custom_unsupported",
                    EventName: "Custom Hack",
                    Path: "/test"
                ),
                new AnalyticsEventItemDto(
                    EventId: event2Id,
                    EventType: "cta_click",
                    EventName: null,
                    Path: "/register",
                    CtaId: "btn-start-now"
                )
            }
        );

        var result1 = await service.RecordClientEventsAsync(request1, null, "127.0.0.1", default);
        Assert(result1.ProcessedCount == 2, "Analytics service accepted 2 valid allowlisted events");
        Assert(result1.RejectedCount == 1, "Analytics service rejected 1 unallowlisted event");
        Assert(result1.DedupedCount == 0, "No deduped events on initial submission");

        // Test identical real repeated click with DIFFERENT EventId is preserved
        var event3Id = "evt-click-3";
        var requestRepeatedClick = new AnalyticsEventBatchRequest(
            BrowserId: "browser-test-123",
            SessionId: "session-test-456",
            Events: new[]
            {
                new AnalyticsEventItemDto(
                    EventId: event3Id,
                    EventType: "cta_click",
                    EventName: null,
                    Path: "/register",
                    CtaId: "btn-start-now"
                )
            }
        );
        var resultRepeated = await service.RecordClientEventsAsync(requestRepeatedClick, null, "127.0.0.1", default);
        Assert(resultRepeated.ProcessedCount == 1 && resultRepeated.DedupedCount == 0, "Identical real click with different EventId is preserved");

        // Test retry with SAME EventId is deduped without error
        var requestRetry = new AnalyticsEventBatchRequest(
            BrowserId: "browser-test-123",
            SessionId: "session-test-456",
            Events: new[]
            {
                new AnalyticsEventItemDto(
                    EventId: event1Id,
                    EventType: "page_view",
                    EventName: "Home Landing",
                    Path: "/pricing"
                )
            }
        );
        var resultRetry = await service.RecordClientEventsAsync(requestRetry, null, "127.0.0.1", default);
        Assert(resultRetry.ProcessedCount == 0 && resultRetry.DedupedCount == 1, "Retry with identical EventId was deduped");

        // Test path sanitization (absolute URLs, queries, reset tokens, GUIDs) and PII filtering
        var event4Id = "evt-sanitization-4";
        var event5Id = "evt-sanitization-5";
        var event6BadScheme = "evt-bad-scheme";
        var requestSanitization = new AnalyticsEventBatchRequest(
            BrowserId: "browser-test-123",
            SessionId: "session-test-456",
            Events: new[]
            {
                new AnalyticsEventItemDto(
                    EventId: event4Id,
                    EventType: "page_view",
                    EventName: "Reset Password Tokenized",
                    Path: "/reset/4f3a9b2c8d1e0f2a3b4c5d6e7f8a9b0c1d2e3f4a",
                    ReferrerPath: "https://example.com/welcome?token=abc123#top",
                    UtmSource: "attacker@pii.example", // PII should be stripped
                    UtmCampaign: "spring_promo"
                ),
                new AnalyticsEventItemDto(
                    EventId: event5Id,
                    EventType: "page_view",
                    EventName: "Room Details Guid",
                    Path: "/rooms/f81d4fae-7dec-11d0-a765-00a0c91e6bf6",
                    CtaId: "cta_book_now"
                ),
                new AnalyticsEventItemDto(
                    EventId: event6BadScheme,
                    EventType: "page_view",
                    EventName: "Dangerous Path",
                    Path: "javascript:alert(1)"
                )
            }
        );
        var resultSanitized = await service.RecordClientEventsAsync(requestSanitization, null, "127.0.0.1", default);
        Assert(resultSanitized.ProcessedCount == 2 && resultSanitized.RejectedCount == 1, "Accepted 2 sanitized events, rejected 1 dangerous scheme event");

        var record4 = await db.AnalyticsEvents.SingleAsync(x => x.EventId == event4Id);
        Assert(record4.Path == "/reset/:token", "Reset token in path normalized to :token");
        Assert(record4.ReferrerPath == "/welcome", "Referrer absolute URL stripped domain and query to /welcome");
        Assert(record4.UtmSource == null, "PII email in UtmSource was stripped");
        Assert(record4.UtmCampaign == "spring_promo", "Safe campaign name preserved");

        var record5 = await db.AnalyticsEvents.SingleAsync(x => x.EventId == event5Id);
        Assert(record5.Path == "/rooms/:id", "GUID in path normalized to :id");

        // Test null events and collection bounds
        var emptyResult = await service.RecordClientEventsAsync(new AnalyticsEventBatchRequest("b1", "s1", null), null, null, default);
        Assert(emptyResult.ProcessedCount == 0 && emptyResult.RejectedCount == 0, "Null events collection handled gracefully");

        var overLimitEvents = Enumerable.Range(1, 101)
            .Select(i => new AnalyticsEventItemDto($"evt-{i}", "page_view", "Name", "/path"))
            .ToList();
        var limitThrown = false;
        try
        {
            await service.RecordClientEventsAsync(new AnalyticsEventBatchRequest("b1", "s1", overLimitEvents), null, null, default);
        }
        catch (ApiException ex) when (ex.StatusCode == 400)
        {
            limitThrown = true;
        }
        Assert(limitThrown, "Batch size > 100 rejected with 400");

        var events = await db.AnalyticsEvents.OrderBy(x => x.EventId).ToListAsync();
        Assert(events.Count == 5, "Database contains exactly 5 distinct valid events");
    }

    private static async Task TestOwnerMilestoneAndFunnelTrackerAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

        var tracker = new OwnerMilestoneTracker(db, TimeProvider.System);

        var attemptId = Guid.NewGuid();
        var attribution = new RegistrationAttributionDto(
            BrowserId: "b-12345",
            UtmSource: "facebook",
            UtmMedium: "social",
            UtmCampaign: "spring_launch",
            LandingPage: "/landing",
            Referrer: "https://facebook.com"
        );

        // 1. Authoritative registration started fact recorded
        await tracker.TrackRegistrationStartedAsync(attemptId, attribution, "Code", default);
        var startedFact = await db.RegistrationFacts.SingleAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "Started");
        Assert(startedFact.UtmSource == "facebook" && startedFact.VerificationMethod == "Code", "Registration started fact recorded with attribution and method");

        // 2. Authoritative registration completed via OTP verification
        var owner = new AppUser
        {
            Id = attemptId,
            Email = "funnel@test.example",
            Name = "Funnel Owner",
            PasswordHash = "fixture"
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        await tracker.TrackRegistrationCompletedAsync(attemptId, owner.Id, "Verified", default);
        await tracker.TrackOwnerRegisteredAsync(owner.Id, null, "Code", default); // Attribution retrieved from started fact

        var verifiedFact = await db.RegistrationFacts.SingleAsync(x => x.RegistrationAttemptId == attemptId && x.Stage == "Verified");
        Assert(verifiedFact.OwnerId == owner.Id, "Registration verified fact linked to OwnerId");

        var attr = await db.RegistrationAttributions.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(attr.FirstTouchSource == "facebook", "Attribution carried through to RegistrationAttribution from durable started fact");

        // 3. Activation milestone: Deposit does NOT activate, first Monthly activates once
        await tracker.TrackInvoiceIssuedAsync(owner.Id, Guid.NewGuid(), "Deposit", default);
        var milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == null, "Deposit invoice does not activate owner");

        var firstMonthlyId = Guid.NewGuid();
        await tracker.TrackInvoiceIssuedAsync(owner.Id, firstMonthlyId, "Monthly", default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt != null && milestone.FirstActivatedInvoiceId == firstMonthlyId, "First monthly invoice activates owner");
        var originalActivatedAt = milestone.ActivatedAt!.Value;

        await tracker.TrackInvoiceIssuedAsync(owner.Id, Guid.NewGuid(), "Monthly", default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.ActivatedAt == originalActivatedAt, "Second monthly invoice preserves original activation milestone");

        // 4. First paid milestone
        await tracker.TrackSubscriptionPaidAsync(owner.Id, 123456, 299000, default);
        milestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == owner.Id);
        Assert(milestone.FirstPaidAt != null && milestone.FirstPaidOrderCode == 123456, "First paid milestone recorded");

        // 5. Meaningful owner activity
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.Owner, "InvoiceCreated", default);
        await tracker.RecordOwnerActivityAsync(owner.Id, ActorType.Admin, "AdminReview", default);
        var today = ApiSupport.VietnamDate(DateTimeOffset.UtcNow);
        var daily = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(daily.HasOwnerAction && daily.OwnerActionCount == 5 && daily.AdminActionCount == 1, "Owner daily activity tracked distinctly");

        // 6. System sweep actor separation: system action must not count towards WAU / OwnerActionCount
        var prevOwnerActions = daily.OwnerActionCount;
        var prevSystemActions = daily.SystemActionCount;
        await tracker.TrackInvoiceIssuedAsync(owner.Id, Guid.NewGuid(), "Monthly", ActorType.System, default);
        var dailyAfterSystem = await db.OwnerDailyActivities.SingleAsync(x => x.OwnerId == owner.Id && x.ActivityDate == today);
        Assert(dailyAfterSystem.OwnerActionCount == prevOwnerActions, "System invoice generation did not increment OwnerActionCount");
        Assert(dailyAfterSystem.SystemActionCount == prevSystemActions + 1, "System invoice generation incremented SystemActionCount");

        // 7. Independent First Touch and Last Touch non-direct (no duplicate overwrite)
        var owner2 = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "touches@test.example",
            Name = "Touches Owner",
            PasswordHash = "fixture"
        };
        db.Users.Add(owner2);
        await db.SaveChangesAsync();

        var touchesAttribution = new RegistrationAttributionDto(
            BrowserId: "b-touches",
            FirstTouch: new AttributionTouchDto(
                Source: "google",
                Medium: "cpc",
                Campaign: "launch_brand",
                LandingPage: "/pricing",
                OccurredAt: DateTimeOffset.UtcNow.AddDays(-10)
            ),
            LastTouch: new AttributionTouchDto(
                Source: "facebook",
                Medium: "retargeting",
                Campaign: "retargeting_q4",
                LandingPage: "/dang-ky",
                OccurredAt: DateTimeOffset.UtcNow.AddDays(-1)
            )
        );
        await tracker.TrackOwnerRegisteredAsync(owner2.Id, touchesAttribution, "Code", default);
        var attr2 = await db.RegistrationAttributions.SingleAsync(x => x.OwnerId == owner2.Id);
        Assert(attr2.FirstTouchSource == "google" && attr2.LastTouchSource == "facebook", "Separate first and last touch preserved independently");
        Assert(attr2.FirstTouchCampaign == "launch_brand" && attr2.LastTouchCampaign == "retargeting_q4", "Campaigns preserved independently");

        // 8. Milestone fallback: authoritative User.CreatedAt or null, never fabricating now
        var legacyOwner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "legacy@test.example",
            Name = "Legacy Owner",
            PasswordHash = "fixture",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-90)
        };
        db.Users.Add(legacyOwner);
        await db.SaveChangesAsync();

        await tracker.TrackInvoiceIssuedAsync(legacyOwner.Id, Guid.NewGuid(), "Monthly", ActorType.System, default);
        var legacyMilestone = await db.OwnerMilestones.SingleAsync(x => x.OwnerId == legacyOwner.Id);
        Assert(legacyMilestone.RegisteredAt.HasValue && Math.Abs((legacyMilestone.RegisteredAt.Value - legacyOwner.CreatedAt).TotalSeconds) < 2,
            $"Milestone fallback used authoritative actual registration date from Users: expected {legacyOwner.CreatedAt}, got {legacyMilestone.RegisteredAt}");

        var nonExistentOwnerId = Guid.NewGuid();
        await tracker.TrackSubscriptionPaidAsync(nonExistentOwnerId, 999999, 100000, ActorType.System, default);
        var unknownMilestone = await db.OwnerMilestones.SingleOrDefaultAsync(x => x.OwnerId == nonExistentOwnerId);
        Assert(unknownMilestone == null, "Milestone tracking for non-existent owner record safely returns without DB error");
    }

    private static async Task TestCampaignMetadataAndCostsServiceAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

        var service = new DefaultRevenueMonitoringQueryService(db, TimeProvider.System);

        // 1. Create campaign metadata before any spend exists
        var createReq = new CreateCampaignRequest(
            Code: "google_search_2026",
            Name: "Google Search Ads Q4",
            Channel: "google",
            Source: "search",
            UtmCampaign: "q4_search",
            UtmMedium: "cpc",
            UtmSource: "google",
            TargetUrl: "https://roomie.io.vn/pricing"
        );
        var campaign = await service.CreateCampaignAsync(createReq, AdminUserId, "Admin", default);
        Assert(campaign.Code == "google_search_2026" && campaign.IsActive, "Campaign metadata created with zero spend");

        // 2. Prevent duplicate campaign code
        var duplicateThrown = false;
        try
        {
            await service.CreateCampaignAsync(createReq, AdminUserId, "Admin", default);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            duplicateThrown = true;
        }
        Assert(duplicateThrown, "Duplicate campaign code rejected with 409 Conflict");

        // 3. Query campaigns by filter
        var campaigns = await service.GetCampaignsAsync(true, "google", "search", default);
        Assert(campaigns.Count == 1 && campaigns[0].Name == "Google Search Ads Q4", "GetCampaignsAsync returned matching campaign");

        // 4. Update campaign
        var updateReq = new UpdateCampaignRequest("Google Search Ads Q4 Updated", "google", "search_brand");
        var updated = await service.UpdateCampaignAsync(campaign.Id, updateReq, AdminUserId, "Admin", default);
        Assert(updated.Name == "Google Search Ads Q4 Updated" && updated.Source == "search_brand", "Campaign metadata updated");

        // 5. Add and query campaign cost
        var date = new DateOnly(2026, 10, 20);
        var costReq = new UpsertCampaignCostRequest(
            Date: date,
            Campaign: campaign.Code,
            Channel: "google",
            Source: "search_brand",
            CashVnd: 1200000m,
            PersonHours: 2.0m,
            HourlyRate: 250000m
        );
        await service.UpsertCampaignCostAsync(costReq, AdminUserId, "Admin", default);
        var costs = await service.GetCampaignCostsAsync(date, date, campaign.Code, default);
        Assert(costs.Count == 1 && costs[0].CashVnd == 1200000m, "Campaign cost linked and filtered by campaign code");
    }

    private static async Task TestNullableHostMetricsAndRetentionAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

        // 1. Host metrics record with null CPU/RAM/disk (unknown/unreachable probe)
        var hostRecord = new HostMetricsRecord
        {
            RecordedAt = DateTimeOffset.UtcNow,
            CpuPercent = null,
            MemoryPercent = null,
            DiskPercent = null,
            ContainerCount = 0,
            HealthEndpointStatus = "unreachable",
            HostStatus = "Warning"
        };
        db.HostMetrics.Add(hostRecord);
        await db.SaveChangesAsync();

        var retrieved = await db.HostMetrics.SingleAsync(x => x.Id == hostRecord.Id);
        Assert(retrieved.CpuPercent == null && retrieved.MemoryPercent == null && retrieved.DiskPercent == null,
            "Host metrics supports nullable CPU/RAM/disk without forcing healthy zeros");

        // 2. Cohort retention row supports nullable immature retention cells
        var retentionRow = new CohortRetentionRowDto(
            CohortPeriod: "2026-10",
            CohortSize: 10,
            RetentionPercentages: new double?[] { 100.0, null, null }
        );
        Assert(retentionRow.RetentionPercentages[0] == 100.0 && retentionRow.RetentionPercentages[1] == null,
            "Cohort retention row accepts nullable immature cells (null, not 0.0)");

        // 3. Revenue summary does NOT have MrrVnd
        var revSummary = new RevenueSummaryDto(
            TotalSubscriptionRevenueVnd: 5000000,
            PaidSubscriptionCount: 10,
            TotalMarketingSpendVnd: 2000000,
            TotalMarketingHours: 15.0m,
            Cac30: new Cac30MetricDto(200000m, true, null)
        );
        Assert(revSummary.TotalSubscriptionRevenueVnd == 5000000 && revSummary.PaidSubscriptionCount == 10,
            "RevenueSummary contains confirmed subscription revenue without unapproved MrrVnd");
    }

    private static async Task TestWorkerHeartbeatTrackerAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);
        await using var db = fixture.CreateDbContext();

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
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: true);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
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
            Events: new[] { new AnalyticsEventItemDto("evt-http-1", "page_view", "Home", "/home") }
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

        // 3. Retry same-origin is deduped
        var retryResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/analytics/events")
        {
            Content = JsonContent.Create(payload),
            Headers = { { "Origin", "http://localhost" } }
        });
        var retryResult = await retryResp.Content.ReadFromJsonAsync<AnalyticsEventBatchResponse>();
        Assert(retryResult!.DedupedCount == 1 && retryResult.ProcessedCount == 0, "HTTP client analytics retry deduped via EventId");

        // 4. Admin endpoints require authentication
        var unauthResp = await client.GetAsync("/api/admin/monitoring/growth");
        Assert(unauthResp.StatusCode == HttpStatusCode.Unauthorized, "Admin growth endpoint requires authentication (401)");

        // 5. Admin endpoints require Admin role (Owner gets 403)
        var ownerClient = app.GetTestClient();
        ownerClient.DefaultRequestHeaders.Add("X-Fixture-Role", "Owner");
        var ownerResp = await ownerClient.GetAsync("/api/admin/monitoring/growth");
        Assert(ownerResp.StatusCode == HttpStatusCode.Forbidden, "Owner role gets 403 Forbidden for admin monitoring");

        // 6. Admin endpoints: growth returns 200 OK in T1, ops returns 501 (owned by Worker C)
        var adminClient = app.GetTestClient();
        adminClient.DefaultRequestHeaders.Add("X-Fixture-Role", "Admin");
        var adminGrowthResp = await adminClient.GetAsync("/api/admin/monitoring/growth");
        Assert(adminGrowthResp.StatusCode == HttpStatusCode.OK, "Admin growth endpoint returns 200 OK in T1");

        var adminRevenueResp = await adminClient.GetAsync("/api/admin/monitoring/revenue");
        Assert(adminRevenueResp.StatusCode == HttpStatusCode.OK, "Admin revenue endpoint returns 200 OK in T1");

        var adminOpsResp = await adminClient.GetAsync("/api/admin/monitoring/operations");
        Assert(adminOpsResp.StatusCode == HttpStatusCode.NotImplemented, "Admin operations endpoint returns explicit 501 Not Implemented in T0 foundation");

        // 7. Campaign metadata CRUD endpoints
        var createCampReq = new CreateCampaignRequest("fb_holiday", "Facebook Holiday Sale", "facebook", "feed");
        var postCampResp = await adminClient.PostAsJsonAsync("/api/admin/monitoring/campaigns/metadata", createCampReq);
        Assert(postCampResp.StatusCode == HttpStatusCode.Created, "POST /campaigns/metadata returned 201 Created");

        var getCampResp = await adminClient.GetAsync("/api/admin/monitoring/campaigns/metadata");
        Assert(getCampResp.StatusCode == HttpStatusCode.OK, "GET /campaigns/metadata returned 200 OK");

        // 8. Admin campaign costs endpoint returns 200 OK
        var campaignsResp = await adminClient.GetAsync("/api/admin/monitoring/campaigns");
        Assert(campaignsResp.StatusCode == HttpStatusCode.OK, "Admin campaigns endpoint returns 200 OK");
    }

    internal sealed class MonitoringFixtureAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var customUserId = Request.Headers["X-Fixture-UserId"].FirstOrDefault();
            var userId = !string.IsNullOrWhiteSpace(customUserId) && Guid.TryParse(customUserId, out var parsedUid)
                ? parsedUid
                : (role == "Admin" ? AdminUserId : OwnerUserId);
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

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

// Actual production route handlers, in-memory HTTP/database and synthetic authentication.
// Every provider request is handled in-process; no live payOS connection is possible.
internal static class TenantLinkHttpTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    public static async Task RunAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        using var provider = new ConfirmWebhookHandler();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "https://roomie.example.invalid",
            ["WebhookBaseUrl"] = "https://callbacks.example.invalid",
            ["RequireVerifiedPaymentForTenantLink"] = "true"
        });
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<TenantTokenService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddScoped<PayOsService>();
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddSingleton<IHttpClientFactory>(provider);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("tenant-payment",
            _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("fixture")));
        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOwnerManagementEndpoints();
        app.MapBillingEndpoints();
        app.MapPaymentEndpoints();
        app.MapPaymentPreferencesEndpoints();
        app.MapManualPaymentEndpoints();

        var buildingId = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new AppUser { Id = OwnerId, Name = "Fixture Owner", Email = "owner@example.invalid", PasswordHash = "unused-fixture", Role = UserRole.Owner });
            db.Buildings.Add(new Building { Id = buildingId, OwnerId = OwnerId, Name = "Fixture building" });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        using var client = app.GetTestClient();
        var room = await JsonAsync(await client.PostAsJsonAsync("/api/rooms", new
        {
            buildingId, name = "Fixture room", rent = 1_000_000, electricityRate = 3000,
            waterFee = 50_000, serviceFee = 0, initialReading = 0
        }), HttpStatusCode.Created);
        var roomId = room.GetProperty("id").GetGuid();
        var occupancy = await JsonAsync(await client.PostAsJsonAsync($"/api/rooms/{roomId}/occupancy", new
        {
            tenantName = "Fixture Tenant", people = 1, startDate = VietnamCalendar.TodayVietnam().AddDays(-1), depositAmount = 100_000
        }));
        var occupancyId = occupancy.GetProperty("occupancy").GetProperty("id").GetGuid();
        Guid invoiceId;
        string? originalHash;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            invoiceId = (await db.Invoices.SingleAsync()).Id;
            originalHash = (await db.Occupancies.SingleAsync()).TenantTokenHash;
        }
        AssertHidden(occupancy, "occupancy creation does not expose a link before payOS verification");
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: true);
        await AssertRejectedAsync(client, roomId);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert((await db.Occupancies.SingleAsync()).TenantTokenHash == originalHash, "rejected rotation preserves the existing token");
        }
        AssertHidden(await JsonAsync(await client.PatchAsJsonAsync($"/api/rooms/{roomId}", new
        {
            name = "Edited fixture room", rent = 1_000_000, electricityRate = 3000, waterFee = 50_000, serviceFee = 0
        })), "room edits cannot leak a tenant link");
        AssertHidden(await JsonAsync(await client.PatchAsJsonAsync($"/api/rooms/{roomId}/occupancy", new
        {
            occupancyId, tenantName = "Edited Fixture Tenant", people = 1
        })), "occupancy edits cannot leak a tenant link");

        // Saved credentials are insufficient when confirmation fails.
        var settings = await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment", new
        {
            clientId = "synthetic-client", apiKey = "synthetic-api", checksumKey = "synthetic-checksum"
        }));
        Assert(settings.GetProperty("webhookStatus").GetString() == "Failed", "saved but unverified payOS remains blocked");
        await AssertRejectedAsync(client, roomId);
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: true);

        provider.Verified = true;
        settings = await JsonAsync(await client.PostAsync("/api/settings/payment/verify-webhook", null));
        Assert(settings.GetProperty("webhookStatus").GetString() == "Verified", "fake provider verifies payOS through the HTTP endpoint");
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: false);
        var secondRoom = await JsonAsync(await client.PostAsJsonAsync("/api/rooms", new
        {
            buildingId, name = "Verified fixture room", rent = 1_000_000, electricityRate = 3000,
            waterFee = 50_000, serviceFee = 0, initialReading = 0
        }), HttpStatusCode.Created);
        var verifiedOccupancy = await JsonAsync(await client.PostAsJsonAsync($"/api/rooms/{secondRoom.GetProperty("id").GetGuid()}/occupancy", new
        {
            tenantName = "Verified Fixture Tenant", people = 1, startDate = VietnamCalendar.TodayVietnam().AddDays(-1), depositAmount = 0
        }));
        Assert(!string.IsNullOrEmpty(verifiedOccupancy.GetProperty("occupancy").GetProperty("token").GetString()),
            "occupancy creation exposes a link after payOS verification");
        var oldToken = (await JsonAsync(await client.GetAsync($"/api/rooms/{roomId}"))).GetProperty("occupancy").GetProperty("token").GetString();
        var rotated = await JsonAsync(await client.PostAsync($"/api/rooms/{roomId}/rotate-link", null));
        var newToken = rotated.GetProperty("token").GetString();
        Assert(!string.IsNullOrEmpty(newToken) && newToken != oldToken, "verified owner can rotate a link");
        using var revoked = await client.GetAsync($"/api/tenant/{oldToken}");
        Assert(revoked.StatusCode == HttpStatusCode.NotFound, "rotation revokes the old guest link");
        await JsonAsync(await client.GetAsync($"/api/tenant/{newToken}"));

        // Verification belongs to the current origin/credentials, not just a saved status.
        builder.Configuration["WebhookBaseUrl"] = "https://changed.example.invalid";
        await AssertRejectedAsync(client, roomId);
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: true);
        var existingGuest = await JsonAsync(await client.GetAsync($"/api/tenant/{newToken}"));
        Assert(existingGuest.GetProperty("room").GetProperty("id").GetGuid() == roomId,
            "previously shared links still show invoices after payment setup changes");
        builder.Configuration["WebhookBaseUrl"] = "https://callbacks.example.invalid";

        var otherOwner = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new AppUser { Id = otherOwner, Name = "Other Fixture", Email = "other@example.invalid", PasswordHash = "unused-fixture", Role = UserRole.Owner });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", otherOwner.ToString());
        await AssertRejectedAsync(client, roomId);
        client.DefaultRequestHeaders.Remove("X-Fixture-Owner");

        provider.Verified = false;
        await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment", new
        {
            clientId = "synthetic-client", apiKey = "changed-synthetic-api", checksumKey = "changed-synthetic-checksum"
        }));
        await AssertRejectedAsync(client, roomId);
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: true);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(!await db.Receipts.AnyAsync() && !await db.PayOsAttempts.AnyAsync(), "link policy tests never create money or payment attempts");
        }
        Assert(provider.Calls == 3, "only three in-process webhook confirmations were made");

        // Manual confirmation is independent of payOS credentials and shares no external traffic.
        await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment/preferences", new { confirmationMode = "invalid" }), HttpStatusCode.BadRequest);
        await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment/preferences", new { confirmationMode = "Manual", bankBin = "000000", accountNumber = "12345678", accountName = "TEST" }), HttpStatusCode.BadRequest);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PayOsAttempts.Add(new PayOsAttempt { OwnerId = OwnerId, InvoiceId = invoiceId, OrderCode = 989898909, Amount = 100_000 });
            await db.SaveChangesAsync();
        }
        await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment/preferences", new {
            confirmationMode = "Manual", bankBin = "970436", accountNumber = "12345678", accountName = "SYNTHETIC TEST ONLY"
        }), HttpStatusCode.Conflict);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert((await db.Users.SingleAsync(x => x.Id == OwnerId)).PaymentConfirmationMode == PaymentConfirmationMode.Automatic,
                "pending payOS prevents switching payment mode without mutating preferences");
            db.PayOsAttempts.RemoveRange(await db.PayOsAttempts.ToListAsync()); await db.SaveChangesAsync();
        }
        var manualSettings = await JsonAsync(await client.PutAsJsonAsync("/api/settings/payment/preferences", new {
            confirmationMode = "Manual", bankBin = "970436", accountNumber = "12345678", accountName = "SYNTHETIC TEST ONLY"
        }));
        Assert(manualSettings.GetProperty("confirmationMode").GetString() == "Manual", "owner selects and persists manual confirmation");
        await AssertReadLinksAsync(client, roomId, invoiceId, hidden: false);
        await JsonAsync(await client.PostAsync($"/api/tenant/{newToken}/invoices/{invoiceId}/payment", null), HttpStatusCode.Conflict);
        var singleRequest = await JsonAsync(await client.PostAsJsonAsync($"/api/tenant/{newToken}/manual-payments", new { invoiceId }));
        Assert(singleRequest.GetProperty("amount").GetDecimal() == 100_000 && singleRequest.GetProperty("qrUrl").GetString()!.StartsWith("https://img.vietqr.io/image/970436-12345678-compact2.png?"), "manual QR uses server-side bank settings and the individual balance");
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.Add(new Invoice { OwnerId = OwnerId, BuildingId = buildingId, RoomId = roomId, OccupancyId = occupancyId,
                RoomName = "Fixture room", BuildingName = "Fixture", TenantName = "Fixture tenant", Period = "2026-09", Total = 120_000 });
            await db.SaveChangesAsync();
        }
        var batch = await JsonAsync(await client.PostAsJsonAsync($"/api/tenant/{newToken}/manual-payments", new { invoiceId = (Guid?)null }));
        var batchId = batch.GetProperty("id").GetGuid();
        Assert(batch.GetProperty("amount").GetDecimal() == 220_000 && batch.GetProperty("invoices").GetArrayLength() == 2, "manual pay-all snapshots both outstanding invoices");
        var reused = await JsonAsync(await client.PostAsJsonAsync($"/api/tenant/{newToken}/manual-payments", new { invoiceId = (Guid?)null }));
        Assert(reused.GetProperty("id").GetGuid() == batchId, "reopening manual pay-all reuses a live unchanged request");
        await JsonAsync(await client.PostAsJsonAsync($"/api/manual-payments/{batchId}/confirm", new { amount = 1 }), HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Add("X-Fixture-Owner", otherOwner.ToString());
        await JsonAsync(await client.PostAsJsonAsync($"/api/manual-payments/{batchId}/confirm", new { amount = 220_000 }), HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Remove("X-Fixture-Owner");
        await JsonAsync(await client.PostAsJsonAsync($"/api/manual-payments/{batchId}/confirm", new { amount = 220_000 }));
        await JsonAsync(await client.PostAsJsonAsync($"/api/manual-payments/{batchId}/confirm", new { amount = 220_000 }));
        var singleId = singleRequest.GetProperty("id").GetGuid();
        await JsonAsync(await client.PostAsJsonAsync($"/api/manual-payments/{singleId}/confirm", new { amount = 100_000 }), HttpStatusCode.Conflict);
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert(await db.Receipts.CountAsync() == 2 && await db.Invoices.Where(i => i.OccupancyId == occupancyId).AllAsync(i => i.Paid == i.Total),
                "manual batch records exactly one receipt per invoice and rejects stale/duplicate confirmation");
        }
        Assert(provider.Calls == 3, "manual payment workflow never calls a payment provider");
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var month = 1; month <= 12; month++) db.Invoices.Add(new Invoice {
                OwnerId = OwnerId, BuildingId = buildingId, RoomId = roomId, OccupancyId = occupancyId,
                RoomName = "Fixture room", BuildingName = "Fixture", TenantName = "Fixture tenant", Period = $"2024-{month:D2}", Total = 10
            });
            await db.SaveChangesAsync();
        }
        var paged = await JsonAsync(await client.GetAsync($"/api/tenant/{newToken}?page=1&pageSize=1"));
        Assert(paged.GetProperty("invoices").GetArrayLength() == 1 && paged.GetProperty("totals").GetProperty("outstanding").GetDecimal() == 120,
            "tenant summary totals cover outstanding invoices outside the current page");
        var allPages = await JsonAsync(await client.PostAsJsonAsync($"/api/tenant/{newToken}/manual-payments", new { invoiceId = (Guid?)null }));
        Assert(allPages.GetProperty("invoices").GetArrayLength() == 12 && allPages.GetProperty("amount").GetDecimal() == 120,
            "pay-all includes all twelve unpaid invoices across pagination");
    }

    private static async Task AssertReadLinksAsync(HttpClient client, Guid roomId, Guid invoiceId, bool hidden)
    {
        var list = await JsonAsync(await client.GetAsync("/api/rooms"));
        var detail = await JsonAsync(await client.GetAsync($"/api/rooms/{roomId}"));
        var invoiceList = await JsonAsync(await client.GetAsync("/api/invoices"));
        var invoice = await JsonAsync(await client.GetAsync($"/api/invoices/{invoiceId}"));
        foreach (var room in new[] { list[0], detail })
        {
            var occupancy = room.GetProperty("occupancy");
            Assert((occupancy.GetProperty("token").ValueKind == JsonValueKind.Null) == hidden &&
                (occupancy.GetProperty("url").ValueKind == JsonValueKind.Null) == hidden,
                $"room list/detail link visibility follows verified payOS (hidden={hidden})");
        }
        foreach (var item in new[] { invoiceList[0], invoice })
            Assert((item.GetProperty("tenantToken").ValueKind == JsonValueKind.Null) == hidden,
                $"invoice list/detail cannot bypass link policy (hidden={hidden})");
    }

    private static async Task AssertRejectedAsync(HttpClient client, Guid roomId)
    {
        var result = await JsonAsync(await client.PostAsync($"/api/rooms/{roomId}/rotate-link", null), HttpStatusCode.BadRequest);
        Assert(result.GetProperty("message").GetString()!.Contains("payOS"), "unverified rotation returns the actual endpoint's payOS rejection");
    }

    private static void AssertHidden(JsonElement room, string label)
    {
        var occupancy = room.GetProperty("occupancy");
        Assert(occupancy.GetProperty("token").ValueKind == JsonValueKind.Null && occupancy.GetProperty("url").ValueKind == JsonValueKind.Null, label);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != expected) throw new Exception($"Tenant link HTTP test: expected {expected}, got {response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }

    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var owner = Request.Headers["X-Fixture-Owner"].FirstOrDefault() ?? OwnerId.ToString();
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner), new Claim(ClaimTypes.Role, "Owner")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class ConfirmWebhookHandler : HttpMessageHandler, IHttpClientFactory
    {
        public bool Verified;
        public int Calls;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri != "https://api-merchant.payos.vn/confirm-webhook")
                throw new InvalidOperationException("Unexpected external request in isolated HTTP fixture");
            Calls++;
            var data = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { code = Verified ? "00" : "01", data = new { webhookUrl = data.GetProperty("webhookUrl").GetString() } })
            };
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Features.Monitoring;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class MarketerSuiteHttpTests
{
    public static async Task RunAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var otpSender = new TestOtpSender();
        await using var app = CreateTestApp(fixture.ConnectionString, otpSender);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // Seed Admin user
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var admin = new AppUser
            {
                Id = Guid.NewGuid(),
                Name = "System Admin",
                Email = "admin@phongtro.local",
                PasswordHash = "",
                Role = UserRole.Admin,
                Status = OwnerStatus.Approved,
                CreatedAt = DateTimeOffset.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, "Admin12345!");
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }

        // Login as Admin
        var adminCookie = await LoginAsync(client, "admin@phongtro.local", "Admin12345!");
        Check(!string.IsNullOrEmpty(adminCookie), "Admin login succeeds");

        // 1. Admin creates Marketer account
        var createMarketerResp = await SendJsonAsync(client, HttpMethod.Post, "/api/admin/marketers", adminCookie,
            new CreateMarketerRequest("Nguyen Thi Mai", "mai.marketer@phongtro.local", "MarketerPass123!"));
        Check(createMarketerResp.StatusCode == HttpStatusCode.Created, "Admin creates Marketer account (201 Created)");
        var marketerDto = await createMarketerResp.Content.ReadFromJsonAsync<MarketerAdminDto>();
        Check(marketerDto != null && marketerDto.Email == "mai.marketer@phongtro.local" && marketerDto.Status == "Approved",
            "Created Marketer DTO has Approved status");

        // Login as Marketer
        var marketerCookie = await LoginAsync(client, "mai.marketer@phongtro.local", "MarketerPass123!");
        Check(!string.IsNullOrEmpty(marketerCookie), "Marketer login succeeds");

        // 2. Role authorization checks for Marketer
        var opsResp = await SendAsync(client, HttpMethod.Get, "/api/admin/monitoring/operations", marketerCookie);
        Check(opsResp.StatusCode == HttpStatusCode.Forbidden, "Marketer is forbidden (403) from /api/admin/monitoring/operations");

        var incidentsResp = await SendAsync(client, HttpMethod.Get, "/api/admin/monitoring/incidents", marketerCookie);
        Check(incidentsResp.StatusCode == HttpStatusCode.Forbidden, "Marketer is forbidden (403) from /api/admin/monitoring/incidents");

        var ownersListResp = await SendAsync(client, HttpMethod.Get, "/api/admin/owners", marketerCookie);
        Check(ownersListResp.StatusCode == HttpStatusCode.Forbidden, "Marketer is forbidden (403) from /api/admin/owners");

        var marketersListResp = await SendAsync(client, HttpMethod.Get, "/api/admin/marketers", marketerCookie);
        Check(marketersListResp.StatusCode == HttpStatusCode.Forbidden, "Marketer is forbidden (403) from /api/admin/marketers");

        var growthResp = await SendAsync(client, HttpMethod.Get, "/api/admin/monitoring/growth", marketerCookie);
        Check(growthResp.StatusCode == HttpStatusCode.OK, "Marketer can access /api/admin/monitoring/growth (200 OK)");

        var revenueResp = await SendAsync(client, HttpMethod.Get, "/api/admin/monitoring/revenue", marketerCookie);
        Check(revenueResp.StatusCode == HttpStatusCode.OK, "Marketer can access /api/admin/monitoring/revenue (200 OK)");
        var revenueJson = await revenueResp.Content.ReadAsStringAsync();
        Check(revenueJson.Contains("campaignRoas"), "Revenue response includes campaignRoas array");

        // 3. Marketer creates Voucher Code & Owner registers with Auto-Promo code (?promo=TET2026)
        var createVoucherResp = await SendJsonAsync(client, HttpMethod.Post, "/api/admin/promotions/codes", marketerCookie,
            new PromotionCodeCreateRequest("TET2026", "Khuyen mai Tet 2026", 3, 100, null, true));
        Check(createVoucherResp.StatusCode == HttpStatusCode.Created, "Marketer creates voucher code TET2026 (201 Created)");
        var voucherDto = await createVoucherResp.Content.ReadFromJsonAsync<PromotionCodeDto>();
        Check(voucherDto != null && voucherDto.Code == "TET2026" && voucherDto.RewardMonths == 3, "Voucher DTO matches");

        // Register new Owner with PromoCode = "TET2026"
        var regStartResp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Chu Tro Promo", "chutropromo@example.com", "OwnerPass123!", "0912345678",
            new RegistrationAttributionDto("bs_123",
                new AttributionTouchDto("facebook", "cpc", "tet_2026_campaign", null, null, "/register?promo=TET2026", null, DateTimeOffset.UtcNow),
                new AttributionTouchDto("facebook", "cpc", "tet_2026_campaign", null, null, "/register?promo=TET2026", null, DateTimeOffset.UtcNow)),
            "TET2026"));
        Check(regStartResp.StatusCode == HttpStatusCode.Accepted, "Registration with valid PromoCode returns 202 Accepted");
        var regChallenge = await regStartResp.Content.ReadFromJsonAsync<RegistrationVerificationDto>();

        var verifyResp = await client.PostAsJsonAsync("/api/auth/verify-registration",
            new VerifyRegistrationRequest(regChallenge!.ChallengeToken, otpSender.LastCode!));
        Check(verifyResp.StatusCode == HttpStatusCode.Created, "Verify registration with PromoCode returns 201 Created");
        var createdOwnerDto = await verifyResp.Content.ReadFromJsonAsync<UserDto>();
        Check(createdOwnerDto != null && createdOwnerDto.SubscriptionTier == "Pro" && createdOwnerDto.SubscriptionExpiresAt != null,
            "Auto-Promo registration automatically grants Pro tier to new Owner");

        // Verify redemptions list endpoint for Marketer
        var redemptionsResp = await SendAsync(client, HttpMethod.Get, $"/api/admin/promotions/codes/{voucherDto!.Id}/redemptions", marketerCookie);
        Check(redemptionsResp.StatusCode == HttpStatusCode.OK, "Marketer can view voucher redemptions list");
        var redemptionsList = await redemptionsResp.Content.ReadFromJsonAsync<List<PromotionRedemptionItemDto>>();
        Check(redemptionsList != null && redemptionsList.Count == 1 && redemptionsList[0].OwnerEmail == "chutropromo@example.com",
            "Voucher redemptions list includes the newly registered Owner");

        // 4. Owner cannot access Marketer endpoints
        var ownerCookie = await LoginAsync(client, "chutropromo@example.com", "OwnerPass123!");
        var ownerLeadsTry = await SendAsync(client, HttpMethod.Get, "/api/marketer/leads", ownerCookie);
        Check(ownerLeadsTry.StatusCode == HttpStatusCode.Forbidden, "Owner is forbidden (403) from /api/marketer/leads");

        // 5. Marketing CRM (/api/marketer/leads)
        var leadsResp = await SendAsync(client, HttpMethod.Get, "/api/marketer/leads?segment=All", marketerCookie);
        Check(leadsResp.StatusCode == HttpStatusCode.OK, "Marketer lists CRM leads (200 OK)");
        var leadsData = await leadsResp.Content.ReadFromJsonAsync<MarketerLeadsResponse>();
        Check(leadsData != null && leadsData.Total >= 1 && leadsData.Items.Any(x => x.Email == "chutropromo@example.com" && x.RedeemedPromoCodes.Contains("TET2026")),
            "CRM leads list contains owner contact, UTM attribution, and redeemed promo codes");
        var rawLeadsJson = await leadsResp.Content.ReadAsStringAsync();
        Check(!rawLeadsJson.Contains("idNumber", StringComparison.OrdinalIgnoreCase) &&
              !rawLeadsJson.Contains("citizenId", StringComparison.OrdinalIgnoreCase) &&
              !rawLeadsJson.Contains("permanentAddress", StringComparison.OrdinalIgnoreCase) &&
              !rawLeadsJson.Contains("bankAccountNumber", StringComparison.OrdinalIgnoreCase),
            "CRM leads payload strictly excludes sensitive CCCD and bank details");

        var filteredLeadsResp = await SendAsync(client, HttpMethod.Get, "/api/marketer/leads?source=facebook&campaign=fb_tet_2026", marketerCookie);
        var filteredLeadsData = await filteredLeadsResp.Content.ReadFromJsonAsync<MarketerLeadsResponse>();
        Check(filteredLeadsData != null && filteredLeadsData.Items.Any(x => x.Email == "chutropromo@example.com"),
            "Marketer filters leads by source and campaign query parameters");

        var targetOwnerId = leadsData!.Items.First(x => x.Email == "chutropromo@example.com").OwnerId;
        var updateLeadResp = await SendJsonAsync(client, HttpMethod.Put, $"/api/marketer/leads/{targetOwnerId}/profile", marketerCookie,
            new { leadStatus = "Interested", tags = "vip-20-phong, zalo-contacted", nextFollowUpAt = DateTimeOffset.UtcNow.AddDays(3), markContactedNow = true });
        Check(updateLeadResp.StatusCode == HttpStatusCode.OK, "Marketer updates lead profile status and tags");

        var addNoteResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/marketer/leads/{targetOwnerId}/notes", marketerCookie,
            new CreateMarketerLeadNoteRequest("Da goi dien tu van kich hoat phong dau tien."));
        Check(addNoteResp.StatusCode == HttpStatusCode.Created, "Marketer adds CRM note for Owner");

        var listNotesResp = await SendAsync(client, HttpMethod.Get, $"/api/marketer/leads/{targetOwnerId}/notes", marketerCookie);
        var notesList = await listNotesResp.Content.ReadFromJsonAsync<List<MarketerLeadNoteDto>>();
        Check(notesList != null && notesList.Count == 1 && notesList[0].Content.Contains("Da goi dien"),
            "Marketer retrieves CRM notes history");

        // 6. Content CMS: SEO Guides & Banners
        var createArticleResp = await SendJsonAsync(client, HttpMethod.Post, "/api/marketer/articles", marketerCookie,
            new
            {
                slug = "kinh-nghiem-quan-ly-nha-tro-20-phong",
                title = "Kinh nghiem quan ly nha tro 20 phong nhan ha",
                summary = "Tom tat quy trinh tu dong hoa nha tro 20 phong.",
                category = "Van hanh nha tro",
                readingTimeMinutes = 6,
                contentMarkdown = "## Buoc 1: Chuan hoa don gia\n- Nhap chi so dien\n- Gui link Zalo",
                ctaText = "Dung thu ngay",
                ctaUrl = "/register?promo=TET2026",
                isPublished = true
            });
        Check(createArticleResp.StatusCode == HttpStatusCode.Created, "Marketer creates published SEO guide article");

        var publicGuideResp = await client.GetAsync("/api/public/guides/kinh-nghiem-quan-ly-nha-tro-20-phong");
        Check(publicGuideResp.StatusCode == HttpStatusCode.OK, "Public reader fetches guide article by slug");
        var ctaClickResp = await client.PostAsync("/api/public/guides/kinh-nghiem-quan-ly-nha-tro-20-phong/cta-click", null);
        Check(ctaClickResp.StatusCode == HttpStatusCode.OK, "Public reader records guide CTA click");

        var createBannerResp = await SendJsonAsync(client, HttpMethod.Post, "/api/marketer/banners", marketerCookie,
            new
            {
                title = "Uu dai Tet 2026",
                message = "Tang 3 thang Roomie Pro voi ma TET2026",
                ctaText = "Nhan uu dai",
                ctaUrl = "/register?promo=TET2026",
                promoCode = "TET2026",
                placement = "Both",
                targetSegment = "All",
                themeColor = "coral",
                isActive = true
            });
        Check(createBannerResp.StatusCode == HttpStatusCode.Created, "Marketer creates active campaign banner");
        var bannerDto = await createBannerResp.Content.ReadFromJsonAsync<MarketingBannerDto>();
        Check(bannerDto != null && bannerDto.PromoCode == "TET2026" && bannerDto.TargetSegment == "All" && bannerDto.ThemeColor == "coral",
            "MarketingBanner persists and returns PromoCode, TargetSegment, and ThemeColor");

        var activeBannersResp = await client.GetAsync("/api/banners/active?placement=LandingTop");
        Check(activeBannersResp.StatusCode == HttpStatusCode.OK, "Public visitor fetches active LandingTop banners");
        var activeBanners = await activeBannersResp.Content.ReadFromJsonAsync<List<MarketingBannerDto>>();
        Check(activeBanners != null && activeBanners.Any(b => b.Id == bannerDto!.Id && b.PromoCode == "TET2026"),
            "Active banner is returned for LandingTop with PromoCode");

        var clickBannerResp = await client.PostAsync($"/api/banners/{bannerDto!.Id}/click", null);
        Check(clickBannerResp.StatusCode == HttpStatusCode.OK, "Banner click tracking succeeds");

        // 7. Segmented Email Campaign & Deduplication
        var createEmailResp = await SendJsonAsync(client, HttpMethod.Post, "/api/marketer/emails", marketerCookie,
            new
            {
                name = "Chao mung chu tro moi",
                segment = "StuckOnboarding",
                subject = "Chao {{OwnerName}}, bat dau tao phong tro dau tien",
                bodyHtml = "<p>Xin chao {{OwnerName}}, nha tro cua ban hien co {{RoomCount}} phong. Nhap ma {{PromoCode}}.</p>",
                ctaText = "Tao phong ngay",
                ctaUrl = "https://roomie.io.vn/buildings",
                promoCode = "TET2026"
            });
        Check(createEmailResp.StatusCode == HttpStatusCode.Created, "Marketer creates segmented email campaign");
        var emailCampaign = await createEmailResp.Content.ReadFromJsonAsync<MarketingEmailCampaignDto>();

        var sendTestResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/marketer/emails/{emailCampaign!.Id}/send-test", marketerCookie,
            new { toEmail = "mai.marketer@phongtro.local" });
        Check(sendTestResp.StatusCode == HttpStatusCode.OK,
            "Marketer sends test email with personalized token replacement");

        var sendCampaignResp = await SendJsonAsync(client, HttpMethod.Post, $"/api/marketer/emails/{emailCampaign.Id}/send", marketerCookie, new { });
        Check(sendCampaignResp.StatusCode == HttpStatusCode.OK, "Marketer dispatches segmented email campaign");
        var dispatchResult = await sendCampaignResp.Content.ReadFromJsonAsync<MarketingEmailCampaignDto>();
        Check(dispatchResult != null && dispatchResult.SentCount >= 1 && dispatchResult.Status == MarketingEmailCampaignStatus.Completed,
            "Segmented email campaign sends to StuckOnboarding owners and marks Completed");
    }

    private static WebApplication CreateTestApp(
        string connectionString,
        TestOtpSender otpSender)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "http://localhost",
            ["EmailVerification:Enabled"] = "true",
            ["Monitoring:Enabled"] = "true",
            ["AuthRequestsPerMinute"] = "100"
        });

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        var dataProtection = new EphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IDataProtectionProvider>(dataProtection);
        builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        builder.Services.AddSingleton<TenantTokenService>();
        builder.Services.AddScoped<InvoiceService>();
        builder.Services.AddScoped<ManualReceiptService>();
        builder.Services.AddScoped<PayOsService>();
        builder.Services.AddScoped<PaymentWebhookService>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IRegistrationEmailSender>(otpSender);
        builder.Services.AddScoped<RegistrationVerificationService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddMonitoringCore(builder.Configuration);

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "phongtro.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("auth", context => RateLimitPartition.GetNoLimiter("test"));
            options.AddPolicy("analytics", context => RateLimitPartition.GetNoLimiter("test"));
        });

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<RequestSecurityMiddleware>();
        app.UseAuthorization();

        app.MapAuthEndpoints();
        app.MapAdminEndpoints();
        app.MapPromotionsRoutes();
        app.MapMonitoringEndpoints();
        app.MapMarketerLeadsEndpoints();
        app.MapMarketerContentEndpoints();
        app.MapMarketerEmailEndpoints();

        return app;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        if (resp.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.FirstOrDefault(v => v.StartsWith("phongtro.session="));
            if (cookie != null) return cookie.Split(';')[0];
        }
        return string.Empty;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string cookie)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", cookie);
        req.Headers.Add("Origin", "http://localhost");
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> SendJsonAsync<T>(HttpClient client, HttpMethod method, string url, string cookie, T body)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("Cookie", cookie);
        req.Headers.Add("Origin", "http://localhost");
        return client.SendAsync(req);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class TestOtpSender : IRegistrationEmailSender
    {
        public string? LastCode { get; private set; }
        public void EnsureConfigured() { }
        public Task SendAsync(string email, string code, CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}

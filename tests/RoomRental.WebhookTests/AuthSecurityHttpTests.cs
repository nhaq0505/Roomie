using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class AuthSecurityHttpTests
{
    public static async Task RunAsync()
    {
        await PasswordPolicyAndRegistrationOtpFlowAsync();
        await LoginAndSessionLifecycleAsync();
        await MultiDevicePasswordChangeSessionRevocationAsync();
        await CsrfOriginEnforcementAsync();
        await RateLimitingEnforcementAsync();
    }

    private static async Task PasswordPolicyAndRegistrationOtpFlowAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var emailSender = new TestRegistrationEmailSender();
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        await using var app = CreateTestApp(fixture.ConnectionString, emailSender, authRateLimit: 50, clock: clock);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // 1. Password policy: reject password shorter than 9 characters
        var shortPassResp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Short Pass User", "shortpass@example.com", "12345678", "0901234567"));
        Assert(shortPassResp.StatusCode == HttpStatusCode.BadRequest, "registration rejects password under 9 characters");

        // 2. Initial registration OTP HTTP flow
        const string email = "newowner@example.com";
        const string password = "StrongPassword123!";
        var regResp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Nguyen Van A", email, password, "0901234567"));
        Assert(regResp.StatusCode == HttpStatusCode.Accepted, "registration OTP start returns 202 Accepted");

        var regDto = await regResp.Content.ReadFromJsonAsync<RegistrationVerificationDto>();
        Assert(regDto != null && regDto.VerificationRequired && !string.IsNullOrEmpty(regDto.ChallengeToken),
            "registration response returns valid challenge token and verification required");
        Assert(emailSender.LastEmail == email && !string.IsNullOrEmpty(emailSender.LastCode) && emailSender.LastCode.Length == 6,
            "verification email was sent with 6-digit OTP code");

        // 3. Reject verify with incorrect code
        var wrongVerify = await client.PostAsJsonAsync("/api/auth/verify-registration", new VerifyRegistrationRequest(
            regDto!.ChallengeToken, "000000"));
        Assert(wrongVerify.StatusCode == HttpStatusCode.BadRequest, "verify registration rejects incorrect OTP code");

        // 4. Verify with correct code -> user created
        var correctVerify = await client.PostAsJsonAsync("/api/auth/verify-registration", new VerifyRegistrationRequest(
            regDto.ChallengeToken, emailSender.LastCode!));
        Assert(correctVerify.StatusCode == HttpStatusCode.Created, "verify registration creates user with 201 Created");
        var userDto = await correctVerify.Content.ReadFromJsonAsync<UserDto>();
        Assert(userDto != null && userDto.Email == email && userDto.Role == "Owner",
            "created user has Owner role and correct email");

        // 5. Duplicate email registration rejection (409 Conflict)
        var duplicateReg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Duplicate Owner", email, password, "0909999999"));
        Assert(duplicateReg.StatusCode == HttpStatusCode.Conflict,
            "registration with duplicate email returns 409 Conflict");
    }

    private static async Task LoginAndSessionLifecycleAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var emailSender = new TestRegistrationEmailSender();
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        await using var app = CreateTestApp(fixture.ConnectionString, emailSender, authRateLimit: 50, clock: clock);
        await app.StartAsync();
        using var client = app.GetTestClient();

        const string email = "loginuser@example.com";
        const string password = "CorrectPassword123!";
        await RegisterAndVerifyAsync(client, emailSender, "Login User", email, password);

        // 1. Login with wrong password -> 401
        var wrongLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPassword123!"));
        Assert(wrongLogin.StatusCode == HttpStatusCode.Unauthorized, "login with wrong password returns 401 Unauthorized");

        // 2. Login with correct password -> 200 and session cookie
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert(loginResp.StatusCode == HttpStatusCode.OK, "login with correct password succeeds with 200 OK");
        var cookie = ExtractSessionCookie(loginResp);
        Assert(!string.IsNullOrEmpty(cookie) && cookie.StartsWith("phongtro.session="),
            "login response sets phongtro.session cookie");

        // 3. Authenticated request using session cookie
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Add("Cookie", cookie);
        var meResp = await client.SendAsync(meReq);
        Assert(meResp.StatusCode == HttpStatusCode.OK, "GET /api/auth/me succeeds with valid session cookie");
        var meDto = await meResp.Content.ReadFromJsonAsync<UserDto>();
        Assert(meDto != null && meDto.Email == email, "authenticated session correctly identifies current user");

        // 4. Request without cookie is rejected
        var unauthResp = await client.GetAsync("/api/auth/me");
        Assert(unauthResp.StatusCode == HttpStatusCode.Unauthorized, "unauthenticated request to protected endpoint returns 401");

        // 5. RememberMe login sets 14-day session and slides when remaining <= 7 days
        var rememberLoginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: true));
        Assert(rememberLoginResp.StatusCode == HttpStatusCode.OK, "rememberMe login succeeds with 200 OK");
        var rememberCookie = ExtractSessionCookie(rememberLoginResp);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latest = await db.AuthSessions.OrderByDescending(x => x.ExpiresAt).FirstAsync();
            Assert(Math.Abs((latest.ExpiresAt - latest.CreatedAt - TimeSpan.FromDays(14)).TotalSeconds) < 2,
                "rememberMe session has 14-day lifetime in database");
        }

        // Advance 5 days (9 days remaining > 7 days threshold): no slide yet
        clock.Advance(TimeSpan.FromDays(5));
        var beforeHalfResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", rememberCookie);
        Assert(beforeHalfResp.StatusCode == HttpStatusCode.OK && string.IsNullOrEmpty(ExtractSessionCookie(beforeHalfResp)),
            "session with more than half lifetime remaining is not re-issued");

        // Advance another 3 days (8 days elapsed, 6 days remaining <= 7 days threshold): slides to now + 14 days
        clock.Advance(TimeSpan.FromDays(3));
        var slideResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", rememberCookie);
        var slidCookie = ExtractSessionCookie(slideResp);
        Assert(slideResp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(slidCookie),
            "session with <= half lifetime remaining slides and re-issues cookie");

        // Advance 10 days after renewal (would have expired without sliding, but still has 4 days left)
        clock.Advance(TimeSpan.FromDays(10));
        var afterSlideResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", slidCookie);
        Assert(afterSlideResp.StatusCode == HttpStatusCode.OK,
            "slid rememberMe session remains valid past original 14-day window");

        // 6. Standard login (RememberMe: false) sets 12-hour session and slides when remaining <= 6 hours
        var stdLoginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password, RememberMe: false));
        Assert(stdLoginResp.StatusCode == HttpStatusCode.OK, "RememberMe=false login succeeds with 200 OK");
        var stdCookie = ExtractSessionCookie(stdLoginResp);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latestStd = await db.AuthSessions.OrderByDescending(x => x.CreatedAt).FirstAsync();
            Assert(Math.Abs((latestStd.ExpiresAt - latestStd.CreatedAt - TimeSpan.FromHours(12)).TotalSeconds) < 2,
                "RememberMe=false session has 12-hour lifetime in database");
        }

        // Advance 5 hours (7h remaining > 6h threshold): no slide
        clock.Advance(TimeSpan.FromHours(5));
        var stdBeforeHalf = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", stdCookie);
        Assert(stdBeforeHalf.StatusCode == HttpStatusCode.OK && string.IsNullOrEmpty(ExtractSessionCookie(stdBeforeHalf)),
            "12h session with 7h remaining (> 6h threshold) does not slide");

        // Advance 1.5 hours (6.5h elapsed, 5.5h remaining <= 6h threshold):
        // Cross-origin mutating request must be blocked by CSRF guard BEFORE sliding session
        clock.Advance(TimeSpan.FromMinutes(90));
        DateTimeOffset preCsrfExpiresAt;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            preCsrfExpiresAt = (await db.AuthSessions.OrderByDescending(x => x.CreatedAt).FirstAsync()).ExpiresAt;
        }
        var csrfBlockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(password, "NewPass12345!", "NewPass12345!"))
        };
        csrfBlockedReq.Headers.Add("Cookie", stdCookie);
        csrfBlockedReq.Headers.Add("Origin", "https://evil.example.com");
        var csrfBlockedResp = await client.SendAsync(csrfBlockedReq);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var postCsrfExpiresAt = (await db.AuthSessions.OrderByDescending(x => x.CreatedAt).FirstAsync()).ExpiresAt;
            Assert(csrfBlockedResp.StatusCode == HttpStatusCode.Forbidden &&
                   string.IsNullOrEmpty(ExtractSessionCookie(csrfBlockedResp)) &&
                   postCsrfExpiresAt == preCsrfExpiresAt,
                "cross-origin mutating request is rejected with 403 and does NOT slide session");
        }

        // Valid GET request at 5.5h remaining slides 12h session to now + 12h
        var stdSlideResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", stdCookie);
        var stdSlidCookie = ExtractSessionCookie(stdSlideResp);
        Assert(stdSlideResp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(stdSlidCookie),
            "12h session with <= 6h remaining slides and re-issues cookie");

        // Slide a second time (+6.5h -> 13h since CreatedAt, new ExpiresAt = 25h since CreatedAt > 24h)
        clock.Advance(TimeSpan.FromMinutes(390));
        var stdSlide2Resp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", stdSlidCookie);
        var stdSlid2Cookie = ExtractSessionCookie(stdSlide2Resp);
        Assert(stdSlide2Resp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(stdSlid2Cookie),
            "second slide of 12h session succeeds");

        // Slide a third time (+6.5h -> 19.5h since CreatedAt, when ExpiresAt - CreatedAt == 25h > 1 day):
        // must remain a 12h session (rm=0), never promoted to 14-day RememberMe lifetime
        clock.Advance(TimeSpan.FromMinutes(390));
        var stdSlide3Resp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", stdSlid2Cookie);
        var stdSlid3Cookie = ExtractSessionCookie(stdSlide3Resp);
        Assert(stdSlide3Resp.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(stdSlid3Cookie),
            "third slide of 12h session past 24h since CreatedAt succeeds");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latestStd = await db.AuthSessions.OrderByDescending(x => x.CreatedAt).FirstAsync();
            Assert(Math.Abs((latestStd.ExpiresAt - clock.GetUtcNow() - TimeSpan.FromHours(12)).TotalSeconds) < 2,
                "12h session sliding past 24h total age retains 12h sliding window and is not promoted to 14d");
        }

        // Advance 7 hours (5h remaining <= 6h threshold):
        // Re-login request with existing cookie must NOT slide the existing session or emit duplicate Set-Cookie headers
        clock.Advance(TimeSpan.FromHours(7));
        DateTimeOffset preReloginExpiresAt;
        string preReloginSessionId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latestStd = await db.AuthSessions.OrderByDescending(x => x.CreatedAt).FirstAsync();
            preReloginExpiresAt = latestStd.ExpiresAt;
            preReloginSessionId = latestStd.Id;
        }

        var failedReloginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, "WrongPassword123!"))
        };
        failedReloginReq.Headers.Add("Cookie", stdSlid3Cookie);
        failedReloginReq.Headers.Add("Origin", "http://localhost");
        var failedReloginResp = await client.SendAsync(failedReloginReq);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldSession = await db.AuthSessions.SingleAsync(x => x.Id == preReloginSessionId);
            Assert(failedReloginResp.StatusCode == HttpStatusCode.Unauthorized &&
                   string.IsNullOrEmpty(ExtractSessionCookie(failedReloginResp)) &&
                   oldSession.ExpiresAt == preReloginExpiresAt,
                "failed /api/auth/login with active cookie at <= half lifetime does not slide session or emit Set-Cookie");
        }

        // Now call /api/auth/logout at 5h remaining (<= 6h threshold): must revoke and not slide
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Add("Cookie", stdSlid3Cookie);
        logoutReq.Headers.Add("Origin", "http://localhost");
        var logoutAtHalfResp = await client.SendAsync(logoutReq);
        Assert(logoutAtHalfResp.StatusCode == HttpStatusCode.OK, "logout at <= half lifetime succeeds");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var latestStd = await db.AuthSessions.SingleAsync(x => x.Id == preReloginSessionId);
            Assert(latestStd.RevokedAt.HasValue &&
                   Math.Abs((latestStd.RevokedAt.Value - clock.GetUtcNow()).TotalSeconds) < 2 &&
                   Math.Abs((latestStd.ExpiresAt - clock.GetUtcNow() - TimeSpan.FromHours(5)).TotalSeconds) < 2,
                "logout at <= half lifetime revokes session at clock time without sliding ExpiresAt");
        }

        // 7. Admin password reset after clock has advanced > 20 days issues valid 30-min token and revokes sessions at clock time
        Guid ownerId;
        const string adminEmail = "admin-reset@example.com";
        const string adminPass = "AdminPass123!";
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var ownerUser = await db.Users.SingleAsync(x => x.Email == email);
            ownerId = ownerUser.Id;
            var adminUser = new AppUser
            {
                Name = "Admin Reset Tester",
                Email = adminEmail,
                PasswordHash = "",
                Role = UserRole.Admin,
                Status = OwnerStatus.Approved,
                CreatedAt = clock.GetUtcNow()
            };
            adminUser.PasswordHash = hasher.HashPassword(adminUser, adminPass);
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var adminLoginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminEmail, adminPass));
        var adminCookie = ExtractSessionCookie(adminLoginResp);
        var adminResetReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/owners/{ownerId}/reset-password")
        {
            Content = JsonContent.Create(new AdminResetPasswordRequest(adminPass))
        };
        adminResetReq.Headers.Add("Cookie", adminCookie);
        adminResetReq.Headers.Add("Origin", "http://localhost");
        var adminResetResp = await client.SendAsync(adminResetReq);
        Assert(adminResetResp.StatusCode == HttpStatusCode.OK, "admin reset-password succeeds when clock is advanced");
        using var adminResetDoc = JsonDocument.Parse(await adminResetResp.Content.ReadAsStringAsync());
        var resetToken = adminResetDoc.RootElement.GetProperty("token").GetString()!;

        const string resetNewPass = "ResetByAdminPass123!";
        var completeResetResp = await client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest(email, resetToken, resetNewPass, resetNewPass));
        Assert(completeResetResp.StatusCode == HttpStatusCode.OK,
            "owner completes admin-issued password reset when TimeProvider clock is advanced > 20 days");
    }

    private static async Task MultiDevicePasswordChangeSessionRevocationAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var emailSender = new TestRegistrationEmailSender();
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        await using var app = CreateTestApp(fixture.ConnectionString, emailSender, authRateLimit: 50, clock: clock);
        await app.StartAsync();
        using var client = app.GetTestClient();

        const string email = "multidevice@example.com";
        const string oldPassword = "InitialPassword123!";
        const string newPassword = "UpdatedPassword456!";
        await RegisterAndVerifyAsync(client, emailSender, "Multi Device Owner", email, oldPassword);

        // Device A logs in
        var loginA = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, oldPassword));
        var cookieA = ExtractSessionCookie(loginA);

        // Device B logs in
        var loginB = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, oldPassword));
        var cookieB = ExtractSessionCookie(loginB);

        Assert(cookieA != cookieB, "distinct devices receive distinct session tokens");

        // Both Device A and Device B can access protected endpoints
        var checkAResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", cookieA);
        var checkBResp = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", cookieB);
        Assert(checkAResp.StatusCode == HttpStatusCode.OK && checkBResp.StatusCode == HttpStatusCode.OK,
            "both device sessions are initially valid and authenticated");

        // Device A changes password
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(oldPassword, newPassword, newPassword))
        };
        changeReq.Headers.Add("Cookie", cookieA);
        changeReq.Headers.Add("Origin", "http://localhost");
        var changeResp = await client.SendAsync(changeReq);
        Assert(changeResp.StatusCode == HttpStatusCode.OK, "Device A successfully changes password");

        // Verify in DB: other sessions revoked, current session active
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await db.AuthSessions.ToListAsync();
            Assert(sessions.Count >= 2, "at least two sessions exist in database");
            var activeCount = sessions.Count(s => s.RevokedAt == null);
            var revokedCount = sessions.Count(s => s.RevokedAt != null);
            Assert(activeCount == 1 && revokedCount >= 1,
                "password change revokes other sessions in database while keeping current session active");
        }

        // Verify via HTTP: Device B receives 401 Unauthorized via RequestSecurityMiddleware
        var deviceBCheck = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", cookieB);
        Assert(deviceBCheck.StatusCode == HttpStatusCode.Unauthorized,
            "revoked Device B session receives 401 Unauthorized via RequestSecurityMiddleware");

        // Verify via HTTP: Device A remains authenticated
        var deviceACheck = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", cookieA);
        Assert(deviceACheck.StatusCode == HttpStatusCode.OK,
            "current Device A session remains authenticated after password change");

        // Device A logs out
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Add("Cookie", cookieA);
        logoutReq.Headers.Add("Origin", "http://localhost");
        var logoutResp = await client.SendAsync(logoutReq);
        Assert(logoutResp.StatusCode == HttpStatusCode.OK, "Device A logs out successfully");

        // After logout, Device A also receives 401
        var deviceAPostLogout = await SendWithCookie(client, HttpMethod.Get, "/api/auth/me", cookieA);
        Assert(deviceAPostLogout.StatusCode == HttpStatusCode.Unauthorized,
            "Device A session is revoked after logout and receives 401 Unauthorized");
    }

    private static async Task CsrfOriginEnforcementAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var emailSender = new TestRegistrationEmailSender();
        var clock = new ControllableClock(DateTimeOffset.UtcNow);
        await using var app = CreateTestApp(fixture.ConnectionString, emailSender, authRateLimit: 50, clock: clock);
        await app.StartAsync();
        using var client = app.GetTestClient();

        const string email = "csrfowner@example.com";
        const string password = "CsrfPassword123!";
        await RegisterAndVerifyAsync(client, emailSender, "CSRF Owner", email, password);

        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var cookie = ExtractSessionCookie(loginResp);

        // 1. Mutating request (POST /api/auth/change-password) without Origin or Referer -> 403 Forbidden
        var noOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(password, "NewCsrfPass456!", "NewCsrfPass456!"))
        };
        noOriginReq.Headers.Add("Cookie", cookie);
        var noOriginResp = await client.SendAsync(noOriginReq);
        Assert(noOriginResp.StatusCode == HttpStatusCode.Forbidden,
            "mutating authenticated request without Origin/Referer returns 403 Forbidden");

        // 2. Mutating request with foreign Origin -> 403 Forbidden
        var foreignOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(password, "NewCsrfPass456!", "NewCsrfPass456!"))
        };
        foreignOriginReq.Headers.Add("Cookie", cookie);
        foreignOriginReq.Headers.Add("Origin", "https://attacker.evil.com");
        var foreignOriginResp = await client.SendAsync(foreignOriginReq);
        Assert(foreignOriginResp.StatusCode == HttpStatusCode.Forbidden,
            "mutating authenticated request with mismatched Origin returns 403 Forbidden");

        // 3. Mutating request with matching Origin -> Succeeds
        var validOriginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(password, "NewCsrfPass456!", "NewCsrfPass456!"))
        };
        validOriginReq.Headers.Add("Cookie", cookie);
        validOriginReq.Headers.Add("Origin", "http://localhost");
        var validOriginResp = await client.SendAsync(validOriginReq);
        Assert(validOriginResp.StatusCode == HttpStatusCode.OK,
            "mutating authenticated request with matching Origin succeeds with 200 OK");
    }

    private static async Task RateLimitingEnforcementAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        var emailSender = new TestRegistrationEmailSender();
        // Configure low permit limit (5 requests per minute) for rate limiting test
        await using var app = CreateTestApp(fixture.ConnectionString, emailSender, authRateLimit: 5);
        await app.StartAsync();
        using var client = app.GetTestClient();

        var rateLimitHit = false;
        for (var i = 0; i < 10; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ratelimit@example.com", "Password123!"));
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimitHit = true;
                break;
            }
        }
        Assert(rateLimitHit, "endpoint with 'auth' rate limiting policy returns 429 TooManyRequests when limit exceeded");
    }

    private static WebApplication CreateTestApp(string connectionString, TestRegistrationEmailSender emailSender, int authRateLimit = 50, TimeProvider? clock = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "http://localhost",
            ["EmailVerification:Enabled"] = "true",
            ["AuthRequestsPerMinute"] = authRateLimit.ToString()
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
        builder.Services.AddSingleton<IRegistrationEmailSender>(emailSender);
        builder.Services.AddScoped<RegistrationVerificationService>();
        builder.Services.AddSingleton(clock ?? TimeProvider.System);

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "phongtro.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = false;
            options.TimeProvider = clock ?? TimeProvider.System;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new { message = "Please sign in." });
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsJsonAsync(new { message = "You do not have access to this resource." });
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            static string PartitionKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many requests. Try again shortly." }, cancellationToken);
            };
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

        return app;
    }

    private static async Task RegisterAndVerifyAsync(HttpClient client, TestRegistrationEmailSender emailSender, string name, string email, string password)
    {
        var regResp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, password, "0901234567"));
        var regDto = await regResp.Content.ReadFromJsonAsync<RegistrationVerificationDto>();
        await client.PostAsJsonAsync("/api/auth/verify-registration", new VerifyRegistrationRequest(regDto!.ChallengeToken, emailSender.LastCode!));
    }

    private static string ExtractSessionCookie(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = values.FirstOrDefault(v => v.StartsWith("phongtro.session="));
            if (cookie != null) return cookie.Split(';')[0];
        }
        return string.Empty;
    }

    private static async Task<HttpResponseMessage> SendWithCookie(HttpClient client, HttpMethod method, string url, string cookie)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", cookie);
        return await client.SendAsync(req);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }

    private sealed class ControllableClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class TestRegistrationEmailSender : IRegistrationEmailSender
    {
        public string? LastEmail { get; set; }
        public string? LastCode { get; set; }
        public void EnsureConfigured() { }
        public Task SendAsync(string email, string code, CancellationToken cancellationToken)
        {
            LastEmail = email;
            LastCode = code;
            return Task.CompletedTask;
        }
    }
}

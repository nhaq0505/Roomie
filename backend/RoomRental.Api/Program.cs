using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true)
        .AddJsonFile("appsettings.Email.local.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables().AddCommandLine(args);

var provider = builder.Configuration["DatabaseProvider"] ?? "SQLite";
var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(configuredConnection))
        throw new InvalidOperationException("ConnectionStrings__DefaultConnection is required when DatabaseProvider is Postgres.");
    builder.Services.AddDbContext<PostgresAppDbContext>(options => options.UseNpgsql(configuredConnection));
    builder.Services.AddScoped<AppDbContext>(services => services.GetRequiredService<PostgresAppDbContext>());
}
else if (provider.Equals("SQLite", StringComparison.OrdinalIgnoreCase))
{
    var defaultDbPath = Path.Combine(builder.Environment.ContentRootPath, ".local-data", "room-rental.db");
    var sqliteConnection = configuredConnection ?? $"Data Source={defaultDbPath}";
    var sqliteFile = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(sqliteConnection).DataSource;
    if (!string.IsNullOrWhiteSpace(sqliteFile) && sqliteFile != ":memory:")
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sqliteFile))!);
    builder.Services.AddDbContext<SqliteAppDbContext>(options => options.UseSqlite(sqliteConnection));
    builder.Services.AddScoped<AppDbContext>(services => services.GetRequiredService<SqliteAppDbContext>());
}
else
{
    throw new InvalidOperationException("DatabaseProvider must be SQLite or Postgres.");
}

var dataProtection = builder.Services.AddDataProtection();
var keyPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    Directory.CreateDirectory(keyPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<TenantTokenService>();
builder.Services.AddScoped<InvoiceService>();
builder.Services.AddScoped<ManualReceiptService>();
builder.Services.AddScoped<PayOsService>();
builder.Services.AddScoped<PaymentWebhookService>();
builder.Services.AddScoped<IRegistrationEmailSender, SmtpRegistrationEmailSender>();
builder.Services.AddScoped<RegistrationVerificationService>();
builder.Services.AddScoped<IPasswordRecoveryEmailSender, SmtpRegistrationEmailSender>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddHostedService<PasswordRecoveryEmailWorker>();
// Disabling verification is reserved for isolated Development integration fixtures.
if (!builder.Configuration.GetValue("EmailVerification:Enabled", true) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Email verification cannot be disabled outside Development.");
if (!builder.Configuration.GetValue("RequireVerifiedPaymentForTenantLink", true) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("RequireVerifiedPaymentForTenantLink cannot be disabled outside Development.");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<DataMaintenanceService>();
if (!bool.TryParse(builder.Configuration["EnableInvoiceSweep"], out var enableInvoiceSweep) || enableInvoiceSweep)
    builder.Services.AddHostedService<InvoiceSweepService>();
if (!bool.TryParse(builder.Configuration["EnablePaymentReconciliation"], out var enablePaymentReconciliation) || enablePaymentReconciliation)
    builder.Services.AddHostedService<PaymentReconciliationHostedService>();
builder.Services.AddHttpClient("payOS", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "phongtro.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = false;
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
    var configuredAuthLimit = int.TryParse(builder.Configuration["AuthRequestsPerMinute"], out var configuredLimit)
        ? Math.Clamp(configuredLimit, 1, 1000)
        : builder.Environment.IsDevelopment() ? 100 : 10;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = configuredAuthLimit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    }));
    options.AddPolicy("tenant-payment", context => RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 12,
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
await DatabaseInitializer.InitializeAsync(app);
if (builder.Configuration.GetValue("Database:MigrateOnly", false)) return;

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto
};
foreach (var value in (builder.Configuration["TrustedProxies"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    if (!IPAddress.TryParse(value, out var address))
        throw new InvalidOperationException("TrustedProxies must contain comma-separated proxy IP addresses.");
    forwardedHeaders.KnownProxies.Add(address);
    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        forwardedHeaders.KnownProxies.Add(address.MapToIPv6());
}
app.UseForwardedHeaders(forwardedHeaders);
app.UseMiddleware<ApiExceptionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.IsHttps) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; img-src 'self' data:; font-src 'self' data: https://fonts.gstatic.com; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseMiddleware<SeoMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<RequestSecurityMiddleware>();
app.UseAuthorization();

app.MapGet("/api/health", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        if (await db.Database.CanConnectAsync(cancellationToken))
            return Results.Json(new { status = "ok", database = "ok" });
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        // Return a stable readiness response without leaking connection details.
    }
    return Results.Json(new { status = "unavailable", database = "unreachable" }, statusCode: 503);
});
app.MapAuthEndpoints();
app.MapPasswordRecoveryEndpoints();
app.MapAdminEndpoints();
app.MapOwnerManagementEndpoints();
app.MapReportsEndpoints();
app.MapBillingEndpoints();
app.MapDepositEndpoints();
app.MapMoveoutEndpoints();
app.MapPaymentEndpoints();

app.MapFallback("{*path:nonfile}", async context =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.Equals("/api", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message = "API route was not found." });
        return;
    }

    var isHome = path == "/";
    var isApp = SeoMiddleware.IsAppPath(path);
    if (!isHome && !isApp) context.Response.StatusCode = StatusCodes.Status404NotFound;
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), isHome ? "index.html" : "app.html");
    if (!File.Exists(index))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { message = "Page was not found." });
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(index, context.RequestAborted);
});

app.Run();

public partial class Program { }

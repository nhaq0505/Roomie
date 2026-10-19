using System.Text.RegularExpressions;

namespace RoomRental.Api.Services;

/// <summary>HTTP index controls also apply before static files and authentication errors.</summary>
public sealed partial class SeoMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
{
    private static readonly HashSet<string> AppPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/login", "/register", "/reset-password", "/change-password", "/account-status",
        "/dashboard", "/buildings", "/rooms", "/readings", "/invoices", "/reports",
        "/settings/payment", "/admin/owners"
    };

    [GeneratedRegex(@"^/(t|tenant)/[^/]+(?:/invoices/[^/]+/checkout)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GuestPath();

    public static bool IsAppPath(string path) => AppPaths.Contains(path.TrimEnd('/')) || GuestPath().IsMatch(path.TrimEnd('/'));

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var publicDocument = path == "/";
        var canonicalHost = context.Request.Host.Host.Equals("roomie.io.vn", StringComparison.OrdinalIgnoreCase);
        var allowIndexing = configuration.GetValue("Seo:AllowIndexing", !environment.IsDevelopment());
        var language = context.Request.Query["lang"];
        var otherLanguage = language.Any(value => !string.IsNullOrEmpty(value) && value != "vi");
        var privateDocument = IsAppPath(path) || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || (!publicDocument && !Path.HasExtension(path));
        context.Response.OnStarting(() =>
        {
            if (!allowIndexing || !canonicalHost || otherLanguage || privateDocument || context.Response.StatusCode >= 400)
                context.Response.Headers["X-Robots-Tag"] = "noindex, follow";
            return Task.CompletedTask;
        });

        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            if (path.Equals("/welcome", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/welcome/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                // Local fixed destination preserves query strings without accepting a redirect URL.
                context.Response.Redirect("/" + context.Request.QueryString, permanent: true);
                return;
            }
            if (path.Equals("/app.html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (publicDocument) context.Response.Headers.CacheControl = "no-cache";
            if (IsAppPath(path)) context.Response.Headers.CacheControl = "no-store";
        }
        await next(context);
    }
}

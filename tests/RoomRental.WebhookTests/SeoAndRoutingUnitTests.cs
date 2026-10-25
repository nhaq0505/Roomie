using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoomRental.Api.Services;

internal static class SeoAndRoutingUnitTests
{
    public static async Task RunAsync()
    {
        TestStaticPathClassificationHelpers();
        await TestSeoMiddlewareHttpPipelineAsync();
    }

    private static void TestStaticPathClassificationHelpers()
    {
        // 1. Public 4-page landing suite classification & file resolution
        (string Path, string ExpectedFile)[] publicRoutes =
        [
            ("/", "index.html"),
            ("/features", Path.Combine("features", "index.html")),
            ("/features/", Path.Combine("features", "index.html")),
            ("/pricing", Path.Combine("pricing", "index.html")),
            ("/pricing/", Path.Combine("pricing", "index.html")),
            ("/huong-dan", Path.Combine("huong-dan", "index.html")),
            ("/huong-dan/", Path.Combine("huong-dan", "index.html")),
            ("/FEATURES", Path.Combine("features", "index.html")),
        ];

        foreach (var (path, expectedFile) in publicRoutes)
        {
            Assert(SeoMiddleware.IsPublicPath(path), $"IsPublicPath('{path}') is true");
            Assert(SeoMiddleware.TryGetPublicHtmlFile(path, out var resolved) && resolved == expectedFile,
                $"TryGetPublicHtmlFile('{path}') resolves to '{expectedFile}'");
            Assert(!SeoMiddleware.IsAppPath(path), $"public path '{path}' is not an app path");
        }

        foreach (var nonPublic in new[] { "/login", "/dashboard", "/huong-dan/bai-viet-1", "/pricing/pro", "/unknown-page" })
        {
            Assert(!SeoMiddleware.IsPublicPath(nonPublic), $"IsPublicPath('{nonPublic}') is false");
            Assert(!SeoMiddleware.TryGetPublicHtmlFile(nonPublic, out _), $"TryGetPublicHtmlFile('{nonPublic}') is false");
        }

        // 2. Private SPA paths (including new routes: /rooms/:id/tenant, /rooms/:id/documents, /admin/promotions, /settings/zalo, /admin/monitoring)
        string[] validAppPaths =
        [
            "/login", "/login/", "/register", "/reset-password", "/change-password", "/account-status", "/profile",
            "/dashboard", "/buildings", "/rooms", "/readings", "/invoices", "/expenses", "/reports",
            "/settings/payment", "/settings/zalo", "/settings/whatsapp",
            "/admin/owners", "/admin/monitoring", "/admin/promotions", "/admin/promotions/",
            "/rooms/room-123/tenant", "/rooms/room-123/tenant/",
            "/rooms/00000000-0000-0000-0000-000000000001/documents",
            "/rooms/00000000-0000-0000-0000-000000000001/documents/",
            "/t/tok_abc123", "/tenant/tok_abc123",
            "/t/tok_abc123/invoices/inv_999/checkout",
            "/tenant/tok_abc123/invoices/inv_999/checkout/"
        ];

        foreach (var appPath in validAppPaths)
        {
            Assert(SeoMiddleware.IsAppPath(appPath), $"IsAppPath('{appPath}') is true");
        }

        string[] invalidAppPaths =
        [
            "/", "/features", "/pricing", "/huong-dan",
            "/rooms/room-123/unknown", "/rooms/room-123", "/rooms/a/b/tenant",
            "/t/tok_abc123/invoices", "/t/tok_abc123/unknown",
            "/api/rooms", "/unknown"
        ];

        foreach (var nonApp in invalidAppPaths)
        {
            Assert(!SeoMiddleware.IsAppPath(nonApp), $"IsAppPath('{nonApp}') is false");
        }
    }

    private static async Task TestSeoMiddlewareHttpPipelineAsync()
    {
        await using var app = CreateSeoTestApp(allowIndexing: true);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // 1. Canonical host (roomie.io.vn) on all 4 public pages -> indexable (no X-Robots-Tag), Cache-Control: no-cache, real prerendered HTML & canonical URL
        (string Path, string ExpectedCanonical, string ExpectedTitleSubstring)[] publicPages =
        [
            ("/", "https://roomie.io.vn/", "Roomie — Phần mềm quản lý phòng trọ"),
            ("/features", "https://roomie.io.vn/features", "Tính năng quản lý phòng trọ, hóa đơn &amp; Zalo | Roomie"),
            ("/pricing", "https://roomie.io.vn/pricing", "Bảng giá phần mềm quản lý phòng trọ &amp; Hỏi đáp | Roomie"),
            ("/huong-dan", "https://roomie.io.vn/huong-dan", "Hướng dẫn quản lý nhà trọ, tính điện &amp; thu tiền QR | Roomie"),
        ];

        foreach (var (publicPath, expectedCanonical, expectedTitle) in publicPages)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://roomie.io.vn{publicPath}");
            using var resp = await client.SendAsync(req);
            var html = await resp.Content.ReadAsStringAsync();
            Assert(resp.StatusCode == HttpStatusCode.OK, $"GET {publicPath} returns 200 OK");
            Assert(!resp.Headers.Contains("X-Robots-Tag"), $"canonical public path '{publicPath}' has no X-Robots-Tag");
            Assert(resp.Headers.CacheControl?.NoCache == true, $"public path '{publicPath}' sets Cache-Control: no-cache");
            Assert(html.Contains($"href=\"{expectedCanonical}\""), $"public path '{publicPath}' contains canonical '{expectedCanonical}'");
            Assert(html.Contains($"{expectedTitle}</title>"), $"public path '{publicPath}' contains title '{expectedTitle}'");
            Assert(html.Contains("content=\"index, follow\""), $"public path '{publicPath}' contains index, follow meta");
        }

        // Trailing-slash and ?lang=vi variants on canonical host
        foreach (var variantPath in new[] { "/features/", "/pricing/", "/huong-dan/", "/huong-dan?lang=vi" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://roomie.io.vn{variantPath}");
            using var resp = await client.SendAsync(req);
            Assert(resp.StatusCode == HttpStatusCode.OK, $"GET {variantPath} returns 200 OK");
            Assert(!resp.Headers.Contains("X-Robots-Tag"), $"canonical public path '{variantPath}' has no X-Robots-Tag");
            Assert(resp.Headers.CacheControl?.NoCache == true, $"public path '{variantPath}' sets Cache-Control: no-cache");
        }

        // 2. Non-canonical host on public page -> X-Robots-Tag: noindex, follow
        using (var nonCanonicalReq = new HttpRequestMessage(HttpMethod.Get, "https://staging.roomie.io.vn/features"))
        using (var nonCanonicalResp = await client.SendAsync(nonCanonicalReq))
        {
            Assert(GetRobotsTag(nonCanonicalResp) == "noindex, follow", "non-canonical host receives X-Robots-Tag: noindex, follow");
        }

        // 3. Non-Vietnamese query (?lang=en) on canonical public page -> X-Robots-Tag: noindex, follow
        using (var langReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/pricing?lang=en"))
        using (var langResp = await client.SendAsync(langReq))
        {
            Assert(GetRobotsTag(langResp) == "noindex, follow", "public page with ?lang=en receives X-Robots-Tag: noindex, follow");
        }

        // 4. Private SPA routes on canonical host -> X-Robots-Tag: noindex, follow + Cache-Control: no-store + app.html shell
        foreach (var privatePath in new[]
        {
            "/login", "/dashboard", "/rooms/", "/rooms/r1/tenant", "/rooms/r1/documents",
            "/admin/promotions", "/admin/monitoring", "/settings/zalo", "/settings/whatsapp",
            "/t/synthetic-seo-token", "/t/synthetic-seo-token/invoices/inv1/checkout"
        })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://roomie.io.vn{privatePath}");
            using var resp = await client.SendAsync(req);
            var html = await resp.Content.ReadAsStringAsync();
            Assert(resp.StatusCode == HttpStatusCode.OK, $"GET private route {privatePath} returns 200 OK");
            Assert(GetRobotsTag(resp) == "noindex, follow", $"private route '{privatePath}' sets X-Robots-Tag: noindex, follow");
            Assert(resp.Headers.CacheControl?.NoStore == true, $"private route '{privatePath}' sets Cache-Control: no-store");
            Assert(html.Contains("content=\"noindex, follow\"") && !html.Contains("rel=\"canonical\"") && !html.Contains("synthetic-seo-token"),
                $"private route '{privatePath}' serves app.html without canonical or token leak");
        }

        // 4b. HEAD request on private SPA route -> 200 OK + noindex + empty body
        using (var headReq = new HttpRequestMessage(HttpMethod.Head, "https://roomie.io.vn/rooms"))
        using (var headResp = await client.SendAsync(headReq))
        {
            var headBytes = await headResp.Content.ReadAsByteArrayAsync();
            Assert(headResp.StatusCode == HttpStatusCode.OK && GetRobotsTag(headResp) == "noindex, follow" && headBytes.Length == 0,
                "HEAD /rooms returns 200 OK with noindex and empty body");
        }

        // 5. Redirects: /welcome, /welcome/, /index.html -> 301 to / preserving query string
        (string Source, string ExpectedLocation)[] redirects =
        [
            ("/welcome", "/"),
            ("/welcome/", "/"),
            ("/welcome?utm_source=zalo&utm_medium=cpc", "/?utm_source=zalo&utm_medium=cpc"),
            ("/index.html", "/"),
            ("/index.html?lang=en", "/?lang=en")
        ];
        foreach (var (source, expectedLocation) in redirects)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://roomie.io.vn{source}");
            using var resp = await client.SendAsync(req);
            Assert(resp.StatusCode == HttpStatusCode.MovedPermanently, $"GET {source} returns 301 MovedPermanently");
            Assert(resp.Headers.Location?.ToString() == expectedLocation, $"GET {source} redirects to '{expectedLocation}'");
        }

        // 6. Direct /app.html request -> 404 NotFound + noindex
        using (var appHtmlReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/app.html"))
        using (var appHtmlResp = await client.SendAsync(appHtmlReq))
        {
            Assert(appHtmlResp.StatusCode == HttpStatusCode.NotFound, "GET /app.html returns 404 NotFound");
            Assert(GetRobotsTag(appHtmlResp) == "noindex, follow", "GET /app.html sets X-Robots-Tag: noindex, follow");
        }

        // 7. Unknown route & API routes -> 404 / 401 with X-Robots-Tag: noindex, follow
        using (var unknownReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/does-not-exist"))
        using (var unknownResp = await client.SendAsync(unknownReq))
        {
            Assert(unknownResp.StatusCode == HttpStatusCode.NotFound, "GET /does-not-exist returns 404");
            Assert(GetRobotsTag(unknownResp) == "noindex, follow", "404 response sets X-Robots-Tag: noindex, follow");
        }

        using (var api404Req = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/api/missing-seo-endpoint"))
        using (var api404Resp = await client.SendAsync(api404Req))
        {
            Assert(api404Resp.StatusCode == HttpStatusCode.NotFound && GetRobotsTag(api404Resp) == "noindex, follow",
                "GET /api/missing-seo-endpoint returns 404 with noindex");
        }

        using (var api401Req = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/api/rooms"))
        using (var api401Resp = await client.SendAsync(api401Req))
        {
            Assert(api401Resp.StatusCode == HttpStatusCode.Unauthorized && GetRobotsTag(api401Resp) == "noindex, follow",
                "GET /api/rooms unauthenticated returns 401 with noindex");
        }

        // 8. Static SEO assets: /sitemap.xml, /robots.txt, /roomie-social.png
        using (var sitemapReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/sitemap.xml"))
        using (var sitemapResp = await client.SendAsync(sitemapReq))
        {
            var xml = await sitemapResp.Content.ReadAsStringAsync();
            Assert(sitemapResp.StatusCode == HttpStatusCode.OK &&
                   xml.Contains("<loc>https://roomie.io.vn/</loc>") &&
                   xml.Contains("<loc>https://roomie.io.vn/features</loc>") &&
                   xml.Contains("<loc>https://roomie.io.vn/pricing</loc>") &&
                   xml.Contains("<loc>https://roomie.io.vn/huong-dan</loc>"),
                "GET /sitemap.xml serves all 4 canonical public URLs");
        }

        using (var robotsReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/robots.txt"))
        using (var robotsResp = await client.SendAsync(robotsReq))
        {
            var robotsTxt = (await robotsResp.Content.ReadAsStringAsync()).Trim().Replace("\r\n", "\n");
            Assert(robotsResp.StatusCode == HttpStatusCode.OK && robotsTxt == "User-agent: *\nDisallow: /",
                "GET /robots.txt blocks all crawlers");
        }

        // 9. When Seo:AllowIndexing=false, even canonical / receives noindex
        await using var noIndexApp = CreateSeoTestApp(allowIndexing: false);
        await noIndexApp.StartAsync();
        using var noIndexClient = noIndexApp.GetTestClient();
        using var rootReq = new HttpRequestMessage(HttpMethod.Get, "https://roomie.io.vn/");
        using var rootResp = await noIndexClient.SendAsync(rootReq);
        Assert(GetRobotsTag(rootResp) == "noindex, follow", "Seo:AllowIndexing=false forces noindex even on canonical root");
    }

    private static WebApplication CreateSeoTestApp(bool allowIndexing)
    {
        var webRoot = FindFrontendDistDirectory();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            WebRootPath = webRoot
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seo:AllowIndexing"] = allowIndexing ? "true" : "false"
        });
        builder.Services.AddAuthentication("SeoTestCookie")
            .AddCookie("SeoTestCookie", options =>
            {
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseMiddleware<SeoMiddleware>();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/api/rooms", () => Results.Ok(Array.Empty<object>())).RequireAuthorization();

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

            var isPublic = SeoMiddleware.TryGetPublicHtmlFile(path, out var publicFile);
            var isApp = SeoMiddleware.IsAppPath(path);
            if (!isPublic && !isApp) context.Response.StatusCode = StatusCodes.Status404NotFound;
            var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), isPublic ? publicFile : "app.html");
            if (!File.Exists(index))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { message = "Page was not found." });
                return;
            }
            if (HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index, context.RequestAborted);
        });
        return app;
    }

    private static string FindFrontendDistDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "frontend", "dist");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                return candidate;
            var wwwroot = Path.Combine(dir.FullName, "wwwroot");
            if (Directory.Exists(wwwroot) && File.Exists(Path.Combine(wwwroot, "index.html")))
                return wwwroot;
            dir = dir.Parent;
        }
        var tempRoot = Path.Combine(Path.GetTempPath(), "roomie-seo-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "features"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "pricing"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "huong-dan"));
        File.WriteAllText(Path.Combine(tempRoot, "index.html"), "<!doctype html><html><head><title>Roomie — Phần mềm quản lý phòng trọ</title><link rel=\"canonical\" href=\"https://roomie.io.vn/\" /><meta name=\"robots\" content=\"index, follow\" /></head><body></body></html>");
        File.WriteAllText(Path.Combine(tempRoot, "features", "index.html"), "<!doctype html><html><head><title>Tính năng quản lý phòng trọ, hóa đơn &amp; Zalo | Roomie</title><link rel=\"canonical\" href=\"https://roomie.io.vn/features\" /><meta name=\"robots\" content=\"index, follow\" /></head><body></body></html>");
        File.WriteAllText(Path.Combine(tempRoot, "pricing", "index.html"), "<!doctype html><html><head><title>Bảng giá phần mềm quản lý phòng trọ &amp; Hỏi đáp | Roomie</title><link rel=\"canonical\" href=\"https://roomie.io.vn/pricing\" /><meta name=\"robots\" content=\"index, follow\" /></head><body></body></html>");
        File.WriteAllText(Path.Combine(tempRoot, "huong-dan", "index.html"), "<!doctype html><html><head><title>Hướng dẫn quản lý nhà trọ, tính điện &amp; thu tiền QR | Roomie</title><link rel=\"canonical\" href=\"https://roomie.io.vn/huong-dan\" /><meta name=\"robots\" content=\"index, follow\" /></head><body></body></html>");
        File.WriteAllText(Path.Combine(tempRoot, "app.html"), "<!doctype html><html><head><title>Roomie</title><meta name=\"robots\" content=\"noindex, follow\" /></head><body><div id=\"root\"></div></body></html>");
        File.WriteAllText(Path.Combine(tempRoot, "sitemap.xml"), "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://roomie.io.vn/</loc></url><url><loc>https://roomie.io.vn/features</loc></url><url><loc>https://roomie.io.vn/pricing</loc></url><url><loc>https://roomie.io.vn/huong-dan</loc></url></urlset>");
        File.WriteAllText(Path.Combine(tempRoot, "robots.txt"), "User-agent: *\nDisallow: /\n");
        return tempRoot;
    }

    private static string? GetRobotsTag(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Robots-Tag", out var values) ? values.SingleOrDefault() : null;

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

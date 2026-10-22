"""Read-only SEO contract against a built frontend served by a disposable local API.

Opt in with SEO_BASE_URL=http://127.0.0.1:<port>, Seo__AllowIndexing=true on API.
See tests/README.md for the isolated server command. No production database needed.
"""
import json
import os
import re
import unittest
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


@unittest.skipUnless(os.environ.get("SEO_BASE_URL"), "Set SEO_BASE_URL for built-site SEO checks")
class SeoHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.base = os.environ["SEO_BASE_URL"].rstrip("/")
        parsed = urllib.parse.urlsplit(cls.base)
        if parsed.hostname not in ("localhost", "127.0.0.1") or parsed.scheme != "http":
            raise RuntimeError("SEO fixture requires a disposable HTTP localhost server")
        cls.client = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def fetch(self, path, host="roomie.io.vn", method="GET"):
        request = urllib.request.Request(self.base + path, headers={"Host": host}, method=method)
        try:
            response = self.client.open(request, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.status, response.headers, response.read()

    def test_home_has_real_html_and_unique_metadata(self):
        status, headers, body = self.fetch("/")
        html = body.decode()
        self.assertEqual(status, 200)
        self.assertNotIn("noindex", headers.get("X-Robots-Tag", ""))
        self.assertEqual(html.count('<h1>'), 1)
        self.assertIn("Gọn gàng từng phòng.", html)
        self.assertIn('id="features"', html)
        self.assertEqual(html.count('rel="canonical"'), 1)
        self.assertIn('href="https://roomie.io.vn/"', html)
        self.assertIn('content="index, follow"', html)
        self.assertIn('Roomie — Phần mềm quản lý phòng trọ</title>', html)
        self.assertIn('property="og:image" content="https://roomie.io.vn/roomie-social.png"', html)
        self.assertIn('name="twitter:card" content="summary_large_image"', html)

    def test_public_styles_exist_without_javascript(self):
        _, _, body = self.fetch("/")
        styles = re.findall(r'<link[^>]+rel="stylesheet"[^>]+href="([^"]+)"', body.decode())
        # Vite emits href before rel in entry links; accept both orders.
        styles += re.findall(r'<link[^>]+href="([^"]+)"[^>]+rel="stylesheet"', body.decode())
        self.assertTrue(styles)
        css = ""
        for path in set(styles):
            if not path.startswith("/") or path.startswith("//"):
                continue  # External font services are outside this isolated fixture.
            status, headers, contents = self.fetch(path)
            self.assertEqual(status, 200)
            self.assertIn("text/css", headers["Content-Type"])
            css += contents.decode()
        self.assertIn(".landing-hero", css)

    def test_structured_data_uses_only_real_brand_fields(self):
        _, _, body = self.fetch("/")
        match = re.search(r'<script id="site-structured-data" type="application/ld\+json">(.*?)</script>', body.decode())
        self.assertIsNotNone(match)
        data = json.loads(match.group(1))
        self.assertEqual([item["@type"] for item in data["@graph"]], ["Organization", "WebSite"])
        self.assertNotIn("aggregateRating", match.group(1))

    def test_legacy_home_redirect_preserves_query(self):
        for path in ("/welcome", "/welcome/", "/index.html"):
            with self.subTest(path=path):
                status, headers, _ = self.fetch(path + "?lang=vi&utm_source=qa")
                self.assertEqual(status, 301)
                self.assertEqual(headers["Location"], "/?lang=vi&utm_source=qa")

    def test_private_shell_never_inherits_landing_or_canonical(self):
        paths = ["/login", "/register", "/reset-password", "/change-password", "/account-status",
                 "/dashboard", "/buildings", "/rooms", "/readings", "/invoices", "/reports",
                 "/settings/payment", "/admin/owners", "/rooms/", "/t/synthetic-seo-token",
                 "/tenant/synthetic-seo-token", "/t/synthetic-seo-token/invoices/fixture/checkout",
                 "/tenant/synthetic-seo-token/invoices/fixture/checkout"]
        for path in paths:
            with self.subTest(path=path):
                status, headers, body = self.fetch(path)
                html = body.decode()
                self.assertEqual(status, 200)
                self.assertIn("noindex", headers.get("X-Robots-Tag", ""))
                self.assertEqual(headers.get("Cache-Control"), "no-store")
                self.assertIn('content="noindex, follow"', html)
                self.assertNotIn('rel="canonical"', html)
                self.assertNotIn('id="features"', html)
                self.assertNotIn("synthetic-seo-token", html)

    def test_unknown_urls_return_real_404(self):
        for path in ("/missing-seo-page", "/missing-seo-page/", "/missing-seo.xml", "/t/x/unknown", "/app.html"):
            with self.subTest(path=path):
                status, headers, _ = self.fetch(path)
                self.assertEqual(status, 404)
                self.assertIn("noindex", headers.get("X-Robots-Tag", ""))

    def test_api_errors_and_auth_still_protected(self):
        for path, expected in (("/api/missing-seo-endpoint", 404), ("/api/rooms", 401)):
            with self.subTest(path=path):
                status, headers, _ = self.fetch(path)
                self.assertEqual(status, expected)
                self.assertIn("noindex", headers.get("X-Robots-Tag", ""))

    def test_demo_and_untrusted_hosts_cannot_index(self):
        for host in ("roomiedemo.duckdns.org", "localhost", "untrusted.invalid"):
            with self.subTest(host=host):
                status, headers, body = self.fetch("/", host=host)
                self.assertEqual(status, 200)
                self.assertIn("noindex", headers.get("X-Robots-Tag", ""))
                self.assertIn('href="https://roomie.io.vn/"', body.decode())

    def test_non_vietnamese_queries_noindex(self):
        for query in ("en", "zh", "ko", "hi", "unknown", "vi&lang=en"):
            with self.subTest(query=query):
                status, headers, _ = self.fetch("/?lang=" + query)
                self.assertEqual(status, 200)
                self.assertIn("noindex", headers.get("X-Robots-Tag", ""))
        _, headers, _ = self.fetch("/?lang=vi&utm_source=qa")
        self.assertNotIn("noindex", headers.get("X-Robots-Tag", ""))

    def test_sitemap_contains_only_published_canonical_urls(self):
        status, headers, body = self.fetch("/sitemap.xml")
        self.assertEqual(status, 200)
        self.assertIn("xml", headers["Content-Type"])
        root = ET.fromstring(body)
        urls = root.findall(".//{http://www.sitemaps.org/schemas/sitemap/0.9}loc")
        self.assertEqual([url.text for url in urls], [
            "https://roomie.io.vn/",
            "https://roomie.io.vn/features",
            "https://roomie.io.vn/pricing",
            "https://roomie.io.vn/huong-dan",
        ])

    def test_robots_blocks_all_crawlers(self):
        status, headers, body = self.fetch("/robots.txt")
        self.assertEqual(status, 200)
        self.assertIn("text/plain", headers["Content-Type"])
        self.assertEqual(body.decode().strip(), "User-agent: *\nDisallow: /")

    def test_social_image_is_valid_1200_by_630_png(self):
        status, headers, body = self.fetch("/roomie-social.png")
        self.assertEqual(status, 200)
        self.assertEqual(headers["Content-Type"], "image/png")
        self.assertEqual(body[:8], b"\x89PNG\r\n\x1a\n")
        self.assertEqual(int.from_bytes(body[16:20], "big"), 1200)
        self.assertEqual(int.from_bytes(body[20:24], "big"), 630)

    def test_head_has_matching_controls_and_no_body(self):
        status, headers, body = self.fetch("/rooms", method="HEAD")
        self.assertEqual(status, 200)
        self.assertIn("noindex", headers.get("X-Robots-Tag", ""))
        self.assertEqual(body, b"")


if __name__ == "__main__":
    unittest.main()

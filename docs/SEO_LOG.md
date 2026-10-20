# Nhật ký thực hiện SEO Roomie

File ghi toàn bộ quá trình SEO. Mỗi đợt bổ sung một mục; sửa kết luận cũ bằng ghi chú mới có thời gian, không xóa lịch sử. Giờ Việt Nam (Asia/Ho_Chi_Minh). Không ghi dữ liệu người thuê, URL có token thật, thông tin tài khoản hoặc secret.

## 15/10/2026 — Bắt đầu tuần 1–2

- **Mục tiêu:** nền tảng SEO tiếng Việt cho chủ trọ trên https://roomie.io.vn; giữ giao diện và luồng nghiệp vụ.
- **Khảo sát:** React/Vite CSR; `/` và `/welcome` cùng landing; `main.tsx` đặt title chung; backend fallback trả 200 cho URL lạ; chưa có sitemap, robots, canonical hoặc HTML nội dung sẵn.
- **Đã thực hiện:** tạo `docs/SEO_PLAN.md` và nhật ký này trước khi sửa mã SEO; người dùng chốt 4 bài trong 8 tuần và chỉ Search Console.
- **Trạng thái:** đang triển khai local, chưa commit/push/deploy. Chưa có kết quả Google index hoặc số liệu Search Console.
- **Kiểm tra và kết quả:** sẽ bổ sung sau khi chạy kiểm tra bản build và HTTP trên môi trường tách biệt.

## 15/10/2026 09:04 — Hoàn tất phần mã nguồn và QA tuần 1–2

- **Đã thực hiện:**
  - Tách `LandingContent` dùng chung cho React và bộ tạo HTML lúc build (`frontend/scripts/prerender.mjs`); xuất CSS cho cả HTML tĩnh, theme dùng chung và tải landing trực tiếp để không hiện spinner chờ xác thực cho khách.
  - Tạo metadata theo route, canonical trang chủ, Open Graph/Twitter, PNG chia sẻ 1200×630 và JSON-LD Organization/WebSite từ thông tin thương hiệu thật. Trang riêng không có canonical/JSON-LD công khai hoặc token trong metadata.
  - Sinh `robots.txt` cho phép bot đọc noindex và sitemap chỉ chứa `https://roomie.io.vn/`. Chưa thêm bài hoặc URL chưa xuất bản.
  - `SeoMiddleware` đặt X-Robots-Tag trước static/auth cho trang riêng/API, host khác tên miền chính, biến thể ngôn ngữ và lỗi HTTP; mặc định Development noindex. `/welcome`, `/welcome/`, `/index.html` HTTP301 về `/`, giữ query; URL lạ HTTP404, `/app.html` trực tiếp HTTP404.
  - SPA quản trị và link khách vẫn nhận app shell riêng; giữ auth, API, callback payOS và điều hướng người đã đăng nhập vào dashboard.
  - Thêm `SEO_ALLOW_INDEXING` vào hai Compose và `.env.example`; thêm 13 kiểm tra SEO HTTP, ghép vào pipeline CI trên image/database tạm. README, kế hoạch và bàn giao yêu cầu tiếp tục ghi nhật ký.
- **Kiểm tra thực tế:**
  - `npm --prefix frontend run build`: PASS, bao gồm TypeScript, Vite client và prerender. HTML trang chủ ~102KB (có CSS inline), app shell ~2KB; renderer build-only được xóa khỏi dist.
  - `dotnet build backend/RoomRental.Api --nologo`: PASS, 0 warnings / 0 errors.
  - `npm --prefix frontend run i18n:check`: PASS, 1014 keys ×4 catalogs và kiểm tra runtime.
  - `SEO_BASE_URL=http://127.0.0.1:5096 /usr/bin/python3 -m unittest discover -s tests -p 'test_seo_http.py' -v`: **13/13 PASS**, bao gồm HTML/CSS không cần JS, canonical/JSON-LD/OG, redirect/query, private noindex, demo, ngôn ngữ, sitemap/robots, auth error,404,HEAD và ảnh PNG.
  - Bộ `test_api_integration.py` trên cùng API tạm: **20/20 PASS** (auth, phân quyền, hóa đơn, cọc, trả phòng, link khách và webhook giả). Outbound proxy chặn các cuộc gọi HTTP(S) ngoài máy.
  - Trình duyệt bản build: trang chủ → đăng ký gỡ canonical/JSON-LD, quay lại khôi phục đúng1 bộ metadata; login bằng tài khoản mẫu local thành công; truy cập `/` khi đăng nhập về `/dashboard`; console không warn/error.
  - HTML tĩnh bỏ module script, đặt trong srcdoc QA375px và1440px: hiển thị tiếng Việt, H1 đúng, scrollWidth bằng viewport, không tràn ngang. Fixture chỉ nằm trong dist bị ignore và đã xóa; không sửa header bảo mật X-Frame-Options DENY.
  - `git diff --check`: PASS. PNG chia sẻ đã xem trực tiếp, chữ tiếng Việt rõ và đúng thương hiệu.
- **Sửa trong lúc QA:** chuyển renderer sang Vite SSR bundle để xử lý tương thích ESM/CJS của thư viện; bộ test chỉ tải CSS nội bộ thay vì gọi dịch vụ font bên ngoài. Python Homebrew hiện lỗi thư viện expat, nên dùng `/usr/bin/python3` cho test XML. Fixture srcdoc ban đầu thiếu charset, đã thêm UTF-8 rồi kiểm tra lại; không phải lỗi charset của HTML sản phẩm.
- **Môi trường:** API5096 dùng SQLite/database/keys tạm hoàn toàn tách biệt. Không nâng schema/reset/seed DB đang dùng, không gửi OTP hay tạo giao dịch thật.
- **Phát hành:** mới hoàn tất local, chưa commit/push/deploy; CI được bổ sung nhưng chưa chạy trên GitHub. Kiểm tra public trước phát hành: `/robots.txt` và `/sitemap.xml` vẫn404, xác nhận website đang chạy bản trước.
- **Search Console:** trình duyệt hiện chỉ tới trang giới thiệu/yêu cầu đăng nhập; chưa có quyền vào property. Chưa xác minh DNS, chưa gửi sitemap, chưa yêu cầu index, chưa có baseline impressions/clicks/CTR/truy vấn. Không suy ra đã index từ QA local.
- **Kết thúc QA:** hai Compose đều PASS `docker compose ... config --no-interpolate --quiet`; API thử nghiệm5096 đã dừng, các fixture HTML đã xóa.
- **Bước tiếp theo:** phát hành bản đã kiểm thử, kiểm tra lại công khai; người dùng đăng nhập Search Console để xác minh domain và gửi sitemap. Nội dung tuần3–6 chưa thực hiện.

## 15/10/2026 09:10 — Search Console trên Edge: đang chờ xác minh DNS

- Người dùng cho biết đã đăng nhập và mở Search Console trong Edge. Đã mở đúng tab có sẵn, xác nhận vào được tài khoản; không ghi thông tin tài khoản vào tài liệu.
- Thêm website theo loại Domain property `roomie.io.vn`; Google đã cung cấp TXT xác minh và hiện hộp thoại chờ xác minh quyền sở hữu DNS. Chưa xác minh thành công, chưa gửi sitemap hoặc yêu cầu index.
- Mở `https://access.pavietnam.vn` trong Edge để tiếp tục bước DNS; hiện ở màn hình đăng nhập, chưa có phiên quản lý DNS. Chưa thêm/sửa/xóa bản ghi tên miền.
- Đã giữ tab Google xác minh và tab P.A Việt Nam để người dùng đăng nhập. Bằng chứng màn hình tạm: `/tmp/roomie-gsc-verification.jpg`.
- Mã SEO vẫn ở local, chưa commit/push/deploy. Cần người dùng đăng nhập trang P.A Việt Nam, sau đó thêm đúng TXT Google cấp và Verify; gửi sitemap sau khi bản SEO được phát hành và URL sitemap trả200.

## 15/10/2026 09:35 — Xác minh Search Console thành công qua DNS

- **Ủy quyền/ngữ cảnh:** người dùng đã đăng nhập P.A Việt Nam sau khi được thông báo cần thêm TXT xác minh Google. Tiếp tục đúng tên miền `roomie.io.vn` trong Edge.
- **Đã thực hiện:** thêm đúng một TXT tại host`@`,TTL3600 với mã Google cấp cho Domain property`roomie.io.vn`; lưu và xác nhận bằng giao diện P.A Việt Nam. Không ghi lại tài khoản hoặc thông tin đăng nhập.
- **Đối chiếu:** sau tải lại trang có10 bản ghi, toàn bộ9 bản ghi cũ khớp host/type/value/TTL với trước khi lưu. Giữ nguyên A,www,DKIM,DMARC và các bản ghi email khác.
- **DNS công khai:** `dig @ns1.pavietnam.vn roomie.io.vn TXT +short` và `dig @8.8.8.8 roomie.io.vn TXT +short` đều trả TXTGoogle vừa thêm.
- **Google:** bấm Xác minh; Search Console hiển thị **Đã xác minh quyền sở hữu**, phương thức Nhà cung cấp tên miền. Đã mở trang Tổng quan cho`sc-domain:roomie.io.vn` và giữ tab cho người dùng.
- **Baseline:** các mục Hiệu suất,Lập chỉ mục,Trải nghiệm,Tính năng nâng cao báo đang xử lý dữ liệu, kiểm tra lại sau khoảng một ngày. Đây là chưa có số liệu, không phải0click/0impression hoặc xác nhận đã index.
- **Bằng chứng:** `/tmp/roomie-gsc-verified.jpg` (Google xác nhận) và `/tmp/roomie-gsc-overview.jpg` (tổng quan). Giữ TXT để duy trì quyền sở hữu; không tự xóa sau xác minh.
- **Phát hành/sitemap:** kiểm tra lại`https://roomie.io.vn/sitemap.xml` cònHTTP404. Chưa gửi sitemap hoặc yêu cầu index, chưa commit/push/deploy bản SEO. Cần phát hành bản đãQA, kiểm tra sitemap200 rồi gửiGoogle.

## 15/10/2026 09:40 — Theo yêu cầu mới: robots.txt chặn tất cả bot

- **Yêu cầu đã chốt:** người dùng xác nhận “robots.txt đóng hết tất cả”, bao gồm cả Googlebot. Tạm dừng mở crawl/gửi sitemap/yêu cầu index cho đến khi có yêu cầu mở lại.
- **Thay đổi:** thêm `frontend/public/robots.txt` với đúng hai chỉ thị `User-agent: *` và `Disallow: /`; gỡ phần prerender tự ghi đè bằng Allow. Vite dev phục vụ file public, bản build sao chép nguyên trạng. Không quảng bá sitemap trong robots.
- **Kiểm tra:** frontend build/prerender PASS; suite SEOHTTP cập nhật hợp đồng robots và chạy **13/13PASS** trên backend với SQLite/keys tạm, APIQA đã dừng. Không đổi DB vận hành.
- **Phát hành tối thiểu:** commit`65d4907` chỉ gồm `frontend/public/robots.txt` (2dòng), đã pushmain để CI/CD áp dụng chặn trên website. Các thay đổi SEO khác và nhật ký hiện vẫn local; đang chờ kết quả CI/deploy trước khi kết luận live.
- **Giới hạn:** robots là chỉ thị cho bot tuân thủ; không phải cơ chế chặn cứng bot bỏ qua, không tự xóa URL đã đượcGoogleindex. Quyền Search Console/TXT xác minh đã có được giữ nguyên; không gửi sitemap hoặc requestindex.
- **Trước phát hành:** `https://roomie.io.vn/robots.txt` trảHTTP404; cần kiểm tra lại HTTP200 và đúngDisallow saudeploy.

## 15/10/2026 09:43 — robots.txt đã phát hành trên website

- Commit`65d49076d5c9a3ab9a72abaf81873506df16fca9` chỉ chứa`frontend/public/robots.txt`; [pipeline38672131045](https://github.com/nhaq0505/ReHouse/actions/runs/38672131045) đã hoàn tất **success** cả Build/test,Publishimage vàDeployVPS. Không phát hành kèm các thay đổiSEO khác còn ởlocal.
- Kiểm traHTTPS public: `https://roomie.io.vn/robots.txt` và`https://www.roomie.io.vn/robots.txt`(redirect) đềuHTTP200,Content-Type`text/plain`,nội dung chính xác `User-agent: *` / `Disallow: /`. Chỉ thị chặncrawl tất cảbot đãlive.
- Kiểm tra sau deploy: trang chủ`/` và`/api/health` đềuHTTP200, website vẫn hoạt động.
- Hostdemo`roomiedemo.duckdns.org` không phân giảiDNS ở lượt kiểm tra này; không kết luận kết quảHTTPchohostđó. Đây không phải lỗiHTTPS của tên miền chính.
- Tiếp tục giữTXTGoogle/quyềnSearchConsole; không gửi sitemap hoặc yêu cầuindex. Mở lạicrawl chỉ khi người dùng yêu cầu. Các chỉnh sửa generator/test/docs để bảo toàn chính sách này vẫnlocal.

## 15/10/2026 11:18 — Nền tảng SEO đã live cùng bản sửa CI/payOS, vẫn chặn crawl

- Người dùng yêu cầu sửa vàpush lỗiCI sau commit2e28b8d. Sourcefix46f1aad đãpushmain; [run38680393633](https://github.com/nhaq0505/ReHouse/actions/runs/38680393633) success build/test,publishimage vàdeployVPS.
- Image mới chứa cả phầnSEOprerender đãpush ở2e28b8d trướcđó nhưng chưađượcdeploy vì testfail. HTTPsaupháthành: trangchủ200/cóH1+canonical,sitemap200,health200; APIroomsunauth401 cónoindex.
- `https://roomie.io.vn/robots.txt` vẫn200 với chínhxác `User-agent: *` / `Disallow: /`. Không mởcrawl; không gửisitemap hoặc yêu cầuGoogleindex. XácminhSearchConsole/TXT trướcđó đượcgiữ.
- Chỉkiểmtra publicread-only, khôngtestghiDBproduction hoặc tạoOTP/thanh toánthật. KếtquảlocalCIchi tiết ở`docs/ci-payos-link-fix.md`.

## Mẫu cho các đợt tiếp theo

```text
## YYYY-MM-DD HH:mm — Tên đợt
- Mục tiêu:
- Công việc đã thực hiện (file/URL):
- Kiểm tra, kết quả, bằng chứng:
- Phát hành (local/commit/deploy và phiên bản):
- Kết quả quan sát được (nguồn, khoảng thời gian):
- Tồn đọng và bước tiếp theo:
```

## 20/10/2026 — Trang riêng nhắc hóa đơn Zalo

- Đăng ký `/settings/zalo` vào danh sách trang riêng của backend và metadata frontend để truy cập trực tiếp nhận app shell, `noindex` và không có canonical công khai. Sitemap/robots không đổi.
- Frontend build và kiểm tra SEO HTTP trong suite SQLite dùng thử: PASS. Chỉ cập nhật mã nguồn/local, chưa push/deploy; không có thao tác Search Console hay thay đổi trạng thái index.

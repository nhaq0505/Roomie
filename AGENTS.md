# Roomie — hướng dẫn tiếp quản

Đọc file này trước khi sửa dự án. Đây là tài liệu bàn giao kỹ thuật chính; README chỉ hướng dẫn vào dự án, runbook trong `docs/` chỉ dành cho thao tác vận hành. Khi code đổi, sửa mục tương ứng ở đây thay vì tạo thêm báo cáo bàn giao theo ngày.

Baseline rà soát: `e4fe403`, ngày 25/10/2026. Mô tả dưới đây dựa trên repository, không phải kết luận về phiên bản hay dữ liệu đang chạy trên VPS. Kiểm tra `git status`, `git log` và CI trước khi tiếp tục; không ghi đè thay đổi đang có của người dùng.

## 1. Sản phẩm và các quyết định đang áp dụng

Roomie (`https://roomie.io.vn`) quản lý nhà trọ cho nhiều chủ trọ: dãy/phòng, lượt thuê, chỉ số điện, hóa đơn, cọc, trả phòng, báo cáo Excel, thanh toán payOS hoặc xác nhận thủ công, nhắc hóa đơn qua Zalo. Admin quản lý chủ trọ, nhân sự Marketer và dashboard growth/revenue/operations. Marketer quản lý phễu tăng trưởng/doanh thu/chi phí chiến dịch (`/admin/monitoring`), mã khuyến mãi & link Auto-Promo (`/admin/promotions`), CRM & phân khúc chủ trọ (`/marketer/leads`), CMS Cẩm nang SEO & Banner (`/marketer/content`, `/huong-dan/:slug`) và chiến dịch Email theo phân khúc (`/marketer/emails`), nhưng bị chặn (`403 Forbidden`) khỏi toàn bộ dữ liệu vận hành hạ tầng (`Operations`, `Incidents`, `Maintenance`), duyệt/khóa chủ trọ và dữ liệu nhạy cảm của khách thuê/ngân hàng. Camera/OCR/AI chưa triển khai.

- Đăng ký email OTP thành công tạo **Owner Approved**, gói Free (hoặc Pro có thời hạn nếu cấu hình `Promotions:NewUserProMonths > 0` hoặc đăng ký qua link Auto-Promo `/register?promo=CODE`). Đừng dựa vào README cũ nói mọi tài khoản mới phải chờ duyệt. Admin vẫn có thể đổi trạng thái Pending/Approved/Rejected/Suspended.
- Sổ chi phí (`/expenses`) mở cho cả Free; lãi/lỗ, đối chiếu điện EVN, Excel hai báo cáo đó, chi định kỳ và chứng từ là Pro. Chi tiết bất biến ở mục 5 "Sổ chi phí, lãi/lỗ và chứng từ".
- Free tối đa 5 phòng đang sử dụng; Pro có thời hạn hoặc vô thời hạn. Kiểm tra `AppUser.IsPro/MaxRooms` và endpoint subscription trước khi sửa quota.
- Đăng nhập chỉ dùng mật khẩu. Các cột MFA cũ vẫn tồn tại để tương thích schema, không phải tính năng cần bật lại.
- Mật khẩu mới 9–256 ký tự, không toàn khoảng trắng, không ép ký tự đặc biệt.
- Tiền thuê tính **đủ tháng**, không chia theo ngày vào/ra. Ngày và kỳ nghiệp vụ theo Việt Nam (UTC+7).
- Khoản thu thủ công mới phải bằng **toàn bộ dư nợ**; dữ liệu thanh toán một phần cũ vẫn hợp lệ. Không phục hồi UI nhập khoản thu một phần từ test/doc lịch sử.
- Tin Zalo dẫn về **`/t/{token}`**, để khách xem hóa đơn rồi tự chọn thanh toán. Không gửi thẳng checkout, số tài khoản hay ảnh QR trong tin.
- `frontend/public/robots.txt` cố ý chặn mọi bot (`User-agent: *`, `Disallow: /`) theo yêu cầu người dùng. Không tự mở crawl, gửi sitemap hay yêu cầu index.
- Cẩm nang hướng dẫn (`/huong-dan` và `/huong-dan/:slug`) kết hợp danh mục hướng dẫn tĩnh với bài viết CMS xuất bản từ `MarketingArticle` (`/api/public/guides`). Đo bằng Search Console và telemetry nội bộ, không tự thêm GA4 hay hứa traffic/thứ hạng.
- Logo đang dùng trong app: `frontend/src/shared/ui/RoomieMark.tsx`, `frontend/public/roomie-logo.svg`, `favicon.svg`; ảnh social `roomie-social.png/.svg`; email `backend/RoomRental.Api/Assets/roomie-email-logo.png`. Bản logo/cover Facebook mới nằm trong `output/facebook/`, chưa tự thay vào app.

## 2. Bản đồ repository

| Đường dẫn | Vai trò / điểm bắt đầu |
| --- | --- |
| `backend/RoomRental.Api/Program.cs` | DI, provider DB, workers, cookie, limiter, middleware, đăng ký routes, SPA fallback |
| `backend/RoomRental.Api/Domain/` | Entities và DTO; monitoring, Zalo, manual payments, recovery, marketer CRM/content/emails có file riêng |
| `backend/RoomRental.Api/Data/AppDbContext.cs` | Toàn bộ mapping, indexes, concurrency tokens, DbSets |
| `backend/RoomRental.Api/Data/ProviderDbContexts.cs` | Context/factory PostgreSQL |
| `backend/RoomRental.Api/Data/Migrations/Postgres/` | Migration, designer và snapshot cho PostgreSQL |
| `backend/RoomRental.Api/Features/` | Endpoint/service chia theo Auth, Admin, Marketer, Properties, Billing, Payments, Reports, Zalo, Monitoring |
| `backend/RoomRental.Api/Shared/` | Security, SEO, calendar, lookup owner/property, pagination, fingerprint, cleanup |
| `compose.postgres-dev.yaml` | Compose PostgreSQL dev local (container `rehouse-pg-migration-db`, port 15432) |
| `frontend/src/app/` | Entry React, routing, layout, CSS assembly, metadata và prerender entry |
| `frontend/src/features/` | API, types, page, hook và CSS theo nghiệp vụ |
| `frontend/src/shared/` | HTTP, i18n, analytics, format, theme, UI dùng chung |
| `frontend/scripts/` | Build/check i18n, architecture, prerender và các test Node |
| `frontend/e2e/` | Playwright với API giả lập; không phụ thuộc API vận hành |
| `zalo-gateway/` | Node 24 + zca-js, đăng nhập Zalo QR, lưu phiên và ledger mã hóa |
| `whatsapp-gateway/` | Node 24 + `@whiskeysockets/baileys`, đăng nhập WhatsApp QR (Linked Devices), lưu phiên và ledger mã hóa |
| `tests/` | Console C# suites, HTTP Python harness, SMTP sink, tests backup/host collector |
| `.github/workflows/ci-cd.yml` | Các cổng CI, image đã kiểm thử, GHCR và deploy theo digest |
| `deploy/` | Production Compose, migration overlay, backup/restore, role DB, Caddy, firewall, host collector |
| `scripts/` | Tiện ích webhook tunnel |
| `rehouse-video/` | Dự án Remotion độc lập, mock quảng cáo dùng tên ReHouse cũ; không nằm trong runtime/build app |
| `.agents/skills/ui-ux-pro-max/` | Skill UI/UX Pro Max từ nextlevelbuilder, thay skill ui-ux cũ; dữ liệu và công cụ tìm kiếm chạy local, tuân thủ stack/quy ước dự án |
| `output/`, `artifacts/`, `out/` | Deliverables, bằng chứng QA hoặc bản sao công việc; không tự coi mọi file trong đây là rác |

Tên assembly/namespace, cookie `phongtro.session`, image `ghcr.io/nhaq0505/rehouse`, path host `/opt/rehouse` vẫn giữ tên cũ. Đổi tên thương hiệu không có nghĩa được đổi những định danh này đồng loạt.

## 3. Đọc code theo việc cần làm

| Việc | Backend | Frontend / test chính |
| --- | --- | --- |
| Đăng ký, login, đổi/reset mật khẩu, hồ sơ chủ trọ (`/profile`) | `Features/Auth/`, `Shared/RequestSecurityMiddleware.cs` | `features/auth/` (`OwnerProfilePage.tsx`), `AuthSecurityHttpTests`, `RegistrationVerificationTests`, `PasswordRecoveryTests` |
| Dãy, phòng, lượt thuê, cấp link | `Features/Properties/OwnerManagementEndpoints.*`, `TenantTokenService.cs` | `features/buildings`, `features/rooms`, `RoomTenantPage.tsx`, `RoomTenantLifecycleHttpTests`, `TenantLinkHttpTests` |
| Hợp đồng thuê trọ, CT01, quét QR CCCD / Passport MRZ & ảnh giấy tờ | `Features/Properties/OwnerManagementEndpoints.Documents.cs` (`/api/rooms/{id}/documents/identity-photo`), `ContractWordGenerator.cs`, `Domain/ContractDocuments.cs`, `Domain/TenantPortalSubmissions.cs`, `Shared/VietnameseMoneyReader.cs` | `RoomDocumentsPage.tsx`, `ContractDocumentsDrawer.tsx`, `shared/ui/CccdQrScannerModal.tsx`, `shared/ui/cccdQr.ts`, `shared/i18n/legalTemplates.ts`, `ContractDocumentsAndMoneyTests.cs`, `test-cccd-qr.mjs`, `RoomTenantLifecycleHttpTests`, `landlord.spec.ts` |
| Chốt điện, tạo/phát hành/sửa hóa đơn | `Features/Billing/BillingEndpoints.*`, `InvoiceService.cs` | `features/readings` (`ReadingsPage.tsx`), `features/invoices`, `AccountingBillingHttpTests`, `RoomTenantLifecycleHttpTests`, `landlord.spec.ts` |
| Ghi thu, cọc, trả phòng | `ManualReceiptService.cs`, `DepositEndpoints.cs`, `Properties/MoveoutEndpoints.cs` | `DepositLedger.tsx`, `MoveoutDrawer.tsx`, `ManualReceiptTests` |
| payOS, checkout gộp, webhook | `Features/Payments/PayOsService.*`, `PaymentEndpoints.cs`, `PaymentReservations.cs` | `features/tenant`, `PayOsPaymentTests`, `BatchPaymentTests`, `PayOsOperationsHttpTests` |
| VietQR, xác nhận chuyển khoản | `ManualPaymentEndpoints.cs`, `PaymentPreferencesEndpoints.cs`, `Domain/ManualPayments.cs` | `ManualPaymentRequests.tsx`, `PaymentModeSettings.tsx`, `ManualPaymentPanel.tsx`, `test_payment_modes.py` |
| Nâng cấp Pro & Khuyến mãi | `BillingEndpoints.Subscription.cs`, `PayOsService.Subscriptions.cs`, `Features/Admin/AdminPromotionsEndpoints.cs`, `Domain/Promotions.cs`, `Features/Auth/RegistrationVerificationService.cs` | `features/subscription`, `features/admin/AdminPromotionsPage.tsx`, `PromotionsTests.cs`, `test_billing_upgrade.py`, `admin.spec.ts`, `landlord.spec.ts` |
| Báo cáo/xuất Excel | `Features/Reports/ReportsEndpoints.*` | `features/reports`, `test_reports_archive.py` |
| Sổ chi phí, chi định kỳ, chứng từ, lãi/lỗ, đối chiếu điện EVN | `Features/Expenses/` (`ExpenseEndpoints*.cs`, `RecurringExpenseEndpoints.cs`, `RecurringExpenseGenerator.cs`, `ExpenseSupport.cs`), `Domain/Expenses.cs`, `Features/Reports/ReportsEndpoints.ProfitLoss.cs` | `features/expenses`, `features/reports/ProfitLossPanel.tsx`, `ElectricityReconciliationPanel.tsx`, `shared/ui/ProUpsell.tsx`, `ExpenseLedgerHttpTests.cs`, `e2e/expenses.spec.ts` |
| Nhắc Zalo & WhatsApp | `Features/Zalo/`, `Features/WhatsApp/`, `Domain/Zalo.cs`, `Domain/WhatsApp.cs` | `features/zalo`, `features/whatsapp`, `RoomRental.ZaloTests`, `test_zalo_http.py`, `zalo-gateway/test`, `whatsapp-gateway/test` |
| Theo dõi growth/revenue & ROAS | `Features/Monitoring/MonitoringServices.cs`, interfaces/entities/DTO cùng nhóm | `features/admin/monitoring`, `shared/analytics`, `MonitoringBusinessTests`, `MarketerSuiteHttpTests` |
| Marketer CRM, CMS SEO/Banner & Email | `Features/Marketer/`, `Features/Admin/AdminPromotionsEndpoints.cs`, `Domain/Marketer*.cs` | `features/marketer`, `features/marketing/GuideArticlePage.tsx`, `shared/ui/ActiveBannerStrip.tsx`, `MarketerSuiteHttpTests`, `admin.spec.ts` |
| Host/API/worker monitoring | `Features/Monitoring/Operations/`, `deploy/monitoring/` | `OperationsTab.tsx`, `OperationsMonitoringTests`, `test_host_collector.py` |
| Landing/SEO | `Shared/SeoMiddleware.cs` | `features/marketing`, `app/seo`, `scripts/prerender.mjs`, `SeoAndRoutingUnitTests.cs`, `test_seo_http.py`, `smoke.spec.ts` |

API thực tế được khai báo bằng `MapGet/MapPost/...` trong các endpoint trên; DTO ở `Domain/ApiDtos.cs`, `MonitoringDtos.cs`, `ReportDtos.cs`, `ManualPayments.cs`, `PasswordRecovery.cs`. Đối chiếu handler và DTO trước khi sửa consumer; không duy trì một bản liệt kê API thủ công tách rời code.

## 4. Kiến trúc và quy ước sửa code

### Frontend

- React 19, TypeScript 5.8, Vite 6, React Router 7, TanStack Query 5, Ant Design 5; CSS thuần. Không tự đưa Tailwind/bộ UI khác vào chỉ để sửa một màn.
- `app → features → shared`. Shared không import app/features. Feature không import app hoặc `src/api.ts`; giữa các feature dùng public `index.ts`, ngoại lệ phiên auth là `auth/session.ts`. `check:architecture` kiểm imports và vòng phụ thuộc runtime.
- `frontend/src/api.ts` là facade tương thích cho consumer/test cũ, không phải nơi thêm logic nghiệp vụ. Không xóa vì thấy app không import trực tiếp.
- Trang phức tạp dùng hook `use*Page` và component nhận dữ liệu/handler; giữ vòng đời idempotency key, query key và invalidation khi refactor.
- HTTP qua `shared/http/client.ts`: same-origin cookie, Accept-Language, timeout, unwrap envelope, `ApiError`; mutation không retry tự động. Excel dùng `requestBlob`.
- `app/styles.css` ghép CSS theo thứ tự có chủ đích. Giữ cascade khi di chuyển rule. Nền/brand nằm ở `shared/styles/foundation.css`, Ant token ở `shared/design/antdTheme.ts`; font toàn hệ thống Be Vietnam Pro (nội dung, tiêu đề, số liệu và Ant Design); giữ monospace cho mã/nội dung chuyển khoản.
- Tiếng Việt là nguồn; `shared/i18n/source.json` và `translations.tsv` cùng các catalog en/zh/ko/hi. Dùng `t()`; sau đổi copy chạy `i18n:build`, `i18n:check`; đọc script trước khi sửa trực tiếp catalog sinh ra.
- Routes private phải đồng bộ `app/App.tsx`, `app/seo/site.ts`, `Shared/SeoMiddleware.cs`. Router React không đủ để hard-refresh chạy trên server.
- Prerender sinh HTML tĩnh cho bộ 4 trang công khai (`/` → `dist/index.html`, `/features` → `dist/features/index.html`, `/pricing` → `dist/pricing/index.html`, `/huong-dan` → `dist/huong-dan/index.html`), `app.html` cho SPA và `sitemap.xml`. Không xuất renderer build-only vào production. Trang lạ/API lạ phải trả 404 thật.
- Bộ Landing page (`LandingPage.tsx`, `LandingVisuals.tsx`, `LandingSections.tsx`, `GuideArticlePage.tsx`, `LandingPage.css`) chia thành 4 trang trên thanh điều hướng: Trang chủ (`/`) ngắn gọn + thẻ khám phá, Tính năng & So sánh (`/features`), Bảng giá Free/Pro & FAQ `<details>/<summary>` (`/pricing`), và Cẩm nang hướng dẫn (`/huong-dan`, cùng trang chi tiết `/huong-dan/:slug`), dùng chung `LandingHeader` (gồm cả `.landing-subnav` mobile), `LandingCtaSection` và `LandingFooter`. Landing motion dùng IntersectionObserver/Web Animations; giữ nội dung nhìn được khi thiếu JS, Reduce Motion hoặc thiếu API; focus bàn phím phải hủy reveal đang che link.
- Dashboard monitoring có Growth/Revenue/Operations, filter ngày Việt Nam, campaign costs và UTM link builder. `null`/Unknown phải hiển thị chưa có dữ liệu cùng lý do, không biến thành 0 hoặc 100%.

### Backend

- ASP.NET Core .NET 10 Minimal APIs, EF Core 10 + Npgsql, ClosedXML cho Excel, MailKit cho SMTP. Chỉ một API project; namespaces cũ `RoomRental.Api.Endpoints`/`Services` vẫn được dùng trong nhiều thư mục Features.
- Middleware: trusted forwarded headers → latency → exception/security headers → SEO/static → routing/limiter → authentication → request security → authorization. Thứ tự ảnh hưởng đo lỗi, HTTPS, cookie và same-origin.
- Mỗi query nghiệp vụ phải scope owner và occupancy đúng; `PropertyLookup` giữ kiểm tra owner/archive, `PaymentReservations.Pending` nhận diện cả thành viên batch và attempt đơn cũ.
- Danh sách hóa đơn gộp `total`/ba tổng tiền/`draftCount` và Dashboard gộp 5 chỉ tiêu tiền trong một aggregate SQL trên toàn bộ query đã lọc; tập rỗng trả 0, tiền chỉ cộng Issued. Không đổi cách tính dư nợ `Total - Paid` hoặc response mảng cũ khi thiếu `page`. Regression và đếm SQL nằm trong `AccountingBillingHttpTests.InvoiceListSummaryAsync`.
- Ngày/kỳ dùng `VietnamCalendar`/`ApiSupport.VietnamDate`, không dùng ngày UTC của máy để chốt tháng Việt Nam. Fingerprint qua `RequestFingerprint`; đổi serializer/chuẩn hóa có thể phá replay đã lưu.
- Model change: tạo migration cho PostgresAppDbContext, commit Up/Down/designer/snapshot. Không chỉnh migration đã phát hành hoặc xóa lịch sử migration để “dọn code”.
- Startup Development có thể migrate; Production chỉ kiểm tra pending migrations. Runner schema hiện tại là **API image với `Database__MigrateOnly=true`**, dùng `deploy/migrate.yaml` và role riêng trước khi thay app.

## 5. Những bất biến không được làm mất

### Auth và dữ liệu riêng tư

- Cookie HttpOnly/SameSite=Lax, mặc định 12 giờ (hoặc 14 ngày khi chọn "Ghi nhớ đăng nhập" `RememberMe = true`), tự động gia hạn trượt (sliding) cả `AuthSessions.ExpiresAt` và cookie khi thời hạn còn lại ≤ một nửa; Production Secure. Session `sid` phải còn hiệu lực trong DB ở mỗi request. Rejected/Suspended bị chặn; Owner Pending chỉ đi auth/account flow.
- Request ghi của phiên đã đăng nhập phải qua same-origin guard; webhook payOS có ngoại lệ riêng và xác minh chữ ký. Không bỏ guard để chữa test thiếu Origin.
- Đổi mật khẩu giữ phiên hiện tại và thu hồi thiết bị khác; reset hoàn tất thu hồi tất cả. OTP không phải MFA login.
- Đăng ký OTP: code 6 số/10 phút, challenge 30 phút, tối đa 5 lần sai, resend 60 giây, 5 email/giờ. Xác minh tạo user và consume bằng chứng nguyên tử. Đừng tạo user login được trước xác minh.
- Recovery: forgot → verify OTP → grant reset dùng một lần/10 phút → complete; phản hồi email tồn tại/không tồn tại cùng dạng. Worker nhận mail trước SMTP; lỗi/dừng lúc gửi không tự retry, người dùng xin mã mới. SMTP accepted không chứng minh đã vào inbox.
- DataProtection keys giải mã tenant tokens, payOS credentials và recovery code đang chờ. Mất keys có thể làm mất khả năng dùng dữ liệu dù DB vẫn còn; backup DB và key ring cùng recovery point.
- Tenant token là bearer secret theo **lượt thuê**, hash để lookup, bản mã để chủ trọ chia sẻ lại. Rotate/thu hồi/trả phòng vô hiệu token cũ; không đổi sang token theo RoomId.
- Cấp/hiện link mới yêu cầu Automatic có webhook Verified hoặc Manual có bank details hợp lệ; xem `TenantTokenService.CanShareAsync`. Bypass chỉ dùng fixture Development, không Production.
- Chế độ nhận tiền mặc định của chủ trọ mới là `Manual` (VietQR thủ công). Lưu cấu hình payOS (`PUT /api/settings/payment`) tự chuyển `PaymentConfirmationMode` sang `Automatic`.
- payOS cấu hình riêng từng owner: Client ID/API Key/Checksum Key lưu mã hóa, không trả secret về UI. Lưu và xác minh gọi `confirm-webhook` với origin HTTPS từ config server; đổi origin/khóa làm verification cũ hết hiệu lực. Manual lưu ngân hàng/tài khoản riêng, không xóa khóa payOS; pending attempt chặn đổi chế độ/thông tin nhận tiền.
- Dashboard chủ trọ (`DashboardPage.tsx`, `OnboardingChecklist.tsx`) hiển thị checklist 5 bước bắt đầu nhanh kèm **Spotlight Guided Tour 5 bước** làm tối màn hình và chiếu sáng lần lượt từng nút thao tác (Tạo dãy → Tạo phòng → Cài đặt VietQR → Thêm người thuê → Chốt điện/nước & phát hành hóa đơn hàng tháng), tự bật lần đầu khi chủ trọ mới chưa hoàn tất bước nào (`roomie:onboarding-tour-seen:*`) hoặc mở lại bất kỳ lúc nào qua nút `Xem hướng dẫn từng bước`.

### Hóa đơn, tiền và trả phòng

- Snapshot đơn giá, số người, meter, tên dãy/phòng/khách thuộc lịch sử hóa đơn. Sửa phòng/lượt thuê không được tính lại Issued invoice cũ bằng giá hiện tại.
- Chỉ số điện và nước (khi phòng bật `WaterByMeter = true`) không lùi; kỳ/occupancy và trạng thái Draft/Issued phải được kiểm tra. Một Monthly/occupancy/period và một Deposit/occupancy theo unique indexes. Các ô nhập chỉ số điện và đồng hồ nước (`m³`) trên UI chỉ nhận số nguyên (`precision={0} step={1}`); chủ trọ có thể sửa `InitialReading`/`InitialWaterReading` trong `PATCH /api/rooms/{id}/occupancy` khi chưa có hóa đơn tháng Issued (hóa đơn Draft đầu kỳ chưa thu tiền tự tính lại).
- Revision/version/request fingerprint/idempotency là hợp đồng chống stale write/replay, không phải trường dư. Giữ transaction bao toàn bộ ghi tiền và cập nhật trạng thái.
- payOS chỉ ghi tiền từ bằng chứng server đã xác minh chữ ký, order, owner và số tiền chính xác. Return URL, QR hay provider status ở browser không đủ để đánh dấu Paid.
- Một batch giữ allocation snapshot và reservation trên **mọi hóa đơn thành viên**. Webhook/reconcile phân bổ receipts trong cùng transaction, replay không cộng tiền lần nữa.
- Manual receipt bằng toàn bộ dư nợ. Với pending payOS đơn: phải xác minh hủy an toàn; nếu provider đã Paid thì reconcile và không ghi tiền tay thêm. Pending batch chặn ghi riêng/sửa/cọc/trả phòng liên quan.
- Manual VietQR chỉ tạo yêu cầu; chỉ owner xác nhận tiền thực nhận. Confirm kiểm toàn bộ allocation/balance và commit nguyên tử; stale request không được ghi một phần. Confirm replay không sinh receipts mới.
- Panel yêu cầu VietQR chỉ liệt kê `ConfirmedAt == null && DismissedAt == null`; đánh giá lại dư nợ, occupancy, Issued lifecycle và reservation của toàn bộ allocation trên mỗi lần đọc/xác nhận. Yêu cầu cũ đã thu đủ/thay đổi/không còn khả dụng chỉ cho đối chiếu rồi bỏ khỏi danh sách qua `POST /api/manual-payments/{id}/dismiss`, không ghi receipt hay đánh dấu confirmed. Hạn 24 giờ là tái sử dụng QR/request, không phải hạn xác nhận tiền thực nhận hoặc bảo đảm ngân hàng ngừng nhận tiền.
- Ảnh VietQR ở `img.vietqr.io` là dịch vụ ảnh bên ngoài, không dùng để xác minh tiền; không gửi tenant token trong URL ảnh, giữ `referrerPolicy=no-referrer` và CSP allowlist cụ thể.
- Cọc và phần giữ/hoàn/khấu trừ là ledger riêng; Refund chỉ ghi nhận giao dịch bên ngoài, không tự chuyển tiền. Không tính cọc/credit cọc thành doanh thu thuê trọ.
- Trả phòng yêu cầu xử lý nợ Issued, Draft và pending payment; lưu completion chống replay, thu hồi token, ngày kết thúc Việt Nam, nâng meter baseline. Không tự tạo hóa đơn cuối hoặc chuyển khoản hoàn cọc.
- Báo cáo Excel giữ ô tiền kiểu số và chống formula injection từ tên/note người dùng.

### Sổ chi phí, lãi/lỗ và chứng từ

- Free được ghi/sửa/xóa khoản chi thủ công. Pro mới có: báo cáo lãi/lỗ, đối chiếu điện EVN (kể cả ghi trường `UtilityPeriod/UtilityKwh`), Excel của hai báo cáo này, chi định kỳ và tải chứng từ. Hết Pro: dữ liệu cũ vẫn xem/tải được, mẫu định kỳ ngừng sinh (`skipReason=ProRequired`), không tải chứng từ mới. Kiểm Pro ở server bằng `ExpenseSupport.IsProAsync` (cùng điều kiện `AppUser.IsPro`), không tin UI.
- "Thực thu" của lãi/lỗ dùng **đúng** điều kiện `invoiceCash` của tab Thu tiền: receipt `Source != "Deposit"` của hóa đơn Issued `Kind != "Deposit"`, theo tháng Việt Nam của `Receipt.CreatedAt`. Không cộng tiền cọc/khấu trừ cọc vào thu; đổi định nghĩa phải đổi cả hai báo cáo.
- Lọc theo dãy loại khoản chi chung (`BuildingId == null`) và trả `sharedExpensesExcluded`; không tự phân bổ chi chung. Khoảng báo cáo tối đa 24 tháng.
- Khoản chi: tiền VND nguyên 1..10 tỷ, `ExpenseDate` không ở tương lai theo ngày Việt Nam, xóa mềm (`DeletedAt`) và xóa luôn chứng từ để giải phóng quota. Tạo có `Idempotency-Key` + fingerprint (unique `(OwnerId, IdempotencyKey)`), sửa/xóa bắt buộc `expectedVersion`. Liên kết mới vào dãy/phòng lưu trữ bị chặn; liên kết cũ không đổi vẫn sửa được.
- Chi định kỳ: `RecurringExpenseWorker` (mỗi giờ, tắt bằng `EnableRecurringExpenses=false`) và lúc tạo/sửa/tiếp tục mẫu gọi `RecurringExpenseGenerator`; chỉ backfill tối đa 2 tháng, ngày 1–28. Chống trùng bằng unique `(RecurringExpenseId, RecurrencePeriod)` gồm cả dòng đã xóa mềm + `INSERT … ON CONFLICT DO NOTHING`: xóa khoản đã sinh thì không sinh lại. Sửa mẫu không sửa các tháng đã ghi.
- Chứng từ lưu `bytea` trong PostgreSQL để backup `pg_dump` hiện có bao gồm luôn; không chuyển sang file system/volume nếu chưa sửa backup/restore. Chỉ nhận JPEG/PNG/WebP/PDF theo magic bytes, ≤2 MB/tệp, ≤5 tệp/khoản, ≤200 MB/owner; tải về có `nosniff`, `private, no-store`, CSP sandbox, PDF luôn `attachment`. List không bao giờ select cột `Content`.
- Đối chiếu điện: kWh/tiền EVN lấy từ khoản chi `Electricity` có `UtilityPeriod`; kWh/tiền đã lập lấy từ snapshot `Invoice.Consumption/ElectricityAmount` của hóa đơn Issued không phải cọc. Thiếu hóa đơn EVN → `lossPercent = null` + `lossNullReason`, không hiển thị 0%; cảnh báo số hóa đơn nháp.

### Zalo & WhatsApp

- Invoice mới/phát hành Draft stage reminder **cùng transaction** với hóa đơn; không gọi Zalo trong request lưu hóa đơn và không sweep lịch sử để tự nhắc hóa đơn cũ khi nâng cấp.
- Một slot DB/InvoiceId, lease dispatch theo owner, attempt id và ledger gateway chống gửi lặp qua restart. Có thể queue khi mất kết nối, nhưng dispatch phải kiểm lại tenant/payment/phone/owner/invoice.
- Giờ gửi 07:00 đến trước 21:00 Việt Nam; cách ít nhất 5 giây/owner; cooldown 20 giờ kể cả kết quả Unknown. Không tự retry Unknown vì tin có thể đã gửi.
- Gateway session + ledger AES-256-GCM, ghi nguyên tử/fsync. Một process gateway/một volume; không chia volume cho nhiều process. Khôi phục backup cũ có thể mất receipt mới: đối chiếu Zalo trước khi bật gửi lại.
- Gateway Zalo (`zalo-gateway/` qua `zca-js`) và Gateway WhatsApp (`whatsapp-gateway/` qua `@whiskeysockets/baileys`) đều dùng kết nối QR cá nhân chạy song song độc lập (`/settings/zalo` và `/settings/whatsapp`), không phải ZNS/OA hay WhatsApp Cloud API trả phí; không coi test fake là nghiệm thu gửi thật.
- Phân luồng kênh theo mã quốc gia (`shared/ui/PhoneInput.tsx`): số di động Việt Nam (`+84` / `0[35789]\d{8}`) **chỉ** gửi qua Zalo (`ZaloReminderService.NormalizePhone`); số quốc tế (E.164 `^\+[1-9]\d{6,14}# Roomie — hướng dẫn tiếp quản

Đọc file này trước khi sửa dự án. Đây là tài liệu bàn giao kỹ thuật chính; README chỉ hướng dẫn vào dự án, runbook trong `docs/` chỉ dành cho thao tác vận hành. Khi code đổi, sửa mục tương ứng ở đây thay vì tạo thêm báo cáo bàn giao theo ngày.

Baseline rà soát: `e4fe403`, ngày 25/10/2026. Mô tả dưới đây dựa trên repository, không phải kết luận về phiên bản hay dữ liệu đang chạy trên VPS. Kiểm tra `git status`, `git log` và CI trước khi tiếp tục; không ghi đè thay đổi đang có của người dùng.

## 1. Sản phẩm và các quyết định đang áp dụng

Roomie (`https://roomie.io.vn`) quản lý nhà trọ cho nhiều chủ trọ: dãy/phòng, lượt thuê, chỉ số điện, hóa đơn, cọc, trả phòng, báo cáo Excel, thanh toán payOS hoặc xác nhận thủ công, nhắc hóa đơn qua Zalo. Admin quản lý chủ trọ, nhân sự Marketer và dashboard growth/revenue/operations. Marketer quản lý phễu tăng trưởng/doanh thu/chi phí chiến dịch (`/admin/monitoring`), mã khuyến mãi & link Auto-Promo (`/admin/promotions`), CRM & phân khúc chủ trọ (`/marketer/leads`), CMS Cẩm nang SEO & Banner (`/marketer/content`, `/huong-dan/:slug`) và chiến dịch Email theo phân khúc (`/marketer/emails`), nhưng bị chặn (`403 Forbidden`) khỏi toàn bộ dữ liệu vận hành hạ tầng (`Operations`, `Incidents`, `Maintenance`), duyệt/khóa chủ trọ và dữ liệu nhạy cảm của khách thuê/ngân hàng. Camera/OCR/AI chưa triển khai.

- Đăng ký email OTP thành công tạo **Owner Approved**, gói Free (hoặc Pro có thời hạn nếu cấu hình `Promotions:NewUserProMonths > 0` hoặc đăng ký qua link Auto-Promo `/register?promo=CODE`). Đừng dựa vào README cũ nói mọi tài khoản mới phải chờ duyệt. Admin vẫn có thể đổi trạng thái Pending/Approved/Rejected/Suspended.
- Sổ chi phí (`/expenses`) mở cho cả Free; lãi/lỗ, đối chiếu điện EVN, Excel hai báo cáo đó, chi định kỳ và chứng từ là Pro. Chi tiết bất biến ở mục 5 "Sổ chi phí, lãi/lỗ và chứng từ".
- Free tối đa 5 phòng đang sử dụng; Pro có thời hạn hoặc vô thời hạn. Kiểm tra `AppUser.IsPro/MaxRooms` và endpoint subscription trước khi sửa quota.
- Đăng nhập chỉ dùng mật khẩu. Các cột MFA cũ vẫn tồn tại để tương thích schema, không phải tính năng cần bật lại.
- Mật khẩu mới 9–256 ký tự, không toàn khoảng trắng, không ép ký tự đặc biệt.
- Tiền thuê tính **đủ tháng**, không chia theo ngày vào/ra. Ngày và kỳ nghiệp vụ theo Việt Nam (UTC+7).
- Khoản thu thủ công mới phải bằng **toàn bộ dư nợ**; dữ liệu thanh toán một phần cũ vẫn hợp lệ. Không phục hồi UI nhập khoản thu một phần từ test/doc lịch sử.
- Tin Zalo dẫn về **`/t/{token}`**, để khách xem hóa đơn rồi tự chọn thanh toán. Không gửi thẳng checkout, số tài khoản hay ảnh QR trong tin.
- `frontend/public/robots.txt` cố ý chặn mọi bot (`User-agent: *`, `Disallow: /`) theo yêu cầu người dùng. Không tự mở crawl, gửi sitemap hay yêu cầu index.
- Cẩm nang hướng dẫn (`/huong-dan` và `/huong-dan/:slug`) kết hợp danh mục hướng dẫn tĩnh với bài viết CMS xuất bản từ `MarketingArticle` (`/api/public/guides`). Đo bằng Search Console và telemetry nội bộ, không tự thêm GA4 hay hứa traffic/thứ hạng.
- Logo đang dùng trong app: `frontend/src/shared/ui/RoomieMark.tsx`, `frontend/public/roomie-logo.svg`, `favicon.svg`; ảnh social `roomie-social.png/.svg`; email `backend/RoomRental.Api/Assets/roomie-email-logo.png`. Bản logo/cover Facebook mới nằm trong `output/facebook/`, chưa tự thay vào app.

## 2. Bản đồ repository

| Đường dẫn | Vai trò / điểm bắt đầu |
| --- | --- |
| `backend/RoomRental.Api/Program.cs` | DI, provider DB, workers, cookie, limiter, middleware, đăng ký routes, SPA fallback |
| `backend/RoomRental.Api/Domain/` | Entities và DTO; monitoring, Zalo, manual payments, recovery, marketer CRM/content/emails có file riêng |
| `backend/RoomRental.Api/Data/AppDbContext.cs` | Toàn bộ mapping, indexes, concurrency tokens, DbSets |
| `backend/RoomRental.Api/Data/ProviderDbContexts.cs` | Context/factory PostgreSQL |
| `backend/RoomRental.Api/Data/Migrations/Postgres/` | Migration, designer và snapshot cho PostgreSQL |
| `backend/RoomRental.Api/Features/` | Endpoint/service chia theo Auth, Admin, Marketer, Properties, Billing, Payments, Reports, Zalo, Monitoring |
| `backend/RoomRental.Api/Shared/` | Security, SEO, calendar, lookup owner/property, pagination, fingerprint, cleanup |
| `compose.postgres-dev.yaml` | Compose PostgreSQL dev local (container `rehouse-pg-migration-db`, port 15432) |
| `frontend/src/app/` | Entry React, routing, layout, CSS assembly, metadata và prerender entry |
| `frontend/src/features/` | API, types, page, hook và CSS theo nghiệp vụ |
| `frontend/src/shared/` | HTTP, i18n, analytics, format, theme, UI dùng chung |
| `frontend/scripts/` | Build/check i18n, architecture, prerender và các test Node |
| `frontend/e2e/` | Playwright với API giả lập; không phụ thuộc API vận hành |
| `zalo-gateway/` | Node 24 + zca-js, đăng nhập Zalo QR, lưu phiên và ledger mã hóa |
| `whatsapp-gateway/` | Node 24 + `@whiskeysockets/baileys`, đăng nhập WhatsApp QR (Linked Devices), lưu phiên và ledger mã hóa |
| `tests/` | Console C# suites, HTTP Python harness, SMTP sink, tests backup/host collector |
| `.github/workflows/ci-cd.yml` | Các cổng CI, image đã kiểm thử, GHCR và deploy theo digest |
| `deploy/` | Production Compose, migration overlay, backup/restore, role DB, Caddy, firewall, host collector |
| `scripts/` | Tiện ích webhook tunnel |
| `rehouse-video/` | Dự án Remotion độc lập, mock quảng cáo dùng tên ReHouse cũ; không nằm trong runtime/build app |
| `.agents/skills/ui-ux-pro-max/` | Skill UI/UX Pro Max từ nextlevelbuilder, thay skill ui-ux cũ; dữ liệu và công cụ tìm kiếm chạy local, tuân thủ stack/quy ước dự án |
| `output/`, `artifacts/`, `out/` | Deliverables, bằng chứng QA hoặc bản sao công việc; không tự coi mọi file trong đây là rác |

Tên assembly/namespace, cookie `phongtro.session`, image `ghcr.io/nhaq0505/rehouse`, path host `/opt/rehouse` vẫn giữ tên cũ. Đổi tên thương hiệu không có nghĩa được đổi những định danh này đồng loạt.

## 3. Đọc code theo việc cần làm

| Việc | Backend | Frontend / test chính |
| --- | --- | --- |
| Đăng ký, login, đổi/reset mật khẩu, hồ sơ chủ trọ (`/profile`) | `Features/Auth/`, `Shared/RequestSecurityMiddleware.cs` | `features/auth/` (`OwnerProfilePage.tsx`), `AuthSecurityHttpTests`, `RegistrationVerificationTests`, `PasswordRecoveryTests` |
| Dãy, phòng, lượt thuê, cấp link | `Features/Properties/OwnerManagementEndpoints.*`, `TenantTokenService.cs` | `features/buildings`, `features/rooms`, `RoomTenantPage.tsx`, `RoomTenantLifecycleHttpTests`, `TenantLinkHttpTests` |
| Hợp đồng thuê trọ, CT01, quét QR CCCD / Passport MRZ & ảnh giấy tờ | `Features/Properties/OwnerManagementEndpoints.Documents.cs` (`/api/rooms/{id}/documents/identity-photo`), `ContractWordGenerator.cs`, `Domain/ContractDocuments.cs`, `Domain/TenantPortalSubmissions.cs`, `Shared/VietnameseMoneyReader.cs` | `RoomDocumentsPage.tsx`, `ContractDocumentsDrawer.tsx`, `shared/ui/CccdQrScannerModal.tsx`, `shared/ui/cccdQr.ts`, `shared/i18n/legalTemplates.ts`, `ContractDocumentsAndMoneyTests.cs`, `test-cccd-qr.mjs`, `RoomTenantLifecycleHttpTests`, `landlord.spec.ts` |
| Chốt điện, tạo/phát hành/sửa hóa đơn | `Features/Billing/BillingEndpoints.*`, `InvoiceService.cs` | `features/readings` (`ReadingsPage.tsx`), `features/invoices`, `AccountingBillingHttpTests`, `RoomTenantLifecycleHttpTests`, `landlord.spec.ts` |
| Ghi thu, cọc, trả phòng | `ManualReceiptService.cs`, `DepositEndpoints.cs`, `Properties/MoveoutEndpoints.cs` | `DepositLedger.tsx`, `MoveoutDrawer.tsx`, `ManualReceiptTests` |
| payOS, checkout gộp, webhook | `Features/Payments/PayOsService.*`, `PaymentEndpoints.cs`, `PaymentReservations.cs` | `features/tenant`, `PayOsPaymentTests`, `BatchPaymentTests`, `PayOsOperationsHttpTests` |
| VietQR, xác nhận chuyển khoản | `ManualPaymentEndpoints.cs`, `PaymentPreferencesEndpoints.cs`, `Domain/ManualPayments.cs` | `ManualPaymentRequests.tsx`, `PaymentModeSettings.tsx`, `ManualPaymentPanel.tsx`, `test_payment_modes.py` |
| Nâng cấp Pro & Khuyến mãi | `BillingEndpoints.Subscription.cs`, `PayOsService.Subscriptions.cs`, `Features/Admin/AdminPromotionsEndpoints.cs`, `Domain/Promotions.cs`, `Features/Auth/RegistrationVerificationService.cs` | `features/subscription`, `features/admin/AdminPromotionsPage.tsx`, `PromotionsTests.cs`, `test_billing_upgrade.py`, `admin.spec.ts`, `landlord.spec.ts` |
| Báo cáo/xuất Excel | `Features/Reports/ReportsEndpoints.*` | `features/reports`, `test_reports_archive.py` |
| Sổ chi phí, chi định kỳ, chứng từ, lãi/lỗ, đối chiếu điện EVN | `Features/Expenses/` (`ExpenseEndpoints*.cs`, `RecurringExpenseEndpoints.cs`, `RecurringExpenseGenerator.cs`, `ExpenseSupport.cs`), `Domain/Expenses.cs`, `Features/Reports/ReportsEndpoints.ProfitLoss.cs` | `features/expenses`, `features/reports/ProfitLossPanel.tsx`, `ElectricityReconciliationPanel.tsx`, `shared/ui/ProUpsell.tsx`, `ExpenseLedgerHttpTests.cs`, `e2e/expenses.spec.ts` |
| Nhắc Zalo & WhatsApp | `Features/Zalo/`, `Features/WhatsApp/`, `Domain/Zalo.cs`, `Domain/WhatsApp.cs` | `features/zalo`, `features/whatsapp`, `RoomRental.ZaloTests`, `test_zalo_http.py`, `zalo-gateway/test`, `whatsapp-gateway/test` |
| Theo dõi growth/revenue & ROAS | `Features/Monitoring/MonitoringServices.cs`, interfaces/entities/DTO cùng nhóm | `features/admin/monitoring`, `shared/analytics`, `MonitoringBusinessTests`, `MarketerSuiteHttpTests` |
| Marketer CRM, CMS SEO/Banner & Email | `Features/Marketer/`, `Features/Admin/AdminPromotionsEndpoints.cs`, `Domain/Marketer*.cs` | `features/marketer`, `features/marketing/GuideArticlePage.tsx`, `shared/ui/ActiveBannerStrip.tsx`, `MarketerSuiteHttpTests`, `admin.spec.ts` |
| Host/API/worker monitoring | `Features/Monitoring/Operations/`, `deploy/monitoring/` | `OperationsTab.tsx`, `OperationsMonitoringTests`, `test_host_collector.py` |
| Landing/SEO | `Shared/SeoMiddleware.cs` | `features/marketing`, `app/seo`, `scripts/prerender.mjs`, `SeoAndRoutingUnitTests.cs`, `test_seo_http.py`, `smoke.spec.ts` |

API thực tế được khai báo bằng `MapGet/MapPost/...` trong các endpoint trên; DTO ở `Domain/ApiDtos.cs`, `MonitoringDtos.cs`, `ReportDtos.cs`, `ManualPayments.cs`, `PasswordRecovery.cs`. Đối chiếu handler và DTO trước khi sửa consumer; không duy trì một bản liệt kê API thủ công tách rời code.

## 4. Kiến trúc và quy ước sửa code

### Frontend

- React 19, TypeScript 5.8, Vite 6, React Router 7, TanStack Query 5, Ant Design 5; CSS thuần. Không tự đưa Tailwind/bộ UI khác vào chỉ để sửa một màn.
- `app → features → shared`. Shared không import app/features. Feature không import app hoặc `src/api.ts`; giữa các feature dùng public `index.ts`, ngoại lệ phiên auth là `auth/session.ts`. `check:architecture` kiểm imports và vòng phụ thuộc runtime.
- `frontend/src/api.ts` là facade tương thích cho consumer/test cũ, không phải nơi thêm logic nghiệp vụ. Không xóa vì thấy app không import trực tiếp.
- Trang phức tạp dùng hook `use*Page` và component nhận dữ liệu/handler; giữ vòng đời idempotency key, query key và invalidation khi refactor.
- HTTP qua `shared/http/client.ts`: same-origin cookie, Accept-Language, timeout, unwrap envelope, `ApiError`; mutation không retry tự động. Excel dùng `requestBlob`.
- `app/styles.css` ghép CSS theo thứ tự có chủ đích. Giữ cascade khi di chuyển rule. Nền/brand nằm ở `shared/styles/foundation.css`, Ant token ở `shared/design/antdTheme.ts`; font toàn hệ thống Be Vietnam Pro (nội dung, tiêu đề, số liệu và Ant Design); giữ monospace cho mã/nội dung chuyển khoản.
- Tiếng Việt là nguồn; `shared/i18n/source.json` và `translations.tsv` cùng các catalog en/zh/ko/hi. Dùng `t()`; sau đổi copy chạy `i18n:build`, `i18n:check`; đọc script trước khi sửa trực tiếp catalog sinh ra.
- Routes private phải đồng bộ `app/App.tsx`, `app/seo/site.ts`, `Shared/SeoMiddleware.cs`. Router React không đủ để hard-refresh chạy trên server.
- Prerender sinh HTML tĩnh cho bộ 4 trang công khai (`/` → `dist/index.html`, `/features` → `dist/features/index.html`, `/pricing` → `dist/pricing/index.html`, `/huong-dan` → `dist/huong-dan/index.html`), `app.html` cho SPA và `sitemap.xml`. Không xuất renderer build-only vào production. Trang lạ/API lạ phải trả 404 thật.
- Bộ Landing page (`LandingPage.tsx`, `LandingVisuals.tsx`, `LandingSections.tsx`, `GuideArticlePage.tsx`, `LandingPage.css`) chia thành 4 trang trên thanh điều hướng: Trang chủ (`/`) ngắn gọn + thẻ khám phá, Tính năng & So sánh (`/features`), Bảng giá Free/Pro & FAQ `<details>/<summary>` (`/pricing`), và Cẩm nang hướng dẫn (`/huong-dan`, cùng trang chi tiết `/huong-dan/:slug`), dùng chung `LandingHeader` (gồm cả `.landing-subnav` mobile), `LandingCtaSection` và `LandingFooter`. Landing motion dùng IntersectionObserver/Web Animations; giữ nội dung nhìn được khi thiếu JS, Reduce Motion hoặc thiếu API; focus bàn phím phải hủy reveal đang che link.
- Dashboard monitoring có Growth/Revenue/Operations, filter ngày Việt Nam, campaign costs và UTM link builder. `null`/Unknown phải hiển thị chưa có dữ liệu cùng lý do, không biến thành 0 hoặc 100%.

### Backend

- ASP.NET Core .NET 10 Minimal APIs, EF Core 10 + Npgsql, ClosedXML cho Excel, MailKit cho SMTP. Chỉ một API project; namespaces cũ `RoomRental.Api.Endpoints`/`Services` vẫn được dùng trong nhiều thư mục Features.
- Middleware: trusted forwarded headers → latency → exception/security headers → SEO/static → routing/limiter → authentication → request security → authorization. Thứ tự ảnh hưởng đo lỗi, HTTPS, cookie và same-origin.
- Mỗi query nghiệp vụ phải scope owner và occupancy đúng; `PropertyLookup` giữ kiểm tra owner/archive, `PaymentReservations.Pending` nhận diện cả thành viên batch và attempt đơn cũ.
- Danh sách hóa đơn gộp `total`/ba tổng tiền/`draftCount` và Dashboard gộp 5 chỉ tiêu tiền trong một aggregate SQL trên toàn bộ query đã lọc; tập rỗng trả 0, tiền chỉ cộng Issued. Không đổi cách tính dư nợ `Total - Paid` hoặc response mảng cũ khi thiếu `page`. Regression và đếm SQL nằm trong `AccountingBillingHttpTests.InvoiceListSummaryAsync`.
- Ngày/kỳ dùng `VietnamCalendar`/`ApiSupport.VietnamDate`, không dùng ngày UTC của máy để chốt tháng Việt Nam. Fingerprint qua `RequestFingerprint`; đổi serializer/chuẩn hóa có thể phá replay đã lưu.
- Model change: tạo migration cho PostgresAppDbContext, commit Up/Down/designer/snapshot. Không chỉnh migration đã phát hành hoặc xóa lịch sử migration để “dọn code”.
- Startup Development có thể migrate; Production chỉ kiểm tra pending migrations. Runner schema hiện tại là **API image với `Database__MigrateOnly=true`**, dùng `deploy/migrate.yaml` và role riêng trước khi thay app.

## 5. Những bất biến không được làm mất

### Auth và dữ liệu riêng tư

- Cookie HttpOnly/SameSite=Lax, mặc định 12 giờ (hoặc 14 ngày khi chọn "Ghi nhớ đăng nhập" `RememberMe = true`), tự động gia hạn trượt (sliding) cả `AuthSessions.ExpiresAt` và cookie khi thời hạn còn lại ≤ một nửa; Production Secure. Session `sid` phải còn hiệu lực trong DB ở mỗi request. Rejected/Suspended bị chặn; Owner Pending chỉ đi auth/account flow.
- Request ghi của phiên đã đăng nhập phải qua same-origin guard; webhook payOS có ngoại lệ riêng và xác minh chữ ký. Không bỏ guard để chữa test thiếu Origin.
- Đổi mật khẩu giữ phiên hiện tại và thu hồi thiết bị khác; reset hoàn tất thu hồi tất cả. OTP không phải MFA login.
- Đăng ký OTP: code 6 số/10 phút, challenge 30 phút, tối đa 5 lần sai, resend 60 giây, 5 email/giờ. Xác minh tạo user và consume bằng chứng nguyên tử. Đừng tạo user login được trước xác minh.
- Recovery: forgot → verify OTP → grant reset dùng một lần/10 phút → complete; phản hồi email tồn tại/không tồn tại cùng dạng. Worker nhận mail trước SMTP; lỗi/dừng lúc gửi không tự retry, người dùng xin mã mới. SMTP accepted không chứng minh đã vào inbox.
- DataProtection keys giải mã tenant tokens, payOS credentials và recovery code đang chờ. Mất keys có thể làm mất khả năng dùng dữ liệu dù DB vẫn còn; backup DB và key ring cùng recovery point.
- Tenant token là bearer secret theo **lượt thuê**, hash để lookup, bản mã để chủ trọ chia sẻ lại. Rotate/thu hồi/trả phòng vô hiệu token cũ; không đổi sang token theo RoomId.
- Cấp/hiện link mới yêu cầu Automatic có webhook Verified hoặc Manual có bank details hợp lệ; xem `TenantTokenService.CanShareAsync`. Bypass chỉ dùng fixture Development, không Production.
- Chế độ nhận tiền mặc định của chủ trọ mới là `Manual` (VietQR thủ công). Lưu cấu hình payOS (`PUT /api/settings/payment`) tự chuyển `PaymentConfirmationMode` sang `Automatic`.
- payOS cấu hình riêng từng owner: Client ID/API Key/Checksum Key lưu mã hóa, không trả secret về UI. Lưu và xác minh gọi `confirm-webhook` với origin HTTPS từ config server; đổi origin/khóa làm verification cũ hết hiệu lực. Manual lưu ngân hàng/tài khoản riêng, không xóa khóa payOS; pending attempt chặn đổi chế độ/thông tin nhận tiền.
- Dashboard chủ trọ (`DashboardPage.tsx`, `OnboardingChecklist.tsx`) hiển thị checklist 5 bước bắt đầu nhanh kèm **Spotlight Guided Tour 5 bước** làm tối màn hình và chiếu sáng lần lượt từng nút thao tác (Tạo dãy → Tạo phòng → Cài đặt VietQR → Thêm người thuê → Chốt điện/nước & phát hành hóa đơn hàng tháng), tự bật lần đầu khi chủ trọ mới chưa hoàn tất bước nào (`roomie:onboarding-tour-seen:*`) hoặc mở lại bất kỳ lúc nào qua nút `Xem hướng dẫn từng bước`.

### Hóa đơn, tiền và trả phòng

- Snapshot đơn giá, số người, meter, tên dãy/phòng/khách thuộc lịch sử hóa đơn. Sửa phòng/lượt thuê không được tính lại Issued invoice cũ bằng giá hiện tại.
- Chỉ số điện và nước (khi phòng bật `WaterByMeter = true`) không lùi; kỳ/occupancy và trạng thái Draft/Issued phải được kiểm tra. Một Monthly/occupancy/period và một Deposit/occupancy theo unique indexes. Các ô nhập chỉ số điện và đồng hồ nước (`m³`) trên UI chỉ nhận số nguyên (`precision={0} step={1}`); chủ trọ có thể sửa `InitialReading`/`InitialWaterReading` trong `PATCH /api/rooms/{id}/occupancy` khi chưa có hóa đơn tháng Issued (hóa đơn Draft đầu kỳ chưa thu tiền tự tính lại).
- Revision/version/request fingerprint/idempotency là hợp đồng chống stale write/replay, không phải trường dư. Giữ transaction bao toàn bộ ghi tiền và cập nhật trạng thái.
- payOS chỉ ghi tiền từ bằng chứng server đã xác minh chữ ký, order, owner và số tiền chính xác. Return URL, QR hay provider status ở browser không đủ để đánh dấu Paid.
- Một batch giữ allocation snapshot và reservation trên **mọi hóa đơn thành viên**. Webhook/reconcile phân bổ receipts trong cùng transaction, replay không cộng tiền lần nữa.
- Manual receipt bằng toàn bộ dư nợ. Với pending payOS đơn: phải xác minh hủy an toàn; nếu provider đã Paid thì reconcile và không ghi tiền tay thêm. Pending batch chặn ghi riêng/sửa/cọc/trả phòng liên quan.
- Manual VietQR chỉ tạo yêu cầu; chỉ owner xác nhận tiền thực nhận. Confirm kiểm toàn bộ allocation/balance và commit nguyên tử; stale request không được ghi một phần. Confirm replay không sinh receipts mới.
- Panel yêu cầu VietQR chỉ liệt kê `ConfirmedAt == null && DismissedAt == null`; đánh giá lại dư nợ, occupancy, Issued lifecycle và reservation của toàn bộ allocation trên mỗi lần đọc/xác nhận. Yêu cầu cũ đã thu đủ/thay đổi/không còn khả dụng chỉ cho đối chiếu rồi bỏ khỏi danh sách qua `POST /api/manual-payments/{id}/dismiss`, không ghi receipt hay đánh dấu confirmed. Hạn 24 giờ là tái sử dụng QR/request, không phải hạn xác nhận tiền thực nhận hoặc bảo đảm ngân hàng ngừng nhận tiền.
- Ảnh VietQR ở `img.vietqr.io` là dịch vụ ảnh bên ngoài, không dùng để xác minh tiền; không gửi tenant token trong URL ảnh, giữ `referrerPolicy=no-referrer` và CSP allowlist cụ thể.
- Cọc và phần giữ/hoàn/khấu trừ là ledger riêng; Refund chỉ ghi nhận giao dịch bên ngoài, không tự chuyển tiền. Không tính cọc/credit cọc thành doanh thu thuê trọ.
- Trả phòng yêu cầu xử lý nợ Issued, Draft và pending payment; lưu completion chống replay, thu hồi token, ngày kết thúc Việt Nam, nâng meter baseline. Không tự tạo hóa đơn cuối hoặc chuyển khoản hoàn cọc.
- Báo cáo Excel giữ ô tiền kiểu số và chống formula injection từ tên/note người dùng.

### Sổ chi phí, lãi/lỗ và chứng từ

- Free được ghi/sửa/xóa khoản chi thủ công. Pro mới có: báo cáo lãi/lỗ, đối chiếu điện EVN (kể cả ghi trường `UtilityPeriod/UtilityKwh`), Excel của hai báo cáo này, chi định kỳ và tải chứng từ. Hết Pro: dữ liệu cũ vẫn xem/tải được, mẫu định kỳ ngừng sinh (`skipReason=ProRequired`), không tải chứng từ mới. Kiểm Pro ở server bằng `ExpenseSupport.IsProAsync` (cùng điều kiện `AppUser.IsPro`), không tin UI.
- "Thực thu" của lãi/lỗ dùng **đúng** điều kiện `invoiceCash` của tab Thu tiền: receipt `Source != "Deposit"` của hóa đơn Issued `Kind != "Deposit"`, theo tháng Việt Nam của `Receipt.CreatedAt`. Không cộng tiền cọc/khấu trừ cọc vào thu; đổi định nghĩa phải đổi cả hai báo cáo.
- Lọc theo dãy loại khoản chi chung (`BuildingId == null`) và trả `sharedExpensesExcluded`; không tự phân bổ chi chung. Khoảng báo cáo tối đa 24 tháng.
- Khoản chi: tiền VND nguyên 1..10 tỷ, `ExpenseDate` không ở tương lai theo ngày Việt Nam, xóa mềm (`DeletedAt`) và xóa luôn chứng từ để giải phóng quota. Tạo có `Idempotency-Key` + fingerprint (unique `(OwnerId, IdempotencyKey)`), sửa/xóa bắt buộc `expectedVersion`. Liên kết mới vào dãy/phòng lưu trữ bị chặn; liên kết cũ không đổi vẫn sửa được.
- Chi định kỳ: `RecurringExpenseWorker` (mỗi giờ, tắt bằng `EnableRecurringExpenses=false`) và lúc tạo/sửa/tiếp tục mẫu gọi `RecurringExpenseGenerator`; chỉ backfill tối đa 2 tháng, ngày 1–28. Chống trùng bằng unique `(RecurringExpenseId, RecurrencePeriod)` gồm cả dòng đã xóa mềm + `INSERT … ON CONFLICT DO NOTHING`: xóa khoản đã sinh thì không sinh lại. Sửa mẫu không sửa các tháng đã ghi.
- Chứng từ lưu `bytea` trong PostgreSQL để backup `pg_dump` hiện có bao gồm luôn; không chuyển sang file system/volume nếu chưa sửa backup/restore. Chỉ nhận JPEG/PNG/WebP/PDF theo magic bytes, ≤2 MB/tệp, ≤5 tệp/khoản, ≤200 MB/owner; tải về có `nosniff`, `private, no-store`, CSP sandbox, PDF luôn `attachment`. List không bao giờ select cột `Content`.
- Đối chiếu điện: kWh/tiền EVN lấy từ khoản chi `Electricity` có `UtilityPeriod`; kWh/tiền đã lập lấy từ snapshot `Invoice.Consumption/ElectricityAmount` của hóa đơn Issued không phải cọc. Thiếu hóa đơn EVN → `lossPercent = null` + `lossNullReason`, không hiển thị 0%; cảnh báo số hóa đơn nháp.

### Zalo & WhatsApp

- Invoice mới/phát hành Draft stage reminder **cùng transaction** với hóa đơn; không gọi Zalo trong request lưu hóa đơn và không sweep lịch sử để tự nhắc hóa đơn cũ khi nâng cấp.
- Một slot DB/InvoiceId, lease dispatch theo owner, attempt id và ledger gateway chống gửi lặp qua restart. Có thể queue khi mất kết nối, nhưng dispatch phải kiểm lại tenant/payment/phone/owner/invoice.
- Giờ gửi 07:00 đến trước 21:00 Việt Nam; cách ít nhất 5 giây/owner; cooldown 20 giờ kể cả kết quả Unknown. Không tự retry Unknown vì tin có thể đã gửi.
- Gateway session + ledger AES-256-GCM, ghi nguyên tử/fsync. Một process gateway/một volume; không chia volume cho nhiều process. Khôi phục backup cũ có thể mất receipt mới: đối chiếu Zalo trước khi bật gửi lại.
, khác `+84`) mặc định gửi qua WhatsApp (`WhatsAppReminderService.NormalizePhone`).

### Monitoring

- Browser events best effort, loại bỏ PII/token/query nhạy cảm, allowlist và giới hạn payload/rate. Client không có quyền tự khai milestones server.
- Activation là phát hành **Monthly** đầu tiên, không phải Deposit/Supplemental. Revenue sản phẩm chỉ là order **Subscription Paid** đã xác minh, không phải tiền thuê/cọc hoặc Pro do admin tặng.
- Actor Owner/System/Admin ảnh hưởng active-owner metrics. Lưu đúng actor/thời điểm publish/paid trong transaction + milestone outbox; telemetry lỗi không rollback sản phẩm.
- Chỉ acknowledge outbox khi milestone đã tồn tại bền vững trong DB. Giữ Pending/Failed để đối chiếu; chỉ prune Completed theo retention.
- Growth/CAC/retention có cohort, attribution, maturity và null reasons. Không thay dữ liệu thiếu bằng số ước đoán hoặc áp filter bằng cách materialize toàn bộ owner IDs trước query.
- Host collector chạy ngoài app, chỉ mount thư mục JSON public `:ro`; tuyệt đối không mount Docker socket/private collector state vào app. Backup-watchdog có authority ở host, không tạo alert trùng trong app.

## 6. Chạy local có kiểm soát

Yêu cầu .NET SDK 10, Node 24, npm, Python 3; Docker cần cho local PostgreSQL dev container (`compose.postgres-dev.yaml`), QA PostgreSQL và deployment. Từ root:

```sh
npm --prefix frontend ci
npm --prefix zalo-gateway ci
dotnet tool restore
```

Development còn nạp `appsettings.Development.local.json`, `appsettings.Email.local.json` đã ignore. Env/command line override chúng. Kiểm tra cấu hình riêng tại máy trước khi chạy; không in secrets. Chạy dev database PostgreSQL qua `compose.postgres-dev.yaml`:

```sh
# Chạy PostgreSQL dev container (nếu chưa chạy):
docker compose -f compose.postgres-dev.yaml up -d

# Terminal API:
ROOMIE_DEV_DIR=$(mktemp -d /tmp/roomie-dev.XXXXXX)
ASPNETCORE_ENVIRONMENT=Development DatabaseProvider=Postgres \
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=15432;Database=room_rental;Username=room_rental;Password=<password>" \
DataProtection__KeysPath="$ROOMIE_DEV_DIR/keys" SeedDemo=true \
Zalo__Enabled=false EnableInvoiceSweep=false EnablePaymentReconciliation=false \
EmailVerification__Enabled=false RequireVerifiedPaymentForTenantLink=false \
HTTP_PROXY=http://127.0.0.1:9 HTTPS_PROXY=http://127.0.0.1:9 ALL_PROXY=http://127.0.0.1:9 NO_PROXY=localhost,127.0.0.1 \
dotnet run --no-launch-profile --project backend/RoomRental.Api --urls http://localhost:5080

# Terminal frontend:
npm --prefix frontend run dev
```

Mở `http://localhost:5173` (Vite proxy `/api` → `localhost:5080`). Fixture demo: `admin@phongtro.local` / `Admin123!`, `marketer@phongtro.local` / `Marketer123!`, `owner@phongtro.local` / `Owner123!`. Đây chỉ là credentials seed dùng thử. Để thử OTP dùng harness SMTP bên dưới, không gỡ guard rồi gọi SMTP thật.

Cấu hình quan trọng: `DatabaseProvider`, `ConnectionStrings__DefaultConnection`, `DataProtection__KeysPath`, `PublicBaseUrl`, `WebhookBaseUrl`, `TrustedProxies`, `Email__*`, `Zalo__*`, `Monitoring__*`. Xem `.env.example` và runbooks; không thêm giá trị thật vào Git.

## 7. Kiểm tra trước bàn giao thay đổi

Chọn nhóm test theo phần sửa. Các test console C# là **executable**, dùng `dotnet run`, không thay bằng `dotnet test` rồi kết luận suite đã chạy.

```sh
dotnet build backend/RoomRental.Api
dotnet run --project tests/RoomRental.WebhookTests
dotnet run --project tests/RoomRental.ZaloTests
npm --prefix zalo-gateway test
npm --prefix whatsapp-gateway test
npm --prefix frontend run build
npm --prefix frontend run i18n:check
npm --prefix frontend run check:architecture
npm --prefix frontend run test:api
npm --prefix frontend run test:http
npm --prefix frontend run test:analytics
npm --prefix frontend run test:dates
npm --prefix frontend run test:cccd-qr
npm --prefix frontend run test:e2e -- --workers=4
dotnet ef migrations has-pending-model-changes --project backend/RoomRental.Api --context PostgresAppDbContext
git diff --check
```

Frontend build phải xong trước Playwright. Browser config tự chạy preview `127.0.0.1:4178`, API trong tests được giả lập. Không trỏ suite vào production hoặc dùng login thật để né fixture lỗi.

HTTP regression và các suite C# yêu cầu container PostgreSQL QA chuyên dụng (tách biệt hoàn toàn với dev container `rehouse-pg-migration-db` cổng 15432). Chạy container QA tạm thời và cấp biến môi trường dùng chung:

```sh
# 1. Khởi chạy container PostgreSQL QA riêng biệt trên cổng loopback (ví dụ 55432):
QA_CONTAINER="roomie-qa-pg-local-$(date +%s)"
QA_PORT=55432
docker run -d --name "$QA_CONTAINER" \
  -e POSTGRES_DB=roomie_qa_bootstrap \
  -e POSTGRES_USER=room_rental \
  -e POSTGRES_PASSWORD=qa_test_password \
  -p "127.0.0.1:$QA_PORT:5432" \
  postgres:17-alpine

until docker exec "$QA_CONTAINER" pg_isready -U room_rental -d roomie_qa_bootstrap; do sleep 1; done

# 2. Cấp biến môi trường dùng chung cho C# suites và Python harness:
export TEST_HTTP_POSTGRES_CONTAINER="$QA_CONTAINER"
export TEST_HTTP_POSTGRES_CONNECTION="Host=127.0.0.1;Port=$QA_PORT;Database=roomie_qa_bootstrap;Username=room_rental;Password=qa_test_password"

# 3. Chạy console C# suites (nếu kiểm tra C#):
dotnet run --project tests/RoomRental.WebhookTests
dotnet run --project tests/RoomRental.ZaloTests

# 4. Chạy HTTP regression với API publish có đầy đủ wwwroot:
npm --prefix frontend run build
ROOMIE_QA_APP=$(mktemp -d /tmp/roomie-qa-app.XXXXXX)
dotnet publish backend/RoomRental.Api -c Release -o "$ROOMIE_QA_APP"
cp -R frontend/dist "$ROOMIE_QA_APP/wwwroot"

python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider postgres
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider otp-postgres

# 5. Dọn dẹp container QA và thư mục publish:
docker rm -f "$QA_CONTAINER"
rm -rf "$ROOMIE_QA_APP"
```

Harness tạo database ngẫu nhiên `roomie_qa_*` trong container chỉ định, keys trong `/tmp/roomie-ci-*`, chặn outbound bằng proxy loopback, callback HTTP localhost; không dùng payOS/SMTP/Zalo thật. Cả hai provider kỳ vọng 0 skip. macOS: nếu Python Homebrew lỗi `pyexpat`, dùng `/usr/bin/python3` như lần QA này. Thiếu webroot gây SEO 404, không chữa bằng bỏ test.

Backup/collector thuần local:

```sh
python3 -m unittest discover -s tests -p 'test_backup_*.py' -v
python3 -m unittest discover -s tests -p 'test_host_collector.py' -v
```

Không chạy `unittest discover -s tests` tùy tiện: các module HTTP có thể trỏ tới API mặc định đang dùng. Test chống gọi provider phải giữ proxy + localhost callback; HTTP callback riêng không đủ để chặn subscription checkout.

## 8. Những giới hạn đã thấy trong code, cần kiểm tra lại khi làm tiếp

1. **Monitoring scheduler:** `MonitoringBackgroundWorker` gọi incident checks mỗi phút và maintenance mỗi giờ, hai vòng độc lập, scope riêng, không overlap trong process. Cấu hình `Monitoring:Schedule` được validate startup; xem monitoring runbook. DB outage gửi SMTP không phụ thuộc DB sau 3 probe cách ≥1 phút, cooldown 1 giờ; trạng thái outage trong RAM đến reconnect nên restart có thể gửi lại. Host collector/systemd gửi cảnh báo host/backup riêng. Chưa xác minh delivery/live VPS.
2. **Monitoring maintenance:** scheduler tính lại 7 ngày đã kết thúc theo Việt Nam trước khi prune raw 90 ngày/activity và aggregate 13 tháng/Completed outbox 30 ngày. Rollup lỗi thì không prune, thử lại chu kỳ sau. Ngày thiếu/chỉnh sửa cũ hơn lookback phải gọi admin rollup trước khi raw hết retention; chưa có backfill toàn lịch sử tự động. Scheduler chưa có distributed lease; không scale nhiều replica hoặc thêm external scheduler trùng mà chưa thiết kế khóa. `DataMaintenanceService` vẫn chỉ dọn auth records.
3. **Dự án chuyển hoàn toàn sang PostgreSQL:** Toàn bộ dữ liệu SQLite cũ và công cụ `RoomRental.DbMigrator` đã được dọn sạch; hệ thống dùng PostgreSQL duy nhất cho cả phát triển, kiểm thử và vận hành. Cấu hình container local chuyển sang `compose.postgres-dev.yaml`.
- **Operations telemetry trong worktree:** response có `histogramBuckets`, frontend đọc DTO có kiểu; minute flush không xóa thống kê RAM và chỉ xuất mỗi phút một lần, retention RAM 24 giờ. Cửa sổ bắt đầu lại khi API restart, chưa khôi phục từ DB. Kiểm chứng: Operations PostgreSQL QA 202 PASS (gồm histogram HTTP và giữ cửa sổ sau flush/qua 3 giờ), collector Python 34/34, backend/frontend build và architecture đạt; chưa deploy code. VPS đã cài collector/timer và mount riêng public JSON read-only, xác minh tự cập nhật và health 200; image live giữ nguyên. SMTP collector đang để `SMTP_HOST=""`, chưa bật gửi cảnh báo. Chi tiết vận hành trong `docs/vps-access.md`. Kiểm chứng trước merge: toàn bộ webhook/monitoring backend 879 PASS trên PostgreSQL QA; sửa test revenue/growth dùng ngày Việt Nam của `PaidAt` thay UTC để không lỗi 00–07h. E2E menu Zalo/Thanh toán dùng hậu tố nhãn vì tên accessible có tiền tố icon Ant Design; không đổi UI sản phẩm.

4. **Mở rộng nhiều replica cần thiết kế thêm:** IP rate limiter/latency aggregator/worker telemetry ở process; invoice sweep và payment reconciliation chạy theo app instance. Zalo có DB lease nhưng gateway volume vẫn chỉ cho một process. Không tự scale Compose rồi suy ra đã HA.
5. **Restore/SMTP/provider thật:** fake tests không chứng minh bank settlement, mailbox delivery, phiên Zalo thực hay production recovery point. Không lấy số test/throughput lịch sử làm capacity hoặc SLA hiện tại.
6. **Source không đại diện toàn bộ host:** Compose repo chưa chứa mọi override Zalo/runtime hiện có trên VPS. Push code không tự cập nhật mọi helper/Compose/env trên host; đọc runbook và đối chiếu trước thay đổi hạ tầng.
7. Frontend build hiện có cảnh báo chunk >500 KB; build vẫn đạt. Nếu tối ưu bundle, đo dependency/chunk thay vì tăng ngưỡng cảnh báo để che vấn đề.
8. **Query profiling (22/10/2026):** đo đúng image live trên API/Postgres tạm cùng quota và fixture 400 owner/96.000 hóa đơn, không đổi production. Dashboard/phòng/hóa đơn/dãy/chỉ số lần lượt 9/9/11/2/3 SELECT/request gồm auth. Theo trọng số stress test, hóa đơn/dashboard/phòng chiếm khoảng 45/31/21% thời gian planning+execution đo tuần tự; không phải CPU share live. Worktree sau profiling đã gộp 5 aggregate invoice list thành một, giảm 4 query (nhánh đã đo dự kiến 11→7); dashboard chưa sửa. Plan đã dùng index owner; các bảng nhánh fixture rỗng chưa đại diện dữ liệu đầy đủ. API build đạt; nhóm Accounting/Billing PostgreSQL đạt 81 PASS, gồm 22 assertion mới về summary/filter/empty/owner/legacy và đếm SQL. Lỗi `PaidSubscriptionCount == 1` do lệch biên ngày UTC/Việt Nam trong monitoring tests đã được sửa ở commit `19d6df7` và toàn bộ suite C# hiện đã vượt qua. Chưa thêm index/đổi quota/deploy/đo lại capacity. Xem [phương pháp, giới hạn và bằng chứng](docs/vps-access.md#profiling-query-trước-khi-tối-ưu). Script tái lập: `tests/profile_read_queries.py --self-check`, `--run-vps` chỉ khi được yêu cầu profiling VPS.

## 9. Triển khai, dữ liệu cần giữ và tài liệu còn lại

Push `main` kích hoạt CI/CD, publish và deploy nếu các cổng thành công; chỉ push khi người dùng yêu cầu. Repo GitHub là `nhaq0505/Roomie`, image vẫn `ghcr.io/nhaq0505/rehouse`.

- [Triển khai / rollback](docs/deployment.md): identity, digest, host paths, migration overlay, Caddy, SMTP.
- [Truy cập VPS chính và tải đọc live](docs/vps-access.md): dùng `roomie-ops` + sudo với key hiện có; root bị chặn SSH bởi hardening. Ngày 22/10/2026 đo owner riêng biệt, profile 3 API reads/10 giây: 200 owner/48.000 hóa đơn giữ 120 giây đạt p95 348 ms; tiếp đó fixture 400 owner/96.000 hóa đơn xác định 225 owner đồng thời giữ 120 giây đạt 67,45 RPS, p95 413 ms, 0 lỗi; 250 dừng vì p95 cửa sổ gần nhất 1013 ms. Ngưỡng theo bước 25, chưa đo 226–249 hoặc tải ghi/soak dài hạn. Login limiter nâng tạm theo yêu cầu và khôi phục 10/phút trước tải GET, fixture đã dọn. Runbook giữ cả lần đo 8 tài khoản trước đó và bằng chứng riêng từng lần.
- [Backup / restore](docs/backup-operations.md): DB + keys, Drive crypt, retention, restore drill.
- [EF migrations](docs/ef-core-migrations.md): scaffold PostgreSQL và áp schema đúng runner.
- [Zalo setup](docs/zalo-setup.md): gateway keys/session, Docker override, cooldown/Unknown.
- [Monitoring runbook](docs/monitoring/runbook-and-metric-dictionary.md): metrics, nguồn dữ liệu, cấu hình và các giới hạn wiring.
- [Test harness](tests/README.md): môi trường cô lập và PostgreSQL.

Không xóa `.local-data`, private config, backup, DataProtection keys, Zalo session volume, `output/worktree-cleanup-*`, file `*.bundle`/archive, tài sản marketing/ảnh người dùng hoặc dự án video chỉ vì chúng không có import. `docs/marketing/` là nghiên cứu kinh doanh riêng, không phải tài liệu kỹ thuật dư.

Các báo cáo triển khai/review theo ngày, handoff cũ, API/architecture mô tả lặp và DOCX thiết kế cũ đã được bỏ khỏi working tree khi hợp nhất ngày 21/10/2026; bản tracked có thể lấy lại bằng `git show 7be139d:<path>`. Không tạo lại chuỗi handoff/report chồng nhau. Khi bàn giao, ghi thay đổi và kiểm chứng thực tế vào mục liên quan, dùng Git lưu lịch sử.

## 10. Kiểm chứng trong đợt hợp nhất tài liệu

Ngày 21/10/2026 trên baseline ở đầu file:

- API publish/build và DbMigrator build đạt; snapshots SQLite/PostgreSQL khớp model.
- Frontend build, i18n (1354 keys × 4 catalogs), architecture (108 source modules), API paging, HTTP client, analytics và dates đạt. Build có cảnh báo chunk lớn đã nêu ở mục 8.
- C# webhook/monitoring suite đạt (816 dòng PASS); Zalo SQLite suite 53 assertions đạt; gateway 12/12 đạt sau `npm ci` (lần đầu máy chưa cài zca-js).
- HTTP harness SQLite 83/83 và OTP 15/15 đạt, không skip; backup 7/7 và host collector 34/34 đạt.
- Playwright toàn bộ: 64 passed, 1 timeout ở `landlord.spec.ts` L3b khi `page.goto('/rooms')`, 2 test sau đó không chạy do serial group. Chạy lại nguyên `landlord.spec.ts` với 1 worker: 6/6 đạt, gồm ca lỗi và hai ca chưa chạy. Chưa xác định nguyên nhân timeout; không gọi lần toàn bộ đầu tiên là xanh.
- Liên kết Markdown local và `git diff --check` đạt. Không chạy lại PostgreSQL runtime/role-container test trong đợt tài liệu này; snapshot check không thay thế chúng.

Không kiểm tra trạng thái live, không chạy migration vào DB vận hành, không gửi tin/thư/chuyển tiền thật. Thay đổi lần này là tài liệu, ignore output và một comment tham chiếu; không sửa hành vi sản phẩm. Chưa commit/push trong đợt này.


## 11. Cập nhật scheduler monitoring — 21/10/2026

- `MonitoringBackgroundWorker` và `MonitoringScheduleOptions`: incident check mỗi phút, rollup 7 ngày hoàn tất + prune mỗi giờ; hai vòng độc lập, retry sau lỗi, scope mới mỗi chu kỳ, heartbeat và cancellation. Xem mục 8/runbook về lookback, giới hạn một replica và DB outage trong RAM.
- `IncidentMonitoringService`: không truy cập DB sau probe thất bại; gửi cảnh báo outage độc lập sau ba probe cách ít nhất một phút, cooldown một giờ. Reconnect lưu lịch sử và dùng recovery/retry hiện có; shutdown cancellation không bị tính thành lỗi SMTP.
- Cả hai Compose map cấu hình bật/tắt scheduler và app alerts, recipient `MONITORING_ALERT_TO`; SMTP tái dùng `SMTP_*`. Phải cấu hình recipient/SMTP trên môi trường triển khai; không suy ra host đã được cập nhật.
- Kiểm chứng: API build đạt, 0 warning/error; console webhook/monitoring suite đạt 839 PASS (gồm scheduler dùng đồng hồ/timer điều khiển, SQLite maintenance thực, outage với connection đã dispose, SMTP failure/retry/cooldown/recovery, disable/config validation/cancellation). Cả hai Compose `config --quiet` đạt với env fixture; model SQLite/PostgreSQL không có pending changes; `git diff --check` đạt.
- Chưa kiểm thử PostgreSQL runtime trong đợt này; không chạy migration DB vận hành, không gửi SMTP thật, chưa commit/push/deploy. Các thay đổi dọn tài liệu trước đó vẫn giữ trong working tree.

Đường triển khai recipient không cần SSH quản trị: repository variable `MONITORING_ALERT_TO` → CI main Docker build argument → image env cùng tên. Chỉ recipient được đóng gói, SMTP credentials vẫn chỉ ở runtime. Notifier ưu tiên recipient runtime không rỗng, rồi fallback env image. Đổi variable cần build/deploy lại; giữ tên/email cụ thể ngoài Git.

## 12. Sửa luồng VietQR thủ công — 21/10/2026

- Yêu cầu QR không phải bằng chứng chuyển tiền. `ManualPaymentAssessment` đánh giá snapshot theo dữ liệu hiện tại, chặn thu trùng/ghi một phần khi hóa đơn trong batch thay đổi. Dismiss lưu thời điểm/lý do, giữ lịch sử và không sửa tiền; chỉ owner của request có quyền thực hiện, chỉ cho yêu cầu stale. Hai concurrency tokens `ConfirmedAt`/`DismissedAt` cùng serializable transaction bảo vệ xác nhận/bỏ yêu cầu.
- `GET /api/tenant/{token}/manual-payments/{id}` scope theo token lượt thuê đang hiệu lực; khách poll trạng thái bằng GET, ẩn QR stale/lỗi, chỉ tạo QR mới khi mở/chủ động cập nhật. Overview manual cũng poll khi còn nợ, dừng khi thanh toán xong.
- Chủ trọ thấy trạng thái đã thu đủ/hóa đơn thay đổi thay vì nút xác nhận sai; refresh list và dữ liệu hóa đơn sau mutation lẫn conflict. Ghi thu/correction/reconcile/cọc/trả phòng invalidate panel. Error i18n chỉ nhận đúng lỗi lượt thuê, không đổi mọi thông báo “đã thay đổi” thành lỗi lượt thuê.
- Schema thêm hai cột nullable `DismissedAt`/`DismissalReason`, migrations cả SQLite/PostgreSQL. Production tenant-payment limiter vẫn 12/phút; Development 120/phút cho HTTP fixture dùng chung loopback.
- Kiểm chứng local: build frontend, i18n 1366 keys × 4, architecture, API/HTTP client đạt; C# suite 864 PASS; Playwright toàn bộ 69/69; HTTP SQLite 87/87 không skip; PostgreSQL 62 đạt/8 skip vì các fixture sửa DB chỉ hỗ trợ SQLite. Cả hai migration snapshots khớp model. Không xác nhận/bỏ yêu cầu thật hoặc ghi tiền trên production để thử nghiệm.

CI lần đầu của bản sửa VietQR: backend/integration đạt, Playwright 68 đạt/1 lỗi selector tên nút khi Ant Design thêm loading icon vào accessible name. Test stale conflict đã sửa để đợi loading kết thúc trước khi kiểm nút vẫn disabled; lặp local 8/8 đạt. Không thêm retry hoặc bỏ assertion chống xác nhận trùng.

## 13. Chuyển đổi toàn diện sang PostgreSQL — 21/10/2026

- Đã xoá toàn bộ 18 file dữ liệu/sidecar/WAL/SHM SQLite và công cụ import cũ `backend/RoomRental.DbMigrator`, `scripts/sqlite-to-postgres/`. Cấu hình container local chuyển sang `compose.postgres-dev.yaml` ở thư mục gốc (giữ nguyên container `rehouse-pg-migration-db` cổng 15432 và volume).
- Loại bỏ SQLite provider khỏi test harness (`tests/run_disposable.py`) và CI workflow (`.github/workflows/ci-cd.yml`); chỉ chạy `postgres` và `otp-postgres` với 0 skip kỳ vọng.
- Chuyển toàn bộ các fixture sửa DB trực tiếp trong Python sang PostgreSQL qua stdlib và `docker exec psql` (`tests/pg_fixture.py`), ràng buộc loopback database tạm `roomie_qa_*` và escape SQL an toàn.
- CI bổ sung service PostgreSQL chuyên biệt và host networking cho các suite C#.
- Kết quả kiểm chứng HTTP disposable thực tế trên container QA `roomie-qa-pg-only-8f80de26` (cổng 50472):
  - `postgres`: 87/87 tests PASS, 0 skip, 0 lỗi (bao gồm đầy đủ 8 ca nghiệp vụ trước đây bị skip, payOS webhook fixture và SEO HTTP). Log tại `/tmp/roomie-pg-only-http-postgres.log`.
  - `otp-postgres`: 15/15 tests PASS, 0 skip, 0 lỗi. Log tại `/tmp/roomie-pg-only-http-otp-postgres.log`.
  - Template database `roomie_qa_bootstrap` được bảo toàn nguyên vẹn, không bị mutate.
- Kết quả kiểm chứng console C# trên PostgreSQL: cả hai suite chạy thành công, 0 dòng FAIL, mã thoát 0:
  - `RoomRental.WebhookTests`: 849 dòng `PASS:`. Log tại `/tmp/roomie-pg-only-csharp-webhook.log`.
  - `RoomRental.ZaloTests`: 54 dòng `PASS:` (dòng tổng kết xác nhận 53 assertions đạt; không gọi provider ngoài). Log tại `/tmp/roomie-pg-only-csharp-zalo.log`.
- Playwright E2E: 69/69 passed.
- PostgreSQL migrations và model snapshot được bảo toàn nguyên vẹn; startup fail-fast đã được xác nhận khi cấu hình `DatabaseProvider=SQLite` hoặc để trống chuỗi kết nối.
- Tiến hành phát hành qua luồng CI/CD chính thức lên VPS 116.118.3.40 sau khi dọn container QA tạm thời; bảo toàn container dev `rehouse-pg-migration-db` cổng 15432.

## 14. Bổ sung test toàn diện và sửa lỗi biên — 23/10/2026

- **Sửa lỗi sản phẩm & tương thích**:
  - `backend/RoomRental.Api/Program.cs`: bỏ `app.UseDefaultFiles()` trước `SeoMiddleware` để `/index.html`, `/welcome`, `/app.html` xử lý đúng chuẩn redirect/404 và canonical/CSP của `SeoMiddleware`.
  - `backend/RoomRental.Api/Features/Properties/OwnerManagementEndpoints.Documents.cs`: clamp giá trị `RoomAreaSqm` ngoài khoảng `(0, 10000]` (ví dụ dữ liệu âm cũ) về `null` và `PaymentDueDay` ngoài `[1, 28]` về `5` trong `BuildOccupancyDocumentDtoAsync` khi đọc `GET /api/rooms/{roomId:guid}/documents` để không kẹt validation khi người dùng chưa sửa diện tích/ngày hạn.
  - `backend/RoomRental.Api/Features/Admin/AdminPromotionsEndpoints.cs`: bọc toàn bộ thân transaction `IsolationLevel.Serializable` của `POST /api/subscription/redeem-code` trong khối `try/catch` bắt `DbUpdateConcurrencyException` và lỗi PostgreSQL (`40001`, `23505`, `40P01`, `55P03`) để trả `409 Conflict` nhất quán khi đua tranh đồng thời.
  - `backend/RoomRental.Api/Shared/RequestSecurityMiddleware.cs`: dùng null-conditional trên `context.RequestServices` khi đọc `TimeProvider`, `IAuthenticationService` và `IMemoryCache` để an toàn khi middleware được gọi trực tiếp với `DefaultHttpContext` tối giản.
  - `frontend/src/app/seo/site.ts`: chuẩn hóa dấu gạch chéo cuối (`pathname.replace(/\/+$/, '') || '/'`) trước khi tra `publicPages` trong `metadataFor` để `/features/`, `/pricing/`, `/huong-dan/` giữ nguyên `public: true` và canonical URL khi hydration ở client.
  - `frontend/src/features/rooms/ContractDocumentsDrawer.tsx` & `frontend/src/features/admin/AdminPromotionsPage.tsx`: bổ sung rule kiểm tra CCCD 9–12 chữ số và giới hạn tối đa 29 thành viên ở cùng trên `Form.List` của drawer hợp đồng/CT01; đặt `name="welcomePromo"` và `name="voucherCode"` cho hai `Form` trên trang khuyến mãi admin để tránh trùng id; thêm alias script `test:cccd-qr` bên cạnh `test:cccd` trong `frontend/package.json`.
  - `zalo-gateway/src/store.mjs` & `tests/run_disposable.py`: chỉ `fsyncSync` thư mục trên nền tảng khác `win32` trong `EncryptedStore.write`, và fallback thư mục tạm hệ thống khi `/tmp` không tồn tại trên Windows trong `tests/run_disposable.py`.
- **Mở rộng bộ kiểm thử**:
  - Backend C# (`tests/RoomRental.WebhookTests`): thêm `ContractDocumentsAndMoneyTests.cs` (`VietnameseMoneyReader`, `ContractWordGenerator` DOCX/CT01 OpenXML, escape ký tự đặc biệt XML `& < > " '`, chuỗi giống công thức và tối đa 29 thành viên ở cùng), `SeoAndRoutingUnitTests.cs` (toàn bộ nhánh `SeoMiddleware`), mở rộng `PromotionsTests.cs` (HTTP endpoints admin/redeem, quota, idempotency, hết hạn, thu hồi và đua tranh `Serializable` trên voucher 1 lượt), `RoomTenantLifecycleHttpTests.cs` (check-in meter validation, cập nhật/xuất DOCX hợp đồng & CT01, clamp dữ liệu cũ trên `GET /documents`, cross-owner isolation), `AuthSecurityHttpTests.cs` (sliding session 12h/14d `RememberMe` và chặn trượt phiên khi vi phạm CSRF Origin).
  - Frontend & Gateway & Python & E2E: mở rộng `frontend/scripts/test-cccd-qr.mjs` (parser CCCD 6/7 trường, ngày năm nhuận/thế kỷ, video/image QR decoders, `legalTemplates`, và `metadataFor` gồm cả biến thể có dấu `/` cuối), `frontend/scripts/test-http-client.mjs` (`requestBlob`), `zalo-gateway/test/gateway.test.mjs` (13 tests gồm kiểm tra khóa sai độ dài, phát hiện giả mạo ciphertext/auth-tag, `owners()`, sự kiện QR trung gian và giới hạn đầu vào `send`), `tests/test_seo_http.py` (14 tests cho 4 trang prerender `/`, `/features`, `/pricing`, `/huong-dan`), `frontend/e2e/admin.spec.ts`, `landlord.spec.ts`, `smoke.spec.ts` (tổng cộng 81 E2E tests).
- **Kết quả kiểm chứng thực tế trên PostgreSQL 17 QA container (`postgres:17-alpine`, cổng 55432)**:
  - `dotnet run --project tests/RoomRental.WebhookTests`: **1.289 dòng `PASS:`**, mã thoát 0.
  - `dotnet run --project tests/RoomRental.ZaloTests`: **57 assertions (`58` dòng `PASS:`)**, mã thoát 0.
  - `python tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider postgres`: **90/90 tests PASS, 0 skip**.
  - `python tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider otp-postgres`: **15/15 tests PASS, 0 skip**.
  - `npm --prefix zalo-gateway test
npm --prefix whatsapp-gateway test`: **13/13 tests PASS**.
  - `npm --prefix frontend run test:e2e -- --workers=4`: **81/81 tests PASS**.
  - Frontend build, `i18n:check` (`1664 keys × 4 catalogs`), `check:architecture` (`118 source modules`), `test:api`, `test:http`, `test:analytics`, `test:dates`, `test:cccd` / `test:cccd-qr`, `dotnet ef migrations has-pending-model-changes` và `git diff --check` đều đạt 100%. (Lưu ý: `test_backup_restore.py` và 2 ca `os.statvfs`/POSIX mode trong `test_host_collector.py` phụ thuộc môi trường Linux/POSIX nên chạy trên runner Ubuntu CI; 32/34 ca logic của `test_host_collector.py` đạt trên Windows). Container QA tạm và thư mục publish tạm đã được dọn sạch sau kiểm thử.

## 15. Tối giản hóa UI/UX Mobile & Luồng chỉ số điện — 25/10/2026

- **Thanh điều hướng dưới (Bottom Tab Bar `<= 767px`) và Topbar (`AppShell.tsx`, `controls.css`, `responsive-states.css`)**:
  - Cố định 5 tab ở đáy màn hình cho chủ trọ: **Dãy trọ (`/buildings`) · Phòng (`/rooms`) · Lập hóa đơn (`/readings`) · Hóa đơn (`/invoices`) · Cấu hình (`/settings/payment`)** (các mục phụ nằm trong menu Hamburger bên trái).
  - Ẩn tiêu đề trang bị lặp bên dưới `.topbar` trên mobile (`<= 767px`), giữ lại nút thao tác chính (`.page-heading-extra`).
- **Tối giản các trang nghiệp vụ và hộp thoại**:
  - **Phòng (`/rooms`)**: rút gọn thẻ phòng mobile còn 3 hàng (Header có badge `Đang thuê` nổi bật, hàng người thuê + giá thuê, hàng nút thao tác); bỏ tag hạn mức phòng và chỉ giữ bộ lọc theo dãy trọ.
  - **Lập hóa đơn (`/readings`)**: bỏ bộ chọn tháng, nút chốt hàng loạt, các pill trạng thái và luồng duyệt chỉ số điện khách gửi, chỉ giữ bộ lọc theo dãy trọ.
  - **Hóa đơn (`/invoices`)**: bỏ 3 thẻ tổng hợp đầu trang và chỉ giữ 3 bộ lọc: **Tìm hóa đơn**, **Kỳ hóa đơn**, **Trạng thái thanh toán** (`Tất cả trạng thái` / `Chưa thanh toán` / `Đã thanh toán`).
  - **Cấp link cho khách (`RoomDialogs.tsx`)**: bỏ mã QR trong modal cấp link; thu gọn ô đường dẫn và đưa nút **Sao chép** lên nằm cùng hàng bên phải ô link.
  - **Cổng khách thuê (`/t/{token}`, `TenantPage.tsx`)**: gỡ bỏ hoàn toàn tính năng khách tự gửi chỉ số điện & khách tự check-in (các component `TenantMeterSubmissionCard.tsx`, `TenantSelfCheckinCard.tsx` và các endpoint `/api/tenant/{token}/self-checkin`, `/api/tenant/{token}/co-residents/{citizenId}`, `/api/tenant/{token}/identity-photo`, `/api/tenant/{token}/meter-submission*`, `/api/readings/submissions*`), chỉ hiển thị thông tin phòng, tổng dư nợ và danh sách hóa đơn. Chủ trọ vẫn xem/sử dụng ảnh CCCD/Hộ chiếu qua `/api/rooms/{id}/documents/identity-photo`.
- **Chỉ số điện nguyên & sửa chỉ số điện ban đầu (`OwnerManagementEndpoints.Occupancies.cs`, `RoomTenantPage.tsx`)**:
  - Toàn bộ ô `InputNumber` nhập chỉ số điện dùng số nguyên (`precision={0} step={1}`, không còn `.000`).
  - Cho phép sửa `InitialReading` trong **Sửa thông tin người thuê** (`PATCH /api/rooms/{id}/occupancy`) khi lượt thuê chưa có hóa đơn tháng đã phát hành (`Issued`); chỉ cập nhật `PreviousReading` cho kỳ chốt điện / hóa đơn nháp (`Draft`) đầu tiên của lượt thuê (không ghi đè các kỳ `Draft` về sau).


## 16. Sửa lỗi không tạo được mã QR kết nối WhatsApp — 26/10/2026

- **Nguyên nhân gốc rễ**:
  1. **Tương thích ESM/CommonJS trong `whatsapp-gateway/src/whatsapp.mjs`**: Khi nạp `@whiskeysockets/baileys` qua `await import('@whiskeysockets/baileys')` trên Node 24 ESM, `baileys.default` là object `module.exports` của CommonJS chứ không phải hàm `makeWASocket` (`baileys.makeWASocket` / `baileys.default.default`), gây `TypeError: makeWASocket is not a function` ngay khi gọi `loginQR` và làm phiên kết nối tự trở về `Disconnected` mà không có ảnh QR.
  2. **Phiên bản WhatsApp Web mặc định trong `@whiskeysockets/baileys@6.7.16` quá cũ (`[2, 3000, 1019707846]`)**: Máy chủ WhatsApp từ chối kết nối đăng ký thiết bị với mã lỗi `405 Connection Failure` trước khi phát sinh sự kiện `qr`.
  3. **Luồng khởi động lại kết nối (`515 restartRequired`) sau khi quét QR**: Giao thức Multi-Device của WhatsApp luôn đóng socket đăng ký ban đầu bằng stream error `515` ngay sau `pair-success` (`isNewLogin: true`) để yêu cầu mở socket xác thực mới; code cũ coi mọi sự kiện `connection: 'close'` trước `open` là lỗi hết hạn QR (`type: 1`).
  4. **Chưa tự khởi chạy `whatsapp-gateway` khi thiếu cấu hình container rời**: Khi chạy local hoặc trên VPS (`rehouse-app-1`) chưa khai báo biến môi trường `WhatsApp__GatewayUrl`/`WhatsApp__GatewayToken` riêng, `WhatsAppGateway.Enabled` trả về `false`.
- **Cách khắc phục**:
  - `whatsapp-gateway/src/whatsapp.mjs`, `gateway.mjs`, `server.mjs`: hỗ trợ giải nén đúng `makeWASocket`/`toDataURL` cho cả CommonJS namespace và ESM mock; tự động lấy `fetchLatestWaWebVersion()` / `fetchLatestBaileysVersion()` (kèm cache 6 giờ và fallback `[2, 3000, 1051697602]`); xử lý `isNewLogin` + tự mở lại socket khi gặp `515 restartRequired`; tắt log pino mặc định để không in khóa/payload ra stdout; `POST /owners/:id/connect` đợi tối đa 5 giây đến khi rời trạng thái `Connecting` để trả thẳng `QrReady` + `qrImage` về UI.
  - `backend/RoomRental.Api/Features/WhatsApp/WhatsAppGateway.cs`, `Program.cs`, `Dockerfile`: bổ sung `WhatsAppLocalGatewayHost` tự động khởi chạy và giám sát tiến trình `whatsapp-gateway/src/server.mjs` nội bộ trên loopback khi không cấu hình `WhatsApp:GatewayUrl` ngoài (lưu khóa `.session-secret` và phiên mã hóa tại `/app/data/whatsapp-sessions` cùng volume với `DataProtection`), đồng thời đóng gói `node` và `whatsapp-gateway` vào `Dockerfile`.

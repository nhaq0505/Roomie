# Roomie — Quản lý phòng trọ

Cấu trúc mã nguồn và quy tắc thêm tính năng: [hướng dẫn kiến trúc codebase](docs/codebase-architecture.md).

**Agent tiếp tục dự án: đọc [kế hoạch và tài liệu bàn giao](docs/PROJECT_HANDOFF.md) trước.**

SEO: xem [kế hoạch 8 tuần](docs/SEO_PLAN.md). **Sau mỗi đợt thay đổi SEO, cập nhật [nhật ký SEO](docs/SEO_LOG.md)** với công việc thực tế, kiểm thử, trạng thái phát hành và kết quả quan sát được; không ghi dự kiến thành đã hoàn thành.

Vận hành cập nhật 16/10/2026: [EF Core Migrations](docs/ef-core-migrations.md), [backup Google Drive mã hóa và phục hồi](docs/backup-operations.md), [xử lý bảo mật và tech debt](docs/tech-debt-remediation-2026-10-16.md). Backup VPS đã hoạt động; trạng thái phát hành app và các bước hardening được ghi trong nhật ký xử lý. Không ghi mật khẩu, token OAuth hoặc khóa giải mã vào git.

MVP ASP.NET Core .NET 10 + React/TypeScript cho nhiều chủ trọ. Admin duyệt chủ trọ; quản lý dãy/phòng/lượt thuê; nhập chỉ số điện thủ công; tính tiền và phát hành hóa đơn; link khách riêng; thu tiền thủ công và tích hợp payOS theo từng chủ trọ. Camera/AI nằm ở giai đoạn sau.

## Chạy thử trên máy

Yêu cầu .NET SDK 10 và Node.js 24 (hoặc phiên bản tương thích Vite).

Terminal backend:

```sh
ASPNETCORE_ENVIRONMENT=Development SeedDemo=true dotnet run --project backend/RoomRental.Api --urls http://localhost:5080
```

Terminal frontend:

```sh
cd frontend
npm ci
npm run dev
```

Mở http://localhost:5173. Vite chuyển `/api` sang backend cổng 5080. Development mặc định dùng SQLite, dữ liệu nằm trong thư mục `backend/RoomRental.Api/.local-data` (tách khỏi thư mục mã nguồn `Data` trên macOS).

Chỉ khi bật `SeedDemo=true` trong Development, có tài khoản thử:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Admin | admin@phongtro.local | Admin123! |
| Chủ trọ | owner@phongtro.local | Owner123! |

Luồng thử: đăng nhập chủ trọ → tạo dãy/phòng → nhận khách thuê → nhập chỉ số tháng → xem hóa đơn → ghi nhận thu tiền hoặc mở link khách. Đăng ký một chủ trọ mới sẽ phải chờ admin duyệt.

## Docker với PostgreSQL

```sh
cp .env.example .env
# Sửa .env: mật khẩu PostgreSQL, email/mật khẩu admin, URL công khai.
docker compose up --build -d
```

Mở http://localhost:8080 (hoặc APP_PORT đã đặt). Docker phục vụ frontend và API cùng origin, không bật dữ liệu demo. Tài khoản admin đầu tiên được tạo từ cấu hình `BootstrapAdmin`; không thay mật khẩu tài khoản đã tồn tại khi khởi động lại.

Hai volume lưu PostgreSQL và khóa mã hóa DataProtection. Sao lưu cả hai trước khi nâng cấp. Không chạy `docker compose down -v` nếu muốn giữ dữ liệu.

Cookie của bản Production yêu cầu kết nối bảo mật. `localhost` dùng để thử tại máy; khi truy cập từ IP LAN hoặc tên miền, phải cấu hình HTTPS. Development có thể migrate lúc startup; Production chạy job `Database__MigrateOnly` bằng identity riêng trước khi thay app. Runtime chỉ kiểm tra schema và dùng role DML; migration/snapshot riêng cho SQLite và PostgreSQL. Thay đổi model cần migration cho cả hai provider; xem [quy trình migrations](docs/ef-core-migrations.md). Reset dữ liệu đã được người dùng cho phép riêng khi chuyển đổi lần đầu; các lần nâng cấp sau không tự xóa database.

Khi mở truy cập công khai qua tên miền, cấu hình HTTPS qua reverse proxy và đặt `PUBLIC_BASE_URL` đúng tên miền; không dùng tài khoản demo. Đặt `TRUSTED_PROXIES` thành IP của reverse proxy (ngăn cách bằng dấu phẩy) để backend nhận đúng HTTPS/host từ forwarded headers và kiểm tra nguồn yêu cầu. Proxy phải chuyển tiếp `Host`, `X-Forwarded-Proto`, `X-Forwarded-For`; không cấu hình tin mọi proxy. Phiên bản hiện tại cần được kiểm thử trên dữ liệu thực trước khi vận hành rộng.

Pilot VPS truy cập qua [Roomie](https://roomie.io.vn); Caddy chuyển tiếp tới API chỉ bind `127.0.0.1:8080`, PostgreSQL không mở cổng. Sau reset đã được cho phép, tài khoản/nghiệp vụ cũ và cấu hình payOS cũ đã xóa, admin được bootstrap lại; số liệu tại thời điểm cutover nằm trong nhật ký, không phải số đếm realtime. Đã có backup Google Drive mã hóa hằng ngày 03:00–03:20 giờ Việt Nam và trước deploy, kèm kiểm tra tải xuống/giải mã và restore thử. Xem [runbook triển khai và khôi phục](docs/deployment.md) trước mọi thay đổi trên host.

## payOS

Chủ trọ vào phần cấu hình thanh toán và nhập Client ID, API Key, Checksum Key của kênh payOS riêng. Backend mã hóa thông tin, không trả khóa bí mật về giao diện. Thiếu cấu hình thì không tạo link thanh toán giả. Xem [hướng dẫn thiết lập payOS cho chủ trọ](docs/payos-setup.md) để biết chi tiết các bước từ đăng ký tài khoản payos.vn đến liên kết ngân hàng và kích hoạt cấp link cho khách.

Khi bấm **Lưu và xác minh payOS**, backend lưu bộ khóa rồi tự gọi API `confirm-webhook` của payOS để đăng ký URL của chủ trọ:

```text
https://ten-mien-cua-ban/api/payments/webhook/{ownerId}
```

Backend lấy origin HTTPS từ `WebhookBaseUrl` (Docker: `WEBHOOK_BASE_URL`), mặc định dùng `PublicBaseUrl`. Không lấy URL từ browser hoặc Host header. Khi phát triển có thể đặt `WebhookBaseUrl` trong `backend/RoomRental.Api/appsettings.Development.local.json` (gitignore), trỏ đến tunnel HTTPS. Tunnel chỉ webhook vẫn cho phép giao diện chạy localhost. Khi tunnel đổi URL, cập nhật cấu hình máy chủ rồi bấm **Xác minh lại webhook**.

Trạng thái xác minh được lưu trong database, gắn với bộ khóa và URL hiện tại. Nếu xác minh thất bại, khóa vẫn được lưu; giao diện báo lỗi và cho thử lại mà không nhập lại khóa. Bộ khóa hoặc URL thay đổi sẽ làm trạng thái cũ hết hiệu lực. Xác minh thành công chỉ chứng minh endpoint nhận webhook được; vẫn phải thử giao dịch thật. Bảng `PaymentWebhookRegistrations` hiện được quản lý trong EF Core Migrations cùng schema ứng dụng.

Chỉ webhook hợp lệ/đối soát máy chủ mới ghi nhận tiền; URL quay về sau thanh toán không chứng minh đã trả tiền. Cần thử giao dịch thật và webhook HTTPS với tài khoản payOS của chủ trọ trước khi nhận tiền. Dự án không chứa khóa API thật.

## Kiểm tra

```sh
dotnet build backend/RoomRental.Api
npm --prefix frontend run build
python3 -m unittest discover -s tests -v
```

Kiểm thử API cần backend đang chạy với dữ liệu thử ở localhost:5080; xem hướng dẫn bổ sung trong thư mục tests. Không chạy bài kiểm thử tích hợp trên cơ sở dữ liệu vận hành.

Xem `docs/api-contract.md` cho API và `docs/implementation-notes.md` cho phạm vi MVP, các giới hạn và bước triển khai AI sau này.


Tiền thuê: mỗi khi ghi chỉ số kỳ tháng, hệ thống lập hóa đơn ngay theo đủ giá thuê tháng, kể cả khách bắt đầu thuê giữa tháng. Tiền thuê không chia theo ngày vào/ra hoặc số ngày của tháng.

Tiền cọc: vào **Phòng & người thuê → Thêm người thuê**, nhập mức cọc thỏa thuận với từng khách (0 nếu không thu). Hệ thống tạo khoản cọc riêng trên link khách; thu qua payOS hoặc ghi nhận thu thủ công. Xem tại **Hóa đơn → Tiền cọc** hoặc menu phòng **Thu / quyết toán cọc**. Khi trả phòng, nhập số tiền cọc muốn ghi nhận đã hoàn; phần còn giữ lại được ghi nhận trong lịch sử quyết toán. Đây chỉ là ghi nhận hoàn tiền bên ngoài, không tự chuyển tiền ngân hàng.

Nhân bản phòng: **Phòng & người thuê → ⋯ → Nhân bản phòng**. Form điền sẵn dãy và mức phí để chỉnh trước khi tạo. Nhập chỉ số công tơ của phòng mới; phòng bản sao bắt đầu trống, chưa có người thuê hoặc công nợ.

Hóa đơn bổ sung: **Phòng & người thuê → ⋯ → Tạo hóa đơn bổ sung**, nhập nội dung, số tiền và hạn thanh toán. Dùng cho phòng đang có khách; khoản thu xuất hiện trên link khách hiện có. Xem lại tại **Hóa đơn → Hóa đơn bổ sung**.

Trả phòng: **Phòng & người thuê → ⋯ → Trả phòng & quyết toán**. Xem hóa đơn còn nợ và tiền cọc đang giữ, nhập số tiền hoàn thủ công rồi xác nhận để kết thúc lượt thuê, thu hồi link khách và trả phòng về trống. Hệ thống ghi nhận khoản hoàn và phần cọc còn giữ trong lịch sử quyết toán; không chuyển tiền ngân hàng. Không chốt công tơ cuối kỳ hoặc lập hóa đơn cuối tại bước này. Cần thanh toán hết hóa đơn đã phát hành (kể cả hóa đơn cọc), xử lý Draft và giao dịch payOS đang chờ trước khi trả phòng. Ngày kết thúc là ngày Việt Nam hiện tại.

Luồng quên mật khẩu, giới hạn OTP và yêu cầu triển khai: [khôi phục mật khẩu](docs/password-recovery.md).

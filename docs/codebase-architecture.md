# Cấu trúc code và cách mở rộng

## Frontend

`frontend/src/app` ghép ứng dụng: entry point, router, layout, SEO và thứ tự nạp CSS. `features` chứa nghiệp vụ; `shared` chứa HTTP, i18n, định dạng, theme và UI dùng chung.

Các tính năng hiện tại: `auth`, `dashboard`, `buildings`, `rooms`, `readings`, `invoices`, `reports`, `payments`, `subscription`, `admin`, `tenant` và `marketing`. `marketing` sở hữu landing page; tenant sở hữu portal và checkout riêng tư.

- Mỗi tính năng có `api.ts`, `types.ts` và `index.ts` công khai API/type khi cần. Tính năng khác import qua `index.ts`; phiên đăng nhập có entry riêng `auth/session.ts` để không kéo React vào client API thuần.
- Trang nhỏ giữ logic tại chỗ. Trang nhiều form hoặc thao tác bất đồng bộ tách thành hook quản lý trạng thái và component nhận dữ liệu/handler. Không chia thêm file chỉ để giảm số dòng.
- `shared` không import `features` hoặc `app`. Feature không import `app` hoặc facade `src/api.ts`. Layout được app truyền vào các trang mật khẩu/trạng thái tài khoản.
- `src/api.ts` là facade tương thích cho kiểm thử và consumer cũ. Không thêm request hoặc kiểu nghiệp vụ trực tiếp vào đó. Cập nhật facade khi cần giữ API tổng hợp.
- HTTP dùng chung giữ cookie cùng origin, ngôn ngữ, timeout và `ApiError`; không tự retry thao tác ghi tiền. Giữ query key, invalidation và vòng đời idempotency key khi mở rộng hook.

`app/styles.css` ghép các file CSS theo thứ tự có chủ đích. Selector, token và các override responsive hiện tại được giữ nguyên. Style thuộc một tính năng đặt cạnh tính năng đó; style dùng chung nằm trong `shared/styles`. Khi di chuyển rule, phải giữ thứ tự cascade và đối chiếu desktop/mobile. Không tự chuyển toàn bộ CSS sang lazy import theo trang.

Chuỗi UI phải đi qua `t()`. Catalog nguồn và bản dịch nằm tại `shared/i18n`; `i18n:check` quét đệ quy mã nguồn, trừ implementation i18n và metadata SEO tiếng Việt cố định.

Thêm tính năng: tạo thư mục dưới `features`, đặt API/type và UI tại đó, công khai API/type cần dùng, rồi đăng ký lazy route tại app. Chỉ đưa thành phần thực sự được nhiều tính năng dùng vào shared.

## Backend

Giữ một project ASP.NET Core Minimal API. `Features/Auth`, `Admin`, `Properties`, `Billing`, `Payments`, `Reports` chứa endpoint/service theo nghiệp vụ. `Shared` chứa helper hạ tầng dùng chung. `Domain` và `Data` tiếp tục sở hữu entity, DTO chung, EF context và migrations.

Namespace và tên lớp hiện tại được giữ để không phá test, tài nguyên nhúng hoặc công cụ migration. Khi thêm endpoint, dùng nhóm nghiệp vụ hiện có và đăng ký tại `Program.cs`; không dựa vào vị trí thư mục để suy ra route.

Tra cứu phòng/dãy qua `PropertyLookup` để giữ kiểm tra owner và archive. Ngày/kỳ hiện tại dùng `VietnamCalendar`; chuyển instant sang ngày Việt Nam vẫn qua `ApiSupport.VietnamDate`. Fingerprint dùng `RequestFingerprint` với đúng hợp đồng JSON hiện tại: thay serializer hoặc cách chuẩn hóa có thể làm hỏng replay của request đã lưu.

Giữ transaction bao trọn thao tác nghiệp vụ hiện tại, kiểm tra version và idempotency. Di chuyển mã không phải lý do đổi schema. Nếu sau này đổi model, tạo/check migrations cho cả SQLite và PostgreSQL theo runbook EF hiện có.

## Kiểm tra

Chạy từ root:

```sh
npm --prefix frontend run build
npm --prefix frontend run check:architecture
npm --prefix frontend run i18n:check
npm --prefix frontend run test:api
npm --prefix frontend run test:http
npm --prefix frontend run test:e2e -- --workers=4
dotnet build backend/RoomRental.Api
dotnet build backend/RoomRental.DbMigrator
dotnet run --project tests/RoomRental.WebhookTests
dotnet ef migrations has-pending-model-changes --project backend/RoomRental.Api --context SqliteAppDbContext
dotnet ef migrations has-pending-model-changes --project backend/RoomRental.Api --context PostgresAppDbContext
```

`check:architecture` chặn phụ thuộc sai tầng, import vào nội bộ feature khác và vòng import runtime. CI chạy kiểm tra này cùng i18n, phân trang API và HTTP transport.

Kiểm thử HTTP phải dùng `tests/run_disposable.py` theo `tests/README.md`, với webroot trỏ tới bản frontend vừa build. PostgreSQL cần container QA riêng và connection loopback `roomie_qa_*`; không dùng container/database ứng dụng. OTP dùng SMTP sink loopback; payOS/SMTP thật không được gọi bởi các bài kiểm thử này.

## Quy tắc dọn file

Chỉ kết luận file dư sau khi xem cả import, entry point, build, CI, Docker, tài nguyên nhúng và công cụ chạy độc lập. Không được coi migration, declaration `.d.ts`, nguồn catalog, asset thiết kế hoặc script vận hành là rác chỉ vì không có import runtime. Giữ dự án video và tài liệu lịch sử; ghi rõ file chuyển và file xóa trong báo cáo refactor.

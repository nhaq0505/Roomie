# Kế hoạch và bàn giao dự án Quản Lý Phòng

Cập nhật: **16/10/2026 — Asia/Ho_Chi_Minh**.

Đây là tài liệu đọc đầu tiên khi agent mới tiếp tục dự án. Tài liệu ghi lại yêu cầu đã chốt, kế hoạch, trạng thái thực tế và cách tiếp tục; mã nguồn và kết quả kiểm tra mới hơn là căn cứ khi có khác biệt. Sau mỗi đợt làm việc, cập nhật tài liệu này để tránh mất ngữ cảnh.

## 1. Trạng thái hiện tại

**Mới nhất16/10 — hardening đã deploy:** app `4f3d1bc`, CI38845852582 xanh verify/publish/deploy. SSH dùng roomie-ops + key/sudo, root/password đã tắt, firewall input chỉ mở22/80/443. App dùng role DML roomie_app, migration job/owner riêng, không còn bootstrap secret trong runtime. Đã bổ sung MFA admin, account budgets, policy password, retention, phân trang, deadline/cảnh báo backup và kiểm thử; xem mục56 và [nhật ký xử lý](tech-debt-remediation-2026-10-16.md). Backup sau deploy đã tải xuống/giải mã/restore tạm PASS. Admin cần tự enroll authenticator và cất recovery codes; SMTP acceptance không đồng nghĩa email đã Delivered. Các checkpoint cũ phía dưới là lịch sử, không mô tả quyền SSH/DB hiện tại.

**Mới nhất16/10 — migrations và backup Drive đã vận hành:** EF Core Migrations live, reset local/VPS theo chấp thuận riêng đã xong, không còn reset tự động. Google Drive crypt + OAuthInproduction đã hoạt động, daily03:00–03:20VN/pre-deploy, retention7ngày/4tuần/3tháng và local10bản. Tải xuống/giải mã/restorePG17 tạm, DataProtection/checksum và forced OAuthrefresh systemd đều PASS. User duyệt công khai3trang thôngtin backup; các file backup/khóa vẫn riêng tư. Đọc mục51–53, [migrations](ef-core-migrations.md), [backup/phục hồi](backup-operations.md) và [bảo mật còn lại](tech-debt-remediation-2026-10-16.md). SSHroot/password và DBsuperuser vẫn là ưu tiên cần sửa. Source/tài liệu đợt backup còn local, chưa commit/push; không nhầm với cấu hình host đã live. Các checkpoint cũ phía dưới giữ để đối chiếu lịch sử.

**Mới nhất15/10 — CI/payOS đã sửa và live:** sourcecommit`46f1aad`, [run38680393633](https://github.com/nhaq0505/ReHouse/actions/runs/38680393633) success verify/publish/deploy. CI bypasspayOS chỉtrongDevelopmentfixture; Production bắtbuộc. APIphòng/hóađơn khôngtrảtoken/link khi chưa xácminh, HTTPfixture thật thaytestlặpif. Trangchủ/health/sitemap200, APIunauth401. **robots vẫnDisallowmọibot theo user; khônggửisitemap/requestindex**. Chi tiếtmục50 và[report](ci-payos-link-fix.md).

**Thương hiệu hiện tại: Roomie** (yêu cầu đổi tên14/10, mục42). Các nhắc Rehouse/Nhà Mình trong lịch sử là tên trước đây; định danh vận hành/mã hóa vẫn giữ tương thích.

**Phát hành 14/10 — người dùng yêu cầu push:** đóng gói các mục36–38 (SMTP OTP, bỏ copy link thanh toán, cash tự hủy payOS) lên main. Kết quả QA tại các mục tương ứng; SMTP thật vẫn chưa cấu hình, cần thiết lập trước khi đăng ký mới có thể gửi OTP. File secret local bị ignore, không nằm trong commit. Push main kích hoạt pipeline CI/publish/deploy hiện có; phải kiểm tra trạng thái workflow riêng, không coi push thành công là deploy thành công. Các câu “chưa commit/push” ở các checkpoint dưới là lịch sử trước yêu cầu phát hành này.

**Mới nhất — thu tiền mặt khi có payOS pending:** source đã tự xác minh/hủy order chưa nhận tiền rồi ghi khoản thu nguyên tử; thu đủ Paid, thu một phần Partial. Online đã trả đủ thì đối soát payOS và không ghi cash; thiếu bằng chứng hủy/đã nhận một phần thì giữ pending. SQLite165/PostgreSQL167 service assertions pass bằng provider giả, HTTP nghiệp vụ59/59. Chưa commit/push/deploy; xem mục38.

**Mới nhất — OTP xác nhận email đăng ký (SMTP):** source/local đã có luồng đăng ký chờ OTP, chỉ tạo user sau xác minh, gửi lại có cooldown/hạn mức. SMTP Gmail là lựa chọn khởi đầu đã hướng dẫn; chưa có hộp thư/mật khẩu ứng dụng, chưa gửi email thật. File `backend/RoomRental.Api/appsettings.Email.local.json` đã tạo rỗng và bị Git bỏ qua, quyền0600; người dùng tự điền thông tin bí mật. OTP mặc định bật; Production không cho tắt. API local5080 đã chạy bản mới, DB được backup trước khi thêm bảng RegistrationVerifications, tài chính319 invoice/250783641/Paid46440913 và digest giữ nguyên. Chi tiết mục36 và [hướng dẫn Gmail/SMTP](email-otp-setup.md). Mã đợt35 đã push main9921eef; thay đổi OTP chưa commit/push/deploy.


**Mới nhất 14/10 — tối ưu backend/security/ops đã triển khai local:** SQL aggregation Dashboard, giới hạn lịch sử đọc số, lọc hóa đơn, health DB, đổi mật khẩu giữ sid hiện tại, Modal/i18n và vendor chunks; backup off-host opt-in. SQLite 59/59; PostgreSQL 55 test methods (48 pass, 7 SQLite-only skip) và một fixture class skip, unittest báo `skipped=8`; service 81 assertions. DB thật 319 hóa đơn / 250.783.641 VND / Paid 46.440.913 VND giữ nguyên qua đối chiếu read-only. **Có sự cố QA ban đầu: 2 yêu cầu checkout bằng khóa giả đã ra payOS và bị từ chối mã 214; không có checkout/giao dịch.** Các lượt sau chặn outbound bằng proxy local. Chi tiết mục 35 và [báo cáo nâng cấp](backend-performance-security-ops-report.md). Không deploy, không nâng schema DB thật.


**Bổ sung 14/10: đã triển khai local sửa lượt thuê active, đổi/reset mật khẩu bằng mã dùng một lần và Dashboard KPI theo tháng. SQLite 52/52, PostgreSQL 40 pass + 8 skip, service 71 assertions; build và i18n 828 × 4 pass. Schema thêm 3 cột nullable, chưa áp vào DB vận hành/VPS; xem mục 33 và [báo cáo triển khai](account-occupancy-dashboard-report.md).**

**Đã hoàn thành MVP, đợt sửa FE, trang thử nghiệm evondevKit tại /rooms và pilot Rehouse trên VPS. Pilot hiện truy cập công khai tại [https://demo.yourdomain.com](https://demo.yourdomain.com), Caddy cấp chứng chỉ Let's Encrypt hợp lệ và chuyển HTTP sang HTTPS. Đăng nhập admin, cookie Secure/HttpOnly, `/api/auth/me`, `/api/admin/owners`, logout cùng kiểm tra Origin đã pass. Lịch sử local ghi nhận một giao dịch payOS 30.000 VND ở trạng thái Paid (mục 14); sự kiện đó tách biệt với VPS mới. Database VPS chưa có dữ liệu khách thuê/Mac hay cấu hình payOS; trên VPS chưa đăng ký webhook với payOS hoặc thực hiện thanh toán. Camera/AI chưa làm.**

**Quick status 13/10:** API cục bộ đã chuyển từ SQLite sang PostgreSQL 17 bằng snapshot nhất quán và importer riêng; đối chiếu toàn bộ 13 bảng/404 dòng, tiền, kiểu dữ liệu, quan hệ/unique và khóa DataProtection đều khớp. API Development đang ở `localhost:5080`, PostgreSQL chỉ bind `127.0.0.1:15432`; đây là chuyển đổi cục bộ trên một máy, không phải quy trình migrate VPS hay HA. VPS pilot là một triển khai riêng với database mới và HTTPS công khai. Xem mục 25 và [hướng dẫn SQLite → PostgreSQL](sqlite-to-postgres-migration.md). Quy trình schema migrations đầy đủ, restore drill định kỳ, phối hợp nhiều instance, telemetry production và autoscaling vẫn chưa sẵn sàng.

**Cập nhật local 14/10:** Ở checkpoint báo cáo/lưu trữ trước thay đổi chính sách tại mục 32, các suite trên DB dùng thử đã xanh: SQLite 38/38; PostgreSQL chạy 34 test, 32 pass và 2 SQLite-only test skip. Báo cáo tính tổng trên toàn bộ tập lọc, khoản cọc dùng nội bộ không bị tính thành tiền mặt mới, lịch sử giữ snapshot dãy/phòng, Excel dùng ô số/ngày có kiểu và giữ text chống công thức. Sau QA clone, root đã nâng cấp schema trên PostgreSQL Development thật từ backup; digest tài chính từng hóa đơn không đổi, không reset/seed hay thêm dữ liệu test. Source và schema chỉ thay đổi local; chưa push/deploy và không tạo thanh toán thật.

Yêu cầu gần nhất: bỏ tính tiền thuê theo ngày vào/ra, luôn tính đủ giá thuê tháng, và đổi trả phòng thành luồng ghi nhận hoàn cọc thủ công với phần còn giữ được kiểm toán. Trạng thái và bằng chứng mới nhất ở mục 32; mục 18 và các mô tả trả phòng trước đó giữ nguyên làm lịch sử. Các mục 15–17 là lịch sử các tính năng cọc, nhân bản phòng và hóa đơn bổ sung đã hoàn tất.

- Workspace: `/Users/nguyenhoanhquan/Documents/Project Cá Nhân/Quản Lý Phòng`.
- Frontend demo: `http://localhost:5173/dashboard`.
- Backend Development: `http://localhost:5080`.
- Frontend gọi `/api` qua proxy Vite tới backend.
- Database ứng dụng hiện tại: PostgreSQL trong container `rehouse-pg-migration-db`, database `room_rental`, chỉ bind `127.0.0.1:15432`.
- SQLite nguồn được giữ nguyên; snapshot đóng băng trước cutover nằm trong `backend/RoomRental.Api/.local-data/sqlite-to-postgres/room-rental-final-frozen-20261013.db`. Không đổi lại SQLite chỉ bằng cách sửa provider/connection string; xem mục 25 về delta sau cutover.
- Dữ liệu được lưu thật trong database, không phải giao diện giả lập.
- Workspace được khởi tạo thành GitHub repository `nhaq0505/rehouse`; image production đầu tiên đã build và deploy thành công trong [Actions run 38338681266](https://github.com/nhaq0505/rehouse/actions/runs/38338681266).
- Đã khôi phục tài khoản chủ trọ mẫu và để tab demo ở trang tổng quan sau kiểm tra FE. Các tiến trình dev có thể dừng khi phiên làm việc kết thúc; kiểm tra trước khi khởi động thêm để tránh trùng cổng.
- **VPS pilot 13/10:** Đã dọn đúng phạm vi ứng dụng `webphim` cũ sau khi duyệt inventory; cài đặt Rehouse với PostgreSQL riêng, chưa import dữ liệu Mac. SSH và dịch vụ hệ thống được giữ nguyên. Pilot công khai tại `https://demo.yourdomain.com`; Caddy reverse proxy chỉ chuyển tiếp tới API loopback `127.0.0.1:8080`, PostgreSQL không publish port. Chứng chỉ Let's Encrypt hợp lệ, HTTP trả 308 sang HTTPS, `/api/health` trả 200. Login admin, `/api/auth/me`, `/api/admin/owners`, cookie Secure/HttpOnly, logout same-origin và bad-Origin rejection đã kiểm tra công khai. Database vẫn mới, không có tenant hay khóa payOS; chưa có giao dịch hoặc đăng ký webhook với payOS thật. Xem [deployment runbook](deployment.md) và mục 26.

## 2. Bối cảnh và các quyết định đã chốt

Người dùng muốn xây website quản lý phòng trọ bằng .NET, với frontend phù hợp.

1. Một chủ trọ quản lý nhiều dãy trọ; mỗi dãy có nhiều phòng.
2. Chủ trọ phải được **admin duyệt** mới được sử dụng nghiệp vụ. Đăng ký tạo tài khoản chờ duyệt; admin có thể duyệt, từ chối, tạm khóa và mở khóa.
3. Người thuê vào link riêng để xem hóa đơn và thanh toán, không cần tài khoản đăng nhập.
4. Link thuộc **lượt thuê**, không phải quyền truy cập vĩnh viễn cho một phòng. Khi người thuê chuyển đi, link bị thu hồi; người thuê mới không nhìn thấy nợ của người cũ.
5. Tích hợp payOS theo cấu hình riêng từng chủ trọ, tiền về kênh của chủ trọ tương ứng.
6. Chỉ xác nhận thanh toán và gạch nợ từ webhook hợp lệ hoặc đối soát phía máy chủ. URL quay về từ trang thanh toán không chứng minh đã trả tiền.
7. Người dùng ban đầu muốn chụp công tơ và dùng AI qua API để nhận diện. Sau đó đã **chuyển camera/AI sang giai đoạn sau**, trước mắt chủ trọ nhập chỉ số điện thủ công. Không đưa AI trở lại phạm vi MVP nếu chưa có yêu cầu tiếp tục.
8. Công thức điện: `(chỉ số mới − chỉ số trước) × đơn giá/kWh`. Không lấy toàn bộ chỉ số tích lũy nhân đơn giá.
9. Chốt chỉ số hợp lệ sẽ lập hóa đơn ngay, gồm tiền phòng, điện, nước và dịch vụ; mức phí và chỉ số được lưu tại thời điểm phát hành.
10. Frontend đã chọn: React + TypeScript + Vite + Ant Design, giao diện tiếng Việt, thương hiệu **Rehouse** (chốt ngày 13/10/2026; trước đây là “nhà mình”).

Người dùng đã yêu cầu triển khai bằng agent Luna reasoning max. Đợt MVP dùng ba sub-agent `gpt-6-luna`, `max`: backend, frontend, verification; agent chính tích hợp và kiểm tra. Các sub-agent đã kết thúc, không có công việc chờ từ chúng. Không giả định agent mới có thể dùng lại các phiên/sub-agent cũ.

## 3. Kế hoạch và tiến độ

| Bước | Nội dung | Trạng thái / kết quả |
| --- | --- | --- |
| 1 | Chốt nghiệp vụ, phân quyền, stack, API | Hoàn thành; có hợp đồng API |
| 2 | Xây backend .NET và database | Hoàn thành MVP; SQLite dev, PostgreSQL Docker |
| 3 | Đăng ký, đăng nhập, admin duyệt chủ trọ | Hoàn thành; chủ trọ chưa duyệt không được thao tác nghiệp vụ |
| 4 | Quản lý dãy, phòng, lượt thuê và link khách | Hoàn thành |
| 5 | Nhập điện, tính phí, phát hành hóa đơn | Hoàn thành; chốt thủ công, không sửa lại chỉ số đã phát hành qua UI |
| 6 | Thu tiền thủ công, trả một phần, tính số dư | Hoàn thành; chống ghi nhận lặp khi retry |
| 7 | payOS checkout, webhook và đối soát | Mã tích hợp hoàn thành; HTTPS callback origin công khai đã cấu hình. VPS chưa có khóa payOS, chưa đăng ký callback với provider hoặc thực hiện giao dịch thật |
| 8 | Frontend chủ trọ, admin, portal khách | Hoàn thành MVP |
| 9 | Docker, bảo mật, kiểm tra SQLite/PostgreSQL | Kiểm tra cục bộ hoàn thành; HTTPS public pilot và auth/Origin smoke đã xác minh |
| 10 | Rà soát và sửa FE theo phản hồi | Hoàn thành; bổ sung pilot evondevKit /rooms, xem mục 12 |
| 11 | UAT trên dữ liệu sử dụng thực, payOS thật | Chưa thực hiện |
| 12 | Migration, triển khai, sao lưu và khôi phục vận hành | EF Core Migrations live; Google Drive crypt/daily/pre-deploy và restore thử đã PASS. Rollback image không rollback schema; chưa có migration job/identity riêng, HA/PITR hoặc alert đầy đủ. Xem mục51–53 |
| 13 | Camera và AI nhận diện qua API | Hoãn theo yêu cầu người dùng; chưa triển khai |
| 14 | Báo cáo thu tiền/công nợ/cọc, Excel và lưu trữ mềm dãy/phòng | Đã triển khai local; SQLite 38/38, PostgreSQL 32 pass + 2 SQLite-only skip; chưa deploy |
| 15 | Tối ưu truy vấn backend, tài khoản, bundle và backup off-host | Đã push main9921eef; xem mục35 về kiểm tra và sự cố outbound QA |
| 16 | OTP email cho đăng ký mới (SMTP) | Source/local xong, SMTP thật chờ cấu hình; service113 assertions, OTP SQLite10/10, PostgreSQL3/3; chưa push/deploy |

## 4. Những gì đang dùng được

### Chủ trọ

- Tổng quan số phòng, phòng đang thuê, tiền hóa đơn, đã thu, còn nợ và số phòng cần chốt điện.
- Tạo dãy trọ với tên và địa chỉ.
- Tạo phòng, thiết lập giá thuê, đơn giá điện, phí nước cố định/theo người, phí dịch vụ, chỉ số điện ban đầu; sửa thông tin và phí phòng.
- Thêm người thuê với tên, điện thoại, số người, ngày vào; ghi nhận chuyển đi.
- Mở link khách đang dùng, sao chép hoặc mở portal khách; thu hồi và tạo link mới qua xác nhận riêng.
- Xem báo cáo **Thu tiền / Công nợ / Tiền cọc** theo ngày, kỳ hóa đơn, dãy và phòng; phân trang nhưng tổng tiền tính trên toàn bộ kết quả lọc; xuất từng báo cáo ra `.xlsx` với ô ngày/số có kiểu.
- Lưu trữ mềm dãy/phòng đã ngừng dùng và khôi phục; mặc định danh sách chỉ hiện mục đang hoạt động. Không thể lưu trữ khi còn lượt thuê đang hoạt động, hóa đơn nháp/chưa thanh toán, cọc còn giữ hoặc giao dịch payOS đang chờ. Khôi phục dãy chỉ khôi phục phòng được lưu trữ cùng đợt.
- Nhập chỉ số điện theo tháng; ngăn chỉ số giảm, kỳ tương lai, bản ghi trùng và các trường hợp không hợp lệ theo lượt thuê/thứ tự kỳ.
- Chốt điện và lập hóa đơn; kiểm tra/tạo hóa đơn còn thiếu không tạo trùng.
- Xem chi tiết hóa đơn và các thành phần tiền; ghi nhận thu tiền mặt/chuyển khoản ngoài, thu một phần hoặc toàn bộ.
- Lưu cấu hình payOS đã mã hóa, xem URL webhook, đối soát giao dịch theo hóa đơn.

### Admin và người thuê

- Admin xem/lọc danh sách chủ trọ, duyệt/từ chối/tạm khóa/mở khóa.
- Người thuê mở link riêng, xem số dư và chi tiết hóa đơn, bắt đầu thanh toán payOS hoặc kiểm tra giao dịch.
- Thiếu cấu hình payOS sẽ không có thanh toán thành công giả. Đã tích hợp nút thanh toán nhưng cần kiểm thử kết nối thật trước khi thu tiền.

### Các đặc tính backend cần giữ khi chỉnh sửa

- Tách dữ liệu theo chủ trọ; kiểm tra vai trò và trạng thái tài khoản trên máy chủ.
- Cookie HttpOnly, kiểm tra Origin/Referer cho yêu cầu thay đổi dữ liệu, giới hạn tần suất ở các luồng cần thiết.
- Link khách ngẫu nhiên, token băm và bản mã phục vụ cấp lại; không ghi token vào log hoặc tài liệu bàn giao.
- Khóa payOS được mã hóa bằng DataProtection. Phải giữ khóa DataProtection khi giữ database.
- Hóa đơn lưu mức phí đã chốt; sửa giá phòng không sửa hóa đơn cũ.
- Hóa đơn có lifecycle Draft/Issued; luồng publish và correction đủ điều kiện ghi revision/version và actor/reason audit. Không có sửa hoặc xóa tự do mọi hóa đơn.
- Ghi nhận tiền có idempotency; webhook lặp chỉ tạo một khoản thu; xác minh chữ ký, chủ nhận, order và số tiền.
- Khi tạo checkout gặp kết quả không chắc chắn, giữ giao dịch để đối soát, không tạo thêm checkout có thể dẫn tới thu trùng.
- Không đổi khóa payOS hoặc chuyển người thuê đi khi còn giao dịch pending chưa được xử lý. Thu ngoài tự kiểm tra/hủy payOS chưa nhận tiền trước khi ghi khoản thu (mục38).

## 5. Phản hồi FE gần nhất và phần đã sửa

Người dùng không thấy nút tạo dãy, tạo phòng và cấp link; sau đó yêu cầu kiểm tra toàn bộ thiết kế FE.

**Nguyên nhân đã xác nhận:** Menu Ant Design dùng màu chữ tối trên sidebar tối, làm các mục gần như biến mất. Ở khung trình duyệt hẹp, điều hướng bị thu vào biểu tượng nhỏ; bảng rộng và thao tác giấu trong menu dấu ba chấm làm chức năng khó tìm.

Đã sửa:

- Menu máy tính/drawer dùng theme dark và màu chữ rõ; drawer có nút đóng.
- Hiện điều hướng trực tiếp trong khung hẹp, có tên từng mục và nhãn Menu.
- Dashboard có ba lối tắt: **Tạo dãy trọ / Tạo phòng trọ / Cấp link cho khách**. Hai lối tắt tạo mở đúng form.
- Nút cấp link hiện trực tiếp trên từng phòng đang thuê; phòng trống hiện Thêm người thuê.
- Mở/sao chép link không tự thu hồi link cũ. Thu hồi và tạo mới là thao tác riêng có xác nhận.
- `ResponsiveTable` dùng ResizeObserver đo vùng nội dung thực tế. Khi không đủ rộng để hiển thị bảng, chuyển sang thẻ giữ đầy đủ dữ liệu, form, hành động và phân trang. Áp dụng cho phòng, điện, hóa đơn, tài khoản chủ trọ.
- Tăng cỡ chữ và độ tương phản, thêm viền focus bàn phím; dashboard dùng hai cột thống kê trên điện thoại.
- Sửa CSS nhãn mô tả ghi đè màu chữ/icon trong nút Thanh toán của portal khách.
- Form dài cuộn phần thân riêng trên điện thoại, vẫn hiện nút lưu/hủy; giới hạn drawer theo viewport.
- Sửa breakpoint đăng nhập/đăng ký, tên/email dài, nhãn bộ lọc/tháng và vị trí cuộn khi đổi trang.

Không coi đợt sửa này là xác nhận mọi tình huống FE đã hoàn hảo. Trang trạng thái tài khoản và lỗi đã rà qua mã, chưa kiểm tra trực quan với tài khoản chờ duyệt trong đợt FE này.

## 6. Bản đồ mã nguồn và tài liệu

| Vị trí | Nội dung |
| --- | --- |
| `backend/RoomRental.Api/Program.cs` | Khởi động, provider database, auth, proxy, middleware, jobs |
| `backend/RoomRental.Api/Domain/` | Entities và DTO, gồm `ReportDtos.cs` cho hợp đồng báo cáo |
| `backend/RoomRental.Api/Data/` | DbContext, khởi tạo schema và demo |
| `backend/RoomRental.Api/Endpoints/` | Auth, admin, dãy/phòng/lượt thuê, hóa đơn, thanh toán, báo cáo (`ReportsEndpoints.cs`) và lưu trữ mềm |
| `backend/RoomRental.Api/Services/` | Tính hóa đơn, token khách, payOS, chữ ký, bảo mật request |
| `frontend/src/App.tsx`, `auth.tsx`, `api.ts` | Routes, gate tài khoản, auth state, hợp đồng gọi API |
| `frontend/src/components/AppShell.tsx` | Sidebar, menu compact/drawer, khung trang |
| `frontend/src/components/ResponsiveTable.tsx` | Bảng/thẻ theo chiều rộng thực của vùng nội dung |
| `frontend/src/styles.css` | Thiết kế và responsive; lưu ý các quy tắc tương phản nút/menu cuối file |
| `frontend/src/pages/` | Toàn bộ trang chủ trọ, admin, auth và portal khách |
| `tests/` | Kiểm thử HTTP nghiệp vụ, báo cáo/lưu trữ mềm và fixture webhook SQLite |
| `Dockerfile`, `compose.yaml`, `.env.example` | Build frontend/backend, PostgreSQL và cấu hình vận hành |

Tài liệu chi tiết: [README](../README.md), [API contract](api-contract.md), [phạm vi/giới hạn](implementation-notes.md), [kiểm tra MVP](verification.md), [rà soát FE](frontend-review.md), [hướng dẫn triển khai VPS](deployment.md), [hướng dẫn tests](../tests/README.md).

Ảnh kiểm tra trong `artifacts/`, gồm `frontend-review-desktop.png` và `frontend-review-current.png`. Ảnh là minh chứng tại thời điểm chụp, không phải dữ liệu chuẩn để viết test.

## 7. Cách chạy và kiểm tra

Chạy từ thư mục gốc dự án. Yêu cầu .NET SDK 10 và Node.js tương thích; lần triển khai dùng Node 24.

Backend:

```sh
ASPNETCORE_ENVIRONMENT=Development SeedDemo=true dotnet run --project backend/RoomRental.Api --urls http://localhost:5080
```

Frontend ở terminal khác:

```sh
cd frontend
npm ci
npm run dev
```

Tài khoản **demo Development**, chỉ dùng khi bật seed:

| Vai trò | Email | Mật khẩu mẫu |
| --- | --- | --- |
| Chủ trọ | owner@phongtro.local | Owner123! |
| Admin | admin@phongtro.local | Admin123! |

Không dùng các tài khoản/mật khẩu mẫu làm cấu hình production. Không lưu khóa payOS thật hoặc link khách thật vào tài liệu này.

Kiểm tra build:

```sh
dotnet build backend/RoomRental.Api
npm --prefix frontend run build
```

Kiểm thử tích hợp, **chỉ với database Development dùng thử** và backend đang chạy:

```sh
python3 -m unittest discover -s tests -v
```

Fixture webhook chỉ chạy khi chỉ định `TEST_SQLITE_DB` đến đúng SQLite của API localhost; xem `tests/README.md`. Test tạo dữ liệu chủ trọ/dãy/phòng/khoản thu ngẫu nhiên, không chạy vào database vận hành. Dữ liệu demo hiện có cả dữ liệu từ các lần test trước, không tự dọn bằng cách xóa database.

Docker: sao chép `.env.example` thành `.env`, thay mật khẩu/URL phù hợp rồi `docker compose up --build -d`. Production dùng PostgreSQL và frontend cùng origin, không bật demo. Phải cấu hình HTTPS và IP reverse proxy thực trong `TRUSTED_PROXIES` khi truy cập qua tên miền/LAN. Không tin mọi proxy.

### Kết quả kiểm tra đã có

- OTP SMTP: API/migrator build0 warning/error; dependency audit không có cảnh báo; frontend build và i18n929 ×4 pass. Service113 assertions; OTP HTTP SQLite10/10, PostgreSQL3/3; SQLite nghiệp vụ59/59. Migration đọc cả snapshot cũ/chứa bảng OTP, source read-only. Chi tiết mục36.

- Đợt mới nhất backend/security/ops: build API 0 warning/error; frontend không còn warning chunk/circular, lớn nhất 484,95 kB; i18n 908 × 4 pass. SQLite 59/59; PostgreSQL 55 methods (48 pass, 7 skip) + fixture class skip. Service 81 assertions; health 200→503→200 trên DB riêng và 7 ca backup giả lập pass. Xem mục 35.

- MVP: backend build 0 lỗi/0 cảnh báo; frontend build thành công.
- Build mới nhất: `dotnet build backend/RoomRental.Api` pass với 0 warning/0 error. Full-suite trên DB dùng thử: SQLite 38/38 pass; PostgreSQL clone chạy 34 test, 32 pass và hai fixture chỉ dành cho SQLite được skip. Reports/archive riêng 6/6 pass, bao gồm ba loại Excel và kiểm tra chống formula injection.
- Docker Production: build, khởi động, admin bootstrap đăng nhập; giữ dữ liệu/phiên khi dựng lại với cùng volume.
- Audit NuGet và npm ở đợt MVP không phát hiện lỗ hổng theo nguồn kiểm tra lúc đó.
- Đợt sửa FE mới nhất: TypeScript và production build thành công; vẫn có cảnh báo kích thước chunk Ant Design dùng chung.
- Reports ở khung 375px không tràn ngang; các luồng draft, correction/audit và archive/restore đã được kiểm tra trực quan. Source audit cuối vẫn do root thực hiện.
- Đã kiểm tra trực quan các màn hình chủ trọ, admin, auth và portal khách; thử các chiều rộng 320, 390, 447, 768, 992, 1280px, form tạo dãy/phòng, chi tiết hóa đơn, link khách hiện có, drawer và phân trang thẻ.
- Không có thanh toán ngân hàng thật trong các lần kiểm tra. Không suy ra kết quả giao dịch thật từ fixture/webhook dùng khóa giả.
- Docker QA trước đó đã dừng và dọn riêng các tài nguyên QA. Các sửa FE mới nhất đã kiểm tra bằng Vite và production build; **chưa dựng lại image Docker sau đợt sửa FE**.

## 8. Việc còn lại và thứ tự đề xuất

Đây là kế hoạch tiếp tục, không phải các việc đã hoàn thành hay một yêu cầu mới được người dùng giao.

1. **Hoàn tất review local hiện tại:** cấu hình Gmail SMTP theo `email-otp-setup.md`, người dùng tự nhập mật khẩu ứng dụng vào file local bị Git bỏ qua, rồi UAT email thật; root đang làm source audit cuối; xem lại thay đổi và trạng thái trước khi kết thúc. Chưa push/deploy. Sau đó UAT cùng người dùng: chủ trọ mới đăng ký/chờ duyệt, tạo dãy/phòng, thêm khách, chốt điện, xem báo cáo/xuất Excel, lưu trữ/khôi phục, mở link khách và kết thúc lượt thuê.
2. **Kiểm chứng payOS thật:** HTTPS callback origin đã có, nhưng VPS chưa có khóa payOS/owner settings và chưa thực hiện giao dịch thật. Chỉ thêm khóa do chủ trọ cung cấp và thử checkout/webhook/đối soát khi có sự đồng ý rõ ràng; kiểm tra webhook lặp, sai số tiền, mất callback và giao dịch hủy/hết hạn. Không tạo giao dịch thật chỉ để smoke test hạ tầng.
3. **Hoàn thiện vận hành công khai:** migrations versioned, backup Drive mã hóa và một restore drill đã xong; tiếp theo siết SSH, giảm quyền DB/tách migration deployment identity, hoàn thiện alert/giám sát và restore drill định kỳ. Caddy HTTPS, redirect và proxy trust đã cấu hình. PostgreSQL vẫn là container một máy, không phải HA. Xem mục51–53 và security review.
4. **Tối ưu FE tiếp theo nếu cần:** giảm bundle thư viện dùng chung, bổ sung kiểm thử UI cho luồng có rủi ro thực tế. Không thay stack hoặc viết lại giao diện chỉ để tối ưu cảnh báo build.
5. **Camera/AI khi người dùng yêu cầu:** tạo luồng chụp công tơ, backend gọi API AI (Gemini là hướng đã ghi trong ghi chú), trả chỉ số đề xuất; kiểm tra độ rõ/ngưỡng/chỉ số trước; người dùng xác nhận hoặc chủ trọ xử lý trường hợp bất thường. Chọn/xác minh model và hợp đồng API tại thời điểm triển khai. AI không được tự xác nhận thanh toán; thuật toán tính tiền vẫn nằm ở backend.
6. **Nghiệp vụ mở rộng theo yêu cầu riêng:** quyết toán giữa tháng, chia kỳ theo lượt thuê, điều chỉnh hóa đơn đã phát hành, tiền cọc/hợp đồng, chỉ số nước, công tơ chung, hoàn tiền, hạn thanh toán tùy chỉnh.

## 9. Giới hạn cần nhớ

- Có thể bắt đầu lượt thuê mới trong cùng tháng sau khi hoàn tất chuyển đi của lượt trước; hóa đơn cuối kỳ ghi ngày kết thúc đã chọn. Chưa có báo cáo phân bổ doanh thu theo ngày cho nhiều lượt thuê trong cùng kỳ.
- Hạn thanh toán mặc định: ngày 5 tháng sau; chưa có UI cấu hình riêng.
- Không có sửa/xóa tự do hóa đơn đã phát hành; chỉ dùng correction/publish có điều kiện và audit. Chưa có hợp đồng điện tử, hóa đơn thuế, hoàn tiền tự động, chỉ số nước hoặc chia công tơ.
- Schema dùng `EnsureCreated`, chưa có quy trình migration vận hành hoàn chỉnh.
- Mất khóa DataProtection có thể làm mất khả năng giải mã cấu hình payOS/link khách dù database vẫn còn.
- Không xóa database hoặc chạy `docker compose down -v` để giải quyết lỗi nâng cấp nếu cần giữ dữ liệu.
- Hướng dẫn và bằng chứng chuyển đổi SQLite → PostgreSQL cục bộ ngày 13/10/2026 nằm ở mục 25 và [sqlite-to-postgres-migration.md](sqlite-to-postgres-migration.md). Đó là import một lần đã đối chiếu, chưa thay thế schema migrations hoặc restore drill vận hành.
- Trên macOS, thư mục `Data` chứa mã nguồn và `data` có thể trùng nhau do filesystem không phân biệt hoa/thường. Dữ liệu SQLite đã tách sang `.local-data`; giữ cách tách này.

## 10. Hướng dẫn agent tiếp theo

1. Đọc tài liệu này, yêu cầu mới nhất của người dùng và mã liên quan trước khi làm; không làm lại những bước đã hoàn thành.
2. Kiểm tra tiến trình/cổng và database đang dùng; giữ nguyên dữ liệu, khóa mã hóa và cấu hình người dùng.
3. Giữ phạm vi nhập điện thủ công cho đến khi được yêu cầu triển khai camera/AI.
4. Nếu sửa UI, xem giao diện thực trong khung trình duyệt hẹp của người dùng, không chỉ dựa vào build hoặc screenshot desktop. Kiểm tra menu và màu chữ trong nút; dùng độ rộng vùng nội dung khi quyết định bảng/thẻ.
5. Nếu sửa thanh toán/phân quyền/tính tiền, chạy kiểm tra phù hợp và giữ các điều kiện xác minh phía máy chủ; không báo “đã thanh toán” từ redirect hoặc từ trạng thái UI.
6. Sau khi làm xong, cập nhật mục 1, 3, 7, 8 và ghi rõ kiểm tra nào đã chạy, giới hạn nào còn tồn tại. Không biến kế hoạch dự kiến thành kết quả đã đạt.


## 11. Nghiên cứu evondevKit — 12/10/2026

Nguồn: https://github.com/evondev/evondevKit . Snapshot mã đã đọc: `d2ac1a1fbfea4cf339e6f392030c3697aa203185`; plugin metadata ở snapshot ghi phiên bản `0.3.24`. Không dùng thư mục `archive/` làm hướng dẫn hiện hành.

### Kết luận

**Có thể áp dụng cho việc chuẩn hóa và rà soát FE; phù hợp với các màn dashboard, danh sách, bảng, form, settings và modal của dự án.** Đây là skill/hướng dẫn cho AI agent kèm mẫu và script đo UI, không phải một ứng dụng quản lý trọ hoàn chỉnh hoặc một thư viện component cần thay Ant Design.

- Skill có quy tắc phát hiện stack, dùng lại component thư viện hiện có (bao gồm Ant), giữ token/màu của dự án, dùng được với CSS thuần.
- README có hướng dẫn dùng với Codex qua skill `ui-ux`; tác giả ghi được thử nghiệm nhiều nhất trên Claude và vẫn ở beta. Chưa kiểm chứng thực thi skill trong phiên Codex của dự án này.
- Có design system gồm bảy nhóm cơ bản: button, badge, input, card, list row, modal và empty state; có nhánh review UI hiện có, giữ brand, refactor giữ hình và dựng luôn không wireframe.
- Script `probe.mjs` dùng Playwright, đo và chụp nhiều chiều rộng; có sweep tìm lỗi giữa các breakpoint, phép đo tương phản/chữ/tràn và kiểm tra lớp nổi.
- License MIT; nếu sao chép/phân phối nội dung đáng kể của kit, giữ thông báo bản quyền và giấy phép.

### Cách áp dụng đề xuất cho dự án

1. Dùng skill trong phạm vi project nếu người dùng yêu cầu cài/áp dụng; không tự cài toàn máy. Có thể ghim snapshot để dễ đối chiếu thay đổi.
2. Chốt hệ thiết kế chung bằng ConfigProvider Ant Design, CSS variables hiện có và các component chia sẻ. Giữ brand “nhà mình”, tiếng Việt và các mức phí/tiền theo hợp đồng API.
3. Thử trước một route `/rooms`: hành động tạo phòng, thêm khách và cấp link phải rõ; trạng thái trống/loading/error và form nhỏ phải sử dụng được.
4. Khi đạt yêu cầu, áp dụng cùng quy tắc cho `/buildings`, `/readings`, `/invoices`, dashboard, cấu hình thanh toán và portal khách.
5. Kiểm tra giao diện và hành vi thật sau mỗi thay đổi, không chỉ chấm điểm hình ảnh. Giữ API, auth gate, query cache, idempotency và xác minh thanh toán đang hoạt động.

### Điểm cần điều chỉnh khi tích hợp

- Skill chỉ xử lý giao diện; tài liệu cho phép để handler/logic thành placeholder. Với app hiện có, phải giữ/nối đầy đủ handler thật, không thay luồng đang hoạt động bằng ví dụ tĩnh.
- Mặc định có bước duyệt brief/chọn wireframe; nếu người dùng muốn làm ngay thì dùng nhánh “dựng luôn, không wireframe”, theo phạm vi đã được giao.
- Probe mở Chromium riêng và có hành vi tự bấm menu/nút tạo form. Mã có bộ lọc loại nút thao tác nguy hiểm nhưng không coi đây là đảm bảo tuyệt đối. Riêng nút “Tạo hóa đơn còn thiếu” của app gọi API trực tiếp, không chỉ mở form; phải loại khỏi lượt tự bấm hoặc dùng dữ liệu Development dùng thử.
- Bộ probe hiện không thấy tùy chọn CLI nạp phiên đăng nhập của app. Route chủ trọ/admin cần phiên xác thực trước khi đo; nếu không có sẽ kiểm tra nhầm trang login. Link portal khách là bí mật, không đưa vào báo cáo công khai.
- Trong phiên Codex này, browser UI phải dùng công cụ browser được cung cấp. Muốn áp dụng phép đo của probe thì thực hiện tương đương qua công cụ đó; không tự chạy Playwright bên ngoài để điều khiển browser trong phiên này.
- Các quy tắc về gu như flat, bóng, icon, padding là đề xuất thiết kế; không tự động kết luận chúng là lỗi nghiệp vụ hoặc lỗi khả dụng.

**Trạng thái tại thời điểm nghiên cứu:** chưa cài/chưa áp dụng. Sau khi người dùng yêu cầu thực hiện, đã cài bằng skill-installer vào project và triển khai pilot; thông tin hiện tại nằm ở mục 12. Không chạy npx skills add và không chạy probe.mjs.

Nguồn chi tiết: `README.md`, `skills/ui-ux/SKILL.md`, `references/review.md`, `references/responsive.md`, `references/system.md`, `scripts/probe.mjs`, `LICENSE` trong repo nguồn.


## 12. Áp dụng evondevKit: pilot Phòng & người thuê — hoàn thành 12/10/2026

### Cài đặt

- Dùng helper của skill-installer, cài `skills/ui-ux` từ repo evondev/evondevKit vào **`.agents/skills/ui-ux`** của project, không cài global.
- Ghim commit `d2ac1a1fbfea4cf339e6f392030c3697aa203185`. Có `SOURCE.json` ghi nguồn/commit và `LICENSE` MIT đi kèm.
- Skill có thể được phát hiện ở lượt sau; agent đã đọc SKILL.md để áp dụng ngay trong lượt này.
- Đi theo hướng dựng trực tiếp cho route đã được người dùng đồng ý, giữ brand và các handler hiện có; không dựng wireframe hoặc thêm route design-system.

### Những gì đã đổi

- **/rooms**: tiêu đề Phòng & người thuê, nút Tạo phòng trọ duy nhất làm hành động chính; bộ lọc có nhãn ngay trên danh sách, số đếm theo dãy đang lọc; bỏ banner hướng dẫn và strip thống kê lặp.
- Desktop: bảng gọn với tên phòng/dãy, người thuê, tiền/tháng, trạng thái và thao tác. Nút cấp link hoặc thêm người thuê luôn hiện; thao tác phụ trong nút ba chấm có aria-label.
- Khung hẹp: dòng phòng có tên/trạng thái ở đầu, người thuê/điện thoại, giá thuê và hành động cuối. Cùng một khung danh sách, không lồng nhiều card viền bên trong.
- Có Xóa lọc khi không tìm được phòng; query dãy cũ hiện “Dãy không còn tồn tại”. Bộ lọc dãy đọc từ URL để đồng bộ khi điều hướng; tạo phòng chỉ điền sẵn dãy hợp lệ.
- Form và modal giữ các field/validation/API hiện có, font nhập 16px trên điện thoại, thân cuộn riêng, nút lưu/hủy vẫn hiện; form không đóng khi bấm mask.
- Link hiện có mở/sao chép được; thu hồi là thao tác nguy hiểm riêng có xác nhận, mặc định focus Hủy. Không thu hồi link trong lần kiểm tra.
- Giữ màu xanh Nhà Mình, DM Sans cho workflow này; dùng accent tối hơn cùng họ màu để chữ trắng đọc rõ. Không thêm dark mode.
- Thêm **Ant Design App provider** ở main để App.useApp có API message/modal hoạt động, và context theo theme ở trang phòng. Đây là sửa tích hợp cần thiết để toast sau lưu/copy và các hộp xác nhận chạy được.
- ResponsiveTable có slot renderCompact tùy chọn cho trang phòng; các trang khác vẫn dùng cách render cũ.

### Hợp đồng thiết kế và vị trí sửa

- `frontend/src/design/roomTheme.ts`: token Ant Design cho pilot; nút/ô nhập 40px desktop và 44px mobile, radius 12px, một font, một accent xanh.
- `frontend/src/pages/RoomsPage.css`: bố cục, viền/padding, màu phụ/badge, khung thẻ, modal và thanh cuộn 4px trong workflow. CSS có scope để không đổi phong cách các trang khác.
- `frontend/src/pages/RoomsPage.tsx`: render trang/table/thẻ/forms và giữ các mutation thực.
- `frontend/src/components/ResponsiveTable.tsx`: slot renderCompact, giữ logic đo vùng nội dung và phân trang.
- `frontend/src/main.tsx`: provider App của Ant Design ở gốc.
- Giữ focus bàn phím rõ theo quy ước đã có của dự án; không áp quy tắc giấu focus của kit. Chưa đổi font toàn app hoặc sidebar của Nhà Mình.

### Kiểm tra đã chạy

- `npm run build --prefix frontend`: TypeScript và production build thành công. Vite vẫn cảnh báo chunk thư viện dùng chung; Ant App provider được dùng thật nên code message/modal nằm trong bundle chung.
- Giao diện thực ở 320, 375, 768, 1024, 1280, 1440, 1920px; quét **55 mức** từ 1440 xuống 375. Phép đo: chiều rộng trang, control vượt viewport, control nhỏ dưới 32px. Không tìm thấy lỗi theo các phép đo này.
- Đo 33 vùng chữ hiện của trang phòng ở 375px bằng màu foreground/background trong DOM: không thấy tỉ lệ dưới 4.5:1. Phép đo này không thay thế kiểm tra đầy đủ opacity, overlay và mọi trạng thái.
- Kiểm tra lọc Còn trống / Xóa lọc, trạng thái rỗng từ query dãy không tồn tại, menu sửa phòng, form tạo/nhận khách, lỗi bỏ trống tên phòng, sao chép link và mở xác nhận thu hồi rồi hủy.
- Lưu lại phòng P101 với **đúng các giá trị đang có**, backend nhận thành công và hiện toast. Không tạo thêm phòng, không đổi mức phí hoặc người thuê, không thu tiền hoặc phát hành hóa đơn trong phiên này.
- Xem lại trang điện và hóa đơn ở màn hẹp để xác nhận renderer mặc định của ResponsiveTable vẫn hoạt động.
- Đã đặt lại kích thước viewport, đóng modal/bộ lọc thử và để tab demo ở **http://localhost:5173/rooms**.

### Minh chứng và giới hạn

- Ảnh: `artifacts/evon-rooms-before.png`, `artifacts/evon-rooms-desktop.png`, `artifacts/evon-rooms-current.png`.
- Đo DOM: `artifacts/evon-rooms-measurements.json`.
- **Không chạy script probe.mjs gốc**: các thao tác browser của phiên này phải qua công cụ browser Codex. Đã làm phép đo/sweep và kiểm tra thủ công bằng công cụ đó; không tuyên bố đã chạy toàn bộ checker upstream hoặc danh sách mã P của kit.
- Không có phiên kiểm tra mới backend/PostgreSQL/Docker trong đợt thay FE này. Các kết quả MVP trước vẫn là lịch sử, chưa chứng minh image Docker đã chứa FE mới.
- Chưa kiểm tra đầy đủ người thuê/tên phòng rất dài, danh sách nhiều trang, lỗi mạng khi lưu, workspace mới chưa có dãy. Các component xử lý đã có trong mã nhưng cần bổ sung UAT nếu tiếp tục.
- Pilot hoàn tất; bước kế tiếp là lấy phản hồi trang phòng rồi áp dụng cùng hệ vào dãy, hóa đơn, điện, dashboard, settings và portal khách theo yêu cầu mới. Tiếp tục giữ phạm vi AI hoãn và payOS thật chưa xác minh.


## 13. Webhook payOS local đã đăng ký — 12/10/2026

- Người dùng đã lưu khóa kênh payOS và yêu cầu dựng tunnel để test.
- Đã cài cloudflared 2026.9.3 qua Homebrew. Gateway `scripts/payos_webhook_gateway.py` nghe tại `127.0.0.1:5081`, chỉ chuyển POST JSON tối đa 64 KiB cho đúng chủ trọ về API local port 5080. Không chuyển cookie, header xác thực hay Origin của người gọi. Các endpoint quản trị và owner khác không được chuyển.
- Tunnel hiện tại: `https://lane-warned-pork-soldiers.trycloudflare.com`.
- Webhook đã đăng ký: `https://lane-warned-pork-soldiers.trycloudflare.com/api/payments/webhook/3b50eed2-9eae-4833-b508-9eca4c4d4200`.
- Đã gọi API chính thức `POST https://api-merchant.payos.vn/confirm-webhook` bằng cấu hình chủ trọ đã lưu, giải mã trong bộ nhớ; không in hoặc ghi khóa plaintext. payOS trả HTTP 200, code `00`, desc `success`. Đây là xác nhận webhook, không phải giao dịch trả tiền thật.
- Kiểm tra qua HTTPS: GET `/api/auth/me` trả 404; POST webhook owner khác trả 404; POST webhook đúng owner thiếu chữ ký trả 401. Gateway local cũng đã kiểm tra cùng các trường hợp và GET callback trả 404.
- Sau xác minh, hai hóa đơn hiện có vẫn tổng 8.260.000 VND, Paid=0, ProcessedWebhooks=0 cho chủ trọ này. Giao dịch mẫu không tạo khoản thu.
- FE hỗ trợ biến `VITE_WEBHOOK_BASE_URL` để hiển thị/copy đúng origin webhook khi UI vẫn dùng localhost. Giá trị phiên này trong `frontend/.env.local` (gitignore). `frontend/.env.example` có hướng dẫn. npm run build --prefix frontend đã pass; cảnh báo kích thước bundle cũ còn tồn tại.
- Các tiến trình đang chạy trong phiên: gateway exec session 92271; cloudflared session 49064. Không cài dịch vụ khởi động cùng máy. Dừng tiến trình hoặc tắt máy sẽ mất webhook; URL Quick Tunnel đổi khi chạy lại. Không dùng URL này cho production.
- Khởi động lại gateway: `/usr/bin/python3 scripts/payos_webhook_gateway.py --owner-id 3b50eed2-9eae-4833-b508-9eca4c4d4200`.
- Khởi động tunnel: `cloudflared tunnel --url http://127.0.0.1:5081 --no-autoupdate --no-prechecks`. Bước kết nối đã mất khoảng một phút; precheck từng báo timeout nhưng tunnel sau đó kết nối QUIC thành công. Không kết luận mạng bị chặn chỉ từ precheck.
- Khi đổi URL tunnel, cập nhật frontend/.env.local và đăng ký/xác minh lại URL trên payOS. Khi build production, bỏ biến tunnel tạm thời hoặc thay bằng origin API ổn định.
- Bước còn lại: người dùng thanh toán một hóa đơn thử số tiền nhỏ rồi kiểm chứng khoản thu/hóa đơn. Chưa tạo hóa đơn thử mới, chưa tạo checkout, chưa chuyển tiền. Return URL hiện vẫn là localhost:5173; phù hợp test trên chính máy này, chưa phù hợp khách dùng điện thoại khác.


## 14. Tự đăng ký/xác minh webhook khi lưu payOS — 12/10/2026

- Theo yêu cầu người dùng, PUT /api/settings/payment giờ lưu/commit khóa rồi tự POST API chính thức /confirm-webhook. Không giữ transaction DB khi gọi HTTP để webhook mẫu có thể đọc khóa vừa lưu.
- PaymentWebhookService lấy URL từ cấu hình server WebhookBaseUrl, mặc định PublicBaseUrl/APP_PUBLIC_URL. Chỉ chấp nhận HTTPS origin; không lấy Host header, URL từ client hoặc frontend. Production dùng WEBHOOK_BASE_URL qua compose; development dùng appsettings.Development.local.json (gitignore).
- Giá trị local hiện tại vẫn là origin tunnel tại mục 13. FE đã bỏ VITE_WEBHOOK_BASE_URL; URL được trả bởi backend để hiển thị thống nhất.
- Thêm bảng PaymentWebhookRegistrations, keyed OwnerId, lưu URL/status/time/error/version hash. DatabaseInitializer tạo riêng bảng mới nếu chưa có trên SQLite/PostgreSQL; không đổi/xóa các bảng hay dữ liệu tài chính cũ. Đây là nâng cấp cộng thêm cụ thể, chưa thay thế nhu cầu migrations đầy đủ.
- GET settings trả webhookUrl, webhookStatus, webhookVerifiedAt, webhookError. Kết quả cũ không hợp lệ khi phiên bản khóa hoặc URL khác hiện tại. Khóa bí mật vẫn không được trả ra FE.
- POST /api/settings/payment/verify-webhook cho thử lại bằng bộ khóa đã lưu; có owner/approval/origin checks và rate limit. Hỏng mạng, timeout, dữ liệu payOS sai, URL không công khai đều báo chưa xác minh; không mất khóa đã lưu và không báo thành công giả.
- UI nút Lưu và xác minh payOS; spinner trong lúc chờ; cảnh báo/đã xác minh phản ánh DTO; Xác minh lại webhook không cần nhập lại khóa. Bỏ trống API/Checksum key giữ khóa cũ khi đã có cấu hình.
- Kiểm chứng thật: đã bấm Lưu và xác minh payOS trên tài khoản đang đăng nhập, giữ nguyên client/khóa hiện có; payOS xác nhận thành công. Tải lại trang vẫn hiện Verified. Ảnh artifacts/payos-auto-webhook-settings.png. Không chuyển tiền bằng agent.
- Đã chạy 11 assertion service tests với fake HttpMessageHandler + SQLite memory: success, persistence, endpoint/header/body, failure giữ khóa, malformed, timeout, URL mismatch, key/origin stale, HTTP localhost rejection, không tạo receipt. Lệnh dotnet run --project tests/RoomRental.WebhookTests.
- HTTP suite trên API QA riêng port 5082 + DB /tmp/room-rental-webhook-qa.db: 12/12 gồm fixture chữ ký/replay. WebhookBaseUrl QA = http://localhost:5082 để không gửi fake keys tới payOS. Test README đã cập nhật điều kiện này.
- Build backend: 0 warning/error. FE TypeScript/Vite build pass (cảnh báo bundle lớn cũ còn). Docker compose config --quiet pass với biến fixture; không rebuild image hoặc chạy PostgreSQL mới trong đợt này.
- Sau đó đọc dữ liệu local thấy PayOsAttempts Paid 30.000 VND và tổng Paid=30.000 VND; trước đợt này chỉ có 2 hóa đơn chưa trả, giờ có 3 hóa đơn. Agent không tạo thêm hóa đơn/checkout/receipt; đây là thay đổi sử dụng đồng thời bên ngoài thao tác xác minh của agent, không tự nhận đã thực hiện chuyển tiền. Chưa kiểm thử đầy đủ hủy/hết hạn/mất webhook trên giao dịch thật.
- Backend đang chạy phiên mới exec 8358, Development port 5080, DB/khóa cũ được giữ. Gateway 92271 và tunnel 49064 vẫn chạy. Khi đổi URL tunnel, sửa backend appsettings.Development.local.json rồi bấm Xác minh lại webhook; không cần sửa FE nữa.


## 15. Tiền cọc riêng cho từng khách — 12/10/2026

- Người dùng chọn nhập cọc khi nhận khách mới, không mặc định theo giá phòng. Phòng & người thuê → Thêm người thuê → Tiền cọc yêu cầu; 0 là không thu. Số nguyên VND, từ 0 đến 2.147.483.647.
- Start occupancy tạo lượt thuê và hóa đơn Kind=Deposit trong cùng transaction nếu số tiền >0. Đây là khoản phải thu, Paid ban đầu=0. Monthly/Deposit có unique index riêng nên cùng phòng/tháng vẫn tạo được hóa đơn tiền phòng bình thường.
- Link khách hiện tại chứa cả cọc và hóa đơn tháng, dùng checkout/webhook/đối soát payOS hiện có. Chủ trọ có thể ghi nhận tiền mặt/chuyển khoản ngoài hệ thống bằng chức năng thu tiền thủ công. Cọc chưa được kiểm chứng bằng giao dịch tiền thật trong phiên này.
- Trang phòng hiển thị cọc yêu cầu/đang giữ; menu phòng có Thu / quyết toán cọc. Trang Hóa đơn thêm bộ lọc Tiền cọc; loại này lấy mọi kỳ, không dùng nút phát hành hóa đơn tháng.
- InvoiceDto thêm kind, occupancyId, depositHeld và depositSettlements. OccupancyDto thêm depositInvoiceId, depositRequired, depositPaid, depositHeld. Held = Paid - tổng đã hoàn/khấu trừ, không làm giảm Paid lịch sử.
- DepositLedger ghi nhận hoàn cọc hoặc khấu trừ, có lý do và idempotency GUID. Hoàn cọc chỉ là **ghi nhận đã trả tiền bên ngoài**, không tự chuyển tiền ngân hàng. Khấu trừ có thể trừ vào hóa đơn tháng còn nợ cùng lượt thuê; tạo receipt Source=Deposit và cập nhật công nợ trong cùng transaction, không ghi nhận hai lần khi gửi lại.
- POST /api/invoices/{id}/deposit-settlements kiểm tra owner/approval/origin, số tiền không vượt cọc đang giữ, target phải cùng owner/lượt thuê, không vượt nợ và chặn khi có giao dịch online chờ xử lý. Khác payload cùng key trả 409. End occupancy bị chặn nếu còn cọc đang giữ; phải quyết toán hết trước.
- Dashboard tổng tiền tính hóa đơn Monthly, tránh cộng tiền cọc vào doanh thu tháng rồi cộng lại khi khấu trừ. Recent invoice có nhãn cọc riêng.
- DatabaseInitializer nâng cấp cộng thêm Kind mặc định Monthly, DepositSettlements và filtered unique indexes trên SQLite/PostgreSQL. Đã sao lưu DB đang dùng vào backend/RoomRental.Api/.local-data/pre-deposit-upgrade.db trước nâng cấp. SQLite bản cũ và DB thực sau nâng cấp đều giữ 32 hóa đơn, Total=124.665.000, Paid=6.850.000; không tạo dữ liệu QA trong DB thực.
- Build backend 0 warning/error; frontend TypeScript/Vite pass, còn cảnh báo bundle lớn cũ. HTTP suite trên SQLite QA riêng port 5082: **15/15 pass**, gồm cọc 0/invalid, cọc + hóa đơn tháng, thu/hoàn/khấu trừ, idempotency, owner scope, chặn chuyển đi, webhook chữ ký cho cọc và replay. Service webhook 11 assertions pass.
- UI QA trên 127.0.0.1:5174 dùng DB riêng: nhập cọc 1.500.000 vào form (không lưu khách mới); ghi khấu trừ 10.000 từ cọc vào hóa đơn tháng, Held từ 250.000 xuống 240.000 và nợ tháng từ 50.000 xuống 40.000; link khách hiển thị đúng. Ảnh artifacts/deposit-per-tenant.png và artifacts/deposit-ledger.png, đều dữ liệu giả QA.
- Chưa kiểm tra PostgreSQL đợt này: Docker daemon không chạy. Chưa hỗ trợ thêm/sửa mức cọc sau khi lượt thuê đã được tạo, tự chuyển tiền hoàn cọc hoặc sửa/xóa quyết toán; cần yêu cầu mới nếu muốn bổ sung. Không mặc định cọc cho khách cũ.
- Backend thực hiện chạy session 75257 port 5080, FE 5173; gateway 92271 và cloudflared 49064 giữ nguyên. Các server QA và tab QA được dừng/đóng sau kiểm thử. Khi restart tunnel, cập nhật origin server rồi Xác minh lại webhook như mục 14.


## 16. Nhân bản phòng — 12/10/2026

- Phòng & người thuê → menu ba chấm của phòng → Nhân bản phòng. Có trên cả desktop/card mobile, phòng trống hoặc đang thuê.
- Dùng form tạo phòng hiện có với mode duplicate: sao chép buildingId, rent, electricityRate, waterFee, waterPerPerson, serviceFee; tên mặc định thêm (bản sao), tăng suffix nếu tên này đã có cùng dãy. Rút ngắn tên gốc để gợi ý vừa giới hạn input 80 ký tự. Có thể sửa tất cả trước khi Tạo phòng.
- Chỉ số công tơ mới mặc định 0, cần nhập số thực của phòng mới. Không sao chép người thuê/token, tiền cọc, hóa đơn/công nợ hoặc lịch sử điện. Gọi POST /api/rooms hiện có; backend scope/validation giữ nguyên, không cần endpoint nhân bản riêng.
- Không đổi CSS/theme: React19/Ant5, form và menu hiện có. Áp dụng ui-ux cho thay đổi component nhỏ. Build frontend pass, vẫn còn cảnh báo chunk lớn cũ.
- Đã tạo bản sao qua UI trên 127.0.0.1:5174/rooms với DB QA /tmp/room-rental-deposit-qa.db. HTTP read kiểm chứng id mới, 6 thiết lập khớp, occupancy=null, baseline=0, không có hóa đơn. Mở nhân bản lần hai thấy tên (bản sao 2), hủy không lưu. Không tạo phòng trên dữ liệu chủ trọ thực. Ảnh artifacts/room-duplicate.png dùng dữ liệu QA.
- Server/tab QA đóng sau kiểm thử. API thực 5080/FE5173 tiếp tục chạy. UI chung đã được kiểm tra responsive ở đợt pilot; đợt này không chạy lại toàn bộ breakpoint sweep.


## 17. Hóa đơn bổ sung từ menu phòng — 12/10/2026

- Menu ⋯ thêm Tạo hóa đơn bổ sung trên desktop/card. Phòng phải có lượt thuê hoạt động; mục bị vô hiệu ở phòng trống. Form: nội dung khoản thu (tối đa 500 ký tự), số tiền nguyên VND 1..2147483647, hạn thanh toán (mặc định hôm nay), nút Tạo hóa đơn.
- POST /api/rooms/{id}/supplemental-invoices body {requestId,occupancyId,description,amount,dueDate}. Owner authorization/approval/origin checks hiện có. OccupancyId phải là khách đang ở đúng phòng, tránh tạo cho khách khác khi dữ liệu màn hình đã cũ. Invoice Kind=Supplemental, Paid=0, Period=tháng phát hành, snapshot phòng/dãy/khách. Có thể tạo nhiều khoản bổ sung cùng phòng/tháng.
- requestId GUID dùng làm Invoice.Id để chống gửi lại: cùng payload trả hóa đơn cũ; khác payload/mismatched scope trả 409. Client giữ requestId trong lúc form đang mở và retry. Nếu đóng/mở form sau lỗi mất mạng thì requestId mới; cần kiểm tra danh sách trước khi tạo lại nếu chưa rõ lần trước đã thành công hay chưa. Không hỗ trợ chỉnh/xóa hóa đơn đã tạo trong đợt này.
- Thêm Description nullable vào Invoice, DTO và model; initializer nâng cấp SQLite/PostgreSQL cộng thêm cột, không đổi hóa đơn cũ. Backup DB thực .local-data/pre-supplemental-upgrade.db. Sau nâng cấp DB vẫn 32 hóa đơn, Total124665000/Paid6850000 như trước. Không phát hành hóa đơn thử trên DB thực.
- Trang Hóa đơn có bộ lọc Hóa đơn bổ sung (mọi kỳ); mở deep link tự chọn đúng loại. Drawer và portal khách có nội dung/số tiền thay cho breakdown điện/nước. Portal dùng link lượt thuê hiện có; checkout, thu thủ công, đối soát/webhook dùng luồng chung. Dashboard cộng Monthly+Supplemental, tiếp tục loại Deposit khỏi tổng để tránh đếm hai lần. Khấu trừ cọc vào hóa đơn vẫn chỉ hỗ trợ Monthly ở thời điểm này.
- Build backend 0 warning/error, FE TypeScript/Vite pass (cảnh báo chunk cũ). SQLite QA full suite **17/17 pass**: validation/phòng trống/scope, request replay/conflict, nhiều hóa đơn/tháng, cùng tồn tại Monthly, guest access, manual receipt, stale ended occupancy và signed supplemental webhook replay không ảnh hưởng Monthly.
- UI QA: menu → tạo phí sửa khóa 120000 → toast thành công; link khách hiện hóa đơn bổ sung, chi tiết nội dung và hạn thanh toán; bộ lọc/drawer được kiểm tra. Ảnh artifacts/supplemental-invoice-form.png và artifacts/supplemental-invoice-detail.png dùng dữ liệu QA.
- PostgreSQL chưa chạy trong đợt này (Docker daemon chưa sẵn sàng), chưa thanh toán bổ sung bằng tiền thật. Không chạy lại toàn bộ responsive sweep vì dùng menu/form/renderer có sẵn; giữ theme Ant/roomTheme.
- Backend thực restart session40135 ở port5080, FE5173 giữ nguyên. Gateway92271/tunnel49064 tiếp tục. QA38895/27901 và tab QA đóng sau kiểm tra.


## 18. Trả phòng & quyết toán — 12/10/2026, bổ sung guard ngày cuối kỳ 13/10/2026

- Phòng & người thuê → ⋯ → Trả phòng & quyết toán (thay Ghi nhận chuyển đi trong UI). Drawer 780px, co theo viewport, bốn bước: chốt điện/khoản còn lại → thu nợ/khấu trừ → ghi nhận hoàn cọc → kết thúc lượt thuê. Giữ Ant/roomTheme, không đổi brand/dark mode. Chọn bố cục tuần tự theo yêu cầu triển khai của người dùng.
- GET /api/rooms/{id}/moveout trả occupancyId/tenantName/roomName/electricityRate/meterBaseline/invoices/pendingInvoiceIds. UI khóa vào occupancy khi mở từ phòng, báo đóng/mở lại nếu khách đã đổi trong lúc thao tác.
- POST `/api/rooms/{id}/final-invoice` nhận requestId, occupancyId, currentReading, additionalAmount, description, optional `endDate` và các dòng phí itemized. Phải đúng lượt thuê đang hoạt động. Meter baseline = max InitialReading, tất cả chỉ số tháng và hóa đơn chốt cuối của phòng (qua các lượt thuê). Chỉ số tối đa 1e9, 3 chữ số thập phân; các khoản VND phải là số nguyên trong giới hạn. `endDate` bỏ trống dùng ngày Việt Nam hiện tại; ngày chọn phải nằm trong khoảng thuê và không đứng trước chỉ số cùng kỳ đã ghi sau đó.
- Hóa đơn cuối là Supplemental, IsFinalMeterReading=true, giữ BillingStartDate/BillingEndDate và số ngày tính phí. Nếu chưa có hóa đơn tháng, bản xem trước tính tiền thuê theo số ngày, điện, nước, dịch vụ và khoản phát sinh; nếu đã có hóa đơn tháng thì có thể điều chỉnh hóa đơn đó khi đủ điều kiện, còn invoice cuối ghi các khoản còn lại (không cộng trùng rent/water). Request kiểu cũ vẫn giữ cách tính tương thích. Tổng 0 vẫn có bản ghi chốt, Status=Paid, không có receipt giả. Chỉ một bản ghi chốt cho một lượt thuê; retry chính xác trả bản cũ, sai payload 409. Sau chốt, API nhập điện tháng chặn để tránh hóa đơn trùng.
- Deposit deduction mở rộng target từ Monthly sang Monthly/Supplemental cùng lượt thuê, vẫn transaction/held/debt/pending/idempotency guards. DepositLedger ở trang hóa đơn cũng cập nhật lựa chọn và invalidate moveout.
- Drawer hiển thị debt non-Deposit, held actual Paid trừ settled, số thu thêm/hoàn sau khấu trừ. Mỗi hóa đơn có Trừ cọc (min held/debt), Ghi nhận đã thu thêm (cho phép thu từng phần), Đối soát payOS. Hoàn cọc chỉ bật khi hết nợ, cọc vẫn còn và không có pending cọc. Đây là ghi nhận hoàn ngoài hệ thống, không chuyển tiền thật.
- POST `/api/rooms/{id}/complete-moveout` {occupancyId,finalInvoiceId}: transaction serializable, scope đúng owner/phòng/lượt thuê và final invoice; chặn hóa đơn nháp/chưa thanh toán, pending payment, cọc còn giữ và các nghĩa vụ chưa quyết toán. EndDate lấy từ BillingEndDate đã chọn; chuyển inactive, thu hồi token, cập nhật baseline công tơ. Exact retry lượt thuê đã kết thúc trả success và không tác động khách mới. Alias legacy `/end-occupancy` cũng yêu cầu hóa đơn final đã phát hành, các điều kiện quyết toán và BillingEndDate hợp lệ; không còn đường kết thúc bỏ qua hóa đơn final.
- InvoiceDto/model thêm bool IsFinalMeterReading; SQLite/PG initializer cộng cột boolean mặc địnhfalse/index unique, không thay đổi hóa đơn cũ. Đã backup DB thực .local-data/pre-moveout-upgrade.db; sau nâng cấp số hóa đơn/tổng tiền vẫn 32/124665000/Paid6850000. Không thử trả phòng trên DB thực.
- Build backend 0 warning/error; FE TypeScript/Vite pass (chunk warning cũ). Full SQLite QA **20/20 pass**: meter delta/no duplicate rent, monthly+Supplemental deduction, refund guard, pending payOS, zero final, owner scope/stale occupancy, final replay/conflict, chặn readings sau chốt, complete replay không chấm dứt khách mới, meter baseline mới115 thay vì110. Không chạy PostgreSQL đợt này (Docker daemon chưa sẵn sàng).
- UI QA trên127.0.0.1:5174: chốt 100→105 với phí50000, debt210000/held240000; trừ cọc vào tháng40000, bổ sung120000, cuối50000; ghi nhận hoàn30000; hoàn tất phòng trống. Không chuyển tiền thật. Case không cọc: điện5kWh*3500 + thêm32500 =50000; ghi nhận thu25000 hai lần, debt về0, chứng minh khóa idempotency không chặn hai khoản thu hợp lệ giống nhau sau thành công. Case thứ hai được giữ trong DB QA, không phải DB người dùng.
- Responsive ở375px: document width375, drawer client/scroll width375, không tràn ngang; chưa quét toàn bộ mọi breakpoint. Ảnh artifacts/moveout-ready.png và artifacts/moveout-mobile.png dùng dữ liệu QA. Đã reset viewport.
- Giới hạn: việc chia phí giữa hóa đơn tháng và invoice cuối kỳ tuân theo preview/itemized fields; không hỗ trợ bỏ nợ/non-Deposit writeoff trong luồng mới. Cọc chưa thu không coi là tiền đang giữ và không tự thu thêm lúc trả phòng. Chưa có biên bản xuất PDF, đổi công tơ hoặc thử thanh toán cuối kỳ bằng tiền thật.
- Backend thực port5080 session13144; FE5173, gateway92271/cloudflared49064 giữ nguyên. QA36193/48494 và tab QA đóng khi kết thúc; docs/README/api-contract cập nhật.


## 19. Chốt tên Rehouse — 13/10/2026

- Người dùng chọn tên **Rehouse** sau khi trao đổi các phương án. Đã cập nhật tên hiển thị ở sidebar/mobile drawer, đăng nhập/đăng ký, tài khoản chờ duyệt, portal khách, settings và tiêu đề tab. README đổi tiêu đề. Giữ màu/icon/font và định danh kỹ thuật/cookie/database đang có để không ảnh hưởng dữ liệu hay phiên đăng nhập.
- Đã tra cứu sơ bộ trước đó: rehouse.vn dùng tên trong lĩnh vực nội thất/nhà ở; Rehouse Group ở Anh hoạt động bất động sản. Đây chưa phải tra cứu quyền nhãn hiệu đầy đủ, chưa xác minh quyền đăng ký/sử dụng tại Việt Nam. Người dùng chốt tên; không coi quyết định này là chứng nhận pháp lý hay đăng ký nhãn hiệu/tên miền.
- Các đoạn “Nhà Mình/nhà mình” trong lịch sử bàn giao và ảnh cũ ghi nhận brand cũ, không phải tên hiện hành. Chưa thiết kế logo mới hoặc mua tên miền.

- Kiểm chứng đổi tên: frontend TypeScript/Vite build pass; giao diện local đã hiện Rehouse trong menu và title tab. Ảnh artifacts/rehouse-brand.png.

## 20. Đa ngôn ngữ toàn ứng dụng — 13/10/2026

- Người dùng chọn Việt + Anh + Trung + Hàn + Ấn và xác nhận **toàn ứng dụng**. Đã triển khai vi/en/zh-CN/ko/hi; Trung giản thể, “Ấn” = Hindi (đã báo giả định cho người dùng).
- 551 khóa giao diện, đủ 4 catalog dịch; menu, dashboard, dãy/phòng, chỉ số, hóa đơn, payOS settings, admin duyệt tài khoản, auth/account-status/not-found, tiền cọc, bổ sung, moveout và portal khách dùng chung `t`. Không thêm thư viện hoặc API dịch. Dùng bộ locale có sẵn của Ant Design/Day.js.
- Bộ chọn ngôn ngữ ở header và các trang public; lưu rehouse.language trong localStorage, mặc định Việt, vẫn hoạt động khi storage bị chặn. Chuyển ngôn ngữ giữ state component, không remount/đăng xuất. Labels/rules vốn nằm module scope đã chuyển vào render để cập nhật đúng.
- Hộp cấp link có lựa chọn ngôn ngữ khách; thêm lang vào URL hiện hành, không đổi token/quyền truy cập/thời hạn. Portal nhận lang, ghi nhớ cho lần quay về từ payOS, vẫn cho khách đổi. Các tham số payment không bị mất.
- VND giữ nguyên, chỉ đổi format; ngày hiển thị theo locale, kỳ nghiệp vụ vẫn Asia/Ho_Chi_Minh. Dữ liệu nhập và lịch sử mô tả/ghi chú không tự dịch. ApiError thông báo hiện tại lấy theo locale khi đọc; lỗi API đã biết ánh xạ guidance, lỗi mới/provider chưa biết dùng fallback status ngoài vi. Chưa đổi contract/backend error codes. Trang payOS bên ngoài không do bộ dịch Rehouse điều khiển.
- `docs/I18N.md` ghi cách bảo trì. `frontend/scripts/build-i18n.mjs` sinh JSON từ source.json/translations.tsv; `check-i18n.mjs` kiểm coverage/placeholders/UI literals/preference/URL/interpolation/errors/VND/dates. Không giữ script migration một lần.
- Kiểm tra: i18n check pass 519 khóa x4; FE build pass (chunk warning đã có). Trình duyệt đổi đủ5 ngôn ngữ ở Rooms, form/rules Anh, form Hàn, link thực mở Hindi đọc-only; portal và login Hindi375px không tràn ngang. Chưa quét mọi màn/breakpoint, chưa review bản dịch bởi người bản ngữ, chưa làm giao dịch thật đa ngôn ngữ. Không thay đổi backend hoặc dữ liệu thuê/thu tiền trong lượt này.
- Ảnh artifacts/rehouse-languages-mobile.png hiển thị form đăng nhập trống và đủ5 lựa chọn, không lộ khóa hoặc token khách. Các tab kiểm tra tạm đóng, viewport reset; lựa chọn trình duyệt đưa về Việt.

- Sửa tiếp header portal: selector `.tenant-header span` từng áp margin-top5px của phụ đề logo vào các span của Ant Select, gây lệch chữ/icon. Đổi markup copy logo thành `.tenant-brand-copy`, giới hạn các rule phụ đề tại `.tenant-brand-copy > span`. Kiểm chứng trên tab Edge tiếng Hàn: tâm theo chiều dọc của Select, selection-item và arrow đều37.5px, text margin-top0; FE build pass.


## 21. QR inline cho thanh toán payOS — 13/10/2026

- Endpoint `POST /api/tenant/{token}/invoices/{id}/payment` trả `PaymentCheckoutDto`: `checkoutUrl`, `qrCode`, `amount`, `orderCode`, `accountName`, `accountNumber`, `bankBin`, `description`, `status`, `expiresAt`. QR và thông tin tài khoản chỉ lưu sau khi kiểm chữ ký payOS cùng order/amount khớp. `expiresAt` lấy từ `expiredAt` của payOS và là Unix seconds. Các trường metadata có thể null; pending cũ vẫn tái sử dụng order/url, `qrCode` null thì portal đưa lựa chọn mở hosted checkout bằng nút rõ ràng nếu URL đúng HTTPS `pay.payos.vn`. Không tạo order thứ hai cho pending và không tự chuyển hướng.
- Portal hiển thị QR, số tiền/chủ tài khoản/nội dung/thời hạn, cho sao chép, kiểm tra thủ công và đối soát khi modal mở/tab hiển thị. Overview làm mới mỗi 5 giây, reconcile khoảng 20 giây và backoff khi lỗi; cả hai dừng khi tab ẩn/modal đóng. Generation guard bỏ response cũ sau khi đóng checkout/đổi token. Khi QR hết hạn hoặc provider trả PAID nhưng invoice chưa được xác nhận, UI ẩn mã/link và tiếp tục chờ. Chỉ invoice đã được cập nhật mới hiện thành công/số tiền đã thanh toán. Lỗi refetch chung giữ dữ liệu đang có, lỗi 401/403/404 kết thúc quyền truy cập.
- Migration SQLite/PostgreSQL thêm 7 nullable fields cho `PayOsAttempts`: `QrCode`, `AccountName`, `AccountNumber`, `BankBin`, `Description`, `ProviderStatus`, `ExpiresAt`. Không đổi guards invoice/payment, xác minh callback/webhook hay replay protection.
- Kiểm tra service fake `HttpClient`: 45 assertions pass (11 kiểm webhook registration cũ + 34 checkout/payment mới): signed QR roundtrip/persistence, reuse pending, legacy pending không QR, invalid signature/amount/order, provider PAID không ghi nhận khi chưa có số tiền xác minh, đối soát thiếu amount, webhook đúng/sai amount và replay idempotent. Fake handler chặn mọi provider request ngoài process.
- API integration suite **20/20 pass** với DB SQLite disposable `/tmp/room-rental-qr-qa.db`, `WebhookBaseUrl=http://localhost:5082`; không tạo giao dịch với payOS thật. Initializer chạy trên bản copy riêng `/tmp/room-rental-qr-init-qa.db`: SQLite integrity `ok`, số dòng Users170, Buildings108, Rooms113, Occupancies112, Invoices106, Receipts57, PayOsAttempts27, ProcessedWebhooks18 không đổi; 7 cột nullable được thêm. FE `npm run build` pass (còn cảnh báo bundle chung 1.14 MB); `npm run i18n:check` pass 551 khóa ×4 catalog.
- Browser QA bằng CUA và local mock server có payload/account giả không thể thanh toán: desktop QR hiển thị; ở 375×812 không tràn ngang (document/body 375px, modal351px), QR render và modal cuộn nội bộ (948px nội dung/667px chiều cao thấy được). QR-null legacy hiện hosted fallback hợp lệ; hostname ngoài payOS bị ẩn. Reconcile giả báo Paid nhưng invoice Unpaid vẫn chờ và ẩn QR/link; khi invoice mock được xác nhận Paid thì portal hiện “Amount paid ₫425,000” và trạng thái Paid. Nút sao chép hiện toast thành công nhưng không xác minh độc lập nội dung clipboard. Ảnh synthetic ở `tests/artifacts/rehouse-inline-qr-desktop.jpg` và `tests/artifacts/rehouse-inline-qr-mobile.jpg`. Không kiểm tra expiry, đóng modal khi reconcile còn bay, hoặc lỗi overview cache trong lượt QA này.
- Rollout backend thực: API khỏe tại `localhost:5080`, session58025/PID46365; integrity `ok` và đủ 7 cột. Backup mode0600 tại `backend/RoomRental.Api/.local-data/room-rental.pre-qr-checkout-20261012T184125Z.db`. Snapshot ngay sau khởi động giữ nguyên 32 invoices, tổng `124665000` VND và Paid `6850000` VND. Không agent nào tạo payment/transfer mới hoặc sửa hóa đơn trên DB thật; đối soát định kỳ bình thường được giữ cho một payment pending có sẵn theo chỉ đạo của người dùng. FE5173, gateway5081 và tunnel không đổi; mock5082/FE QA5174 và tab CUA thử nghiệm đã dừng/đóng sau kiểm tra.
- Chưa thực hiện giao dịch payOS/ngân hàng thật; kiểm thử chỉ chứng minh xử lý chữ ký, dữ liệu và trạng thái nội bộ. Khi deploy, tiếp tục xác minh một giao dịch có sự đồng ý/thao tác của khách và webhook/đối soát thật trước khi mở rộng.


## 22. Trang Rehouse checkout riêng — 13/10/2026

- Nút **Pay/Thanh toán** trong portal điều hướng sang trang checkout Rehouse riêng, không mở modal: `/t/{token}/invoices/{invoiceId}/checkout`; alias `/tenant/{token}/invoices/{invoiceId}/checkout` cũng được hỗ trợ. Query hiện tại như `?lang=en` được giữ khi đi vào và khi quay về. Back trong trang và lịch sử trình duyệt đều quay lại portal đúng token/query.
- Trang xác minh lại overview và scope hóa đơn trước khi tạo/tái sử dụng checkout; một lần mount chỉ gọi create một lần, còn pending được backend tái sử dụng. Hóa đơn thiếu/đã trả không tạo checkout. QR nullable tiếp tục hiển thị fallback có nút mở hosted checkout rõ ràng chỉ với HTTPS `pay.payos.vn`; trang không tự redirect. Provider `paid` khi invoice chưa Paid giữ giao diện chờ, ẩn QR/link; chỉ overview/reconcile có invoice Paid mới hiện thành công và số tiền đã trả. Polling/reconcile giữ như mô tả tại [API contract](api-contract.md).
- UI QA bằng CUA trên mock server chạy riêng tại localhost đã xác nhận route/query/back, QR synthetic, legacy QR-null fallback an toàn, một create request trên mount, và quy tắc invoice-authoritative: `providerCreateStatus=paid` cùng `reconcileMode=provider-paid` vẫn chờ cho đến khi invoice mock được đổi sang Paid; sau đó Check now hiện Paid / `Amount paid ₫425,000`. 375×812: document/body 375 px và SVG QR render; desktop 1466 px không tràn ngang. Screenshot dùng payload/account giả không thể thanh toán: `tests/artifacts/rehouse-checkout-page-desktop.jpg`, `tests/artifacts/rehouse-checkout-page-mobile.jpg`.
- Kiểm tra frontend cuối: `npm run build` pass (frontend agent xác nhận); `npm run i18n:check` pass 554 khóa ×4 catalog. Không chạy lại backend API suite vì thay đổi chỉ ở route/UI; các kết quả service/API/migration trong mục 21 vẫn là kiểm tra backend gần nhất. QA chỉ sửa state trong mock process trong bộ nhớ; không gọi payOS, không tạo chuyển khoản, không sửa hóa đơn hoặc database thật.
- Chưa kiểm tra hard refresh trực tiếp trên route checkout, UI hết hạn, hoặc mở link hosted ở trình duyệt. Đây là các giới hạn của lượt QA, không phải xác nhận các trường hợp đó đã được kiểm thử.


## 23. PostgreSQL Docker QA và tải đọc giới hạn — 13/10/2026

- Kiểm tra Production Docker image với PostgreSQL 17 trên Compose project dùng riêng `rehouse-pg-qa`, Caddy TLS cục bộ và dữ liệu fixture tổng hợp. PostgreSQL schema khởi tạo/lặp lại thành công; các cột checkout nullable `QrCode`, `AccountName`, `AccountNumber`, `BankBin`, `Description`, `ProviderStatus`, `ExpiresAt` có mặt. API qua TLS local health trả 200. Callback QA trỏ HTTP localhost để không gọi payOS.
- HTTP API integration: `tests/test_api_integration.py`, 16/16 pass trong 3.396 giây. Test tạo dữ liệu tổng hợp trong DB dùng một lần; không chạy SQLite-only fixture `test_payos_fixture.py` trên PostgreSQL. Không tạo checkout, không gọi payOS và không dùng DB/khóa production.
- Read-only load probe: `tests/load_postgres_readonly.py` tạo một owner/phòng/occupancy giả trước khi đo; hai stage chỉ GET overview khách thuê trong 5 giây, lần lượt 50 và 100 worker. 50 worker: 7.085 request/5.017 giây, 1.412,3 request/giây, p50 29,8 ms, p95 44,8 ms. 100 worker: 7.078 request/5.051 giây, 1.401,4 request/giây, p50 57,3 ms, p95 92,0 ms. Cả hai stage đều chỉ trả HTTP 200, không có HTTP 429 hoặc lỗi truyền tải.
- Đây là phép đo cục bộ cache nóng với một tenant/phòng tổng hợp, API và PostgreSQL chạy cùng máy qua proxy; host Darwin arm64 có 12 logical CPU/24 GiB RAM, Docker VM 12 CPU/7,65 GiB RAM. Không đặt giới hạn CPU/RAM riêng cho container. Kết quả không phải ước tính số người dùng hay năng lực production; không đo ghi dữ liệu, nhiều tenant, mạng thật hoặc payOS. Probe có chặn đích chỉ cho HTTPS localhost:18443, TLS verify bật, tối đa 5 giây/stage và 20.000 request/stage.
- Docker QA project `rehouse-pg-qa`, volume, image tags và fixture `/tmp/rehouse-pg-qa` đã được dọn sau kiểm thử. Không còn instance QA của đợt mục này; PostgreSQL migration target cục bộ được tạo sau đó và mô tả ở mục 25. API 5080, FE 5173, gateway 5081 và container `edusphere` không bị thay đổi trong lượt QA. Chi tiết cách chạy và kết quả nằm trong [báo cáo PostgreSQL QA](POSTGRES_QA.md); hướng dẫn test ở [tests/README.md](../tests/README.md).


## 24. Runbook ứng phó tăng trưởng nhanh Rehouse — 13/10/2026

- Đã tạo [runbook tăng trưởng nhanh](SCALING_RUNBOOK.md) dựa trên code hiện tại và kết quả PostgreSQL QA mục 23. Nội dung gồm trạng thái hiện có/chưa có, ngưỡng cảnh báo **đề xuất** (chưa có telemetry cấu hình), checklist trước public launch, playbook burst 10×, SQLite→PostgreSQL cutover/restore guardrails, giữ webhook/đối soát và các điều kiện cần trước scale-out.
- Đây là tài liệu kế hoạch; không đổi service/compose, database, secrets, polling, limiter hoặc số replica. Không có cam kết users/RPS; kết quả một tenant/room QA không phải capacity benchmark. Chưa triển khai ra internet; telemetry, EF migrations, restore drill, shared/distributed limiter, job lease/fairness và runtime incident knobs còn cần làm.


## 25. Chuyển database cục bộ từ SQLite sang PostgreSQL — 13/10/2026

- Người dùng cho phép chuyển database đang dùng; API được dừng bình thường trong khoảng 2 phút 30 giây. SQLite online backup cuối cùng: `backend/RoomRental.Api/.local-data/sqlite-to-postgres/room-rental-final-frozen-20261013.db` (0600, integrity `ok`, foreign-key violations 0). SQLite gốc vẫn được giữ. Import đích `room_rental` hoàn tất khi API dừng, rồi API Development chạy lại bằng PostgreSQL; backend xác nhận không còn kết nối SQLite sau khi khởi động.
- PostgreSQL 17.11 chạy trong Compose project `rehouse-pg-migration`, container `rehouse-pg-migration-db`, named volume bền vững `rehouse-pg-migration-data`, host port chỉ bind loopback `127.0.0.1:15432`. Database hiện dùng là `room_rental`. API tiếp tục chạy host-side trong cùng content root `backend/RoomRental.Api` tại `localhost:5080`; cấu hình Development đặt `DatabaseProvider=Postgres`, `SeedDemo=false`. Không mở cổng PG ra LAN/Internet.
- Import được kiểm chứng độc lập trước khi restart API: đủ 13 bảng, 404/404 dòng; không thiếu/thừa dòng và không trường nào lệch sau chuẩn hóa GUID, decimal, ngày và DateTimeOffset về UTC/microsecond. Các kiểm tra dữ liệu về tham chiếu, email/unique nghiệp vụ, idempotency, một Pending attempt mỗi hóa đơn và `Invoice.Paid = tổng Receipts` đều có 0 vi phạm ở hai phía. 18 cột PostgreSQL quan trọng (UUID, numeric precision/scale, date, boolean, timestamptz, bigint và enum/string) đúng kiểu.
- Tài chính khớp: 32 hóa đơn, tổng `124.665.000` VND, đã trả `6.850.000` VND và receipts `6.850.000` VND. Có 10 receipts; 11 payOS attempts gồm 1 Pending, 5 Paid, 5 Failed. So sánh tổng hợp theo từng owner có 0 sai khác. Không tạo checkout/receipt/transfer giả.
- DataProtection giữ cùng content root và key ring mặc định `~/.aspnet/DataProtection-Keys`. Bản sao bảo vệ cutover nằm trong `backend/RoomRental.Api/.local-data/sqlite-to-postgres/DataProtection-Keys-cutover-20261013/` (0700). Với application discriminator hiện tại, 38/38 tenant token được mở bằng key ring và hash khớp; 14/14 bộ payOS API/checksum key được mở ở cả SQLite và PostgreSQL, hash plaintext nguồn/đích khớp. Không ghi token, khóa hoặc session cookie ra log/tài liệu.
- Sau restart, `GET /api/health` trả 200; một tenant overview GET bằng token cũ còn hiệu lực trả 200 và một hóa đơn. Không có owner browser session đang đăng nhập để kiểm tra route owner-auth; GET không xác thực `/api/auth/me` và `/api/settings/payment` trả 401. Không đăng nhập/tạo session mới trong QA. Việc kiểm tra DP và toàn bộ bản ghi `AuthSessions` khớp không tự chứng minh một cookie cụ thể còn hiệu lực. Sau khởi động, số hóa đơn/receipts vẫn lần lượt là 32/10 với tổng tiền không đổi. Đối soát bình thường cho attempt Pending được phép hoạt động; verifier không gọi payOS và không gửi POST.
- Các backup cấu hình/DB trước khởi động: frozen SQLite như trên; logical PostgreSQL dump `backend/RoomRental.Api/.local-data/postgres-migration/room_rental-pre-api-start-20261013.dump` (0600, 58 mục trong archive list; restore tạm xác nhận 13 bảng/404 dòng); cấu hình cũ `backend/RoomRental.Api/.local-data/sqlite-to-postgres/appsettings.Development.local.before-postgres-cutover-20261013.json` (0600). Connection credential chỉ lưu ở file ignored `backend/RoomRental.Api/.local-data/postgres-migration/postgres.env` (0600); không đưa giá trị vào log/tài liệu. Cách tái tạo môi trường thử, lệnh import có guard, khởi động và rollback nằm trong [hướng dẫn migration](sqlite-to-postgres-migration.md).
- Đây là chuyển đổi local đã xác minh, chưa phải production deployment. Không có reverse importer PostgreSQL→SQLite hoặc EF migration history; không rollback bằng cách chỉ đổi connection string sau khi PostgreSQL đã nhận writes. Trước rollback, dừng writes, backup PostgreSQL và key ring cùng restore point rồi đối chiếu/replay mọi delta; ưu tiên sửa tiến về phía PostgreSQL. Không xóa volume.

## 26. Public HTTPS pilot trên VPS — 13/10/2026

- Domain `demo.yourdomain.com` trỏ tới `<YOUR_VPS_IP>`. Caddy 2.11.7 được cài từ apt repository chính thức, chạy bằng systemd và phục vụ chứng chỉ Let's Encrypt hợp lệ. HTTP công khai trả 308 sang HTTPS; `https://demo.yourdomain.com/api/health` trả 200.
- Caddy reverse proxy tới `127.0.0.1:8080`; app vẫn chỉ bind loopback và PostgreSQL không publish port. Caddy admin API chỉ nghe loopback port 2019. App nhận peer proxy qua Docker gateway `172.18.0.1`; `TRUSTED_PROXIES=172.18.0.1`. `/etc/rehouse/runtime.env` đặt cả `PUBLIC_BASE_URL` và `WEBHOOK_BASE_URL` thành `https://demo.yourdomain.com`.
- Trên HTTPS công khai, admin login pass; cookie có cờ Secure/HttpOnly; `/api/auth/me` và `/api/admin/owners` trả 200. Logout với Origin domain đúng trả 200; Origin không khớp bị từ chối 403. Không truy cập trực tiếp cổng app từ Internet.
- Trước khi đổi origin/recreate app đã tạo backup PostgreSQL và DataProtection key. Helper xác minh DNS, lấy địa chỉ gateway hiện tại, serialized operation bằng lock chung, recreate app bằng digest đang chạy và kiểm tra health, DB, bootstrap admin, key volume.
- Database VPS vẫn là fresh deployment, chỉ có bootstrap admin; chưa import database/khóa từ Mac hoặc dữ liệu tenant. `WEBHOOK_BASE_URL` đã sẵn sàng để tạo callback HTTPS, nhưng trên VPS mới chưa có owner payOS settings/credentials, chưa gọi `confirm-webhook`, và không tạo checkout hay giao dịch. Lịch sử local về giao dịch Paid 30.000 VND được giữ như đã ghi ở phần trên; payOS chưa được xác minh trên VPS mới.
- Cấu hình chuẩn nằm trong [`deploy/Caddyfile`](../deploy/Caddyfile), helper đổi public origin tại [`deploy/configure-public-origin.sh`](../deploy/configure-public-origin.sh), và [deployment runbook](deployment.md). Phần còn lại trước vận hành rộng: migrations có version, off-host backup/khôi phục định kỳ, giám sát, UAT nghiệp vụ và kiểm chứng payOS thật khi chủ trọ cung cấp credentials/đồng ý thử.

## 27. Mặc định hiển thị toàn bộ hóa đơn — 13/10/2026

- Trang hóa đơn tải danh sách không giới hạn kỳ và mặc định hiển thị mọi loại, mọi trạng thái, kể cả đã thanh toán. Chủ trọ có thể lọc loại/kỳ/trạng thái và tìm theo phòng, dãy hoặc người thuê; mỗi dòng có nhãn loại. Ba tổng số cập nhật theo đúng kết quả lọc và ghi rõ tiền cọc đã nhận không phải doanh thu. Bộ lọc rỗng có nút xóa; trạng thái rỗng ban đầu hướng dẫn các cách tạo hóa đơn.
- Drawer chi tiết và link `?invoice=...` được giữ nguyên. Nút tạo hóa đơn còn thiếu chỉ hiện cho loại tháng khi đã chọn kỳ. Không đổi backend, dữ liệu hoặc số tiền. CI run `38356146349` deployed green; initial read-only browser check showed two Paid invoices totaling 20.000 VND by default (one Supplemental and one Deposit), and the Deposit filter showed one invoice totaling 10.000 VND. At a 1440px window the content area was about 1040px; the previous 1080px compact-layout threshold selected cards, so the invoice table threshold was lowered to 960px.
- Public read-only browser QA: default list shows 2 Paid invoices, total 20.000 VND, paid 20.000 VND, due 0. Deposit filter shows 1 invoice / 10.000 VND. Unpaid filter shows 0 with the empty state and clear control. A non-matching search shows 0; search `101` shows 2. Period `09/2026` shows 0; clearing filters restores the full list. Paid Supplemental detail shows total 10.000 VND, paid 10.000 VND, due 0. At viewport 375px, document width measured 360px with no horizontal overflow; filter controls measured 304px wide.
- CI run `38356760544` confirmed the normal desktop table, then browser review found the room/tenant column still too narrow. A follow-up assigns widths totaling 980px: room 220, kind 140, period 120, each amount 90, status 130 and action 100; room/tenant text wraps across two lines and can break at unusually long tokens. Final [CI run `38357433938`](https://github.com/nhaq0505/rehouse/actions/runs/38357433938) succeeded and deployed commit `35560bf`. Public desktop QA at 1440px confirmed the normal table, two Paid rows, total 20.000 VND, paid 20.000 VND and due 0; room width was 239.75px, rows were 91.125px tall, names wrapped to two lines, and document width stayed 1440px without global overflow. Final mobile QA at 375×812 confirmed two compact invoice records, four filter controls, default list of two Paid invoices totaling/paid 20.000 VND with due 0, and document width 360px (within the 375px viewport). Desktop and mobile verification are complete.
- Local-only screenshot follow-up, pending publish: removed `fixed: 'right'` from the invoice action column so Ant Design no longer draws the fixed-column shadow stripe; the Details action remains available. Frontend build passes with the existing bundle-size warning. Local UI is running at `http://127.0.0.1:5173/invoices`, API at `http://127.0.0.1:5080` (`/api/health` returns 200), and the existing Development PostgreSQL stays loopback-bound at `127.0.0.1:15432`. API started with `SeedDemo=false`; no database reset/seed or production/payment activity. Local browser QA completed: at 1440px the normal table showed 0 fixed-right cells, 3 Details buttons and no vertical shadow; opening the first Details button showed invoice P101 with total 4.060.000 VND, then it was closed without writes. At 375×812, the document width was 360px and 3 compact invoice records were shown. Screenshot: `/tmp/rehouse-invoice-review/local-no-stripe.png`. This fix has not been committed, pushed, or deployed.
- Kiểm tra frontend: `npm run i18n:build`, `npm run i18n:check` pass với 568 khóa × 4 catalog; `npm run build` pass (cảnh báo chunk lớn hiện có vẫn xuất hiện).

## 28. Báo cáo, lưu trữ mềm và local PostgreSQL upgrade — 13–14/10/2026

- Backend local có ba API báo cáo theo chủ trọ: `/api/reports/receipts`, `/api/reports/debt`, `/api/reports/deposits`; từng loại có route `/{reportType}/export.xlsx`. API nhận lọc ngày, dãy/phòng và phân trang. Tổng tiền truy vấn theo toàn bộ tập lọc, không theo trang hiện tại. Thu cọc mới được tách riêng; khoản ghi có nội bộ từ quyết toán cọc không được tính thành tiền mặt mới. Công nợ là số dư hiện tại của hóa đơn đã phát hành, lọc ngày theo kỳ hóa đơn và có cả hóa đơn cọc còn nợ. Báo cáo cọc hiển thị phát sinh trong kỳ cùng số dư cọc đang giữ hiện tại.
- Các ô số/ngày trong Excel có kiểu dữ liệu phù hợp; nội dung do người dùng nhập được ghi thành text có bảo vệ chống formula injection. Export giới hạn số dòng và trả lỗi rõ khi vượt giới hạn thay vì cắt dữ liệu âm thầm.
- Dãy/phòng dùng archive fields (`ArchivedAt`, `ArchivedBy`, `ArchiveBatchId`) và không bị xóa vật lý. Lưu trữ bị chặn khi còn occupancy, hóa đơn nháp/chưa thanh toán, cọc đang giữ hoặc payOS đang chờ. Lưu trữ dãy và phòng con là giao dịch nguyên tử; khôi phục dãy chỉ khôi phục phòng nằm trong cùng archive batch. Danh sách nghiệp vụ mặc định chỉ hiện mục active; báo cáo giữ lịch sử qua snapshot BuildingId/RoomId/tên.
- Invoice lưu `BuildingId` snapshot; initializer backfill giá trị cho hóa đơn cũ và tạo các cột archive bổ sung. Schema vẫn chưa có versioned EF migrations. Hai hosted service có công tắc `EnableInvoiceSweep` và `EnablePaymentReconciliation`, mặc định bật nếu không cấu hình; chỉ tắt rõ ràng trên database clone QA hoặc runtime local hiện tại để tránh job nền/provider call trong quá trình kiểm tra.
- **Ưu tiên 1 — correction có kiểm soát:** đủ điều kiện correction dùng version/request ID để chống ghi lặp, kiểm tra trạng thái thu tiền/pending trước khi sửa và ghi revision trước/sau cùng actor/reason để audit. Không cho sửa/xóa tự do hóa đơn đã phát hành.
- **Ưu tiên 2 — Draft và identity theo occupancy:** hóa đơn kỳ lẻ có thể ở Draft đến khi chủ trọ publish; Draft chưa phải khoản phải thu/không được tính vào debt. Reading/hóa đơn gắn với `OccupancyId`, cho phép khách mới vào lại cùng tháng sau khi khách cũ đã chuyển đi và không nhận nhầm trạng thái đã chốt của occupancy trước.
- Cùng tháng có thể bắt đầu occupancy mới sau khi kết thúc occupancy trước; dashboard đối chiếu meter reading bằng `OccupancyId` để lượt thuê mới không kế thừa trạng thái đã chốt của người cũ. Alias `/end-occupancy` yêu cầu hóa đơn final đã phát hành và các điều kiện quyết toán, đồng thời dùng `BillingEndDate` đã chọn trên hóa đơn. Chặn chuyển đi nếu đã có meter measurement cùng kỳ sau `BillingEndDate`; số tiền VND phải là số nguyên, không nhận phần lẻ.
- Các test reports/archive vẫn 6/6 pass, gồm owner isolation, batch restore, block active tenant, snapshot lịch sử, tính tổng và typed XLSX cho cả ba báo cáo. Test module Excel cần chạy với Python 3.9 trên môi trường này vì Expat của Python 3.14 báo lỗi khi parse XML trong workbook; workbook vẫn xác nhận bằng Python 3.9.
- QA webhook cần được ghi đúng phạm vi: một lần chạy sớm của agent trên API port 5082 dùng DB disposable riêng `/workspace/.local-data/billing-upgrade-qa/qa.db` nhưng kế thừa HTTPS callback. Khi test cấu hình, nó đã gửi 4 POST đăng ký/xác nhận webhook với khóa giả và tất cả thất bại; không có webhook được xác minh, giao dịch hay receipt nào được tạo. Không được mô tả lần chạy này là “không có request ra provider”. Root dùng DB clone riêng `backend/RoomRental.Api/.local-data/billing-upgrade-qa/qa.db` với HTTP localhost callback trong các suite sau; lần chạy đó không đăng ký webhook hoặc gọi provider. Không có khoản thanh toán thật.
- Root đã xác nhận full-suite mới nhất xanh. FE build/i18n có 746 khóa × 4 catalog; report layout ở 375px không tràn ngang, draft/correction/audit và lưu trữ/khôi phục đã được kiểm tra bằng CUA. Moveout preview ban đầu cho phép finalize; chỉnh tiền thuê làm nút finalize tắt, sau tính lại preview giữ tiền thuê 1.700.000 VND và bật lại.
- PostgreSQL Development `room_rental` đã được backup rồi nâng cấp schema. Sau nâng cấp: 32 hóa đơn hiện có, tổng 124.665.000 VND, đã trả 6.850.000 VND; toàn bộ vẫn `Issued`, `Version=1`, `SchemaUpgrades=1`. Digest tài chính theo từng hóa đơn giống hệt trước/sau; suite dùng thử không ghi vào DB thật. API `localhost:5080` khỏe (`/api/health` 200).
- Runtime hiện tại dùng `SeedDemo=false`, `EnableInvoiceSweep=false`, `EnablePaymentReconciliation=false` do DB thật còn một PayOS attempt `Pending`; các job có default bật khi không đặt cấu hình. Giữ hai job tắt cho đến khi xử lý QA này an toàn; không dùng lần chạy này để đối soát khoản pending hoặc gửi yêu cầu thanh toán.
- Source và schema vẫn local; chưa push/deploy. UAT với người dùng và kiểm tra payment thật chưa thực hiện.

## 29. Quy tắc hết hạn QR payOS 10 phút — 14/10/2026

- Order mới lưu `ExpiresAt = CreatedAt + 600 giây` trước khi gọi provider và gửi cùng giá trị `expiredAt` dưới dạng Unix timestamp `Int32`. Nếu response có `expiredAt` đã ký, giá trị phải khớp; nếu response bỏ trường này thì dùng deadline đã lưu. Canonical request signature không đổi. Xem [payOS API](https://payos.vn/docs/api/).
- Retry pending giữ nguyên deadline. Đồng hồ cục bộ hoặc QR hết hạn không tự chuyển attempt sang failed: chỉ trạng thái provider đã reconcile `CANCELLED`/`EXPIRED` mới kết thúc attempt; request tiếp theo sau đó mới được tạo order mới. Pending legacy thiếu `ExpiresAt` vẫn giữ null, không gán hạn hồi tố.
- FakeHttpClient service suite pass **58 assertions** cho 600-second deadline, signed response mismatch/fallback, giữ hạn khi reuse, legacy null expiry, không tự fail theo đồng hồ và tạo order tiếp theo sau provider-confirmed terminal status. Đây là kiểm thử local giả lập, không gọi payOS và không tạo payment thật. Không chạy API integration suite trong phạm vi expiry này. API local đã restart và `/api/health` trả 200.
- Cập nhật này chỉ sửa tài liệu, không chạy thêm test hoặc gọi provider. Runtime local vẫn `SeedDemo=false`, invoice sweep và payment reconciliation đang tắt vì còn một PayOS attempt Pending; chưa push/deploy.

## 30. Cô lập cuộn trong drawer hóa đơn và trả phòng — 14/10/2026

- CSS chỉ áp dụng cho `.ant-drawer-content.invoice-drawer` và `.ant-drawer-content.moveout-drawer`: content dùng `overflow:hidden`, body là pane `overflow:auto` duy nhất với `overscroll-behavior:contain`. Drawer menu mobile và drawer toàn cục không đổi.
- CUA chỉ đọc trên invoice P102: desktop 1440×900, cuộn body 0→912→462 trong khi header giữ y=0 và main y=213; mobile 375×812, body 0→1245→0 và chiều rộng trang vẫn 375px. Content hidden, body auto/contain.
- Moveout P102 desktop preview: body 0→841→0; header/main giữ vị trí, content hidden và body contain. Chỉ mở preview; không chốt, lưu, thu tiền hay gọi payOS. Frontend và root build đều pass.
- Giới hạn: screenshot trạng thái ổn định không còn vùng trắng, nhưng CUA không tái hiện transient white flicker trước sửa, nên chưa chứng minh chính xác compositor/root cause của hiện tượng ban đầu. Thay đổi và kiểm tra chỉ local, chưa push/deploy.

## 31. Khoảng trắng trong drawer vẫn còn được báo cáo — 14/10/2026

- Tại thời điểm audit ban đầu, người dùng vẫn báo vùng trắng sau cô lập scroll ở mục 30. Read-only audit xác nhận cuộn native của trình duyệt, không có React scroll handlers; pane cuộn lồng đúng và portal lock chỉ đổi khi drawer mở/đóng.
- Giả thuyết ban đầu là tương tác compositor của Mac Edge giữa topbar sticky, trong suốt có `backdrop-filter: blur(10px)` và mask/overlay. Frontend thử đổi topbar thành nền đục `#fff` và bỏ backdrop filter; khi mới có build pass, chưa xác nhận candidate đã xử lý symptom. Native Edge A/B chưa hoàn tất vì người dùng chuyển app làm gián đoạn điều khiển.
- Sau reload trong IAB, computed topbar là `backdrop-filter: none`, nền `rgb(255, 255, 255)`; body giữ `overflow:hidden`, pane body `overflow:auto`/`overscroll-behavior:contain`. Trên mobile, pane cuộn tới 1306.5px trong khi header và main giữ y=0. Khi được hỏi vị trí, người dùng xác nhận vùng trắng ảnh hưởng **toàn trang**; sau candidate topbar opaque và hard reload, người dùng báo **“Đã hết”**. Issue được xem là đã hết trong kiểm tra local của người dùng sau candidate.
- Kết quả này cho thấy tương quan giữa candidate CSS và symptom đã biến mất sau hard reload; chưa có GPU/compositor diagnostics để chứng minh nguyên nhân gốc. Thay đổi chỉ local, chưa push/deploy; cập nhật này chỉ sửa tài liệu, không chạy test.

## 32. Tính đủ tiền thuê tháng và trả phòng chỉ ghi nhận hoàn cọc — 14/10/2026

- Chính sách mới thay thế luồng tính theo ngày và hóa đơn cuối kỳ được mô tả trong mục 18 và các ghi chú trả phòng trước đó. Mỗi lần lưu chỉ số kỳ tháng tạo hóa đơn `Monthly` trạng thái `Issued` ngay theo đủ `Room.Rent`, bất kể ngày vào giữa tháng hoặc độ dài 28/30/31 ngày. Không còn tạo Draft mới để chờ tính tiền lẻ theo ngày. Draft cũ vẫn hiện để chủ nhà xem/sửa/phát hành, nhưng phải xử lý trước khi trả phòng.
- Luồng trả phòng chỉ hiển thị công nợ, trạng thái cọc và các chặn; người dùng nhập một số tiền hoàn thủ công. Không có chốt công tơ cuối, invoice cuối hoặc khoản phí/tiền thuê bổ sung trong drawer. Xác nhận đóng occupancy vào ngày Việt Nam hiện tại, thu hồi guest link, đưa phòng về trống và cập nhật baseline bằng số đo gần nhất đã có. `GET /api/rooms/{id}/moveout` vẫn là summary đọc; preview tính lẻ và route tạo final invoice đã bị bỏ.
- `POST /api/rooms/{id}/complete-moveout` nhận `{occupancyId, requestId, refundAmount, expectedHeld, expectedDepositVersion}`; alias `/end-occupancy` dùng cùng handler. Serializable transaction kiểm tra owner/phòng/occupancy và held/version snapshot; chặn Draft, PayOS pending, hóa đơn đã phát hành chưa thanh toán (kể cả hóa đơn cọc) và refund vượt số cọc đang giữ. Ghi đồng thời settlement hoàn tiền và phần cọc còn giữ (`Deduction`), không tạo receipt giả và không chuyển tiền ngân hàng. Audit lưu actor, thời điểm, request/fingerprint, số hoàn và số giữ; retry payload giống hệt trả kết quả cũ, payload khác bị conflict. `GET /moveout-completions` theo owner vẫn đọc được lịch sử kể cả phòng đã archive.
- FE build và i18n kiểm tra pass (789 source keys × 4 catalog). CUA chỉ đọc trên phòng P102: drawer mới không có datepicker, final-meter hoặc fee input; công nợ 4.200.000 VND khóa nút hoàn tất đúng, case không có cọc hiển thị đúng; viewport 447px không tràn ngang. Không thực hiện refund/payment mutation bằng CUA; ảnh QA `/tmp/rehouse-refund-only-local.png`.
- Build API pass với 0 warning/error. Trên SQLite dùng thử, chạy riêng và pass `test_api_integration.py` 17/17, `test_billing_upgrade.py` 13/13, `test_reports_archive.py` 6/6 và `test_payos_fixture.py` 4/4 (40 test qua các lần chạy tách biệt, không phải một full-suite run). Một lần chạy full lặp lại bị auth limiter HTTP 429; sau reset limiter, các module kiểm tra trên được chạy riêng. PostgreSQL clone: billing 13 test, 11 pass và 2 SQLite-only skip. FakeHttpClient payOS suite pass 58 assertions; không gọi provider thật.
- PostgreSQL Development thật sau lần restart: 33 invoice, tổng 124.675.000 VND, đã trả 6.850.000 VND, digest tài chính `4f3b2fd5b796ff4f19ec78a2a9146f0f`; số liệu và digest giữ nguyên, `MoveoutCompletions=0`. API `localhost:5080` khỏe. Runtime vẫn `SeedDemo=false`, `EnableInvoiceSweep=false`, `EnablePaymentReconciliation=false`; dữ liệu thật không bị mutate. Source/docs local only; chưa commit, push hay deploy.

## 33. Thu gọn mặc định phần điều chỉnh hóa đơn — 14/10/2026

- Trong drawer chi tiết hóa đơn, phần điều chỉnh mặc định đóng; bấm tiêu đề điều chỉnh để mở/đóng form. Trường, guard, xác nhận lưu và audit history không đổi; lịch sử điều chỉnh vẫn nằm riêng và hiển thị bên dưới. Form được render sẵn để giữ giá trị hóa đơn đã nạp trong khi phần nội dung đóng.
- Chọn hóa đơn khác hoặc đóng rồi mở lại drawer sẽ đưa phần điều chỉnh về trạng thái đóng. Root CUA đã kiểm tra invoice P102: form mặc định ẩn, mở ra giữ tiền thuê 3.000.000 VND, điện 1.000.000 VND và chỉ số 1220; Enter bật/tắt được `aria-expanded`; lịch sử luôn nhìn thấy bên ngoài phần thu gọn. Đóng/mở lại drawer trở về trạng thái đóng. Ảnh `/tmp/rehouse-invoice-correction-collapsed.png`; chỉ đọc, không lưu điều chỉnh hay gọi provider. `npm run build` pass; thay đổi local, không commit/push.

## 34. Phân cách hàng nghìn trong các ô tiền VND — 14/10/2026

- Component `MoneyInput` dùng dấu phẩy để nhóm hàng nghìn ở toàn bộ 23 ô nhập tiền hiện có: cấu hình phòng/tiền cọc, hóa đơn/thu tiền/điều chỉnh, bản nháp kỳ đọc, trả cọc và quyết toán cọc. Parser chỉ bỏ dấu phẩy phân nhóm; giá trị Form/API vẫn là số, thao tác xóa tiếp tục để trống và `precision={0}` giữ cách xử lý số VND nguyên hiện có của Ant InputNumber. KWh và số người vẫn dùng InputNumber thường, không nhóm.
- Root CUA chỉ đọc trên hóa đơn P102: ô thu tiền nạp sẵn `4,200,000`; gõ `1234567` rồi blur thành `1,234,567`; paste `2,345,678` giữ nguyên; xóa rồi blur để trống; khôi phục `4200000` hiển thị `4,200,000`. Điều chỉnh nạp tiền thuê `3,000,000`; gõ `1234567` rồi blur thành `1,234,567`; khôi phục `3000000`. Số đo kWh vẫn là `1220.000`. Ảnh `/tmp/rehouse-money-input-comma.png`; không submit, ghi receipt/điều chỉnh hay gọi provider.
- `npm run build` và `npm run i18n:check` pass; i18n có 789 khóa nguồn × 4 catalog và không thêm chuỗi dịch. Build có cảnh báo hiện hữu về một bundle lớn hơn 500 kB. `git diff --check` pass; chỉ local, chưa commit/push.

## 33. Sửa người thuê, đổi/reset mật khẩu và Dashboard theo tháng — 14/10/2026

- Occupancy: PATCH theo room + occupancyId, sửa tên/SĐT/số người; kiểm tra ownership, active và stale ID. Giữ nguyên mọi hóa đơn Issued, gồm Unpaid/Partial/Paid. Draft chưa thu và không pending payOS được tính lại nước theo WaterRate đã lưu, tăng Version và ghi revision. Reading mới lưu OccupantsCount; sweep dùng snapshot đó, bỏ qua legacy per-person reading thiếu snapshot thay vì suy diễn lịch sử.
- Mật khẩu: Owner/Admin tự đổi bằng current/new/confirm, mật khẩu mới 12–256 ký tự có chữ hoa/thường/số/ký tự đặc biệt. Thu hồi toàn bộ phiên khi đổi. Admin xác nhận mật khẩu hiện tại rồi cấp mã reset 256-bit dùng một lần trong 30 phút; DB chỉ lưu SHA-256 + expiry. Chủ trọ phải dùng mã để đặt mật khẩu mới trước khi đăng nhập; không gửi email tự động, admin giao mã riêng sau khi xác minh danh tính.
- Dashboard: 4 KPI tháng hiện tại theo kỳ Việt Nam, excludes Draft/Deposit. Trường totalBilled/totalPaid/totalOutstanding cũ giữ all-time; UI có block all-time riêng. Đã thu tháng này là số Paid của hóa đơn kỳ này, không phải dòng tiền receipt-date.
- Schema: Users.PasswordResetHash (nullable TEXT), PasswordResetExpiresAt (nullable INTEGER SQLite / timestamptz PostgreSQL); Readings.OccupantsCount (nullable INTEGER). Startup upgrade idempotent, transaction; không backfill số người lịch sử. DbMigrator đọc được source SQLite chưa có 3 cột này mà không sửa backup.
- QA riêng: SQLite 52/52; PostgreSQL 48 run, 40 pass, 8 skip; service 71 assertions. API và migrator build 0 warning/error; frontend build pass (cảnh báo bundle lớn sẵn có); i18n 828 keys × 4 pass; diff whitespace pass. Migration trên legacy SQLite giữ nguyên 211 invoice rows; PG giữ nguyên invoice digest. Import legacy giữ nguồn read-only và khớp 211 snapshot tài chính/identity.
- CUA: form sửa người thuê trên desktop/mobile375, self-change mobile, reset public, admin reset dialog và Dashboard render đúng; không nhập/submission mật khẩu mới bằng browser. Mutation credential/financial trong suite chỉ dùng dữ liệu giả và API riêng. Ảnh nằm tests/artifacts/rehouse-*.jpg.
- Chỉ source local; chưa commit/push/deploy hoặc nâng cấp database vận hành. QA dùng /tmp và PostgreSQL container riêng, không dùng container rehouse-pg-migration-db. Không sửa thư mục video-shotcraft untracked của người dùng.

Chi tiết file, API, kiểm tra và lưu ý: [account-occupancy-dashboard-report.md](account-occupancy-dashboard-report.md).

## 35. Tối ưu kiến trúc, tài khoản và vận hành — 14/10/2026

- Dashboard PostgreSQL dùng SQL SUM, SQLite fallback decimal để tránh sai số tiền; pending readings dùng NOT EXISTS theo occupancy. Rooms chỉ nạp bản đọc kỳ được chọn và bản gần nhất trước kỳ, SQL MAX riêng cho baseline toàn lịch sử/final legacy, giữ logic nhiều lượt thuê cùng tháng. Invoices thêm kind/lifecycleState/status/search và giữ JSON array. Health CanConnectAsync trả 200/503 với trạng thái database.
- Tự đổi mật khẩu (Owner/Admin) nhận currentPassword/newPassword 8–256, optional confirmPassword tương thích form cũ; hash + revoke mọi phiên khác bằng transaction Serializable, giữ sid/cookie hiện tại. Rate limit auth và Same-Origin giữ nguyên. Reset bằng mã admin vẫn 12–256 mạnh và revoke toàn bộ. AppShell mở Modal dùng Ant Design; route cũ đồng bộ hành vi. Thêm 6 khóa nguồn, đủ vi/en/zh/ko/hi; 908 × 4 check pass.
- Chia vendor theo React, query, icons, Ant Design/core/controls/data/form; phân loại package lồng và giữ utility/context/locale ở tầng dùng chung để tránh circular initialization. Build cuối không có cảnh báo chunk/circular; lớn nhất 484,95 kB (gzip 133,47 kB). Bản build thật đã mở Dashboard và Modal mobile375; dialog359px nằm trong viewport375, đủ ba ô và nút Hủy/Lưu. Không nhập/submission mật khẩu mới bằng CUA; ảnh `/tmp/rehouse-upgrade-qa.VfeGS0/password-modal-mobile.jpg`.
- Cập nhật contract/implementation notes cho trả phòng hoàn cọc thủ công, reports/export và archive/restore. Backup hỗ trợ OFFHOST_SYNC_CMD hoặc RCLONE_DEST từ runtime.env, opt-in; không source toàn bộ file, giữ local và báo lỗi khi sync hỏng, retention chỉ chạy sau sync thành công. Bash syntax + 7 ca hook/rclone/default/priority/failure giả lập pass; chưa chạy off-host thật/VPS.
- Test ban đầu phát hiện fixture SQLite thiếu Kind của PayOsAttempts; đã sửa fixture tường minh Kind=Invoice. Đồng thời phát hiện lỗi callback Invoice có thể ghi tiền khác chủ trọ; sửa PayOsService giới hạn owner URL và bỏ fallback khóa chủ khác, giữ kênh Subscription admin riêng. Service thêm 3 assertion cross-owner (kể cả cùng signing key), không thay invariant signature/amount/idempotency.
- Bản cuối: API build 0 warning/error; service 81 assertions. Full disposable SQLite 59/59: API20, billing17, reports6, payos4, account12. PostgreSQL container riêng `rehouse-upgrade-qa-20261014`, DB rehouse_qa port15494: API20 pass, billing15 pass+2 skip, reports6 pass, account7 pass+5 skip; fixture payos class skip. `Ran 55 tests`, `OK (skipped=8)`; 48 methods pass, 7 methods skip + 1 class skip. Health kiểm tra khi dừng/khởi động đúng container QA: 200→503→200. Logs tại `/tmp/rehouse-upgrade-qa.VfeGS0`.
- **Minh bạch sự cố an toàn:** 2 lượt suite ban đầu chạy callback HTTP localhost nhưng test Subscription upgrade có sẵn vẫn gọi payOS bằng khóa giả; cả hai nhận HTTP200/code214, không có PaymentLinkId/CheckoutUrl, 2 subscription attempts Failed. Vì vậy không được báo toàn phiên là “không gọi API payOS thật”. Đã dừng API, đặt HTTPS_PROXY/HTTP_PROXY/ALL_PROXY về proxy local chỉ trả502 CONNECT, NO_PROXY cho loopback, rồi chạy lại toàn bộ suite trên cả SQLite/PostgreSQL. Outbound sau đó bị chặn tại máy. Tests README nay yêu cầu proxy chặn outbound vì callback HTTP chỉ ngăn đăng ký webhook.
- DB Development thật chỉ đọc trong transaction READ ONLY: trước/sau đều 319 invoice, Total250783641.00, Paid46440913.00, digest theo Id/Total/Paid `aebb0c3698576d08c7f515c15ebb40c9`. Không reset/seed/test/upgrade DB thật, không sửa snapshot `.local-data`; không commit/push/deploy. Không đối soát pending thật. Tài nguyên QA riêng được dừng sau kiểm tra; log/ảnh giữ trong /tmp.

## 36. OTP email xác nhận đăng ký — SMTP (14/10/2026)

- Người dùng yêu cầu xác minh email đăng ký, chọn SMTP rồi nhờ hướng dẫn vì chưa chọn hộp thư. Đã hướng dẫn dùng Gmail riêng, bật xác minh2 bước và tạo App Password; không yêu cầu gửi mật khẩu qua chat. SMTP thật chưa cấu hình và chưa gửi mail thật.
- Register mặc định trả202 challenge, chưa tạo Users. Verify nhận token +OTP6digits, tạo Approved Free Owner nguyên tử và trả201; đăng nhập riêng sau đó. Challenge256-bit30 phút, code10 phút; chỉ lưu token/code hash gắn secret, không lưu OTP plaintext.5 lần sai/mã, resend60 giây và5 sends/email/hour, hạn mức giữ khi đăng ký lại. Resend thay mã, đăng ký lại thay token/credentials để tránh kế thừa mật khẩu do người khác đặt trước. SMTP lỗi rollback reservation, giữ code cũ khi resend lỗi. Concurrent verify một winner; replay bị chặn.
- Sender MailKit4.18.1, TLS bắt buộc khi gửi ra ngoài; None chỉ Development +loopback. From config/SMTP credentials không log; thiếu cấu hình trả503, không bypass. Pkcs10.0.13 ghim theo advisory để dependency audit sạch. Chỉ được tắt OTP bằng EmailVerification__Enabled=false trong Development cho fixture nghiệp vụ; Production từ chối startup nếu tắt (đã kiểm tra trước khi tạo DB).
- FE register hai bước, nhập code một ô numeric/one-time-code; đếm ngược resend, báo hết hạn/lỗi, sửa thông tin, giữ challenge/email/timing trong sessionStorage khi refresh nhưng không giữ mật khẩu/OTP. Copy chờ admin duyệt được thay bằng xác minh email/gói Free. i18n thêm21 chuỗi, tổng929 ×4 catalogs (5 ngôn ngữ gồm vi). Bản build thật test bằng UI fixture synthetic:375px không tràn, validation bỏ trống và quay lại form hoạt động, console không warn/error. Ảnh `/tmp/rehouse-otp-qa.68NCpB/otp-mobile.jpg`; không nhập password hay OTP thật bằng CUA.
- Schema: thêm bảng RegistrationVerifications có hash password đang chờ, state/hạn mức/consumed audit; không đổi Users/Invoices cũ. DbMigrator cho phép snapshot legacy thiếu bảng mới và copy bảng khi có. Đã import disposable snapshot legacy63 invoice và snapshot hiện tại7 registration rows vào DB riêng; nâng startup clone cũ tạo lại bảng với financial digest không đổi.
- QA chỉ dùng /tmp, PostgreSQL container riêng rehouse-otp-qa-20261014 port15497 và SMTP sink127.0.0.1:5098 không relay. Service113 assertions; HTTP OTP SQLite10/10 và PostgreSQL3/3. Full SQLite nghiệp vụ59/59 (OTP classes opt-in skip), không gọi payOS thật; HTTPS/HTTP proxy trỏ loopback từ đầu, callback HTTP localhost, jobs tắt. API/migrator build0 warning/error; frontend build không warning chunk/circular, i18n929 ×4 pass; dependency audit không có package vulnerability.
- Runtime local thật: đã dump backup `/tmp/rehouse-otp-qa.68NCpB/development-before-otp.dump` quyền0600 trước restart. Chỉ thêm bảng OTP rỗng, không reset/seed/integration test vào DB thật. Trước/sau319 invoice, Total250783641.00, Paid46440913.00, digest `aebb0c3698576d08c7f515c15ebb40c9`; RegistrationVerifications=0. API5080/FE5173 health OK, sweep/reconciliation tắt và payOS outbound vẫn chặn. Không sửa SQLite snapshots .local-data.
- File secret `appsettings.Email.local.json` chỉ nạp trong Development, tự reload; ignore đã kiểm tra. Cần người dùng điền Email.FromAddress/Email.Smtp.Username bằng Gmail và Email.Smtp.Password bằng App Password trước khi test gửi thật. Production cấu hình SMTP_* trong runtime.env; host cũ phải cập nhật compose rồi recreate, không chỉ đổi runtime.env. Xem `docs/email-otp-setup.md`; thay đổi mới chưa commit/push/deploy. Dịch vụ QA riêng được dừng sau kiểm tra, local5080/5173 giữ chạy.

## 37. Bỏ thao tác sao chép link thanh toán riêng — 14/10/2026

- Theo yêu cầu người dùng, bỏ nút/icon “Sao chép link thanh toán” ở cả danh sách và drawer chi tiết hóa đơn trong InvoicesPage.tsx; xóa handler/import không còn dùng. Khách tiếp tục dùng link portal đã cấp để xem hóa đơn và thanh toán. Thao tác sao chép tin nhắn thu tiền hiện có giữ nguyên.
- Frontend production build và git diff --check pass. Không đổi API, thanh toán, guest token hoặc dữ liệu; không chạy integration test trên DB thật. Bản local nhận cập nhật qua Vite; chưa commit/push/deploy thay đổi này, các thay đổi OTP đang làm giữ nguyên.

## 38. Thu tiền mặt tự hủy payOS đang chờ — 14/10/2026

- Theo yêu cầu người dùng, POST receipts tự kiểm tra GET order và POST cancel của payOS nếu pending chưa nhận tiền. Xác minh chữ ký, order, amount và amountPaid cho cả hai phản hồi; chỉ CANCELLED/EXPIRED có amountPaid=0 mới ghi cash. Không suy diễn từ lỗi lookup hoặc thiếu link metadata. Hủy trạng thái local + receipt + Paid/Version commit trong Serializable transaction; Status concurrency token và unique pending index ngăn checkout/receipt xen vào. Thu đủ Paid, một phần Partial và checkout sau dùng số dư.
- Nếu provider đã nhận đủ trước/trong lúc hủy: nhánh cash rollback, đối soát payOS bằng bằng chứng đã ký qua luồng receipt hiện có; trả409 giải thích và FE tải lại invoice/dashboard/rooms/reports. Nếu online một phần, trạng thái lạ, sai chữ ký/amount, timeout hoặc chưa xác nhận hủy: không ghi thêm cash. Reconciliation cũ cũng được sửa để không giải phóng order đóng nhưng có khoản tiền chưa khớp.
- Idempotent retry trả receipt cũ trước provider/version check, payload khác409. expectedVersion optional chặn dữ liệu cũ. Remote cancel thành công nhưng DB rollback/timeout: retry lấy lại bằng chứng CANCELLED rồi ghi cash, không hủy local tùy tiện. UI cập nhật lời dẫn/xác nhận, giữ nút đối soát là tùy chọn; i18n940 ×4 catalogs, FE build pass.
- QA provider hoàn toàn giả lập: SQLite165 assertions; PostgreSQL167 gồm hai lần thu đồng thời chỉ một commit. DB riêng mỗi fixture tại instance disposable port15496, không dùng DB thật; test bao gồm cash đầy đủ/một phần, payOS thắng race, partial online, terminal reconciliation, invalid signature/order/amount/missing amountPaid, lỗi mạng, retry/rollback, owner/version/idempotency và callback muộn. HTTP regression59/59 trên SQLite riêng với outbound proxy chặn và callback HTTP localhost. Logs /tmp/rehouse-cash-qa.D6729K và /tmp/rehouse-manual-cancel-service.log.
- Không tạo/hủy thanh toán payOS thật, không ghi cash thử vào DB vận hành. API local5080 đã restart bản mới (session78399), FE5173 vẫn chạy; health200. Đối chiếu READ ONLY trước/sau giữ319 hóa đơn, Total250783641.00, Paid46440913.00; digest định dạng Id:Total:Paid phân cách | là63eb0245c7bc11c9355be6a8efcb591c ở cả hai lần. Không đổi schema cho tính năng cash. API/container QA riêng đã dừng/xóa. Các thay đổi OTP/mục37 giữ nguyên; chưa commit/push/deploy. Local vẫn tắt seed/sweep/reconciliation và chặn outbound payOS trong phiên QA, nên chưa dùng local này để hủy order thật.

## 39. Tên miền roomie.io.vn — DNS P.A Việt Nam (14/10/2026)

- Người dùng mua roomie.io.vn, yêu cầu thao tác trực tiếp trên Edge tại access.pavietnam.vn; cung cấp IP VPS116.118.3.40. Đã lưu A @→116.118.3.40 và CNAME www→roomie.io.vn, TTL3600 qua UI, xác nhận các hàng đã lưu. Query authoritative ns1.pavietnam.vn trả đúng cả hai bản ghi. Screenshot /tmp/roomie-dns-configured.png.
- VPS trả Server:Caddy và HTTP308 khi truy cập IP. Chưa đổi Caddy/PublicBaseUrl/WebhookBaseUrl trên VPS, chưa xác nhận chứng chỉ HTTPS cho roomie.io.vn. Cần quyền SSH quản trị hoặc phiên terminal VPS để hoàn tất; khóa rehouse_cicd_ed25519 hiện có chỉ dành restricted deployment, không coi là quyền shell root. Không yêu cầu mật khẩu qua chat. Không thay email/MX hoặc hồ sơ đăng ký tên miền.

## 40. roomie.io.vn live HTTPS — 14/10/2026

- Người dùng cung cấp quyền SSH root bằng khóa hiện có để hoàn tất domain. Đã xác minh DNS từ VPS đúng116.118.3.40, backup DB/DataProtection bằng helper và config cũ root-only tại /root/roomie-domain-before-20261014. Backup dữ liệu /var/backups/rehouse/rehouse-20261014T110608Z-tKPjuH.
- Caddy mới nhận roomie.io.vn, www301 về apex và giữ roomiedemo.duckdns.org proxy tới cùng app để bảo toàn link khách/payment return/webhook đã cấu hình. Let's Encrypt cấp chứng chỉ hai tên miền thành công; curl xác minh TLS bình thường (không bypass). PUBLIC_BASE_URL/WEBHOOK_BASE_URL=https://roomie.io.vn, trusted proxy172.18.0.1. Recreate app bằng đúng image digest hiện có, không đổi DB/key volumes.
- Health apex và host cũ200, login apex200, www301 đúng đường dẫn, HTTP308 HTTPS. Edge mở trang login thật, không nhập tài khoản hoặc submit; ảnh /tmp/roomie-domain-live.png. Trước/sau READ ONLY:4 invoice, Total30000.00, Paid20000.00. VPS có1 PaymentSettings và1 Pending attempt; không giả định còn DB trống như tài liệu pilot ban đầu. Không tạo/hủy/reconcile giao dịch để test, không đổi đăng ký webhook payOS; callback host cũ giữ hoạt động.
- deploy/Caddyfile và docs cập nhật local theo cấu hình live; chưa commit/push đợt domain. Các thiết lập SMTP vẫn là công việc riêng chưa hoàn tất.

## 41. Rà soát bảo mật đọc-only — 14/10/2026

- User hỏi bảo mật hiện tại. Đọc source + SSH cấu hình live, GET public login/auth/me; không đăng nhập, thử mật khẩu, tạo giao dịch, scan tải cao hay sửa runtime. Live image revision e384a674646bc1c1b3b2779256d77f652d6e5999. Không phải pentest đầy đủ.
- Ưu tiên cao: sshd -T cho permitrootlogin yes/passwordauthentication yes; SSH22 bind public, maxauthtries6/maxstartups10:30:100, fail2ban inactive, host INPUT ACCEPT (UFW không có). Chưa xác minh firewall phía nhà cung cấp hay Match override. Có root key đã dùng thành công, nên bước tiếp theo nên tắt password auth có kiểm tra session thứ hai, cân nhắc user sudo thay root và giới hạn SSH; KHÔNG tự thay trong review.
- apt list --upgradable từ cache hiện có hiển thị nhiều gói noble-security/noble-updates (apparmor/bind9/system libs); unattended-upgrades active nhưng chưa xác minh patch level mới nhất hoặc từng CVE. Cần kế hoạch update/khởi động lại an toàn.
- Header live có nosniff, DENY, Referrer-Policy no-referrer, Permissions-Policy; API401/no-store. Chưa có HSTS/CSP. Source có cookie Secure Production/HttpOnly/Lax, check session DB, owner scope và Origin guard, auth rate limit10/IP/min. Không thấy MFA/admin hay account-based login lockout (giới hạn hiện tại theo IP). Guest link là bearer secret256-bit, người giữ link có quyền xem/khởi tạo payment theo thiết kế; không coi OTP email chứng minh danh tính thật.
- Runtime secret/bootstrap file0600 root, backup dir0700; app userapp/read-only/no-new-privileges/drop ALL, API chỉ127.0.0.1:8080, Caddyadmin127.0.0.1:2019 và DB không publish. Backup timer đang có, backuphelper live chưa có hỗ trợ offhost trong source mới; chưa xác minh backup ngoài VPS/restore drill.
- npm audit --omit=dev:0 known vulnerabilities; dotnet package audit include-transitive:0 known vulnerabilities theo feeds hiện tại. Không kết luận toàn hệ thống không có lỗ hổng hoặc không bị xâm nhập. Cloudflare không tự khắc phục SSH/patching/MFA/backups.

## 42. Đổi thương hiệu hiển thị thành Roomie — 14/10/2026

- User yêu cầu đổi tên toàn hệ thống. Đã đổi tên hiển thị ở AppShell desktop/mobile/footer, login/register/account status, tenant portal +checkout dùng chung TenantHeader, copy payOS và gói Pro, tiêu đề HTML/document.title cả5 ngôn ngữ, description metadata, subject/body/from-name mặc định OTP, tên file Excel frontend/backend thành roomie-*.xlsx. README/hướng dẫn SMTP cập nhật tên. Logo nhà/màu/bố cục giữ nguyên theo yêu cầu đổi tên.
- Bộ i18n source/TSV/catalogs đồng bộ940 khóa ×4 pass; FE production build pass, API build0 warning/error, diffcheck pass. Edge local xác nhận login Roomie +title Roomie — Quản lý phòng trọ; ảnh /tmp/roomie-rebrand-login.png. Không submit form hoặc chạy kiểm thử tài chính trên DB thật.
- Giữ nguyên cookie/sessionStorage/localStorage keys, DataProtection purposes, namespace, DB/volume/service/đường dẫn vận hành và GitHub/GHCR repo tên cũ để bảo toàn session/secrets/dữ liệu/deploy. Đây là tên kỹ thuật nội bộ, không còn xuất hiện như thương hiệu trên sản phẩm. File SMTP local ignored chỉ đổi FromName nếu đang là Rehouse; không đổi credentials, không gửi mail thật.
- Người dùng đã yêu cầu commit và push đợt đổi thương hiệu cùng cấu hình domain/docs mục39–41 lên main. Build/i18n đã đạt như trên; push kích hoạt CI/publish/deploy, chỉ kết luận live cập nhật khi workflow deploy thành công. Secrets SMTP local bị ignore và không đưa vào commit.

## 43. Brevo SMTP cho OTP — 14/10/2026

- User tạo tài khoản Brevo Roomie, yêu cầu thao tác Edge. User xác nhận riêng việc thêm4 DNS records, cấp quyền gửi bằng tên miền, tạo SMTP key Roomie OTP và lưu secret trên VPS; xác nhận gửi đúng1 email kiểm tra tới Gmail của mình. Không mua/nâng gói; UI báo300 email/ngày.
- P.A DNS đã lưu TXT xác minh Brevo,2 CNAME brevo1._domainkey /brevo2._domainkey và TXT DMARC p=none,rua về Brevo (đã được user chấp thuận). Authoritative DNS trả đúng; Brevo xác nhận cả4 record khớp và domain authenticated. Không đổi A/www/MX. Sender noreply@roomie.io.vn tênRoomie Verified, DKIM theo domain và DMARC configured. Ảnh /tmp/roomie-brevo-domain-authenticated.png.
- SMTP key riêng Roomie OTP Standard, expiry UI14/10/2027 và hết hạn sau90 ngày không dùng. Secret chỉ lưu /etc/rehouse/runtime.env quyền0600 root, không log/chat/Git. File trung chuyển tạm được xóa cả local/VPS, key trong runtime cua đã bỏ biến. SMTP smtp-relay.brevo.com:587 StartTls. VPS compose cũ thiếu mappingSMTP; đã thêm riêng7 Email__* env mappings và recreate app cùng digest hiện tại. Kiểm tra SMTP injected=true/TLSStartTls/sender đúng, health local+public200. Không thay keypayOS hoặc ghi tài chính.
- Backup trước đổi /var/backups/rehouse/rehouse-20261014T141126Z-e2VgjH và config cũ /root/roomie-smtp-before-20261014. SMTP TLS+login thành công. Đúng1 thư kiểm tra không chứaOTP gửi bằng SMTP từ VPS, máy chủSMTP chấp nhận. Brevo Logs vẫn0 và onboarding Verification “Waiting for log”; CHƯA xác nhận Gmail delivery, chưa tạo user thử hoặc test OTP thật qua API. Không tự suy diễn đã Delivered hoặc account activation hoàn tất. User được hướng dẫn kiểm tra Inbox/Spam. Nếu không nhận cần kiểm tra Brevo activation/log sau, không gửi lặp tự động.
- Workflow rebrand6c0d6f4 đã success (38575829507). Runtime SMTP đã cấu hình live, tài liệu bàn giao cập nhật local chưa commit/push. Local Development SMTP vẫn rỗng; chỉ VPS được user cho lưu key.

## 44. Kiểm tra thư Brevo chưa tới Gmail — 14/10/2026

- User báo không nhận thư. Brevo Logs hiện1 event Sent cho đúng thư21:12; xem history chỉSent từIP77.32.148.25, chưaDelivered/Bounce/Deferred/Blocked. Message-ID179198714754.3427847.17281573457235103919@roomie.io.vn. Gmail tìm in:anywhere from:noreply@roomie.io.vn không có kết quả; không đọc các email ngoài phạm vi điều tra.
- DNS public1.1.1.1: cảDKIM CNAME resolve tới public keys đúng, DMARC đúng. APIhealth200. UsageBrevo còn300email và0paused; không phải quota hết. Completed SMTP onboarding “It works!” sau khiBrevo nhận diện eventđầu, chưa coi làdelivery. Official statusBrevo operational; không có bằng chứnglỗi hạtầngtoàncục.
- Chưa xác định nguyên nhân nội bộ giao nhậnBrevo/Gmail; không được khẳng địnhchắc làchưaactivate, spamhoặcDNSsai khi chưacóbằngchứng. Userđược hỏi xinphép gửi thêm đúng1test vì lượttrước chỉcho1email; chờphảnhồi, chưagửithêm. Ảnh /tmp/roomie-brevo-sent-not-delivered.png. Nếu cóphép testlại theo dõiSMTPresponse/Message-ID vàBrevoDelivered; khôngtạouserthử trênDBthật.

- Tiếp mục44: userđồngý gửi thêm đúng1email, đã gửi subject “Roomie — Kiểm tra gửi email lần 2”, Message-ID179198760278.3429262.4736557813570049769@roomie.io.vn. SMTPDATA250 “OK: queued as” cùngID; chưacoilàDelivered. Khônggửithêm. Draft hỗtrợ /tmp/roomie-brevo-delivery-support.txt chưa gửi (cần userủyquyền liênhệBrevo).

- Userđãủyquyền gửi ticketBrevo kèmdomain/sender/recipient/2messageIDs. ClickHelp→SupportandTickets quaUI mởtab1262497990 nhưng browserURLpolicy chặnvìinvalidURL, yêucầukhôngworkaround. CHƯA gửi ticket; khôngđổibrowser/hackURL/callAPIđểvượtchặn. Userđược hướngdẫn gửitrựctiếp bằngdraft /tmp/roomie-brevo-delivery-support.txt. Cả2logđãhiệnSent; chưaDelivered/Gmailkhôngcókếtquả.

## 45. Thử địa chỉ nhận thứ hai — 14/10/2026

- User cungcấpảnh Gmail quandev051@gmail.com đểthửnhận. Đãgửiđúng1thưtớiđịachỉnày, subject Roomie — Kiểm tra gửi email tới tài khoản thứ hai; khôngOTP, khôngtạouser. SMTPDATA250queued, Message-ID179198885620.3432314.14742206766741529510@roomie.io.vn.
- Địachỉthứhai vẫn làGmail; khôngdùngphépthửnày đểkếtluậnlỗiGmailso vớiOutlook/Yahoo. Hai thưcũ lúc21:12/21:20 vẫnSent,chưaDelivered trênBrevo khi mởkiểmtralại. Khôngmua/nângplan, khôngtựgửithêmemail.

## 46. Chuyển SMTP sang Resend — 14/10/2026

- UsertạotàikhoảnResend vàxácnhận riêng:3DNSrecords, khóaSending access chỉroomie.io.vn, lưuVPS, tốiđa1testmỗiđịachỉanhquan050503@gmail.com/quandev051@gmail.com; chỉchuyểnruntimekhi cóDelivered.
- P.ADNS thêmTXT resend._domainkey(publickeytừUI), CNAME rsend→rsend-apne1.forge.rmta.net vàsend→send.forge.rmta.net. GiữA/www/MXvàrecordBrevo; DMARCđangp=none/ruaBrevo. DNSauthoritative+1.1.1.1trảđúng. ResendTokyo(ap-northeast-1)DNSverified21:47,domainverified21:49. KhôngkíchhoạtReceiving.
- KhóaRoomieOTP hạnchếSendingaccess/domainroomie.io.vn, tạoquaUIkhôngfullaccess. Secretchỉruntime.env0600root, khôngGit/chat/log; filetrungchuyểnJSONlocal/VPSđãxóa, biếncuađãbỏ. Hostsmtp.resend.com:587/StartTls,userresend.
- TestSMTPthậtđúng2thưkhôngOTP, khôngtạotàikhoản:anhquan ID01a13ae4-f5e8-7d87-aea1-834673e9821b;quandev ID01a13ae5-b6bf-7778-8049-e61f0ef99db1. CảhaiResendEmailsđềudelivered, screenshot/tmp/roomie-resend-delivered-both.png. Deliveredlàmáychủnhậntiếpnhận, chưađọchộpthưGmailđểxácnhậnInbox/Spam. KhônggửithêmthửhoặcOTPthậtvượthạnmứcđãchấpthuận.
- SauDeliveredđầu, backupDB/keys /var/backups/rehouse/rehouse-20261014T145132Z-ubwTDc; configBrevo cũroot-only /root/roomie-before-resend-20261014/runtime.env. Chuyểnruntime7SMTPvars sangResend,recreateappcùngimage. Inspectchỉinhost/TLS/sender/boolsecret:đúngResend/StartTls/noreply;publichealth200. Khôngđổicomposevolume, khôngghihoáđơnhoặcpayOS.
- Kếtluậnvận hành:SMTPResendđãlivecóbằngchứngDeliveredcả2Gmail. LuồngOTPcodeđãtestfakeởmục36, chưađăngký/verifytạouserthậttrongđợtnày. NguyênnhânnộibộBrevochưađượcgiảiquyết, khôngkhẳngđịnhđãbiếtrootcause. Brevokeyvẫnởprovider/configbackup,chưathu hồi; sourceappkhôngcầnđổi. DocsđợtSMTPchưa commit/push; khôngpushsecrets.

## 47. Thiết kế và triển khai Landing Page — 14/10/2026

- **Triển khai `LandingPage` và route `/` & `/welcome`:**
  - Tạo `frontend/src/pages/LandingPage.tsx` và `frontend/src/pages/LandingPage.css`.
  - Giao diện đồng bộ bảng màu Navy (`#15343e`), Teal (`#109d83`), Canvas (`#f4f7f6`) và font `Manrope` / `DM Sans`.
  - Cấu trúc:
    a) Sticky Header: Logo Rehouse, `LanguageSelector` (5 ngôn ngữ: vi, en, zh, ko, hi), nút `Đăng nhập` (`/login`), CTA `Đăng ký làm chủ trọ` (`/register`) và phím tắt `Bảng điều khiển` khi đã đăng nhập.
    b) Hero Section 2 cột: Cột trái thông điệp cốt lõi, 2 CTA và 3 lợi điểm nổi bật; Cột phải UI Mockup HTML/CSS gồm thẻ thống kê phòng (12 phòng, 11 đang thuê, 92% lấp đầy, doanh thu tháng), hóa đơn mẫu `P101` (tiền phòng, điện, nước, dịch vụ) và chip QR payOS.
    c) Lưới 6 tính năng chính: Dãy trọ, Phòng & người thuê, Chỉ số điện, Hóa đơn, Thanh toán trực tuyến (payOS), Báo cáo & Đối soát.
    d) Cách hoạt động 3 bước (trải nghiệm liên kết riêng tư của khách thuê không cần app) kết hợp thẻ Bảo mật giao dịch (mã hóa máy chủ, webhook đối soát, thu hồi tự động).
    e) Banner CTA 3 bước tạo tài khoản chủ trọ và Footer đồng bộ.
- **Routing trong `frontend/src/App.tsx`:** Lazy-load `LandingPage` cho `/` khi `!user`, thêm route `/welcome` trực tiếp để người dùng đã đăng nhập vẫn xem lại trang giới thiệu khi cần; giữ nguyên tự động điều hướng vào `/dashboard` hoặc `/admin/owners` khi đã có phiên đăng nhập tại `/`.
- **i18n & Build:** Bổ sung các khóa mới vào `source.json` và `translations.tsv` (4 ngôn ngữ: en, zh, ko, hi). Chạy `i18n:check` PASS 989 keys × 4 catalogs. `npm run build` PASS (chunk `LandingPage-Bo2ucVIc.js` 14.99 kB / gzip 3.84 kB).
- **Responsive DOM Verification:** Kiểm tra tự động bằng Chrome headless qua CDP tại 320px, 375px, 768px, 1440px: cả 4 kích thước đều đạt `hasHorizontalScroll = false` và `overflowingElements = 0` (không tràn ngang).


## 48. SEO tuần 1–2 — 15/10/2026 (local, chưa phát hành)

- Người dùng chốt SEO cho chủ trọ Việt Nam trên `roomie.io.vn`, 8 tuần/4 bài, chỉ Search Console. Yêu cầu mỗi đợt SEO phải có file ghi toàn bộ quá trình. **Đọc [kế hoạch SEO](SEO_PLAN.md) và cập nhật [nhật ký SEO](SEO_LOG.md) sau mọi đợt SEO tiếp theo**, phân biệt local/QA/commit/deploy/index.
- Source đã có prerender landing dùng chung React, CSS SSR/theme dùng chung; public index tách app shell riêng. Canonical `/`,301 `/welcome` và `/index.html` giữ query; title/description/OG/Twitter/PNG1200×630/Organization/WebSite; robots/sitemap sinh từ registry public (hiện chỉ `/`).
- Backend noindex trang riêng/API/demo/ngôn ngữ khácvi và lỗi HTTP,404 thật cho URL lạ; giữ link khách/callback/auth hiện có. Development mặc định noindex, Production chỉ hostroomie.io.vn; `Seo__AllowIndexing`/Compose`SEO_ALLOW_INDEXING` cho phép tắt toàn bộ index.
- QA: frontend/backend build PASS; i18n1014×4 PASS;13/13SEOHTTP và20/20API trên SQLite tạm PASS; UI chuyển metadata và login/root→dashboard PASS, console sạch; HTML tĩnh375/1440 không tràn. Chi tiết, lệnh và giới hạn ở nhật ký. CI bổ sung suite SEO nhưng chưa chạy GitHub.
- Chưa commit/push/deploy; website public robots/sitemap vẫn404 khi kiểm tra trước phát hành. Search Console chưa đăng nhập/xác minh/gửi sitemap; chưa có baseline hoặc xác nhận Google index. Không chạy test trên DB thật, không gửi OTP hoặc tạo thanh toán thật. Nội dung tuần3–6 chưa làm.

### Tiếp mục48 — Search Console trên Edge

- User đã đăng nhập Search Console trong Edge. Đã thêm Domain property`roomie.io.vn`; Google cấp TXT và đang chờ DNS verify, chưa thành công/chưa gửi sitemap. Tab Google được giữ.
- Trang P.A Việt Nam mở tại`https://access.pavietnam.vn/dang-nhap`, cần user đăng nhập; chưa đổi DNS. Nhật ký SEO đã cập nhật. Source vẫn local, chưa phát hành.

### Tiếp mục48 — DNS/Google xác minh thành công (15/10/2026)

- Sau user đăng nhậpP.A: đã thêm1TXTGoogle host@,TTL3600 quaUI; tải lại thấy10records,9records cũ giữ nguyên. Authoritative ns1.pavietnam.vn vàGoogleDNS8.8.8.8 đã trả TXT mới.
- Search Console báo **Đã xác minh quyền sở hữu**, Domain property`roomie.io.vn`. Tab Tổng quan trongEdge được giữ cho user; báo cáo đang xử lý dữ liệu, chưa có baseline/index kết luận. GiữTXT để duy trì quyền sở hữu.
- `https://roomie.io.vn/sitemap.xml` vẫn404; chưa gửiGoogle/chưa yêu cầu index. SourceSEO vẫn local, chưa commit/push/deploy. Nhật ký và checklist đã cập nhật, ảnh xác nhận `/tmp/roomie-gsc-verified.jpg`.

## 49. Chặn mọi bot qua robots.txt — 15/10/2026

- User xác nhận “robots.txt đóng hết tất cả”, gồm cảGooglebot; tạm dừng mởcrawl/gửi sitemap/yêu cầuindex cho đến khi user mở lại.
- `frontend/public/robots.txt` chứa `User-agent: *` và `Disallow: /`. Prerender local không còn ghi đè thànhAllow, suiteSEOHTTP13/13PASS và buildPASS.
- Commit`65d4907` chỉ gồm file robots2dòng, đã pushmain để pipeline hiện có phát hành; đang chờCI/deploy và kiểm tra public. Các thay đổi SEO trước đó/nhật ký vẫn local. Chi tiết ởSEO_LOG.

### Mục49 — đã live

- Pipeline38672131045 success cảverify,publish,deploy cho commit65d4907. Chỉfile`frontend/public/robots.txt` được phát hành. HTTPSapex vàwww/robots.txt đều200,trả chính xác `User-agent: *` / `Disallow: /`; chặncrawlđãlive.
- Giữchặn cảGooglebot theo yêu cầu mới, không gửi sitemap/requestindex hoặc tự mở lại. Các thay đổiSEOkhác vàlog vẫnlocal.

## 50. Sửa CI và chặn cấp link payOS đầy đủ — 15/10/2026

- User yêu cầu sửa và push sau review run38676273367 của2e28b8d: hai integrationtest rotate-link fail400 vì fixture chưa xác minhpayOS, khiến publish/deployskip.
- Tích hợp3file sửa đang có: CI chỉbypass trongDevelopment, Productioncấmbypass, README lệnhtest. Điều kiệnchung còn ẩn token/url trongroom/occupancy create/update/list/detail vàinvoicelist/detail để không láchquaAPI khác. Linkđãchia sẻ vẫnxemhóađơn, khôngđổiDBschema.
- Thaytest tựlặpif bằngHTTPfixtureTestServer route thật, auth/payOSfake, DBin-memory, policy=true. Frontendtypesnullable/APIcontract/hướngdẫnpayOS cậpnhật.
- Local:206assertionservice/HTTP PASS; SQLite72testsOK(skipped2); PostgreSQL17 riêng API20/20+SEO13/13PASS; ProductionbypassfalsebịchặntrướckhitạoDB; FEbuild vài18n1030×4PASS. KhôngchạmDBthật/OTP/payOSthật, dịchtạmđãdừng. Chi tiết ở[ci-payos-link-fix.md](ci-payos-link-fix.md).
- Đangchuẩnbịpush vàtheodõiCI/CD, chưakếtluậndeploy. GiữrobotsDisallowmọibot, khônggửisitemap/requestindex.

### Mục50 — xác nhận đã triển khai

- Commit46f1aad đãpush; run38680393633 xanh cảverify,publish,deploy. KiểmtraHTTPS: home cóH1/canonical200,health200,robotsblockall200,sitemap200,APIroomsunauth401/noindex. KhôngtestghiDBproduction.
- SourceSEOtrước đó cũngđãlive trongimage này, nhưng chínhsáchrobotschặntoànbộ giữnguyên; chưarequestGoogleindex hoặcgửisitemap. SEO_LOG/SEO_PLAN vàreportđãcậpnhật.

## 51. Chuyển sang EF Core Migrations — 15/10/2026

- User cho phép bỏ dữ liệu cũ và xác nhận reset cả local + VPS. Đã cung cấp SSH key quản trị đúng VPS116.118.3.40; kết nối root thành công. Không dùng key CI để chạy lệnh quản trị.
- Hai context theo provider kế thừa AppDbContext; initial migrations/snapshots SQLite và PostgreSQL, local dotnet-ef10.0.12, startup MigrateAsync thay EnsureCreated/manualDDL. Seed/bootstrap giữ nguyên. DbMigrator import cũng dùng migrations. CI kiểm tra snapshot cảhai.
- Thao tác reset riêng có SQL transaction/allowlist/từchối DB đãmigrate; không autoerase. Xem [hướng dẫn và nhật ký](ef-core-migrations.md). Đây là checkpoint trước cutover; kết quả hoàn tất được ghi ngay bên dưới.
- Giữ robots Disallow mọi bot, SMTP/secrets/DataProtection; không gửi OTP hoặc payOS thật.

### Mục51 — hoàn tất 16/10/2026

- Local và VPS đã reset đúng DB room_rental theo yêu cầu, tạo schema bằng EF migrations. Local có admin+owner demo,0invoice,API5080healthOK. VPS có1adminbootstrap,0room/invoice,1initial migration; không còn SchemaUpgrades.
- CI đầu38682614280 fail do SDK container snapshot check thiếu EF Design; c6b9f43 bổ sung restoreforce, kiểm chứng container sạch PASS. [Run38782104449](https://github.com/nhaq0505/ReHouse/actions/runs/38782104449) SUCCESS cả build/test,publish,deploy; live revision c6b9f432904ded5fe1cb9b3b1dfcab707a0359d1.
- Backup local `/tmp/roomie-ef-backups/local-before-migrations.dump`; VPS trước reset `/var/backups/rehouse/rehouse-20261015T180513Z-kXOpKF`. Hook reset dùng1lần đã gỡ và helper gốc được khôi phục; không gài reset cho CI tương lai.
- Publichealth/home/robots200,robots vẫn Disallow mọi bot. Runtimeenv/SMTP/DataProtection/SSH giữ nguyên; owner/payOS config cũ đã xóa theo yêu cầu, cần tạo lại owner và cấu hình payOS. Không test ghi production; tài nguyên QA riêng đã dọn. Chi tiết ở ef-core-migrations.md.

## 52. Security/tech debt review — 16/10/2026

- User hỏi rủi ro còn lại; đã rà soát read-only source/VPS/HTTP/dependency advisories, không đổi cấu hình hoặc dữ liệu. [Báo cáo](tech-debt-remediation-2026-10-16.md) hiện local, chưa commit/push.
- Ưu tiên: SSH root/password vẫn bật trên22public, host chưa có input filter/fail2ban; DB app role thực tế là superuser; backup helper live chưa có off-host code như source, chưa xác minh off-site restore.
- Tiếp theo: MFA admin/account throttling, thống nhất password policy, bỏ bootstrap secret khỏi runtime sau khi có recovery, CSP/HSTS, che tenant token ở mọi nhánh log conflict. Tech debt: migration startup + rollback image không rollback schema, source/live helpers lệch phiên bản, payment/endpoint modules lớn.
- Audit frontend production dependencies và API direct/transitive packages không có advisory được báo ở thời điểm kiểm tra. Không kết luận không có lỗ hổng; chưa audit OS/images/provider firewall hoặc pentest. Robots block-all giữ nguyên.

## 53. Backup Google Drive mã hóa — hoàn tất 16/10/2026

- User có Drive5TB, cung cấp folder[private-folder-id] **Roomie Backups**, xác nhận tài khoản [account-owner]. Đã cấp quyền qua Edge và duyệt công khai3trang thông tin `/backup-info/*` + OAuthProduction.
- Google Cloud project riêng **Roomie Backup** `[private-cloud-project]`, Desktop client **Roomie VPS Backup**, DriveAPI enabled; AudienceInproduction. Không sửa appTVOnline. OAuthscopefullDrive đã giải thích/chấp thuận; folderID không phải ranh giới bảo mật token.
- VPS rclone crypt + root0600config, helper/service/timer/Caddy đã cập nhật khớp source, backup.env root0600 gọi syncscript. Daily03:00–03:20VN và pre-deploy đều backupPostgres+DataProtection+manifest/checksum rồi uploadcrypt/checkdownload trước retention7daily/4weekly/3monthly; local10bản. Syncfail giữlocalvàchặnappswap.
- Backup thật đã tải xuống/giải mã, SHA256khớp, restorePG17local riêng networknonePASS (1admin/0invoice/initialmigration), DataProtectionXMLvalid. Systemdthật buộcrefreshtokenPASS Resultsuccess/Exit0,4matchingfiles/0differences; container và file giải mãtạmđãdọn. Khôngrestoreproduction/OTP/payOSthật.
- Recoverypassword+salt tại `/Users/nguyenhoanhquan/Documents/Roomie Backup Recovery/recovery.json`0600,thưmục0700; usercầncấtpasswordmanager/offlineriêng. Khôngđưa filekhóa/OAuthtokenvàogit/chat/Drivebackup. Đọc [nhật ký và phục hồi](backup-operations.md).
- Chỉ công khaiHTMLthôngtin đãduyệt,200/noindex,robotsblockallgiữnguyên; health200. SSHroot/passwordvàDBsuperuserchưasửa. Source/tài liệu thayđổilocal, chưacommit/push; không cần deploy lại imageapp cho đợt hostconfig này.

## 54. Đồng bộ tài liệu vận hành — 16/10/2026

- Đối chiếu và cập nhật README, deployment, implementation notes, scaling runbook và tests/README theo trạng thái migrations/backup hiện tại. Bỏ các mô tả hiện trạng cũ về EnsureCreated và backup chỉ trên VPS; các checkpoint lịch sử được giữ và ghi rõ để đối chiếu.
- Nhật ký backup ghi cấu hình, OAuth Production, lịch chạy, retention, bằng chứng tải xuống/giải mã/restore và cách phục hồi; báo cáo bảo mật đánh dấu phần backup đã xử lý, giữ rõ SSH và quyền DB còn tồn đọng.
- Chỉ ghi vị trí lưu bí mật và quy trình sử dụng; không chép token, client secret hoặc khóa giải mã vào tài liệu. Cấu hình host đã live; các thay đổi source/tài liệu đợt này vẫn local, chưa commit/push.
- Đợt đồng bộ này chỉ sửa tài liệu; kiểm tra diff và liên kết nội bộ, không chạy lại test hay thay đổi VPS/database.

## 55. Rà soát tech debt bổ sung — 16/10/2026

- User yêu cầu rà soát tiếp; đã đọc source backend/frontend, CI, các suite test, backup/deploy và kiểm tra VPS read-only. [Báo cáo cập nhật](tech-debt-remediation-2026-10-16.md) có TD-01–TD-10, bằng chứng và tiêu chí hoàn tất, phân biệt phát hiện mới với việc còn mở.
- Xác nhận lại live: SSH root/password vẫn bật; PostgreSQL room_rental vẫn superuser/createdb/createrole. Backup service Result=success/Exit0, timer active, nhưng timeout tổng infinity và OnFailure trống. Không sửa cấu hình host/DB.
- Phát hiện mới: workflow không chọn45 Python HTTP test đã có (billing17/reports6/account12/OTP10), transaction OTP giữ qua SMTP, chưa cleanup registration hết hạn, backup chưa deadline tổng/cảnh báo và một số danh sách chưa phân trang. Nợ frontend về test/error recovery và tái lập hạ tầng/build được ghi riêng P3.
- Ưu tiên tiếp: SSH/DB privileges và redaction log tenant; regression fixtures/CI và backup deadline/alert; sau đó migration job, SMTP transaction/retention. Backup off-host/restore đã hoàn tất, không ghi lại thành việc chưa làm.
- Chỉ phân tích tĩnh/query cấu hình; không chạy test ghi trên DB thật, gửi OTP/payOS hoặc deploy. Tài liệu/source vẫn local, chưa commit/push.

## 56. Tech debt remediation - released16/10/2026

- User authorized handling the full reviewed debt list. Main code592b61f and account-budget follow-upb9a49e5 were pushed; CI38843210171 and38843629888 passed verify/publish/deploy. Live revision is b9a49e5e31ec34d6983177cb790281d508119895. Documentation-only changes do not change the app image revision.
- SSH operator roomie-ops/key/sudo tested before/after disabling root/password; nft host input policy drop allows22/80/443, established/loopback/ICMP without flushing Docker rules. Rollback timer canceled only after a new successful SSH connection and HTTPS health.
- PostgreSQL bootstrap superuser renamed roomie_dba (OID10 cannot be demoted); a new room_rental owns migrations, roomie_app has DML only. Both app/migration role superuser/createdb/createrole flags false, runtime cannot CREATE schema or modify EF history. Root-only admin credentials separated, bootstrap credentials removed from app env. Additive migration2 applied in deploy job before app swap; no resets or Down migrations.
- Admin TOTP + one-use recovery codes, persistent hashed-account budgets (login/MFA/change/recovery), new-password policy12-256 and tenant path redaction. Production gates admin business routes until owner enrollment. No agent enrollment, credential exposure or live OTP/payOS call.
- OTP commits reservation before SMTP and compensates unchanged reservation on failure; no transaction held over email. Independent retention job every10minutes batches200, keeps expired rows7days, with indexes. Invoice/admin/tenant server pages and full-filter totals; mobile table now honors controlled pagination. API timeout, auth503 retry/error boundary and synthetic browser regressions. Large modules split by operation.
- CI runs isolated SQLite75/75, PostgreSQL58/50pass/8explicit skips, OTP10/10, browser5/5, backup tests7/7 and real role provisioning fixture; image tested is the artifact promoted, base/action digests pinned. Recorded service suites207SQLite/209PostgreSQL assertions. Dependency reports at this checkpoint: frontend npm audit0, API NuGet0reported; no OS/provider-wide audit claim.
- Backup15-minute deadline for direct/timer/pre-deploy, systemd16minutes, rclone subprocess300seconds/network bounds, SMTP TLS failure hook + hourly27-hour watchdog. A false-positive initial alert occurred before the last-success marker existed; timer changed to OnActiveSec1h and current successful backup creates marker. SMTP acceptance confirmed; provider Delivered lookup unavailable; no repeated live test email.
- Latest Drive snapshot rehouse-20261016T031149Z-bw09fc: downloaded/decrypted4files, SHA256/keyXML passed, restored under migration ownership in network-none PostgreSQL17, runtime reads2migrations/1admin/0invoices. Temporary decrypted files/restore containers removed. Production not restored, invoice aggregates unchanged0/0/0. Local migration preserved0/0/0; Development API5080/outbound guard/payment jobs disabled retained. Private pre-migration local DB+default ASP.NET key ring archive at /tmp/roomie-hardening-before-local-migration.
- Public operating instructions: [remediation/release](tech-debt-remediation-2026-10-16.md), [backup](backup-operations.md). Full account/folder audit stays in local-only docs/google-drive-backup.md and docs/security-tech-debt-review-2026-10-16.md; recovery keys remain outside repo. Host rollback config at /root/roomie-hardening-20261016, root-only. Single-instance/HA/PITR/immutable storage and broad load/OS/provider audit limits are documented, not silently provisioned.

### Follow-up to56: scoped pagination and session ordering

Older checkout uses invoiceId scoped to the tenant occupancy; secondary invoice screens fetch bounded pages for their period/occupancy and retain data after100rows. Added older-invoice HTTP regression,102-row frontend API test and delayed-/me login race regression. Follow-up local checks: SQLite76/76, PostgreSQL59run/8known skips, browser7/7. CI38845308613 failed only because the role fixture saw the temporary PostgreSQL init server; final TCP readiness fixed, CI38845575264 succeeded. Final app candidate4f3d1bc is going through CI38845852582; release completion below will confirm live state.

### Final completion56

CI38845852582 passed verify/publish/deploy, live revision4f3d1bce01ef3a7482d85978b7fea0870a7c32a6 verified. SQLite76/76, PostgreSQL59run/8explicit skips, browser7/7, OTP10/10, backup tests7/7 and frontend102-row paging check PASS. Final Drive snapshot rehouse-20261016T083836Z-5YVMHS verified with four matching files/SHA256; restore verification is stored privately. Financial values above belong to earlier checkpoints: new non-fixture owner/business activity appeared later and has been retained. No resets or mutating production integration tests. Historical admin enrollment count0; the requirement is superseded by section57 below. Documentation/test-fixture-only follow-up commits do not change the live app image revision.


## 57. User-requested MFA removal — 16/10/2026

- Removed the login code field, enrollment page/route, backend MFA verification/service/setup/confirm endpoints and Production admin enrollment gate. Admins with either legacy MFA flag can sign in using email/password.
- Compatibility: login still accepts and ignores optional `code`; login/me return both MFA flags as false. Legacy columns, concurrency mappings and applied migrations are unchanged; no schema migration or data reset.
- Registration email OTP, reset tokens, password policy, persistent account/IP throttling, authorization, session expiration/revocation and Origin checks remain active.
- Replaced enrollment/replay tests with password-only login against legacy enabled/disabled rows, compatibility responses, bad password rejection, admin access, logout and removed endpoints. Browser coverage checks no code input, direct admin access with stale flags and no enrollment at the old URL.
- Validation and release status: see [MFA removal log](mfa-removal-2026-10-16.md). This task changes the workspace only; it does not push/deploy or modify production.


## 58. Simpler password policy — 16/10/2026

- User requested passwords longer than eight characters only: minimum9, maximum256 retained. No uppercase, lowercase, digit or symbol requirement; whitespace-only input remains invalid and confirmation must still match.
- Applied consistently to registration (including OTP), password change, admin-issued password reset and bootstrap admin creation. Existing passwords are not changed and login/current-password checks retain compatibility.
- Registration/change/reset UI and all four translation catalogs reflect the rule. Hashing, session revocation, reset-token handling and request/account limits are unchanged.
- Verification results: [password policy log](password-policy-2026-10-16.md). Local source update only; no push/deploy or production data changes.


## 59. Auth pages return to landing — 16/10/2026

- Login and registration now expose an explicit `Về trang chính` React Router link to `/` above the form. Registration keeps its existing bottom login link, and the home link remains visible during the OTP step.
- Reuses existing back-link styling, icon and translated copy; gives the auth-page link a44px target and existing dark teal token. No backend/session/OTP changes. Authenticated visits to `/` still follow the existing dashboard redirect.
- Validation: four real-browser navigation checks PASS (login/register at375/1280px); screenshots inspected; frontend build, i18n and diff checks PASS. UI probes still report pre-existing muted-text/input issues outside this change; the new home link has a44px target and dark teal contrast. Source/local only, not pushed or deployed.


## 60. Combined auth update release — 16/10/2026

- User authorized pushing sections57–59 to main: MFA removal, password minimum9 without composition rules, and landing-page return links on login/register.
- Local verification completed as recorded above. This release entry supersedes the earlier local-only status; CI/CD is the source of truth for publication/deployment. [Actions](https://github.com/nhaq0505/ReHouse/actions).
- No schema changes, database reset or live-provider test calls. Private recovery/config files are excluded from this commit.


## 61. Mobile payment header overflow — 16/10/2026

- Reproduced `Thanh toán trực tuyến` wrapping out of the fixed-height header into compact navigation at375px. The header's inherited line-height amplified wrapped text; in production, late-injected Ant Design header styles also overrode the application rule at equal specificity.
- Set normal header line-height, allowed the left/title flex items to shrink, kept title text on one line with ellipsis, and protected language/profile/menu controls from shrinking. Scoped header rules to `.topbar.ant-layout-header` across existing breakpoints so the production build retains application dimensions.
- Before/after screenshots inspected at mobile/desktop with a synthetic owner and fake payment-settings responses; no provider call or payment-settings write. Added Playwright regression at320/375/768px checking header/title/navigation bounds, no page overflow, and visible language/profile controls.
- Frontend build, i18n check, diff check and all10Playwright cases PASS. Local Vite reflects the fix. This follow-up is not yet committed/pushed/deployed.


### System Design document — 17/10/2026

- User selected the System Design artifact template. Created [Vietnamese architecture document](system-design/Roomie_System_Design_2026-10-17.docx) from the retained DOCX, not a blank style preset; reference unchanged.
-12sections/7pages cover architecture diagram, requirements, components, business flow, receipt/API contracts, consistency, security, operations, tradeoffs and open decisions. Baseline e869b76 CI/CD confirmed successful via Actions; no new live-server inspection or application test run claimed.
- Rendered and inspected all pages; section/styles/numbering/font/header preservation and package inventory checked. Source/provenance and output hash: [document log](system-design/README.md). No credentials/customer values included; source changes from other ongoing work were preserved. Not committed/pushed in this task.

### 20/10/2026 — Zalo invoice reminders (local implementation)

- User requested implementing the MONA-style personal Zalo Web flow: QR sign-in, persistent session, invoice details + transfer QR to tenants' phone numbers, queued sends 07:00–before21:00 Vietnam, minimum5seconds apart, maximum one submission attempt/invoice/20hours, monthly schedule, stop after recorded full payment, reconnect by QR.
- Added optional `zalo-gateway` Node24 service using pinned **zca-js2.2.0 (unofficial)** and qrcode1.5.4. AES-256-GCM encrypted session/attempt ledger, atomic/fsynced writes, authenticated internal HTTP, real QR callback/listener/restart restore, no reply content retention, single-account/owner binding and explicit disconnect. One gateway process per session volume; ambiguous sends never automatically replay.
- Backend feature `Features/Zalo`, owner-only/same-origin API, persistent per-invoice queue, DB owner lease, serialized manual/scheduled enqueue and atomic monthly marker. Both provider migrations add only `ZaloSettings`/`ZaloReminders`. Background worker opt-in `Zalo:Enabled`; no database reset or live migration performed.
- UI `/settings/zalo` reuses Ant Design/Roomie teal styling: connection QR/status, receiving bank configuration, monthly day/time (clamp to month end), manual unpaid queue, paginated latest status and help. Added62translated keys in4catalogs plus exact localized API guidance; private route remains noindex.
- Transfer QR is direct **VietQR/NAPAS**, generated locally at send time from saved bank BIN/account and current outstanding amount; it does not create a payOS order or automatically confirm a bank transfer. Full payment must be recorded manually or by existing verified reconciliation. Short-lived payOS checkout QR is intentionally not used. Replies remain in normal Zalo. History is the latest queue slot per invoice, not an audit of every send.
- Setup, semantics, monthly catch-up behavior, failures, deployment and limitations: [Zalo setup](zalo-setup.md). Optional `compose.zalo.yaml` does not publish a gateway port; existing VPS needs a separate controlled gateway/config/migration deployment. No Zalo credentials, real login QR, tenant messages or bank transfers used in QA. Source only; not committed/pushed/deployed, no production data changed.
- Validation: .NET Zalo suite36assertions SQLite and PostgreSQL17; gateway14tests incl actual HTTP authentication, session restart, unknown-result replay and VietQR fields/CRC; Playwright3tests desktop/mobile/unavailable service with screenshots inspected; frontend build/i18n1118×4/architecture and both migration snapshots PASS. Existing payment/service regression PASS after replacing a stale fixed migration count with applied-vs-discovered migrations. Disposable API suite SQLite80/80; PostgreSQL63run/8existing explicit skips PASS. First Python3.14 run hit a local libexpat linkage issue; reran with system Python successfully. No live-provider calls. CI now includes gateway and Zalo service tests; new API tests are included in disposable suites.

### 20/10/2026 — Local Zalo test instance started

- User asked to run locally for hands-on testing. Started detached frontend at `http://localhost:5173`, API at `http://localhost:5080`, gateway loopback `127.0.0.1:5090`.
- Uses a separate persistent SQLite demo database under `.local-data/zalo-local/test.db`, separate DataProtection/session storage, and private generated runtime credentials (ignored by Git, mode0600). Did not touch the existing PostgreSQL/local app data. Logs and process IDs are in the same ignored directory.
- API build PASS; frontend-proxied health, seeded owner login, Zalo settings/connection/history and page HTTP200 verified. Gateway enabled; Zalo disconnected, schedule disabled, queue empty. No real QR requested, no messages sent. User can log in with the documented demo owner and click **Tạo QR kết nối**.

### 20/10/2026 — Remove selected Zalo settings panel

- User selected the entire **Tài khoản nhận tiền & lịch gửi** section in the browser and requested removing it. Removed that UI block, its form state/mutation and unused styles; connection card now spans the available width. Removed guide steps pointing to those deleted controls and updated the existing browser flow accordingly.
- Backend configuration/storage and saved schedules were not modified. Without an existing receiving account, manual queue remains unavailable under the current backend contract. Local Vite reflects the change automatically; no real Zalo interaction or message sending was needed.

### 20/10/2026 — Integrate Zalo worktree with local main

- Combined Zalo reminders with main's manual payment confirmation and batch checkout. Resolved API registration, navigation, translation catalogs, disposable test registration and EF provider snapshots; kept existing migration IDs and both payment/Zalo behavior. The user-requested removal of the banking/schedule UI panel remains in place.
- Validation after integration: frontend build, i18n1151×4 and architecture94modules PASS; all49Playwright cases PASS; payment/service regression PASS; Zalo36assertions on SQLite and PostgreSQL PASS; gateway14tests PASS. Disposable HTTP suites: SQLite83/83PASS; PostgreSQL66run with8expected skips PASS. Both EF snapshots have no pending model changes; DbMigrator build and diff checks PASS.
- Authorized integration into local main only. No push, deployment or production database change; private local Zalo session/runtime files remain ignored.

### 20/10/2026 — Zalo reminders send Roomie checkout links

- User clarified that Zalo should send a payment link, not receiving-bank details or a transfer QR. Removed the Zalo bank prerequisite from UI, manual/scheduled queue and dispatch; first enqueue creates the owner lease/settings row. Legacy stored bank columns are preserved, with no migration/reset.
- Messages carry invoice details plus the existing tenant token's per-invoice Roomie checkout URL. Payment-sharing policy still applies. Queue validates active token/hash; dispatch resolves the latest token after rotation and skips revoked/unreadable links. Gateway sends text only and verifies the text-message acknowledgment; removed VietQR generation/dependency. Cooldown/window/spacing and ambiguous-delivery protection remain.
- VPS gateway was enabled with private root-owned secrets and a persistent internal-only volume after an encrypted/off-site verified pre-change backup. No customer message or real payment was sent for verification.
- Validation: frontend build/i18n1153×4/architecture PASS; all3Zalo browser flows PASS with empty Zalo bank settings; gateway12tests PASS; service41assertions PASS on SQLite and PostgreSQL; disposable SQLite HTTP83/83PASS. First HTTP harness run lacked static frontend assets; copied the built frontend into the published fixture and reran successfully. No live-provider test calls.

### 20/10/2026 — Group payment and Zalo navigation under settings

- User requested a collapsible Cấu hình menu with Thanh toán and Zalo children. Reused Ant Design's inline submenu for desktop/mobile drawer and click dropdown for compact mobile navigation; parent opens on settings routes, children retain active selection, and clicking the parent does not navigate. Breadcrumbs resolve child titles.
- Local Vite reflects the change. Frontend build/i18n1153×4 and15targeted browser cases PASS, including collapse/expand, payment/Zalo navigation, mobile dropdown/drawer and header bounds. Mocked owner data only; no Zalo/payment API mutations. Adapted ui-ux probe with synthetic API responses; screenshots inspected at375/1280px with no horizontal overflow.
- Probe flags P1–P4 concern existing styles outside the navigation change: disconnect-button contrast, breadcrumb separator contrast, empty-table text and table heading contrast. Recorded without expanding this menu-only scope. Current theme retained; no brand/font changes. This navigation update is local, not pushed/deployed.

### 20/10/2026 — Zalo links open tenant invoice overview

- User clarified that reminders must link to the existing `/t/{token}` tenant portal, where tenants review invoices and choose Thanh toán before QR appears. Removed the direct per-invoice checkout suffix and changed the message prompt to Xem hóa đơn. Links remain scoped to each invoice's active occupancy; no hardcoded customer token, new token or payment order.
- Regression assertions require the exact overview URL followed by a newline and reject `/invoices/` or `/checkout` in sent messages; rotation/revocation rules remain active. Includes the already tested settings-menu grouping in this release.
- Validation: all41service assertions and12gateway tests PASS with fixture tokens only; existing menu build/i18n and15browser checks passed before commit. CI/CD will verify the combined release. No real tenant link was opened or shared during QA.

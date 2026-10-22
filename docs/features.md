# Danh sách tính năng hệ thống Roomie

Hệ thống quản lý nhà trọ đa chủ trọ (Multi-tenant Rental Management Platform).

---

## 1. Quản lý Nhà trọ & Phòng trọ (Properties & Rooms)

- **Quản lý Dãy / Nhà trọ (Buildings):**
  - Thêm, sửa, lưu trữ (archive), xóa dãy trọ.
  - Thiết lập địa chỉ, ghi chú và đơn giá mặc định (giá điện, nước, dịch vụ vệ sinh, internet, rác).
- **Quản lý Phòng trọ (Rooms):**
  - Quản lý danh sách phòng theo tầng, diện tích, giá thuê cơ bản, tiền cọc tiêu chuẩn, danh mục tiện nghi/nội thất.
  - Trạng thái phòng thời gian thực: Phòng trống (Available) hoặc Đang thuê (Occupied).
- **Quản lý Lượt thuê (Occupancies):**
  - Tạo lượt thuê mới: Khai báo khách đại diện (họ tên, số điện thoại, CCCD, ngày sinh, quê quán), số lượng người ở, ngày bắt đầu thuê, tiền cọc thực thu.
  - Quản lý danh sách người ở cùng phòng.
- **Quản lý Tiền cọc (Deposit Ledger):**
  - Theo dõi sổ quỹ tiền cọc từng lượt thuê: nạp cọc, khấu trừ công nợ, hoàn cọc.
- **Quy trình Trả phòng (Move-out Workflow):**
  - Kiểm tra và chốt công nợ hóa đơn chưa thanh toán.
  - Ghi nhận chỉ số điện/nước cuối cùng và tính toán bù trừ.
  - Cấn trừ nợ vào tiền cọc, hoàn tiền cọc thừa hoặc thu thêm nợ thiếu.
  - Tự động thu hồi phòng về trạng thái trống và vô hiệu hóa liên kết khách thuê.
- **Cổng liên kết khách thuê (Tenant Portal Link):**
  - Cấp token bí mật truy cập độc bản (`/t/{token}`) theo từng lượt thuê để khách tự theo dõi hóa đơn và thanh toán.

---

## 2. Quản lý Chỉ số Điện & Tiện ích (Meter Readings)

- **Ghi chỉ số định kỳ:**
  - Nhập chỉ số điện/nước đầu kỳ và cuối kỳ theo từng phòng hoặc ghi hàng loạt theo dãy trọ.
  - Cảnh báo và chặn ghi chỉ số lùi (chỉ số mới nhỏ hơn chỉ số cũ).
  - Cảnh báo mức tiêu thụ bất thường so với chu kỳ liền kề.
- **Tích hợp kỳ lập hóa đơn:**
  - Tự động kết chuyển sản lượng tiêu thụ thực tế vào hóa đơn tiền phòng kỳ tương ứng.

---

## 3. Lập & Quản lý Hóa đơn (Billing & Invoices)

- **Đa dạng loại hóa đơn:**
  - Hóa đơn định kỳ tháng (Monthly): Tiền phòng, điện, nước, dịch vụ.
  - Hóa đơn cọc (Deposit): Lập khoản thu cọc khi ký hợp đồng.
  - Hóa đơn phát sinh (Supplemental): Các khoản chi phí sửa chữa, phạt, hoặc phát sinh ngoài hợp đồng.
- **Tính toán linh hoạt:**
  - Tiền phòng tính trọn tháng (theo chuẩn lịch Việt Nam UTC+7).
  - Tiền điện/nước tính theo công tơ hoặc định mức cố định theo đầu người/phòng.
  - Các loại phí dịch vụ cố định hàng tháng (wifi, rác, thang máy, gửi xe).
- **Vòng đời hóa đơn:**
  - Bản nháp (Draft) $\rightarrow$ Đã phát hành (Issued) $\rightarrow$ Đã thanh toán (Paid) / Đã hủy (Cancelled).
- **Snapshot dữ liệu bất biến:**
  - Đóng băng toàn bộ đơn giá, chỉ số đồng hồ, tên phòng, thông tin khách tại thời điểm phát hành hóa đơn. Mọi thay đổi giá phòng hiện tại không làm biến đổi số liệu hóa đơn quá khứ.
- **Tổng hợp & Đối soát:**
  - Tổng hợp tự động tổng doanh thu, số tiền đã thu, dư nợ còn lại (`Total - Paid`) trên toàn bộ danh sách đã lọc bằng 1 truy vấn tổng hợp SQL duy nhất.

---

## 4. Thanh toán & Đối soát Giao dịch (Payments)

- **Thanh toán tự động qua payOS (Dynamic VietQR):**
  - Tích hợp cổng payOS riêng cho từng chủ trọ (Client ID, API Key, Checksum Key được mã hóa).
  - Sinh mã VietQR động theo từng hóa đơn hoặc thanh toán gộp nhiều hóa đơn (Batch payment).
  - Webhook tự động gạch nợ tức thì khi tiền về tài khoản ngân hàng.
  - Cơ chế idempotency và xác thực chữ ký số webhook chống ghi nhận trùng lặp giao dịch.
- **Thanh toán thủ công qua VietQR tĩnh (Manual VietQR):**
  - Tạo mã VietQR tĩnh gắn kèm thông tin tài khoản ngân hàng chủ trọ và mã nội dung chuyển khoản định danh.
  - Khách quét mã chuyển khoản và gửi yêu cầu xác nhận.
  - Bảng điều khiển duyệt chuyển khoản (Manual Payment Requests Panel): Đánh giá công nợ, đối chiếu nội dung thực nhận, chủ trọ xác nhận 1 chạm để gạch nợ.
- **Ghi thu tiền mặt trực tiếp:**
  - Cho phép chủ trọ chủ động ghi thu toàn bộ dư nợ khi nhận tiền mặt trực tiếp từ khách.

---

## 5. Cổng Tra cứu Khách thuê (Tenant Portal)

- **Truy cập nhanh:** Khách truy cập trực tiếp qua liên kết `/t/{token}` mà không cần tạo tài khoản hay đăng nhập.
- **Minh bạch chi phí:**
  - Xem chi tiết bảng kê hóa đơn: chỉ số điện cũ/mới, lượng tiêu thụ, đơn giá, chi tiết dịch vụ.
  - Xem trạng thái thanh toán và lịch sử các lần trả tiền.
- **Tự chọn hình thức thanh toán:**
  - Thanh toán chuyển khoản tự động quét QR payOS.
  - Hoặc lấy mã VietQR tĩnh chuyển khoản thông thường kèm cú pháp chuẩn.

---

## 6. Tự động Nhắc nợ qua Zalo (Zalo Gateway)

- **Đăng nhập Zalo cá nhân qua QR Code:**
  - Chủ trọ kết nối tài khoản Zalo thông qua dịch vụ Gateway nội bộ bảo mật (`zca-js`).
  - Lưu phiên đăng nhập và sổ cái tin nhắn mã hóa (AES-256-GCM).
- **Nhắc hóa đơn tự động:**
  - Tự động xếp lịch gửi tin nhắn thông báo kèm đường dẫn `/t/{token}` tới số điện thoại của người thuê ngay khi phát hành hóa đơn.
- **Cơ chế an toàn & chống khóa tài khoản:**
  - Giới hạn khung giờ gửi tin: 07:00 đến 21:00 (giờ Việt Nam).
  - Giãn cách tối thiểu 5 giây giữa các tin nhắn của cùng một chủ trọ.
  - Thời gian chờ (cooldown) 20 giờ cho mỗi hóa đơn, chống gửi lặp tin nhắn.

---

## 7. Báo cáo & Thống kê (Reports & Analytics)

- **Báo cáo doanh thu & Dòng tiền:**
  - Thống kê tổng doanh thu thực thu, công nợ tồn đọng theo từng tháng/quý/năm.
  - Báo cáo phân loại theo từng dãy trọ hoặc danh mục dịch vụ.
- **Báo cáo vận hành:**
  - Tỷ lệ lấp đầy phòng (Occupancy Rate), tỷ lệ phòng trống.
  - Danh sách khách nợ tiền phòng quá hạn.
- **Xuất dữ liệu Excel (.xlsx):**
  - Xuất bảng kê hóa đơn, dòng tiền chi tiết ra file Excel định dạng chuẩn số học, hỗ trợ chống lỗi formula injection.

---

## 8. Gói dịch vụ & Khuyến mãi (Subscription & Promotions)

- **Gói Miễn phí (Free):**
  - Cho phép quản lý tối đa 5 phòng đang sử dụng (`Active Rooms <= 5`).
  - Hỗ trợ đầy đủ các tính năng cơ bản quản lý, hóa đơn và cổng khách thuê.
- **Gói Chuyên nghiệp (Pro):**
  - Mở rộng không giới hạn số phòng quản lý (hoặc theo hạn mức thuê bao cấu hình).
  - Tự động nâng cấp và gia hạn trực tuyến qua thanh toán payOS.
- **Chương trình Khuyến mãi & Mã quà tặng (Promotions):**
  - Ưu đãi chào mừng (Welcome Promotion): Tự động tặng tháng dùng thử Pro khi người dùng đăng ký mới.
  - Mã ưu đãi (Promo Code): Nhập mã đổi thưởng để nhận thêm thời hạn sử dụng gói Pro.

---

## 9. Quản trị Hệ thống (Admin Portal)

- **Quản lý Chủ trọ (Owner Management):**
  - Danh sách chủ trọ toàn hệ thống.
  - Quản lý trạng thái phê duyệt: Chờ duyệt (Pending), Đã duyệt (Approved), Từ chối (Rejected), Tạm khóa (Suspended).
- **Quản lý Khuyến mãi (Promotions Management):**
  - Cấu hình chương trình tặng Pro cho người dùng mới (bật/tắt, số tháng tặng, thời gian áp dụng).
  - Tạo, kích hoạt/hủy kích hoạt mã khuyến mãi (Promo Codes), giới hạn số lượt nhập và thời hạn hiệu lực.
- **Giám sát Tăng trưởng & Doanh thu (Growth & Revenue Monitoring):**
  - Đo lường chỉ số kích hoạt (Activation milestone: phát hành hóa đơn phòng đầu tiên).
  - Theo dõi doanh thu từ các lượt thanh toán gói Pro.
  - Phân tích cohort người dùng theo thời gian và nguồn đăng ký.
- **Giám sát Kỹ thuật (Operations Monitoring):**
  - Bảng điều khiển trạng thái API, background worker, độ trễ và tỷ lệ lỗi.
  - Giám sát tài nguyên máy chủ VPS (CPU, RAM, ổ đĩa) thông qua Host Collector chuyên dụng.

---

## 10. Định danh & An toàn Hệ thống (Auth & Security)

- **Xác thực Đăng ký bằng Email OTP:**
  - Đăng ký tài khoản bảo vệ bởi mã OTP 6 chữ số gửi qua email (hạn 10 phút, giới hạn 5 lần thử).
  - Tự động kích hoạt tài khoản Owner Approved sau khi xác thực OTP thành công.
- **Đăng nhập & Quản lý phiên bảo mật:**
  - Xác thực bằng mật khẩu mạnh (9 - 256 ký tự).
  - Session lưu trên cookie `HttpOnly`, cờ `SameSite=Lax`, thời hạn 12 giờ cố định.
  - Kiểm tra tính hợp lệ của session ID (`sid`) trong cơ sở dữ liệu trên từng yêu cầu.
  - Same-origin guard ngăn chặn các cuộc tấn công CSRF.
- **Khôi phục mật khẩu (Password Recovery):**
  - Quy trình quên mật khẩu xác thực qua OTP email, cấp mã reset dùng 1 lần trong 10 phút.
  - Đổi/khôi phục mật khẩu tự động thu hồi toàn bộ phiên đăng nhập trên các thiết bị khác.
- **Bảo mật Đa người thuê (Multi-tenant Isolation):**
  - Cô lập tuyệt đối dữ liệu giữa các chủ trọ qua phạm vi `OwnerId` trên toàn bộ các truy vấn cơ sở dữ liệu.

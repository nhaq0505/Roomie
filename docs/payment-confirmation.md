# Chế độ xác nhận và thanh toán nhiều hóa đơn

Trong **Cấu hình**, chủ trọ chọn một trong hai chế độ:

- **Tự động xác nhận thanh toán:** giữ luồng payOS, chỉ ghi thu khi nhận được bằng chứng đã xác minh đúng chữ ký, đơn và số tiền.
- **Xác nhận thanh toán thủ công:** chọn ngân hàng, nhập số tài khoản và tên chủ tài khoản; khách chuyển khoản bằng VietQR. Chủ trọ đối chiếu tiền thực nhận rồi xác nhận tại danh sách **Chuyển khoản chờ xác nhận** trong **Hóa đơn**. Không yêu cầu khóa payOS.

Tài khoản hiện có và tài khoản mới mặc định `Automatic`, giữ hành vi cũ. Việc lưu chế độ thủ công không xóa khóa payOS. Khi còn giao dịch payOS đang chờ, đổi chế độ hoặc thông tin nhận tiền bị chặn cho tới khi đối soát giao dịch đó.

## Link người thuê

Danh sách vẫn trình bày riêng từng hóa đơn, có thanh toán từng hóa đơn và nút **Thanh toán tất cả**. Tổng thanh toán lấy từ dư nợ ở máy chủ của toàn bộ hóa đơn đã phát hành thuộc đúng lượt thuê, kể cả những hóa đơn nằm ngoài trang danh sách đang xem. Không bao gồm bản nháp, hóa đơn đã trả đủ hoặc hóa đơn thuộc lượt thuê khác.

- Một hóa đơn: `/t/{token}/invoices/{invoiceId}/checkout`.
- Tất cả: `/t/{token}/invoices/all/checkout`.
- Chỉ token của lượt thuê đang hoạt động mới có thể tạo yêu cầu hoặc kiểm tra đơn. Token đã bị thu hồi không dùng lại được.

## VietQR thủ công

Máy chủ lưu yêu cầu chuyển khoản với danh sách hóa đơn, phần tiền của mỗi hóa đơn, ngân hàng, tài khoản và nội dung chuyển khoản riêng. Mở lại cùng lựa chọn với số dư/tài khoản không đổi trong 24 giờ tái sử dụng yêu cầu hiện tại. Đây là thời hạn tái sử dụng yêu cầu, không phải cam kết ngân hàng vô hiệu hóa mã QR sau 24 giờ.

Hình QR dùng [Quick Link của VietQR.io/Casso](https://www.vietqr.io/qr-dong). Số tiền trong URL là VND nguyên; tên tài khoản/nội dung được URL-encode. Trình duyệt gửi thông tin ngân hàng, tên người nhận, số tiền và nội dung cho dịch vụ hình ảnh này; không gửi token truy cập phòng, và ảnh đặt `referrerPolicy=no-referrer`. CSP chỉ bổ sung đúng host `img.vietqr.io`. Không dùng dịch vụ này để xác nhận tiền đã về.

Danh sách ngân hàng là bản đóng gói lấy từ [API Banks](https://www.vietqr.io/api-bank), ngày 20/10/2026, tại `Features/Payments/Banks.json`. Có thể cập nhật từ `https://api.vietqr.io/v2/banks`, giữ các mục hỗ trợ chuyển khoản; cấu hình ngân hàng được kiểm tra với danh sách này. Không có bước tra cứu/xác minh tên chủ tài khoản tự động: chủ trọ phải kiểm tra thông tin đã nhập, và khách kiểm tra người nhận trong ứng dụng ngân hàng.

Xác nhận thủ công yêu cầu số tiền thực nhận khớp toàn bộ yêu cầu. Máy chủ kiểm tra quyền chủ trọ, số dư từng hóa đơn và giao dịch payOS đang chờ trước khi ghi thu. Toàn bộ hóa đơn được cập nhật trong một transaction. Xác nhận lặp không ghi thêm tiền; số dư đã thay đổi làm yêu cầu cũ bị từ chối, không ghi thu một phần vào các hóa đơn còn lại.

**Ghi nhận khoản thu** là xác nhận thanh toán toàn bộ dư nợ của hóa đơn, không có thanh toán một phần hoặc ô nhập số tiền. Máy chủ từ chối số tiền không bằng dư nợ hiện tại. Với chuyển khoản gộp, xác nhận thanh toán đầy đủ mọi hóa đơn trong yêu cầu; thiếu/thừa tiền không được xác nhận. Dữ liệu khoản thu cũ và khấu trừ cọc vẫn được giữ nguyên, xác nhận mới thu hết phần còn lại. Yêu cầu hết hạn tái sử dụng vẫn có thể xác nhận khi thực nhận đủ tiền và số dư còn khớp.

## payOS gộp

Một đơn payOS giữ snapshot số tiền phân bổ và giữ chỗ trên mọi hóa đơn thành viên. Mở lại cùng tập hóa đơn tái sử dụng đơn đang chờ. Giao dịch riêng hoặc giao dịch gộp khác không được giữ chỗ trùng. Tạo thêm hóa đơn sau khi đặt lệnh không tự sửa đơn đang chờ.

Webhook/đối soát hợp lệ ghi một receipt cho mỗi hóa đơn, cập nhật trạng thái đơn và dấu chống xử lý lặp trong cùng transaction. Thông báo lặp không cộng thêm tiền. Thiếu tiền, sai chữ ký, sai đơn hoặc số tiền không khớp không đánh dấu toàn bộ hóa đơn đã trả.

Các thao tác sửa hóa đơn, quyết toán cọc, kết thúc lượt thuê và ghi thu riêng đều nhận diện mọi thành viên của giao dịch gộp, bao gồm hóa đơn không phải hóa đơn đầu tiên. Ghi thu riêng vào hóa đơn thuộc đơn gộp đang chờ bị chặn; cần đối soát hoặc đợi trạng thái kết thúc được xác minh trước.

## API bổ sung

- `GET /api/settings/payment`: thêm `confirmationMode` và `manualBank` (nullable).
- `GET /api/settings/payment/banks`: danh sách `{bin,name,shortName}`; yêu cầu quyền Owner.
- `PUT /api/settings/payment/preferences`: `{confirmationMode,bankBin?,accountNumber?,accountName?}`; cấu hình của chính Owner đang đăng nhập.
- `GET /api/tenant/{token}`: thêm `confirmationMode`, `totals.unpaidCount`.
- `POST /api/tenant/{token}/manual-payments`: `{invoiceId:null}` để tạo QR tất cả, hoặc `{invoiceId:"..."}` cho một hóa đơn.
- `GET /api/manual-payments?page=1&pageSize=20`: danh sách phân trang `{items,total,page,pageSize}` của Owner, có tên phòng, người thuê, reference, danh sách phân bổ và trạng thái xác nhận.
- `POST /api/manual-payments/{id}/confirm`: `{amount}`; chỉ Owner sở hữu yêu cầu được ghi nhận.
- `POST /api/tenant/{token}/payment`: tạo đơn payOS gộp từ dư nợ máy chủ.
- `POST /api/tenant/{token}/orders/{orderCode}/reconcile`: chỉ đối soát đơn có hóa đơn gốc thuộc đúng lượt thuê/token.

Các endpoint thanh toán từng hóa đơn và ghi nhận khoản thu cũ tiếp tục hoạt động.

## Triển khai và kiểm tra

Migration `PaymentModesAndBatchCheckout` bổ sung trường cấu hình trên Users, snapshot trên PayOsAttempts, mã giữ chỗ trên Invoices và bảng ManualPaymentRequests cho cả SQLite/PostgreSQL. Cần chạy migration bằng DbMigrator trước khi triển khai app mới. Không áp dụng migration vào database vận hành trong đợt triển khai source này.

Kiểm tra gồm build frontend/API/DbMigrator, i18n/ranh giới module, Playwright, C# suite (có batch webhook ký giả lập và replay), snapshot cả hai provider và harness HTTP trên database SQLite/PostgreSQL tạm. `tests/test_payment_modes.py` được thêm vào harness thông thường, kiểm tra tất cả trang, token/quyền, số tiền, yêu cầu cũ và xác nhận đồng thời. Mọi lời gọi nhà cung cấp trong test đều giả lập hoặc bị chặn; ảnh thử là hình không thanh toán được. Chưa thực hiện giao dịch ngân hàng/payOS thật.

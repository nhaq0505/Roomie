# Nhắc hóa đơn qua Zalo — Roomie

Cập nhật 20/10: tin Zalo gửi thông tin hóa đơn và **link xem hóa đơn Roomie**, không gửi số tài khoản hay ảnh QR chuyển khoản. Trang Zalo không yêu cầu cấu hình ngân hàng riêng; khối tài khoản/lịch đã được bỏ theo yêu cầu.

## Phạm vi và cách dùng

Vào **Nhắc hóa đơn qua Zalo** (`/settings/zalo`), tạo QR, mở ứng dụng Zalo trên điện thoại để quét và xác nhận. Đây là kết nối tài khoản cá nhân qua Zalo Web, dùng thư viện **zca-js 2.2.0 không chính thức**, không phải Zalo OA/ZNS. Zalo có thể thay giao thức hoặc hạn chế tài khoản; cần kiểm chứng bằng tài khoản thử trước khi vận hành.

Kết nối xuất hiện như thiết bị **Zalo Web** trên điện thoại. Đăng xuất thiết bị sẽ làm Roomie ngừng gửi. Quay lại trang này và tạo QR mới để kết nối lại. Tránh mở một phiên Zalo Web khác cho cùng tài khoản. Một tài khoản Zalo chỉ được gắn với một chủ trọ trong cùng gateway; muốn chuyển chủ trọ phải ngắt kết nối trước. Ngắt kết nối tại Roomie xóa thông tin phiên cục bộ, tắt lịch và hủy các tin đang chờ; chủ trọ có thể đăng xuất thiết bị từ điện thoại để thu hồi tại Zalo. Một tin đã được gửi đi trước khi ngắt không thể thu hồi qua thao tác này.

Kết nối Zalo rồi bấm **Nhắc hóa đơn còn nợ** và xác nhận **Thêm vào hàng đợi**. Chủ trọ dùng cấu hình thanh toán hiện có của Roomie; quy tắc cấp link người thuê vẫn áp dụng (Automatic cần payOS xác minh, Manual cần cấu hình nhận tiền trong phần thanh toán). Không nhập thông tin nhận tiền ở trang Zalo.

Link mở trang người thuê `/t/{token}`, nơi khách xem danh sách hóa đơn rồi chọn **Thanh toán** để mở QR. Tin Zalo không gửi đường dẫn `/invoices/{invoiceId}/checkout` trực tiếp. Dùng token hiện có của lượt thuê, không tự tạo hoặc đổi link. Chỉ xếp hàng khi token giải mã được và khớp hash đang hoạt động; khi gửi kiểm tra lại token hiện tại để không gửi link đã thu hồi. Nếu link không còn hợp lệ sau khi xếp hàng, tin bị bỏ qua và trang hiển thị hướng dẫn kiểm tra/cấp lại link trong trang phòng.

Backend vẫn giữ API lịch đã lưu (giao diện hiện không có khối chỉnh lịch): ngày 1–31 và giờ 07:00–20:59, giờ Việt Nam. Tháng không có ngày đã chọn dùng ngày cuối tháng. Lịch chạy một lần/tháng với toàn bộ hóa đơn đã phát hành còn nợ của người thuê đang ở, gồm hóa đơn tháng, bổ sung và cọc. Nếu máy chủ khởi động lại hoặc bật lịch sau mốc đã chọn, đợt tháng hiện tại được xếp hàng bù; tháng đã ghi nhận chạy sẽ không tự chạy lại khi đổi giờ. Hóa đơn phát hành sau đợt đó có thể thêm bằng nút **Nhắc hóa đơn còn nợ**, hoặc chờ đợt tháng sau. Lịch không tự tạo/phát hành hóa đơn và không tự nhắc hằng ngày.

Nút **Nhắc hóa đơn còn nợ** thêm các hóa đơn đủ điều kiện vào hàng đợi; không gửi đồng loạt ngay. Số điện thoại phải là số di động Việt Nam hợp lệ; hỗ trợ chuẩn hóa `+84`, `84`, dấu cách và dấu phân cách sang `0…`. Việc có số điện thoại không bảo đảm Zalo cho phép tìm tài khoản hoặc gửi tin; lỗi được hiển thị trong hàng đợi. Tin gồm dãy/phòng, người thuê, kỳ, các khoản phí, tổng/đã trả/còn nợ, hạn và link xem/thanh toán hóa đơn trên Roomie. Không có số tài khoản hay ảnh QR trong tin Zalo. Người thuê trả lời trong cuộc trò chuyện Zalo của chủ trọ như bình thường; Roomie không lưu nội dung trả lời.

Gửi từ **07:00 đến trước 21:00**, cách nhau ít nhất 5 giây cho mỗi chủ trọ. Công việc ngoài giờ/mất phiên được giữ chờ. Mỗi hóa đơn có một vị trí trong hàng đợi và tối đa một lần thử gửi trong 20 giờ, kể cả lần gửi có kết quả không rõ. Nhấn lặp hoặc chạy lịch không tạo bản sao. Sau 20 giờ có thể thêm lại bằng nút nhắc; không tự lặp lại tin đã gửi. Worker kiểm tra lại hóa đơn đã phát hành, tiền còn nợ, người thuê đang ở, điện thoại và tài khoản chủ trọ được duyệt ngay trước khi chuẩn bị gửi. Thanh toán được ghi nhận trước bước kiểm tra thì tin được bỏ qua; khoản thanh toán xảy ra đồng thời sau bước này vẫn có thể trùng với tin đang gửi.

Tin nhắc chỉ gửi link; không tạo order payOS tại thời điểm gửi. Khách mở link và thanh toán theo chế độ hiện có của Roomie; QR/thông tin chuyển khoản nằm trong trang thanh toán. Quy tắc xác nhận/đối soát giữ nguyên; khi hóa đơn được ghi nhận trả đủ, worker bỏ qua lần nhắc tiếp theo.

**Chưa rõ kết quả** nghĩa là gateway/API bị gián đoạn sau khi bắt đầu gửi hoặc Zalo chưa xác nhận đầy đủ. Kiểm tra cuộc trò chuyện trước khi thêm lại sau 20 giờ. Không tự retry tình huống này để tránh tin trùng. Trang hiển thị trạng thái gần nhất của từng hóa đơn, phân trang 20 dòng; đây là hàng đợi/trạng thái gần nhất, không phải nhật ký lưu mọi lần gửi.

## Chạy tại máy

Cần Node.js 24 và .NET 10. Chạy một gateway cho một thư mục phiên. Tạo hai giá trị khác nhau trong cấu hình riêng (không commit):

```sh
openssl rand -hex 32
openssl rand -base64 32
```

Giá trị đầu là `ZALO_GATEWAY_TOKEN`, giá trị sau là `ZALO_SESSION_KEY`. Khóa mã hóa phải giữ nguyên qua lần khởi động; mất/đổi khóa làm các phiên cũ không đọc được. Sao lưu thư mục phiên cùng khóa, bảo quản riêng tư.

```sh
cd zalo-gateway
npm ci
# Nạp ZALO_GATEWAY_TOKEN, ZALO_SESSION_KEY từ cấu hình riêng của bạn.
# Mặc định chỉ listen 127.0.0.1:5090, dữ liệu ở .local-data.
npm start
```

Cấu hình API trong file Development local đã được Git bỏ qua hoặc biến môi trường:

```json
{
  "Zalo": {
    "Enabled": true,
    "GatewayUrl": "http://127.0.0.1:5090",
    "GatewayToken": "THAY_BANG_TOKEN_DA_TAO"
  }
}
```

Development áp migrations theo quy trình sẵn có. Chạy frontend/backend bình thường, mở `/settings/zalo`. Không điền khóa vào JSON mẫu được commit. Khi chưa bật/cấu hình gateway, trang báo chưa sẵn sàng và không tạo QR giả.

## Docker và vận hành

File bổ sung `compose.zalo.yaml` là tùy chọn, không tự bật Zalo ở bản đang vận hành. Điền `ZALO_GATEWAY_TOKEN`, `ZALO_SESSION_KEY` trong `.env` riêng rồi:

```sh
docker compose -f compose.yaml -f compose.zalo.yaml up --build -d
```

Gateway nằm trong mạng Docker, **không publish cổng**. Volume `zalo_sessions` chứa phiên và biên nhận gửi được AES-256-GCM mã hóa, file ghi nguyên tử và fsync trước thao tác gửi. API gọi gateway bằng Bearer token, chỉ chủ trọ đăng nhập được duyệt mới dùng API. Không đưa token gateway xuống frontend, không log cookie/QR/nội dung tin. Lưu ý override Compose này dành cho cấu hình Compose của repo; VPS hiện có quy trình deploy/role DB riêng, không chạy lệnh trên đè lên môi trường đó.

Production cần chạy migration SQLite/PostgreSQL tương ứng bằng identity triển khai trước khi thay API; hai migrations `ZaloReminders` chỉ thêm bảng `ZaloSettings` và `ZaloReminders`. Không reset database. Với deploy VPS hiện tại cần bổ sung riêng container gateway, volume và biến `Zalo__Enabled`, `Zalo__GatewayUrl`, `Zalo__GatewayToken` theo [runbook triển khai](deployment.md). VPS đã bật gateway nội bộ ngày 20/10/2026 với volume `rehouse_zalo_sessions`, khóa riêng trong `/etc/rehouse/runtime.env` và cấu hình Compose root-owned; không publish cổng gateway.

API có thể nhiều instance: lease trong DB serialize gửi theo chủ trọ; transaction serialize xếp hàng/lịch và khóa chính InvoiceId chặn trùng. Chỉ chạy **một gateway process/container trên một volume**, không chia volume cho nhiều gateway. Gateway khôi phục phiên đã lưu khi API kiểm tra trạng thái; websocket tự reconnect lỗi mạng ngắn hạn, đăng xuất/kết nối bị thay thế cần QR lại. Bảo quản cả hai DB và volume phiên khi backup/restore; phục hồi bản backup cũ có thể mất biên nhận mới nên phải đối chiếu Zalo trước khi bật gửi lại.

Trạng thái lần gửi chưa chắc được gửi sẽ không bị retry tự động. Ledger gateway lưu attempt id, fingerprint, thời điểm và kết quả để chặn replay qua lần restart; không lưu nguyên văn tin hay số người nhận. Dữ liệu phiên và ledger tồn tại cho đến khi dọn volume có kiểm soát. Dịch vụ này chưa có nghiệm thu Zalo/ngân hàng thật; thử đầu tiên nên dùng người nhận thử do chủ trọ chỉ định.

## Kiểm tra

```sh
dotnet run --project tests/RoomRental.ZaloTests
npm --prefix zalo-gateway test
npm --prefix frontend run test:e2e -- zalo.spec.ts --workers=2
```

Các kiểm thử dùng Zalo giả lập, database và link dùng thử, không gửi tin thật. Suite .NET có thể chạy PostgreSQL dùng thử qua `ZALO_TEST_POSTGRES`, chỉ chấp nhận loopback và tên database `roomie_qa_zalo*`. Xem kết quả thực tế trong bàn giao dự án.

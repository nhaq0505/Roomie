# Khôi phục mật khẩu qua email

Trang `/reset-password` hỗ trợ chủ trọ và quản trị viên: nhập email → nhận OTP → xác minh OTP → tự đặt mật khẩu mới → đăng nhập lại. Mã do quản trị viên cấp vẫn dùng được tại `/reset-password?method=admin`.

## API và giới hạn

- `POST /api/auth/forgot-password` với `{email}` trả HTTP 202 `{challengeToken,email,expiresAt,resendAvailableAt}`. Email tồn tại và không tồn tại có cùng dạng phản hồi. Việc gửi thư chạy nền để không chờ SMTP trong request.
- `POST /api/auth/verify-password-recovery` với `{challengeToken,code}` trả `{resetToken,expiresAt}`. OTP gồm 6 chữ số, hết hạn sau 10 phút và cho phép tối đa 5 lần nhập sai. Xác minh thành công tiêu thụ OTP.
- `POST /api/auth/complete-password-recovery` với `{resetToken,newPassword,confirmPassword}` trả `{success:true}`. Quyền đặt lại hết hạn sau 10 phút, chỉ dùng một lần. Mật khẩu mới dài 9–256 ký tự và phải khác mật khẩu cũ.
- Gửi lại sau 60 giây, tối đa 5 yêu cầu/email/giờ. Gửi lại thay thế mã cũ; thay đổi mật khẩu hoặc cấp mã quản trị mới làm mất hiệu lực bằng chứng cũ.
- Yêu cầu OTP và nhập sai OTP không khóa tài khoản hay đăng xuất phiên đang dùng. Hoàn tất đặt lại thu hồi toàn bộ phiên của tài khoản, xóa cookie trình duyệt và yêu cầu đăng nhập lại. Không đổi vai trò hay trạng thái tài khoản.

Challenge và quyền đặt lại được lưu dạng hash; trình duyệt chỉ giữ chúng trong bộ nhớ. OTP chờ gửi được mã hóa bằng ASP.NET Data Protection và xóa khi worker nhận xử lý. Không ghi OTP hay mật khẩu vào log.

## Vận hành

Dùng cấu hình SMTP hiện có trong [hướng dẫn email](email-otp-setup.md). Worker kiểm tra hàng đợi mỗi 2 giây, gửi OTP và thông báo sau khi đổi mật khẩu thành công. Thư được gửi theo cơ chế best effort: worker nhận trước khi gọi SMTP; lỗi SMTP hoặc dừng tiến trình lúc gửi không tự phát lại thư đã nhận. Người dùng có thể yêu cầu mã mới sau thời gian chờ. Không cam kết SMTP đã phát thư vào hộp thư đến.

Giữ bền vững thư mục khóa Data Protection để giải mã thư đang chờ sau khi khởi động lại. Migration `PasswordRecovery` bổ sung bảng riêng cho cả SQLite và PostgreSQL; cần chạy bước migration trước khi triển khai phiên bản mới. Không thay đổi bảng tài khoản hay dữ liệu tính tiền. Bản triển khai này chưa chạy migration trên database vận hành và chưa gửi thử email thật.

## Kiểm chứng

`dotnet run --project tests/RoomRental.WebhookTests` kiểm tra giới hạn, hết hạn, mã dùng một lần, thay đổi mật khẩu làm mất hiệu lực bằng chứng, thu hồi phiên và xử lý lỗi gửi thư. Harness `tests/run_disposable.py` với provider `otp` và `otp-postgres` chạy các API thật qua database tạm và SMTP sink cục bộ, gồm hoàn tất đồng thời chỉ một request thắng và đăng nhập lại ngay sau khi đặt lại.

`npm --prefix frontend run test:e2e` kiểm tra luồng ba bước, lỗi OTP, mật khẩu yếu, gửi lại, hết hạn, lỗi dịch vụ và giao diện mobile. Không dùng database hoặc dịch vụ email thật cho các kiểm thử này.

Thiết kế dựa trên [OWASP Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html): phản hồi thống nhất, bằng chứng có hạn dùng và dùng một lần, không thay đổi tài khoản trước khi xác minh.

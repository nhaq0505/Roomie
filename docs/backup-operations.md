# Backup và khôi phục Roomie

Backup PostgreSQL custom dump, DataProtection keys, manifest và SHA256 được lưu root-only trên VPS và mã hóa qua rclone crypt vào Google Drive do chủ dự án cấp quyền. Lịch03:00–03:20 giờ Việt Nam và trước deploy; local10bản, remote chọn snapshot mới nhất của7ngày/4tuần ISO/3tháng có dữ liệu. Upload và tải xuống/giải mã kiểm chứng thành công trước retention; giữ bản local nếu sync lỗi.

Cài helper/service/timer/disclosure pages bằng `sudo bash deploy/install-backup.sh`. Host mới phải được cấp riêng private OAuth/crypt config qua `ROOMIE_RCLONE_CONFIG`; folder được duyệt qua `ROOMIE_DRIVE_FOLDER_ID` hoặc lấy từ config đã cấp quyền. Không commit config/token/crypt password/salt. OAuth dùng scope Drive đầy đủ; folder cấu hình chỉ giới hạn routing của công cụ. Mất OAuth/token cần chủ tài khoản tự cấp lại; mất cả crypt password/salt thì không thể giải mã.

Deadline toàn lượt15phút gồm pre-deploy; systemd16phút, subprocess300giây và network timeout. Cảnh báo SMTP TLS dùng `BACKUP_ALERT_TO` trong root-only `/etc/rehouse/backup.env`; cấu hình SMTP đọc từ runtime.env. Watchdog mỗi giờ phát hiện quá27giờ chưa có backup thành công; cảnh báo có cooldown1giờ. Unit tests không chứng minh thư thật đã Delivered. Kiểm tra service/timer và kênh cảnh báo thực tế khi bàn giao host.

Khôi phục: tải xuống/giải mã vào thư mục riêng có quyền hạn chế, kiểm SHA256, restore dump vào PostgreSQL17 tạm và kiểm dữ liệu/key ring. Chỉ sau khi xác nhận recovery point mới chạy helper thay database có khóa vận hành, downtime và backup hiện trạng. `rehouse-restore` kiểm checksum, dùng DBA cho tạo/swap DB, restore objects dưới migration role và cấp lại DML cho runtime. Không đưa snapshot còn mã hóa trực tiếp vào helper.

Production dùng role DML `roomie_app`, migration owner `room_rental`, DBA `roomie_dba` tách riêng. SSH quản trị dùng tài khoản sudo/key, không dùng root/password. Khóa giải mã phải lưu trong password manager/offline riêng, cùng cả password và salt; không lưu cạnh snapshot Drive.

Đã có bằng chứng upload/download/giải mã/restore thử trên database tạm và refresh OAuth thực tế; lịch sử nội bộ chứa định danh tài khoản/folder và vị trí recovery được giữ riêng. Không coi backup là HA, PITR hoặc lưu trữ bất biến. Mã nguồn triển khai: `deploy/rehouse-backup.sh`, `rehouse-drive-sync.py`, `rehouse-backup-alert.py`, `rehouse-restore.sh`; xem [runbook triển khai](deployment.md).

## Kiểm tra vận hành

Xem `rehouse-backup.timer`, `rehouse-backup-watchdog.timer`, service status/journal và marker `/var/lib/rehouse/backup-last-success`; không coi unit đã cài là backup đã thành công. Restore drill trên database tạm trước khi chọn recovery point. Helper restore `sudo /usr/local/sbin/rehouse-restore /var/backups/rehouse/<snapshot> --replace-current-data` thay dữ liệu hiện tại; chỉ dùng cho recovery đã được xác định và cho phép. Không tự chạy trong QA hay dọn repository.

Backup DB/DataProtection không tự chứng minh đã bảo vệ session Zalo. Khi có gateway, kiểm riêng session volume, key mã hóa và ledger theo [hướng dẫn Zalo](zalo-setup.md); đối chiếu các tin đã gửi sau recovery point trước khi cho gửi lại. Không lưu khóa giải mã cạnh snapshot.

# Triển khai và rollback

Runbook từ mã nguồn ngày 21/10/2026; không xác nhận trạng thái live. Đọc [AGENTS.md](../AGENTS.md) trước. Website chính `https://roomie.io.vn`; repo `nhaq0505/Roomie`, image giữ `ghcr.io/nhaq0505/rehouse`.

## Chuỗi phát hành

`.github/workflows/ci-cd.yml` chạy khi PR/push main hoặc workflow_dispatch. Các job frontend/backend, Zalo gateway, Playwright và HTTP integration phải đạt trước publish. Image được integration kiểm tra được upload/download để promote đúng image, không build lại một image khác sau QA. Publish theo SHA rồi lấy digest; deploy chỉ nhận `ghcr.io/nhaq0505/rehouse@sha256:...`.

SSH deploy dùng tài khoản bị giới hạn forced-command, pinned known_hosts và key từ Actions secrets. `deploy/rehouse-ssh-command.sh` chỉ chấp nhận repository/digest đúng rồi gọi helper root. Token GHCR qua stdin, Docker credentials tạm dưới `/run`, xóa sau pull. Không đưa token vào command line, log hoặc Git.

`deploy/rehouse-deploy.sh`:

1. Lấy ops lock chung `/var/lib/rehouse/ops.lock` với backup/restore.
2. Pull digest, kiểm Compose và DB readiness.
3. Backup DB + DataProtection và kiểm off-site trước khi thay app; lỗi backup chặn swap.
4. Chạy schema bằng API image `Database__MigrateOnly=true` qua migration overlay, role `room_rental`, cấp lại DML runtime.
5. Thay app, kiểm health/DB/admin/key ownership, ghi digest hiện tại. Khi candidate lỗi, quay về app digest trước; không tự rollback schema.

Đọc script trước thao tác; pipeline xanh không chứng minh provider thật hay mọi cấu hình host đã được kiểm tra.

## Hợp đồng host

| Thành phần | Đường dẫn / quy tắc |
| --- | --- |
| Compose vận hành | `/opt/rehouse/compose.yaml` (root-owned), nguồn `deploy/compose.yaml` |
| Migration overlay | `deploy/migrate.yaml`; không dùng project import `RoomRental.DbMigrator` |
| Runtime config | `/etc/rehouse/runtime.env`, quyền 0600, không commit |
| Helper | `/usr/local/sbin/rehouse-deploy`, `rehouse-backup`, `rehouse-restore`, `rehouse-db-roles` |
| State/digest | `/var/lib/rehouse/current-image`, `/var/lib/rehouse/ops.lock` |
| Backup | `/var/backups/rehouse`, root-only |
| DB volume | `rehouse_postgres_data` |
| DataProtection volume | `rehouse_data_protection_keys` |
| Monitoring mount | `/var/lib/rehouse/monitoring/public:/app/monitoring-host:ro` |

PostgreSQL 17 không publish port. App chỉ bind `127.0.0.1:8080`; Caddy công khai HTTPS 80/443, config nguồn `deploy/Caddyfile`. `TrustedProxies` chỉ chứa IP peer thực tế; không tin mọi proxy. Giữ Host/X-Forwarded-Proto/X-Forwarded-For. API Production dùng Secure cookie, không chạy login/payment bằng HTTP public.

DB runtime `roomie_app` chỉ DML, migration owner `room_rental`, DBA `roomie_dba` tách riêng. Không đưa DBA/migration password vào steady-state app. `deploy/rehouse-db-roles.py` cấp quyền; `tests/check_db_roles.py` kiểm DML/denied DDL trên container tạm.

Config Compose host và repo phải đối chiếu trước khi cài helper. Override Zalo đã được ghi nhận trong lịch sử vận hành nhưng không nằm đầy đủ trong Compose host mẫu của repo; không ghi đè host config chỉ để đồng bộ source. SSH quản trị dùng sudo/key; các lệnh provision root là bootstrap host mới, không phải cách đăng nhập thường ngày.

## Cấu hình tích hợp

- Origin: `PUBLIC_BASE_URL`, `WEBHOOK_BASE_URL` = origin HTTPS đúng. Webhook `/api/payments/webhook/{ownerId}`; đổi URL/khóa làm verification cũ hết hiệu lực. Chủ trọ xác minh lại bằng UI; không tự tạo payment để thử.
- SMTP: `SMTP_FROM_ADDRESS`, `SMTP_FROM_NAME`, `SMTP_HOST`, `SMTP_PORT`, `SMTP_SECURITY`, `SMTP_USERNAME`, `SMTP_PASSWORD` được Compose map sang `Email__*`. StartTls/587 hoặc SslOnConnect/465; Production từ chối SMTP không mã hóa. Thêm env vào file chưa đủ nếu Compose chưa map biến.
- Admin bootstrap: chỉ tạo admin chưa có từ `BootstrapAdmin__Email/Password`; không reset mật khẩu admin đã tồn tại. Migration overlay nhận bootstrap khi cần; steady-state host app không mang bootstrap password.
- Zalo: [zalo-setup.md](zalo-setup.md), gateway nội bộ, một process/một session volume, token và key riêng. Root Compose dev override không phải lệnh thay cấu hình VPS.
- Monitoring: [runbook](monitoring/runbook-and-metric-dictionary.md). Không giả định biến host tự truyền vào container; cần map env/mount thật.
- SEO: `SEO_ALLOW_INDEXING` điều khiển noindex, nhưng robots vẫn cố ý chặn tất cả bot. Không tự mở crawl.

## Kiểm tra chỉ đọc

Trên host, qua tài khoản quản trị phù hợp:

```sh
sudo systemctl status caddy rehouse-backup.timer rehouse-host-collector.timer
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
curl --fail http://127.0.0.1:8080/api/health
curl --fail https://roomie.io.vn/api/health
```

Đối chiếu digest/revision container với commit và Actions run trước khi ghi “đã deploy”. Health 200 chỉ xác minh kết nối DB, không chứng minh hàng đợi/providers hoạt động. Không dùng `docker inspect` in toàn bộ env hoặc `compose config` xuất secrets vào log; dùng `config --quiet` khi kiểm cú pháp.

## Rollback và phục hồi

Ứng dụng quay image trước chỉ an toàn nếu schema mới tương thích. Đối chiếu Up/Down và backup trước khi quyết định. Không tự chạy Down migration trên production. Restore thay dữ liệu cần đúng recovery point và downtime; xem [backup-operations.md](backup-operations.md). Không lấy backup empty/pilot cũ để restore dữ liệu hiện tại.

Không chạy `docker compose down -v`, `docker volume prune`, reset SQL hoặc broad prune trên host để chữa deploy lỗi. Khóa, sessions và DB là dữ liệu cần bảo toàn. Không seed, chạy HTTP test mutating hoặc đăng ký webhook/payment thật trong QA production.

Host mới: review `deploy/provision-vps.sh`, `install-host.sh`, security/firewall và ingress trước khi chạy. Provisioner cần target/key rõ ràng; backup Drive, monitoring và Zalo có installer/config riêng. Không coi việc app boot được là nghiệm thu backup, provider hay HA.

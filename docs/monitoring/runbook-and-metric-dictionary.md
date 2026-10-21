# Monitoring — nguồn dữ liệu và vận hành

Đối chiếu source ngày 21/10/2026, baseline `7be139d`; không xác nhận host live. Kiến trúc chung và các điểm còn thiếu: [AGENTS.md](../../AGENTS.md). Các báo cáo review theo task đã được thay bằng runbook này.

## Nguồn đúng theo phần việc

| Phần | Source |
| --- | --- |
| API/DTO/entity | `Features/Monitoring/MonitoringEndpoints.cs`, `Domain/MonitoringDtos.cs`, `MonitoringEntities.cs` |
| Intake, attribution, milestones, growth/revenue, maintenance | `Features/Monitoring/MonitoringServices.cs` |
| Đăng ký service | `Features/Monitoring/MonitoringServiceCollectionExtensions.cs` |
| Latency/host/worker/incident/SMTP | `Features/Monitoring/Operations/` |
| Browser tracker | `frontend/src/shared/analytics/browserTracker.ts` |
| Admin UI và query keys | `frontend/src/features/admin/monitoring/` |
| Host collector | `deploy/monitoring/rehouse-host-collector.py`, installer và units cùng thư mục |

`AddMonitoringCore` đăng ký tracking/query/maintenance; `AddMonitoringOperations` thay các đăng ký Ops bằng implementation thật và đăng ký hai hosted workers: minute flush và monitoring scheduler.

## Metrics và ranh giới

- `/api/analytics/events`: intake public có limiter; event server timestamp là căn cứ. Browser/session IDs và attribution không phải user identity. Không lưu email/phone/OTP/tenant token/bank details/IP thô hay raw query nhạy cảm vào telemetry.
- `/api/admin/monitoring/growth`: funnel đăng ký, activation, active owners, retention và nguồn attribution. Activation dùng phát hành Monthly đầu tiên; Deposit/Supplemental không tính. Owner action khác System/Admin/tenant viewing. Raw telemetry hết retention phải trả null/reason thích hợp.
- `/api/admin/monitoring/revenue`: order `Subscription Paid` đã xác minh. Không cộng tiền thuê, cọc hoặc Pro admin tặng. Milestones được server kiểm từ nghiệp vụ, không tin client khai đã paid.
- CAC30 dùng chi phí cash + giờ công đã định giá và cohort đăng ký chuyển paid trong 30 ngày. Giữ lý do null `MissingCostData`, `MissingValuation`, `NoRegistrations`, `CohortImmature`, `ZeroDenominator`.
- Campaign metadata `/campaigns/metadata`, costs `/campaigns/costs`, tracking URL `/campaigns/generate-url`; có aliases tương thích ở handler. Kiểm DTO trước đổi field/filter.
- `/operations?window=1h|24h`: route/status counts và p95 ước lượng từ histogram, DB health, worker states và host JSON. Không lấy trung bình các p95 để gộp. Bộ đếm process không phải tổng toàn hệ thống nhiều replica.
- `/incidents`: đọc các incident đang lưu; PATCH state ghi acknowledged/resolved và note. Unknown/thiếu mẫu không được tự coi là Healthy hay recovery.

Outbox `MilestoneOutboxEvents` lưu cùng transaction product; sau commit thử tracker trong context riêng. Chỉ chuyển Completed khi milestone đã ghi bền vững. Pending replay tối đa theo logic service, Failed cần đối chiếu; không xóa để làm sạch dashboard. Invoice sweep chạy repair khi được bật, không phải queue daemon riêng liên tục.

## Lịch chạy trong ứng dụng

| Tác vụ | Hiện trạng trong source |
| --- | --- |
| Latency minute flush và prune route buckets >30 ngày | `OpsMinuteFlushWorker`, mỗi phút |
| Auth cleanup | `DataMaintenanceService`, mỗi 10 phút; không dọn analytics |
| Raw analytics >90 ngày; activity/daily aggregate >13 tháng; Completed outbox >30 ngày | `MonitoringBackgroundWorker`, sau rollup thành công mỗi giờ; vẫn có admin POST `/maintenance/prune` |
| Daily rollup | `MonitoringBackgroundWorker`, mỗi giờ tính lại 7 ngày đã kết thúc theo giờ Việt Nam; vẫn có admin POST `/maintenance/rollup` |
| App incident engine/SMTP notifier | `MonitoringBackgroundWorker` gọi `TriggerIncidentCheckAsync` mỗi phút |
| Host collector/host SMTP | systemd timer riêng, mỗi phút sau khi installer/config được áp dụng |

Hai vòng lịch chạy ngay khi API khởi động, dùng scope riêng mỗi chu kỳ và không chạy chồng chính nó. Maintenance chậm/lỗi không chặn incident probes; lỗi được ghi telemetry/log và thử lại ở chu kỳ sau. Dừng host hủy cả tác vụ đang chạy và lịch chờ. `incident-check` có SLA heartbeat 10 phút, `monitoring-maintenance` 2 giờ.

Cấu hình section `Monitoring:Schedule` (env thay `:` bằng `__`):

| Key | Mặc định | Giới hạn |
| --- | --- | --- |
| `IncidentChecksEnabled` | `true` | Tắt vẫn hiển thị worker Disabled |
| `MaintenanceEnabled` | `true` | Tắt vẫn hiển thị worker Disabled |
| `IncidentIntervalSeconds` | `60` | 60–300 giây |
| `MaintenanceIntervalSeconds` | `3600` | 300–3600 giây |
| `RollupDays` | `7` | 1–89 ngày, trong raw retention 90 ngày |

Cấu hình ngoài giới hạn làm startup thất bại, tránh busy loop hoặc tổng hợp từ raw đã hết hạn. Recompute chỉ gồm ngày hoàn tất; các chỉnh sửa/ngày thiếu cũ hơn `RollupDays` cần admin rollup riêng trước khi raw hết retention. Khi một rollup lỗi, chu kỳ đó không prune. Lịch và chống overlap ở một process, **chưa có distributed lease cho nhiều replica**. Xóa lịch external trùng nếu host đã cấu hình riêng; chưa kiểm tra host live.

Mất DB: xác nhận sau 3 probe thất bại cách nhau ít nhất một phút; gửi SMTP trực tiếp không truy vấn/ghi DB. Incident/cooldown nằm trong RAM đến khi kết nối lại, sau đó ghi lịch sử vào DB và gửi recovery qua luồng retry hiện có. Restart trong lúc DB mất sẽ mất trạng thái RAM và có thể gửi lại cảnh báo sau khi đủ probe. Các rule cần DB chờ kết nối hồi phục. App reminders giữ cooldown 1 giờ; SMTP lỗi không tính là gửi thành công. Collector vẫn chịu trách nhiệm gửi cảnh báo CPU/RAM/disk/container và backup watchdog; app không gửi lại các cảnh báo collector này.

Source có scheduler không chứng minh VPS đang chạy bản này hoặc đã nhận thư: cần deploy, cấu hình recipient/SMTP và kiểm tra runtime riêng.

## Host collector: cài và cấu hình

Review rồi chạy `sudo bash deploy/monitoring/install-monitoring.sh` trên đúng host. Installer cài `/usr/local/sbin/rehouse-host-collector`, units `rehouse-host-collector.service/.timer`, tạo `/etc/rehouse/monitoring.env` nếu chưa có; không chép unit mẫu tự chế thay installer.

- Public JSON: `/var/lib/rehouse/monitoring/public/host-metrics.json`, quyền 0644; app mount đúng thư mục public read-only `/app/monitoring-host`.
- Private state/SMTP queue: `/var/lib/rehouse/monitoring/collector-state.json`, quyền 0600; không mount thư mục cha vào app.
- Backup marker: `/var/lib/rehouse/backup-last-success`, watchdog quá 27 giờ. Backup không được app incident engine gửi cảnh báo trùng.
- Collector đọc cấu hình private từ host; `MONITORING_ALERT_TO` có fallback `BACKUP_ALERT_TO`/`ALERT_TO`. SMTP dùng cấu hình đã cấp, không log địa chỉ/credentials. Kiểm `load_config` khi cần thêm biến.
- API host reader: `Monitoring__HostMetricsPath` mặc định `/app/monitoring-host/host-metrics.json`, hoặc `MONITORING_HOST_METRICS_PATH` override; `Monitoring__StalenessThresholdSeconds` mặc định 180.
- App notifier: section `Monitoring:Alerts` với `AlertEmailTo`, `AlertEmailFrom`, `AlertEmailFromName`, `Enabled`; recipient fallback `MONITORING_ALERT_TO`. Cả hai Compose đã map `MONITORING_ALERT_TO`, `MONITORING_ALERTS_ENABLED`, `MONITORING_INCIDENT_CHECKS_ENABLED`, `MONITORING_MAINTENANCE_ENABLED` vào container. SMTP tái dùng biến `SMTP_*` hiện có; host collector đọc env riêng nên phải cấu hình riêng.
- Analytics limiter hiện được khai báo cố định trong `Program.cs` (120/phút Production, 600 Development), không có cấu hình `RateLimiting__AnalyticsPermitLimit` như tài liệu cũ.

Host JSON dùng `generated_at`, host metrics, containers/services, incidents/history và backup-watchdog; schema thực tế ở collector writer và `LinuxHostCollectorMetricsReader`. Reader chặn file quá 2 MB/JSON depth quá sâu, timestamp tương lai và dữ liệu stale. Thiếu file/thiếu số không biến thành CPU=0 hay host healthy.

Chỉ collector host truy cập Docker; không mount `/var/run/docker.sock` vào API. Collector chạy trên cùng VPS không thể gửi mail khi toàn máy/mạng chết; phát hiện whole-host outage cần probe ngoài VPS.

## Kiểm tra/diễn tập

```sh
python3 -m unittest discover -s tests -p 'test_host_collector.py' -v
dotnet run --project tests/RoomRental.WebhookTests
npm --prefix frontend run test:analytics
npm --prefix frontend run test:dates
npm --prefix frontend run build
npm --prefix frontend run test:e2e -- monitoring --workers=2
```

Trên host chỉ kiểm đọc: status/timer/journal của collector, generated_at và quyền/mount public JSON, admin operations UI. Không in private state hoặc full runtime.env.

Alert drill dùng environment/disposable fixture và người nhận thử được chỉ định. Kiểm trigger, cooldown, Unknown/thiếu mẫu không tự resolve, recovery và retry SMTP queue; fake SMTP hoặc accepted response không chứng minh mailbox Delivered. Không cố ý làm DB/VPS production lỗi để chứng minh cảnh báo. Kiểm hai worker mới trong Operations, sau đó diễn tập trên fixture cô lập; không suy ra delivery từ số lần gửi trong DB.

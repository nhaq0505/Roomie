# Truy cập VPS chính Roomie

## Thông tin đã kiểm tra

Kiểm tra ngày 21/10/2026 qua Enduser Panel đang đăng nhập trong Microsoft Edge:

| Mục | Giá trị |
| --- | --- |
| VPS chính | `116.118.3.40` — ACTIVE_EXECUTION_NODE theo người dùng |
| VPS ID / dịch vụ | `904` / `1603` |
| Hostname | `thuevpsgiare-1786212404` |
| OS panel / console | Ubuntu 24.04 x86_64 / Ubuntu 24.04 LTS |
| Tài nguyên panel | 2 vCPU, 2 GB RAM, ổ 40 GB |
| Trạng thái panel | Online; ổ dùng 10.47 GB tại thời điểm xem |
| Enduser Panel | `https://103.78.2.31:4083` |
| Trang dịch vụ | `https://support.thuevpsgiare.com/clientarea.php?action=productdetails&id=1603` |
| SSH quản trị đã xác minh | `roomie-ops`, cổng 22, key `/Users/nguyenhoanhquan/.ssh/crypto_signal_hub_vps`; `sudo -n` hoạt động |

Đã kiểm tra thêm trong OS: RAM 1967 MiB, swap 1023 MiB; trước tải còn khoảng 1258 MiB RAM khả dụng. Container live: `rehouse-app-1` (1,25 CPU/640 MiB), `rehouse-db-1` (0,65 CPU/384 MiB), `rehouse-zalo-1` (192 MiB). Image API khi kiểm tra: `sha256:661e75f6bc3ec5b1a0d305ee7b61e86267ddeb52b8c018a84b00495d72666d1a`, revision `f9b53909182326b4c180c02f26c8be12c4b5f82b`. Đọc lại tài nguyên/image mỗi lần đo.

## Mở panel và console

1. Trong Edge, mở trang dịch vụ ở trên bằng phiên đăng nhập của chủ tài khoản.
2. Chọn **Enduser Panel**, xác nhận đúng IP và hostname trước khi thao tác. URL có `/sess.../` là phiên tạm; không lưu URL đó vào repo.
3. **Overview** hiển thị cấu hình và đồ thị. **Settings → SSH Keys** liệt kê tên `crypto-signal-hub-temporary-deploy` và `WebPhim` khi kiểm tra; danh sách này không chứng minh key đang đăng nhập được vào OS.
4. Nút màn hình phía trên có nhãn **Connect to the VPS via VNC.** → **Launch VNC** mở console noVNC. Panel hiển thị VNC host `sv2.ezvps.vn`, port `6272` tại thời điểm kiểm tra; nên lấy lại từ panel mỗi lần dùng.
5. Console đã kết nối mã hóa tới QEMU, hiện `Ubuntu 24.04 LTS ... tty1` và dấu nhắc `login:`. Cần tài khoản/mật khẩu hệ điều hành hiện có; phiên panel không tự đăng nhập root.

Nếu trình duyệt hiện cảnh báo chứng chỉ, người dùng tự xử lý cảnh báo. Không tự reset mật khẩu, bấm **Use SSH Key**, thêm key, bật Rescue Mode, cài lại OS, reboot hoặc dừng VPS để thử truy cập.

## SSH và chẩn đoán

```sh
ssh -i /Users/nguyenhoanhquan/.ssh/crypto_signal_hub_vps \
  -o IdentitiesOnly=yes -o BatchMode=yes \
  -o ConnectTimeout=10 -o StrictHostKeyChecking=yes \
  roomie-ops@116.118.3.40
```

Đã xác định bằng console: cấu hình hardening `00-roomie-hardening.conf` đặt **`PermitRootLogin no`**. Vì vậy `root@116.118.3.40` bị từ chối dù key hợp lệ. Cùng key đăng nhập `roomie-ops@116.118.3.40` thành công, có sudo không tương tác. Không bật SSH root để chữa lỗi; dùng tài khoản quản trị hiện có. Người dùng đã đăng nhập root qua console; quyền console tách biệt với chính sách SSH.

Fingerprint key được đưa ra: `SHA256:UwD4w553Y0xI/hh7VTC56exIdW/Z0aoM4tQBAFA5hJo`.

Sau khi chủ tài khoản đăng nhập console bằng thông tin hiện có, dùng các lệnh chỉ đọc để xác định lỗi:

```sh
whoami
sudo journalctl -u ssh --since '15 minutes ago' --no-pager -n 80
sudo passwd -S root
sudo sshd -T | rg 'permitrootlogin|pubkeyauthentication|authorizedkeysfile|authenticationmethods|allowusers|denyusers'
sudo ssh-keygen -lf /root/.ssh/authorized_keys
```

Ubuntu có thể chưa cài `rg`; khi đó thay bằng `grep -E`. Nếu có `Match` trong cấu hình SSH, cần kiểm tra thêm `sshd -T -C` với user/IP thực tế. Không đưa log chứa IP riêng hoặc thông tin tài khoản vào Git. Chỉ sửa quyền/key/cấu hình khi đã xác định nguyên nhân và có phạm vi cho phép rõ ràng.

## Trước khi đo tải

Kiểm tra `uptime`, `free -m`, `df -h /`, `docker ps`, `docker stats --no-stream` và digest triển khai. Không in toàn bộ container env hoặc `/etc/rehouse/runtime.env`. Xem [deployment](deployment.md) về đường dẫn và dữ liệu phải giữ.

Dùng fixture riêng, tăng tải từng mức và theo dõi sức khỏe dịch vụ đang chạy. Không dùng các HTTP regression harness với DB production; không gửi SMTP/Zalo/payOS thật. Báo cáo số người dùng đồng thời phải kèm hành vi, dữ liệu, thời gian đo, p95 và tỷ lệ lỗi. Không suy sức chứa từ health 200 hoặc cấu hình CPU/RAM.

## Bài tải đọc live được người dùng yêu cầu

`tests/load_live_readonly.py` là bài đo riêng, chỉ chạy sau khi người dùng yêu cầu đo dịch vụ live. Khác regression harness: setup SQL tạo 8 owner giả (20 phòng/240 hóa đơn mỗi owner), login bằng mật khẩu ngẫu nhiên chỉ giữ trong RAM, phần tải chỉ GET dashboard/phòng/hóa đơn/dãy/chỉ số. Không dùng credentials hoặc tenant tokens của người thật. Tài khoản test không có số điện thoại, tenant token hay cấu hình payOS. Setup không tạo Zalo reminder slots. `finally` xóa đúng UUID fixture và xác minh owner test không còn; không xóa theo prefix chung. Những fixture này có thể tạm xuất hiện trong thống kê khi đo; HTTP latency/counter của bài test vẫn được hệ thống monitoring ghi nhận.

```sh
/usr/bin/python3 tests/load_live_readonly.py --self-check
# Chỉ sau khi được yêu cầu đo live, và chọn output mới để giữ bằng chứng cũ:
/usr/bin/python3 -u tests/load_live_readonly.py --allow-live-fixture \
  --output artifacts/stress-live-<run-id>.jsonl
```

Mỗi virtual user có 3 API reads/10 giây, phân tán thời điểm bắt đầu; nhiều virtual users dùng lại 8 phiên owner. Ramp 10/25/50/100/200/400/800 users, 40 giây/mức; giữ mức đạt cao nhất thêm 120 giây. Bỏ 10 giây đầu mỗi mức khi tính steady latency/rate. Dừng mức khi lỗi ≥1% hoặc p95 >1 giây trên ít nhất 50 request gần nhất; hoặc health không 200/chậm >1 giây, RAM khả dụng <250 MiB, CPU host ≥90% ở hai lần lấy mẫu liên tiếp. Probe SSH/Docker lấy mẫu, không giám sát liên tục. Nếu mất SSH, dừng tải và dùng manifest UUID riêng trong `artifacts/*.fixture.json` để xác minh/dọn đúng fixture; không chạy lại bài tải chỉ để dọn.

Để đo **200 chủ trọ khác nhau**, thêm `--distinct-owners`: 200 owner/200 session riêng, tổng 4.000 phòng và 48.000 hóa đơn; ramp 25/50/100/200. Mặc định đăng nhập cách nhau 6,5 giây để tuân thủ limiter. Chỉ khi người dùng yêu cầu tạm nâng giới hạn, thêm `--temporary-auth-limit`: override `/run/roomie-stress-auth.yaml` nâng `AuthRequestsPerMinute` từ 10 lên 1000 trong lúc login setup. Không tắt guard theo tài khoản. API được recreate bằng đúng image hiện tại, không pull; Compose environment phải khớp runtime trước khi đổi. Hẹn giờ systemd `roomie-stress-auth-restore.timer` khôi phục sau 15 phút nếu mất kết nối; harness chủ động khôi phục ngay sau login và đợi health trước khi bắt đầu tải GET. Hai lần recreate API có thể gây gián đoạn ngắn.

Khôi phục thủ công nếu setup bị ngắt: SSH bằng `roomie-ops`, chạy `sudo -n python3 /run/roomie-stress-auth-restore.py`, rồi kiểm tra `sudo -n docker exec rehouse-app-1 printenv AuthRequestsPerMinute` trả `10` và `/api/health` trả 200. Script khôi phục dùng Compose gốc và image đã lưu trong `/run`; không thay file Compose gốc hoặc runtime secrets. Phải dọn fixture riêng theo manifest kể cả khi timer đã khôi phục cấu hình.

Đây là tải API đọc trên VPS live, không tải trình duyệt/assets và không đo ghi tiền, checkout, Zalo, SMTP hay login hàng loạt. Không đổi số virtual users thành số chủ trọ duy nhất hoặc tổng số tài khoản có thể lưu. Cần dữ liệu lớn hơn và bài đo ghi riêng nếu phạm vi đó được yêu cầu.

### Kết quả đã đo ngày 21/10/2026

Đo HTTPS public từ máy người dùng, đúng image/revision nêu đầu file; không đổi cấu hình live. Fixture có 8 owner Pro giả, tổng 160 phòng, 160 occupancy, 1920 hóa đơn Monthly trong 12 tháng; không có lịch sử chốt điện/token khách/provider settings. Các phiên mô phỏng chia sẻ 8 tài khoản đăng nhập.

| Mức tải | Request/giây thực đo | p95 API | Lỗi | Kết quả |
| --- | --- | --- | --- | --- |
| 10 virtual users | 3,00 | 120 ms | 0 | Đạt ramp 40 giây |
| 25 virtual users | 7,50 | 117 ms | 0 | Đạt ramp 40 giây |
| 50 virtual users | 15,00 | 135 ms | 0 | Đạt ramp 40 giây |
| 100 virtual users | 30,00 | 270 ms | 0 | Đạt ramp 40 giây |
| 200 virtual users | 60,10 | 268 ms | 0 | Đạt ramp 40 giây |
| 400 virtual users | 99,81 (mục tiêu 120) | 2443 ms | 0 | Tự dừng sau 20,48 giây do p95 >1 giây |
| 200 virtual users, duy trì 120 giây | 59,99 | 298 ms | 0 | Đạt; cửa sổ steady 110 giây có 6599 request |

Mốc đã kiểm chứng: **200 phiên hoạt động mô phỏng theo profile 3 lần đọc/10 giây**. 400 phiên không đạt ngưỡng độ trễ; chưa xác định ngưỡng chính xác giữa 200–400 hoặc sức chịu tải ghi/soak dài hạn. PostgreSQL đo gần hết quota 0,65 CPU ở mức 400 (Docker khoảng 68%, với 100% tương đương một CPU), trong khi API khoảng 60% và RAM host vẫn còn >1,1 GiB; đây là dấu hiệu nút thắt quota CPU DB, chưa phải phép đo chứng minh mọi query đã tối ưu.

Sau đo: fixture owner/building/invoice/session đều còn 0; số user/phòng/hóa đơn toàn DB trở về 3/2/5 như trước đo. Health 200 (~108 ms); API/DB không restart, không OOM, digest giữ nguyên. Đã thoát phiên root console, không đổi chính sách SSH.

Bằng chứng local: [raw timings](../artifacts/stress-live-20261021.jsonl), [summary](../artifacts/stress-live-20261021-summary.json). Rate trong summary tính lại từ timestamp request, bỏ 10 giây đầu và thời gian SSH observer/drain sau cửa sổ; raw log của lần chạy đầu dùng cả observer elapsed trong mẫu số nên rate in trực tiếp thấp hơn. Không sửa raw timings. Manifest UUID trong cùng thư mục đã đánh dấu cleanup verified. Các artifact được ignore, không tự push chúng.

Không lưu mật khẩu, private key, session panel, URL console chứa token hay API credentials trong tài liệu này.

### 200 chủ trọ riêng biệt — 22/10/2026

Đã đo 200 owner/200 session riêng, mỗi owner 20 phòng và 240 hóa đơn: tổng 4.000 phòng/48.000 hóa đơn. Người dùng yêu cầu tạm nâng login limiter; setup dùng 1000/phút rồi khôi phục 10/phút trước khi chạy GET. API được recreate hai lần, cùng image/quota; không đổi DB/gateway. Ramp 25/50/100/200 đạt, p95 tương ứng 277/171/273/330 ms. Giữ 200 owner trong 120 giây đạt 59,88 request/giây, p95 348 ms, p99 522 ms; cửa sổ steady 110 giây có 6587 request, toàn bộ HTTP 200. Đây là capacity đọc ngắn hạn theo profile đã nêu, chưa đo ghi hoặc soak dài hạn.

Harness xác minh dọn đủ 200 fixture owner. Preflight lần kế tiếp xác minh fixture owner còn 0, DB totals về 3 user/2 phòng/5 hóa đơn, auth limit 10, image giữ nguyên, API/DB restart count 0 và OOM false. Restart count không tính việc recreate có chủ đích khi đổi limiter. Bằng chứng: `artifacts/stress-live-200-owners-20261022.jsonl`, `*-summary.json`, `*.fixture.json`.

Đo ngưỡng cao hơn bằng `--max-owners 400` hoặc `800` (mỗi virtual user dùng một owner khác nhau). Ramp 100/200/250/300/350/400, thêm 500/600/800 khi chọn 800; khi có mức không đạt thì thử khoảng giữa theo bước 25, sau đó giữ mức tốt nhất 120 giây. Nếu giữ tải không đạt, giảm 25 và đo lại. Các ngưỡng là kết quả theo thời gian đo, không phải bảo đảm về maximum tuyệt đối; nếu đạt toàn bộ ramp thì mới chỉ có cận dưới tại mức cao nhất đã thử. Chỉ thêm `--temporary-auth-limit` khi có yêu cầu nâng limiter, như lần đo này.

### Ngưỡng tải đọc — 22/10/2026

Chạy `--max-owners 400 --temporary-auth-limit`, cùng image/quota và profile 3 reads/10 giây. Fixture có 400 owner riêng, 8.000 phòng, 8.000 occupancy, 96.000 hóa đơn. Mỗi mức tải N dùng N owner/phiên khác nhau; các owner còn lại không gửi workload. Login limiter đã trả về 10/phút trước tải GET.

| Mức tải | RPS thực đo | p95 API | Kết quả |
| --- | --- | --- | --- |
| 100 owner | 30,13 | 281 ms | Đạt ramp |
| 200 owner | 60,30 | 298 ms | Đạt ramp |
| 250 owner | 70,80 / mục tiêu 75 | 973 ms toàn cửa sổ; 1013 ms cửa sổ gần nhất | Dừng sau 27,74 giây vì latency guard |
| 225 owner | 67,40 | 419 ms | Đạt refine |
| 225 owner, giữ 120 giây | 67,45 | 413 ms (p99 560 ms) | Đạt; 7420/7420 steady request HTTP 200 |

Ngưỡng đã kiểm chứng theo bước 25 là **225 owner hoạt động đồng thời đạt, 250 không đạt ngưỡng độ trễ**; chưa đo từng mức 226–249. Không chạy tiếp lên 300/400 vì đã chạm ngưỡng dừng. Cửa sổ steady bỏ 10 giây đầu; stage observer/drain có thể làm elapsed thực >120 giây. Toàn bộ request ghi nhận trong các stage đều HTTP 200; failure ở 250 là độ trễ. Với 400 owner lưu sẵn, lượng dữ liệu nền gấp đôi lần đo 200 owner; không diễn giải kết quả trước và sau như cùng một dataset.

PostgreSQL nhiều mẫu khoảng 65–69% CPU theo Docker (100% = một CPU), gần quota 0,65 CPU; API thấp hơn quota 1,25 CPU và host còn >1,1 GiB RAM. Đây là dấu hiệu nút thắt CPU DB, chưa phải kết luận từ profiling query. Không tự tăng quota/deploy trong lần đo này. Đây là test đọc ngắn hạn, không phải capacity ghi tiền/provider hoặc SLA dài hạn.

Xác minh cuối: fixture owner/building/invoice/session đều còn 0, DB totals về 3 user/2 phòng/5 hóa đơn; limiter 10/phút, override tạm đã xóa, image giữ nguyên, API/DB đang chạy, restart count 0/OOM false. Health 200 trong 91 ms. API đã recreate có chủ đích hai lần khi đổi limiter; restart count 0 không có nghĩa không recreate. Self-check harness và `git diff --check` đạt, chưa commit/push. Bằng chứng: `artifacts/stress-live-max-owners-20261022.jsonl`, `*-summary.json`, `*-verification.json`, `*.fixture.json`.

## Profiling query trước khi tối ưu

Đã thực hiện theo yêu cầu bước 1 ngày 22/10/2026. PostgreSQL live chưa preload/cài `pg_stat_statements`, `log_min_duration_statement=-1`, EF command log mức Warning: không có lịch sử query để suy ra CPU theo endpoint của lần stress test. Không đổi logging, extension, role hay restart DB/API live để lấy số liệu.

`tests/profile_read_queries.py` chạy đúng API image digest đã đo và đúng PostgreSQL image hiện có, với quota API 1,25 CPU/640 MiB và DB 0,65 CPU/384 MiB, shared_buffers 96 MB/work_mem 2 MB/max_connections 20. Tạo mạng Docker `--internal`, API không publish port, DB/keys riêng; provider bị chặn bằng network/proxy, tắt invoice sweep/reconciliation/Zalo/incident/maintenance scheduler. Những worker luôn đăng ký chỉ truy cập DB tạm, không có dữ liệu gửi thư/provider. Áp migration từ image vào DB tạm, không sao chép dữ liệu hoặc keys thật. Fixture giống lần stress ngưỡng: 400 owner/8.000 phòng/96.000 hóa đơn, thêm đủ 400 auth session trong lần đo có planning. Backend worktree không khác revision live `f9b5390`.

```sh
/usr/bin/python3 tests/profile_read_queries.py --self-check
# Khi được yêu cầu profiling trên VPS; dùng output mới để giữ bằng chứng:
/usr/bin/python3 -u tests/profile_read_queries.py --run-vps \
  --output artifacts/query-profile-<run-id>.json
```

Mỗi endpoint warm-up 3 lần rồi đo 20 GET tuần tự trên một owner. `pg_stat_statements.track_planning=on` chỉ trên DB tạm; reset stats riêng trước mỗi endpoint. Lọc role API và loại SELECT của recovery worker, đếm cả query kiểm tra session bắt buộc. Ghi nhận SQL thực từ PostgreSQL log, bind lại tham số fixture, chạy `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` trong transaction read-only/timeout 3 giây. Thu được đủ 9/9/11/2/3 plan tương ứng query shape của từng endpoint. Không lưu password/cookie, redact session bearer khỏi plan. `finally` xóa đúng container/anonymous data volume/network riêng; tên tài nguyên lưu trong `*.resources.json` để kiểm tra nếu bị ngắt.

### Kết quả và thứ tự ưu tiên

Thời gian dưới đây là trung bình PostgreSQL trên mỗi API request, không phải p95 API hay thời gian CPU thuần:

| Endpoint | SQL/request, gồm auth | Planning | Execution | Tổng planning + execution | Tỷ trọng theo workload |
| --- | --- | --- | --- | --- | --- |
| Dashboard | 9 | 3,864 ms | 2,430 ms | 6,294 ms | 31,1% |
| Phòng | 9 | 2,649 ms | 0,931 ms | 3,580 ms | 21,3% |
| Hóa đơn, pageSize 10 | 11 | 3,036 ms | 4,548 ms | 7,584 ms | 45,0% |
| Dãy | 2 | 0,308 ms | 0,112 ms | 0,420 ms | 0,8% |
| Chỉ số | 3 | 1,434 ms | 0,302 ms | 1,737 ms | 1,7% |

Tỷ trọng lấy trọng số dashboard/phòng/hóa đơn/dãy/chỉ số 25/30/30/10/5% từ bài tải. Đây là tỷ trọng thời gian SQL trong phép đo cô lập, không phải phần trăm CPU production. Trung bình 8,6 SELECT/API request: 1000 người theo profile 0,3 request/giây sẽ tạo khoảng 2580 SELECT/giây nếu giữ nguyên các nhánh đã đo. Không dùng phép nhân này để dự đoán capacity sau tối ưu.

1. **Hóa đơn ưu tiên đầu:** `BillingEndpoints.InvoiceList.cs` thực hiện total count, ba SUM và draft count thành **5 lượt riêng** trên tập hóa đơn cùng owner. Plan dùng `IX_Invoices_OwnerId_Period` rồi Bitmap Heap Scan; mỗi lượt chạm 240 heap blocks cho 240 hóa đơn fixture. Draft count trả 0 vẫn duyệt/lọc 240 row. Hướng sửa bước 2 là gộp count/totals/draftCount trong một aggregate có điều kiện, mục tiêu giảm endpoint từ 11 xuống 7 query, giữ nguyên filter/search/lifecycle và snapshot tiền.
2. **Dashboard tiếp theo:** `OwnerManagementEndpoints.Dashboard.cs` có **5 SUM riêng**: toàn bộ billed/paid và ba chỉ tiêu tháng. Plan dùng cùng index; hai SUM toàn bộ chạm 242 shared buffers/lượt (240 heap + index), ba SUM tháng chạm 22/lượt. Ngoài ra hai count occupancy lặp join Buildings/Rooms/Occupancies; trong stats hai query này có planning khoảng 1,039 và 0,917 ms/request, cao hơn execution khoảng 0,281 và 0,255 ms. Gộp 5 SUM thành một query là mục tiêu nhỏ nhất, giảm 9 xuống 5 query; giữ đúng `max(0,billed-paid)` toàn bộ và tổng dư nợ từng invoice theo tháng.
3. **Phòng sau đó:** 9 query gồm auth, 6 query room/occupancy/meter/deposit và 2 query kiểm tra điều kiện chia sẻ link. `PaymentWebhookService.GetAsync` load toàn AppUser dù DTO chỉ cần thông tin payment/manual; nên xem xét projection khi làm bước 2. Không bỏ kiểm tra session hoặc điều kiện chia sẻ token để giảm query.

Plan không cho thấy full scan bảng Invoices 96.000 row trong các query owner nói trên. Các Seq Scan ở Readings/PaymentSettings/PayOsAttempts/DepositSettlements là trên bảng fixture rỗng, không phải bằng chứng cần thêm index. Tất cả SELECT đo có shared_blks_read=0: cache đã nóng, không ghi nhận đọc mới vào shared buffers trong cửa sổ đo. Không kết luận disk/CPU đã được profile đầy đủ từ riêng số này. Fixture tạo hóa đơn theo layout tổng hợp, không đại diện heap locality dữ liệu tăng dần thực tế. Chưa có readings, cọc, trả phòng, pending payment hoặc cấu hình provider, nên endpoint có các nhánh đó cần đo thêm sau này.

Đây là 20 lần đo tuần tự/endpoint trên owner đại diện, có overhead instrumentation; **không phải trace query của đỉnh tải live 250 người**. Planning/execution và EXPLAIN riêng có thể khác nhau do cache, process và CPU scheduling; dùng để chọn việc tối ưu, không thay benchmark tải đồng thời. Bước 1 chưa sửa code sản phẩm, thêm index, nâng quota hoặc hứa đạt 500–1000 người.

Bằng chứng local: `artifacts/query-profile-20261022.json` (lần đo execution), `artifacts/query-profile-planning-20261022.json` (planning + execution, dùng cho bảng trên), gồm normalized SQL/calls/buffers và toàn bộ JSON execution plans. Self-check parser có quote escaping, prefix/inline DETAIL, Parameters viết hoa và remote syntax; `git diff --check` đạt. Đã dọn toàn bộ môi trường tạm; chỉ còn ba container live và network gốc. DB live vẫn 3 user/2 phòng/5 hóa đơn; API/DB image và thời điểm start giữ nguyên, restart count 0/OOM false; public health 200 trong 94 ms. Chưa commit/push/deploy.

### Tối ưu invoice list trong worktree

Theo yêu cầu tiếp theo, đã thay 5 aggregate riêng bằng một aggregate có điều kiện trong `BillingEndpoints.InvoiceList.cs`. Query vẫn scope owner và toàn bộ bộ lọc trước phân trang; count bao gồm Draft, tổng tiền chỉ Issued, tập rỗng trả 0, dư nợ vẫn là tổng `Total - Paid` và không clamp dữ liệu cũ. Không sửa query lấy trang/cọc/reservation/occupancy/link hoặc response mảng cũ khi không có page. Giảm 4 query/request, tương ứng nhánh profiling 11→7; chưa deploy hoặc benchmark lại nên chưa có kết luận về capacity mới.

Kiểm chứng trên PostgreSQL QA local riêng, cổng 55432 (không dùng dev/live): API build 0 warning/error; toàn bộ nhóm Accounting/Billing đạt 81 PASS, gồm 22 assertion mới về owner scope, bộ lọc kỳ/lifecycle/status/kind/search/occupancy, totals ngoài pageSize=1, dữ liệu rỗng/nháp, số dư âm cũ và response legacy. Log EF xác minh mỗi ca chỉ một aggregate, tổng 4 SQL trong fixture (fixture bỏ auth DB và bypass payment verification; production nhánh cũ còn thêm 3 query tương ứng). Suite rộng đạt 600 PASS rồi dừng ở assertion monitoring `PaidSubscriptionCount == 1`; chạy riêng ca đó trên bản HEAD chưa sửa cũng lỗi đúng assertion này. Test dùng UTC date trong khi nghiệp vụ theo Việt Nam là dấu hiệu cần điều tra riêng, chưa kết luận/sửa nguyên nhân. CI mới nhất của HEAD `0bc2096` cũng đang failure trước thay đổi này.

Bằng chứng: `artifacts/invoice-query-accounting-tests.log`, `artifacts/invoice-query-full-tests.log`, `artifacts/invoice-query-baseline-tests.log`. Test regression giữ trong suite hiện có, không thêm framework. Chưa commit/push/deploy.


### Collector cho mục Vận hành của admin

Đã xác minh VPS trước đó không có collector/timer, không có public JSON và container API chỉ mount volume keys. Đã cài đúng bốn file từ `deploy/monitoring/` bằng installer của repo; `rehouse-host-collector.timer` enabled/active, service oneshot mỗi khoảng một phút, Result=success/ExecMainStatus=0. Private state `/var/lib/rehouse/monitoring/collector-state.json` không mount vào app. Public JSON `/var/lib/rehouse/monitoring/public/host-metrics.json` được mount `/app/monitoring-host:ro` bằng một dòng thêm vào `/opt/rehouse/compose.yaml`. Bản Compose trước sửa nằm tại `/opt/rehouse/compose.before-monitoring-20261022.yaml` (0600); giữ để đối chiếu các thay đổi host sau này, không ghi đè nguyên bản cũ khi đã có thay đổi khác.

Cài đặt tạo `/etc/rehouse/monitoring.env` (0600) với `SMTP_HOST=""` để việc thu thập không tự gửi thư thật. Chưa nghiệm thu email host alerts. Backup watchdog đọc mtime `/var/lib/rehouse/backup-last-success`, ngưỡng 27 giờ; trạng thái Healthy chỉ xác nhận marker còn mới, không chứng minh restore thành công.

API được recreate riêng với `--no-deps --pull never` và đúng image digest cũ `sha256:661e75f6bc3ec5b1a0d305ee7b61e86267ddeb52b8c018a84b00495d72666d1a`; start mới `2026-10-21T18:05:06.573345795Z`, DB không restart. Mẫu `18:07:22Z`: CPU 2,2%, RAM 34,3%, đĩa 32,1%, app/db running, DB healthy, API probe healthy, backup Healthy; public health 200. Đã đọc JSON qua chính container API và xác minh generated_at tiến qua ba chu kỳ. Chưa kiểm tra màn hình bằng phiên admin thật.

Khi vận hành, dùng `systemctl status rehouse-host-collector.timer`, `systemctl show rehouse-host-collector.service -p Result -p ExecMainStatus`, kiểm tra mtime/generated_at JSON và mount read-only. Không in runtime.env/private state hoặc mount Docker socket vào app. Repo Compose đã có mount tương ứng, nhưng cấu hình host vẫn phải giữ các override thực tế.

Lỗi biểu đồ trống do frontend đòi `histogramBuckets` mà API chưa trả; bộ đếm 1h/24h bị hụt do minute flush xóa bucket RAM. Hai lỗi đã sửa trong worktree, chưa commit/push/deploy. Giới hạn cửa sổ mới vẫn là một process, tối đa 24h kể từ restart. Regression Operations PostgreSQL QA local 202 PASS, collector Python 34/34; backend build 0 warning/error, frontend build và architecture đạt (cảnh báo chunk lớn vẫn còn). Log `artifacts/monitoring-operations-tests.log`. Không chạy lại stress test hoặc thay đổi dữ liệu nghiệp vụ live.

Kiểm chứng trước merge theo yêu cầu người dùng: toàn bộ backend 879 PASS trên PostgreSQL QA. Lỗi test revenue/growth nêu trên được xác định là filter UTC lệch ngày Việt Nam; đã đổi test dùng `ApiSupport.VietnamDate(persistedPaidAt)`, không sửa logic doanh thu. Hai ca E2E Zalo lỗi trên main do accessible name có tiền tố icon (`message Zalo`/`credit-card Thanh toán`); selector đổi sang hậu tố nhãn.

Kiểm chứng merge: toàn bộ Playwright 69/69 đạt với 4 worker, API giả lập local; `git diff --check` đạt.

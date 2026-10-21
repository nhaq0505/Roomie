# Kiểm thử Roomie

Quy tắc nghiệp vụ và các test theo feature: [AGENTS.md](../AGENTS.md#7-kiểm-tra-trước-bàn-giao-thay-đổi). Các lệnh dưới đây chạy từ root. Không chạy test ghi dữ liệu vào API/DB production, DB Development đang dùng, hoặc bản backup có dữ liệu người dùng.

## Suites độc lập

```sh
# C# suites yêu cầu TEST_HTTP_POSTGRES_CONNECTION từ container QA (xem mục bên dưới):
dotnet run --project tests/RoomRental.WebhookTests
dotnet run --project tests/RoomRental.ZaloTests
npm --prefix zalo-gateway ci
npm --prefix zalo-gateway test
npm --prefix frontend run build
npm --prefix frontend run test:e2e -- --workers=4
python3 -m unittest discover -s tests -p 'test_backup_*.py' -v
python3 -m unittest discover -s tests -p 'test_host_collector.py' -v
```

Console C# dùng in-process TestServer, PostgreSQL/fakes (yêu cầu `TEST_HTTP_POSTGRES_CONNECTION` trỏ tới container QA) và ephemeral keys. Chạy bằng `dotnet run`, không phải `dotnet test`. Playwright tự mở preview 4178, API/provider được giả lập. Build frontend trước; không cần API vận hành.

## HTTP + PostgreSQL / OTP

Yêu cầu container PostgreSQL QA chuyên dụng trên loopback (tách biệt hoàn toàn với dev container `rehouse-pg-migration-db` cổng 15432; các guard an toàn sẽ từ chối container dev và cổng 15432). Database khởi tạo phải có tiền tố `roomie_qa_`. Cách khởi chạy và cấp biến môi trường dùng chung:

```sh
# 1. Khởi chạy container PostgreSQL QA riêng biệt trên cổng loopback (ví dụ 55432):
QA_CONTAINER="roomie-qa-pg-local-$(date +%s)"
QA_PORT=55432
docker run -d --name "$QA_CONTAINER" \
  -e POSTGRES_DB=roomie_qa_bootstrap \
  -e POSTGRES_USER=room_rental \
  -e POSTGRES_PASSWORD=qa_test_password \
  -p "127.0.0.1:$QA_PORT:5432" \
  postgres:17-alpine

until docker exec "$QA_CONTAINER" pg_isready -U room_rental -d roomie_qa_bootstrap; do sleep 1; done

# 2. Cấp biến môi trường dùng chung cho Python harness và C# suites:
export TEST_HTTP_POSTGRES_CONTAINER="$QA_CONTAINER"
export TEST_HTTP_POSTGRES_CONNECTION="Host=127.0.0.1;Port=$QA_PORT;Database=roomie_qa_bootstrap;Username=room_rental;Password=qa_test_password"

# 3. Chạy HTTP regression với API publish có đầy đủ wwwroot:
npm --prefix frontend run build
ROOMIE_QA_APP=$(mktemp -d /tmp/roomie-qa-app.XXXXXX)
dotnet publish backend/RoomRental.Api -c Release -o "$ROOMIE_QA_APP"
cp -R frontend/dist "$ROOMIE_QA_APP/wwwroot"

python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider postgres
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider otp-postgres

# 4. Dọn dẹp container QA và thư mục publish:
docker rm -f "$QA_CONTAINER"
rm -rf "$ROOMIE_QA_APP"
```

`run_disposable.py` tạo API cổng ngẫu nhiên, DB tạm ngẫu nhiên `roomie_qa_*` trong container chỉ định, keys/outbox trong `/tmp/roomie-ci-*`, dừng process và dọn database/dữ liệu khi kết thúc. Provider `otp-postgres` bật SMTP sink loopback và chạy registration/recovery. Provider `postgres` chạy business, Zalo-disabled, payment, webhook và SEO fixtures; cả hai provider kỳ vọng 0 skip.

Phải copy đủ frontend thành `wwwroot`, nếu không SEO tests sẽ 404. Trên macOS, khi Python Homebrew lỗi thư viện `pyexpat`, thay `python3` bằng `/usr/bin/python3`.

Harness chặn HTTP(S) ngoài máy bằng proxy `127.0.0.1:9` và dùng HTTP localhost callback. Giữ cả hai: callback HTTP không tự ngăn subscription checkout gọi provider. Harness tắt Zalo, invoice sweep, reconciliation, dùng credentials thử và không gửi tin/thư/thanh toán thật. Đảm bảo cổng 9 không có proxy đang hoạt động. Không nạp credentials provider thật vào fixtures. CI dùng cùng harness với `--image` cho image sẽ được publish; Docker host networking theo môi trường Linux CI.

Các suite console C# (`RoomRental.WebhookTests`, `RoomRental.ZaloTests`) dùng chung helper `PostgresTestDb` và biến môi trường `TEST_HTTP_POSTGRES_CONNECTION`. Helper kiểm tra an toàn: chỉ chấp nhận host loopback (`127.0.0.1`, `localhost`, `::1`), cấm tuyệt đối cổng 15432 của dev container, và database khởi tạo phải bắt đầu bằng tiền tố `roomie_qa_`. Mỗi test run tự tạo database tạm `roomie_qa_<guid>` và tự động dọn dẹp sau khi chạy.

`tests/check_db_roles.py` tự dựng PostgreSQL network-none riêng và chạy provisioner để kiểm quyền DML/DDL. `load_postgres_readonly.py` là load smoke có guard disposable riêng, không phải benchmark production. Không chạy đại trà `unittest discover -s tests`: một số module HTTP mặc định trỏ tới cổng API local đang dùng.

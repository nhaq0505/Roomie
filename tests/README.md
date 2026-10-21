# Kiểm thử Roomie

Quy tắc nghiệp vụ và các test theo feature: [AGENTS.md](../AGENTS.md#7-kiểm-tra-trước-bàn-giao-thay-đổi). Các lệnh dưới đây chạy từ root. Không chạy test ghi dữ liệu vào API/DB production, DB Development đang dùng, hoặc bản backup có dữ liệu người dùng.

## Suites độc lập

```sh
dotnet run --project tests/RoomRental.WebhookTests
dotnet run --project tests/RoomRental.ZaloTests
npm --prefix zalo-gateway ci
npm --prefix zalo-gateway test
npm --prefix frontend run build
npm --prefix frontend run test:e2e -- --workers=4
python3 -m unittest discover -s tests -p 'test_backup_*.py' -v
python3 -m unittest discover -s tests -p 'test_host_collector.py' -v
```

Console C# dùng in-process TestServer, SQLite/fakes và ephemeral keys. Chạy bằng `dotnet run`, không phải `dotnet test`. Playwright tự mở preview 4178, API/provider được giả lập. Build frontend trước; không cần API vận hành.

## HTTP + SQLite/OTP

```sh
npm --prefix frontend run build
ROOMIE_QA_APP=$(mktemp -d /tmp/roomie-qa-app.XXXXXX)
dotnet publish backend/RoomRental.Api -c Release -o "$ROOMIE_QA_APP"
cp -R frontend/dist "$ROOMIE_QA_APP/wwwroot"
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider sqlite
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider otp
```

`run_disposable.py` tạo API cổng ngẫu nhiên, DB/keys/outbox trong `/tmp/roomie-ci-*`, dừng process và dọn dữ liệu khi kết thúc. Provider `otp` bật SMTP sink loopback và chạy registration/recovery. Provider `sqlite` chạy business, Zalo-disabled, payment và SEO/webhook fixtures; skip kỳ vọng 0.

Phải copy đủ frontend thành `wwwroot`, nếu không SEO tests sẽ 404. Trên macOS, khi Python Homebrew lỗi thư viện `pyexpat`, thay `python3` bằng `/usr/bin/python3`.

Harness chặn HTTP(S) ngoài máy bằng proxy `127.0.0.1:9` và dùng HTTP localhost callback. Giữ cả hai: callback HTTP không tự ngăn subscription checkout gọi provider. Harness tắt Zalo, invoice sweep, reconciliation, dùng credentials thử và không gửi tin/thư/thanh toán thật. Đảm bảo cổng 9 không có proxy đang hoạt động. Không nạp credentials provider thật vào fixtures.

## HTTP + PostgreSQL

Tạo riêng container PostgreSQL QA, có published port chỉ trên loopback và database bắt đầu `roomie_qa_`; không dùng container/volume app. Cấp:

```sh
export TEST_HTTP_POSTGRES_CONTAINER=<ten-container-qa-rieng>
export TEST_HTTP_POSTGRES_CONNECTION='Host=127.0.0.1;Port=<cong-qa>;Database=roomie_qa_local;Username=<user-qa>;Password=<password-qa>'
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider postgres
python3 tests/run_disposable.py --app "$ROOMIE_QA_APP" --provider otp-postgres
```

Harness tạo một database ngẫu nhiên mới trong container được chỉ định, dùng database đó rồi drop chính nó. PostgreSQL business kỳ vọng 8 skip SQLite-only; OTP kỳ vọng 0. Không giảm gate skip để che lỗi. CI dùng cùng harness với `--image` cho image sẽ được publish; Docker host networking theo môi trường Linux CI.

C# payment fixtures PostgreSQL có guard riêng `TEST_ALLOW_DISPOSABLE_PAYMENT_POSTGRES=1` + `TEST_PAYMENT_POSTGRES_CONNECTION`, chỉ loopback port15496 và database tạm. Zalo service dùng `ZALO_TEST_POSTGRES`, loopback/database `roomie_qa_zalo*`. Đọc code guard trước khi cấp biến; không thay bằng connection app.

`tests/check_db_roles.py` tự dựng PostgreSQL network-none riêng và chạy provisioner để kiểm quyền DML/DDL. `load_postgres_readonly.py` là load smoke có guard disposable riêng, không phải benchmark production. Không chạy đại trà `unittest discover -s tests`: một số module HTTP mặc định trỏ tới cổng API local đang dùng.

# Roomie — Quản lý phòng trọ

Ứng dụng cho nhiều chủ trọ: dãy/phòng, người thuê, hóa đơn, cọc, thanh toán payOS/VietQR, nhắc Zalo và báo cáo. Backend ASP.NET Core .NET 10 + EF Core; frontend React 19/TypeScript/Vite/Ant Design; PostgreSQL là cơ sở dữ liệu duy nhất (vận hành và local/test).

**Agent tiếp quản: đọc [AGENTS.md](AGENTS.md) trước.** File này chứa bản đồ code, quy tắc nghiệp vụ, giới hạn hiện tại và lệnh kiểm chứng. Source là căn cứ cho hành vi; trạng thái VPS phải được kiểm tra riêng.

## Bắt đầu

Yêu cầu .NET SDK 10, Node.js 24, npm và Python 3. Docker dùng khi cần PostgreSQL QA hoặc triển khai.

```sh
npm --prefix frontend ci
npm --prefix zalo-gateway ci
dotnet tool restore
```

Chạy API và frontend theo [hướng dẫn local với DB riêng](AGENTS.md#6-chạy-local-có-kiểm-soát). Vite chạy ở `http://localhost:5173`, proxy API tới cổng 5080. Không seed/test vào DB đang có dữ liệu. Các file Development local và biến môi trường có thể override cấu hình mặc định.

## Kiểm tra

```sh
dotnet run --project tests/RoomRental.WebhookTests
dotnet run --project tests/RoomRental.ZaloTests
npm --prefix zalo-gateway test
npm --prefix frontend run build
npm --prefix frontend run i18n:check
npm --prefix frontend run check:architecture
npm --prefix frontend run test:e2e -- --workers=4
```

Test HTTP phải chạy bằng [harness cô lập](tests/README.md); không chạy unittest toàn bộ vào API local đang dùng hoặc production. [AGENTS.md](AGENTS.md#7-kiểm-tra-trước-bàn-giao-thay-đổi) có đầy đủ các nhóm kiểm tra frontend, schema, backup và collector.

## Vận hành

- [Deploy và rollback](docs/deployment.md)
- [Backup và khôi phục](docs/backup-operations.md)
- [EF Core migrations](docs/ef-core-migrations.md)
- [Kết nối Zalo](docs/zalo-setup.md)
- [Monitoring](docs/monitoring/runbook-and-metric-dictionary.md)

Push `main` kích hoạt CI/CD và có thể deploy. Repo là `nhaq0505/Roomie`; image vẫn `ghcr.io/nhaq0505/rehouse`. Không lưu secrets trong Git. Không xóa volume DB, DataProtection hoặc Zalo sessions để xử lý lỗi triển khai.

`rehouse-video/` là dự án Remotion riêng. `output/facebook/` chứa logo và cover JPG dành cho Facebook. Hai phần này không được app tự động sử dụng.

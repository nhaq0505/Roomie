# EF Core migrations

Mô hình chung ở `backend/RoomRental.Api/Data/AppDbContext.cs`; `SqliteAppDbContext` và `PostgresAppDbContext` có migrations/designer/snapshot riêng. API/service sử dụng scoped `AppDbContext`.

## Thay đổi model

Từ root, scaffold cho cả hai provider:

```sh
dotnet tool restore
dotnet ef migrations add TenThayDoi --project backend/RoomRental.Api --context SqliteAppDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add TenThayDoi --project backend/RoomRental.Api --context PostgresAppDbContext --output-dir Data/Migrations/Postgres
dotnet ef migrations has-pending-model-changes --project backend/RoomRental.Api --context SqliteAppDbContext
dotnet ef migrations has-pending-model-changes --project backend/RoomRental.Api --context PostgresAppDbContext
```

Factory chỉ đọc `ConnectionStrings__DefaultConnection` từ env, không đọc file secrets local. Mặc định factory chỉ để scaffold; đừng đặt env production khi chạy scaffold/check. Đọc Up/Down của cả hai, kiểm tra unique/filtered indexes và concurrency tokens, thử apply/restart trên DB dùng thử, commit migration + designer + snapshot cùng model. Không sửa migration đã phát hành.

## Áp schema

- Development: `DatabaseInitializer` có thể `MigrateAsync()` khi `Database:MigrateOnStartup` cho phép.
- Production: startup chỉ kiểm tra pending migrations; schema chưa đúng thì từ chối chạy.
- Deploy: chạy **API image** với `Database__MigrateOnly=true` qua `deploy/migrate.yaml`, identity sở hữu schema `room_rental`, rồi cấp lại quyền DML cho `roomie_app` trước khi thay app. Xem [deployment.md](deployment.md).
- **`RoomRental.DbMigrator` không phải runner schema**: đây là công cụ import dữ liệu SQLite cũ sang PostgreSQL, hiện danh sách bảng chưa theo kịp model mới. Không dùng cho schema/data hiện tại khi chưa cập nhật và kiểm chứng.

Trước thay schema có dữ liệu: xác định target/provider, backup DB + DataProtection, xem khả năng rollback image với schema mới. Rollback image không rollback schema. Không tự chạy Down migration hoặc reset để chữa mismatch.

`deploy/reset-legacy-database.sql` chỉ phục vụ chuyển đổi schema legacy một lần trong lịch sử, không phải bước triển khai thông thường. Không chạy lại, không gắn vào startup/CI/CD.

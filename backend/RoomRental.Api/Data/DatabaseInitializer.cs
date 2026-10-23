using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Data;

public static class DatabaseInitializer
{
    private static readonly PasswordHasher<AppUser> PasswordHasher = new();

    public static async Task InitializeAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (app.Configuration.GetValue("Database:MigrateOnly", false))
        {
            await db.Database.MigrateAsync();
            await BootstrapAdminAsync(db, app.Configuration);
            return;
        }
        if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:MigrateOnStartup", true))
            await db.Database.MigrateAsync();
        else if ((await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Apply database migrations with the deployment identity before starting the application.");

        var seedDevelopmentData = ReadFlag(app.Configuration["SeedDemo"]) || ReadFlag(app.Configuration["SeedDevelopmentData"]);
        if (app.Environment.IsDevelopment() && seedDevelopmentData)
        {
            await SeedDevelopmentAsync(db, scope.ServiceProvider.GetRequiredService<TenantTokenService>());
        }
        else
        {
            await BootstrapAdminAsync(db, app.Configuration);
        }

        await SeedDefaultGuidesAsync(db);
    }

    private static async Task BootstrapAdminAsync(AppDbContext db, IConfiguration configuration)
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim().ToLowerInvariant();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set both BootstrapAdmin__Email and BootstrapAdmin__Password to bootstrap the initial administrator.");
        if (!PasswordSecurity.IsValidNewPassword(password))
            throw new InvalidOperationException("BootstrapAdmin__Password must contain 9 to 256 characters.");
        if (await db.Users.AnyAsync(user => user.Email == email)) return;

        var admin = new AppUser
        {
            Name = "Administrator",
            Email = email,
            PasswordHash = "",
            Role = UserRole.Admin,
            Status = OwnerStatus.Approved
        };
        admin.PasswordHash = PasswordHasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }

    private static async Task SeedDevelopmentAsync(AppDbContext db, TenantTokenService tokenService)
    {
        _ = await FindOrAddDevUserAsync(db, "Admin", "admin@phongtro.local", "Admin123!", UserRole.Admin, OwnerStatus.Approved);
        _ = await FindOrAddDevUserAsync(db, "Chuyên viên Marketing", "marketer@phongtro.local", "Marketer123!", UserRole.Marketer, OwnerStatus.Approved);
        var owner = await FindOrAddDevUserAsync(db, "Chủ trọ mẫu", "owner@phongtro.local", "Owner123!", UserRole.Owner, OwnerStatus.Approved);
        if (await db.Buildings.AnyAsync(building => building.OwnerId == owner.Id)) return;

        var building = new Building { OwnerId = owner.Id, Name = "Nhà trọ Bình An", Address = "12 Nguyễn Trãi, Quận 5, TP. Hồ Chí Minh" };
        db.Buildings.Add(building);
        var roomOne = new Room
        {
            BuildingId = building.Id, Name = "P101", Rent = 3_500_000, ElectricityRate = 4_000,
            WaterFee = 80_000, WaterPerPerson = true, ServiceFee = 120_000, InitialReading = 1250
        };
        var roomTwo = new Room
        {
            BuildingId = building.Id, Name = "P102", Rent = 3_000_000, ElectricityRate = 4_000,
            WaterFee = 100_000, WaterPerPerson = false, ServiceFee = 100_000, InitialReading = 970
        };
        var roomThree = new Room
        {
            BuildingId = building.Id, Name = "P201", Rent = 3_800_000, ElectricityRate = 4_000,
            WaterFee = 80_000, WaterPerPerson = true, ServiceFee = 150_000, InitialReading = 700
        };
        db.Rooms.AddRange(roomOne, roomTwo, roomThree);

        var guestOne = tokenService.Create();
        var guestTwo = tokenService.Create();
        db.Occupancies.AddRange(
            new Occupancy
            {
                RoomId = roomOne.Id, TenantName = "Nguyễn Minh Anh", Phone = "0901234567", People = 2,
                StartDate = new DateOnly(2026, 9, 1), TenantTokenHash = guestOne.Hash, ProtectedTenantToken = guestOne.ProtectedToken
            },
            new Occupancy
            {
                RoomId = roomTwo.Id, TenantName = "Trần Quốc Bảo", Phone = "0912345678", People = 1,
                StartDate = new DateOnly(2026, 10, 1), TenantTokenHash = guestTwo.Hash, ProtectedTenantToken = guestTwo.ProtectedToken
            });
        await db.SaveChangesAsync();
    }

    public static async Task SeedDefaultGuidesAsync(AppDbContext db)
    {
        var existingSlugs = await db.MarketingArticles
            .Select(a => a.Slug)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var defaults = new[]
        {
            new MarketingArticle
            {
                Slug = "quan-ly-phong-tien-coc-va-tra-phong",
                Title = "Quản lý phòng, lượt thuê, tiền cọc và trả phòng không sót khoản",
                Category = "Quản lý vận hành",
                ReadTimeLabel = "5 phút đọc",
                Summary = "Chuẩn hóa danh mục dãy nhà, phòng trọ, hợp đồng thuê và sổ tiền cọc tách biệt với tiền phòng hằng tháng.",
                SeoTitle = "Quản lý phòng, lượt thuê, tiền cọc và trả phòng không sót khoản | Roomie",
                SeoDescription = "Chuẩn hóa danh mục dãy nhà, phòng trọ, hợp đồng thuê và sổ tiền cọc tách biệt với tiền phòng hằng tháng.",
                CtaLabel = "Dùng thử miễn phí 5 phòng",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=quan_ly_phong_coc",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Chuẩn hóa danh mục dãy nhà và phòng trọ

Thiết lập đơn giá thuê, đơn giá điện, phí nước và dịch vụ cho từng phòng ngay từ đầu giúp chủ trọ không phải nhớ thủ công mỗi kỳ chốt sổ.

- Theo dõi trạng thái phòng trống, đang thuê và lịch sử lượt thuê rõ ràng.
- Lưu thông tin khách thuê, số người ở và chỉ số điện bàn giao đầu kỳ.

## 2. Sổ tiền cọc tách biệt và quy trình trả phòng minh bạch

Tiền cọc được quản lý trên sổ riêng (thu cọc, khấu trừ vào hóa đơn, hoàn cọc), không trộn lẫn vào doanh thu tiền phòng hằng tháng. Khi khách trả phòng, hệ thống kiểm tra toàn bộ dư nợ và nâng chỉ số điện cơ sở cho lượt thuê tiếp theo.
"""
            },
            new MarketingArticle
            {
                Slug = "cach-chot-chi-so-dien-va-lap-hoa-don",
                Title = "Cách chốt chỉ số điện nước và phát hành hóa đơn minh bạch mỗi tháng",
                Category = "Hóa đơn & Điện nước",
                ReadTimeLabel = "4 phút đọc",
                Summary = "Tự động lấy chỉ số cũ, kiểm tra chống lùi số điện và khóa đơn giá lịch sử ngay khi phát hành hóa đơn cho khách thuê.",
                SeoTitle = "Cách chốt chỉ số điện nước và phát hành hóa đơn minh bạch mỗi tháng | Roomie",
                SeoDescription = "Tự động lấy chỉ số cũ, kiểm tra chống lùi số điện và khóa đơn giá lịch sử ngay khi phát hành hóa đơn cho khách thuê.",
                CtaLabel = "Tạo hóa đơn tự động trên Roomie",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=chot_dien_hoa_don",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Chốt chỉ số điện nhanh chóng, chống nhầm lẫn

Hệ thống tự động kế thừa chỉ số điện kỳ trước hoặc chỉ số bàn giao khi khách mới vào ở, đồng thời cảnh báo ngay nếu nhập chỉ số mới thấp hơn chỉ số cũ.

## 2. Phát hành hóa đơn giữ nguyên snapshot lịch sử

Mỗi hóa đơn sau khi phát hành đều lưu snapshot đơn giá phòng, điện, nước và số người ở tại thời điểm đó. Việc điều chỉnh giá phòng cho các tháng sau tuyệt đối không làm lệch số liệu hóa đơn cũ.
"""
            },
            new MarketingArticle
            {
                Slug = "thu-tien-phong-qua-ma-qr-va-nhac-zalo",
                Title = "Tự động hóa thu tiền trọ qua VietQR, payOS và nhắc lịch Zalo",
                Category = "Thanh toán & Nhắc lịch",
                ReadTimeLabel = "6 phút đọc",
                Summary = "Gửi liên kết hóa đơn riêng cho từng phòng qua Zalo, tích hợp mã QR chuyển khoản điền sẵn số tiền và nội dung thanh toán.",
                SeoTitle = "Tự động hóa thu tiền trọ qua VietQR, payOS và nhắc lịch Zalo | Roomie",
                SeoDescription = "Gửi liên kết hóa đơn riêng cho từng phòng qua Zalo, tích hợp mã QR chuyển khoản điền sẵn số tiền và nội dung thanh toán.",
                CtaLabel = "Bắt đầu thu tiền trọ qua QR",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=vietqr_payos_zalo",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Liên kết hóa đơn riêng tư cho từng lượt thuê

Khách thuê nhận tin nhắn nhắc lịch qua Zalo kèm đường dẫn xem chi tiết hóa đơn của riêng phòng mình mà không cần cài đặt ứng dụng hay tạo tài khoản.

## 2. Đối chiếu chuyển khoản VietQR hoặc gạch nợ tự động payOS

Chủ trọ có thể chọn chế độ xác nhận chuyển khoản thủ công qua VietQR hoặc kết nối cổng thanh toán payOS để hệ thống tự động gạch nợ ngay khi khách chuyển khoản thành công.
"""
            },
            new MarketingArticle
            {
                Slug = "quan-ly-cong-no-ghi-thu-va-xuat-excel",
                Title = "Hướng dẫn xuất báo cáo Excel và đối soát công nợ nhà trọ cuối tháng",
                Category = "Báo cáo & Đối soát",
                ReadTimeLabel = "5 phút đọc",
                Summary = "Tổng hợp doanh thu đã thu, dư nợ còn phải thu và lịch sử phiếu thu ra file Excel chuẩn kế toán chỉ với 1 cú nhấp chuột.",
                SeoTitle = "Hướng dẫn xuất báo cáo Excel và đối soát công nợ nhà trọ cuối tháng | Roomie",
                SeoDescription = "Tổng hợp doanh thu đã thu, dư nợ còn phải thu và lịch sử phiếu thu ra file Excel chuẩn kế toán chỉ với 1 cú nhấp chuột.",
                CtaLabel = "Trải nghiệm báo cáo Excel tự động",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=bao_cao_excel",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Bảng điều khiển công nợ trực quan theo kỳ

Theo dõi ngay trên một màn hình tổng tiền phát hành, số tiền đã thu thực tế và danh sách các phòng còn dư nợ để ưu tiên đôn đốc.

## 2. Xuất file Excel đa sheet chuẩn kế toán

Chỉ với một thao tác, chủ trọ có thể tải về file Excel gồm đầy đủ các sheet: Tổng hợp, Hóa đơn, Phiếu thu, Sổ cọc và Trả phòng với định dạng số tiền chuẩn xác.
"""
            },
            new MarketingArticle
            {
                Slug = "cach-quan-ly-nha-tro-khong-nham-lan",
                Title = "Cách quản lý nhà trọ từ 5 đến 50 phòng không nhầm lẫn",
                Category = "Quản lý vận hành",
                ReadTimeLabel = "5 phút đọc",
                Summary = "Quy trình chuẩn hóa danh sách phòng, hợp đồng, tiền cọc và khách thuê giúp chủ trọ tiết kiệm 80% thời gian mỗi tháng.",
                SeoTitle = "Cách quản lý nhà trọ từ 5 đến 50 phòng không nhầm lẫn | Roomie",
                SeoDescription = "Quy trình chuẩn hóa danh sách phòng, hợp đồng, tiền cọc và khách thuê giúp chủ trọ tiết kiệm 80% thời gian mỗi tháng.",
                CtaLabel = "Dùng thử miễn phí 5 phòng",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=quan_ly_nha_tro",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Tại sao sổ tay và Excel rời rạc dễ gây thất thoát?

Khi quản lý từ 5 phòng trở lên, việc ghi chép chỉ số điện nước trên giấy rồi nhập lại vào Excel hoặc nhắn tin rời rạc qua Zalo rất dễ dẫn đến:

- Nhầm chỉ số điện kỳ trước của phòng này sang phòng khác.
- Quên thu các khoản phí dịch vụ cố định hoặc thu thiếu tiền cọc.
- Không đối soát được lịch sử sửa hóa đơn khi khách thắc mắc.

## 2. Quy trình 4 bước chuẩn hóa vận hành nhà trọ

1. **Chuẩn hóa danh mục dãy & phòng**: Thiết lập đơn giá thuê, đơn giá điện, nước (theo đầu người hoặc theo phòng) và phí dịch vụ ngay từ đầu.
2. **Quản lý theo lượt thuê (Occupancy)**: Mỗi khách vào ở có ngày bắt đầu, số người ở, tiền cọc và chỉ số điện bàn giao rõ ràng.
3. **Chốt điện và phát hành hóa đơn định kỳ**: Hệ thống tự lấy chỉ số cũ, kiểm tra chống lùi số và tính tổng tiền tự động.
4. **Theo dõi công nợ và trả phòng minh bạch**: Mọi khoản thu, hoàn cọc hoặc khấu trừ cọc đều có lịch sử đối chiếu rõ ràng.
"""
            },
            new MarketingArticle
            {
                Slug = "cach-tinh-tien-dien-nuoc-phong-tro-chuan",
                Title = "Hướng dẫn chốt số điện nước và tạo phiếu thu minh bạch",
                Category = "Tài chính & Hóa đơn",
                ReadTimeLabel = "4 phút đọc",
                Summary = "Cách ghi nhận chỉ số cũ - mới, tránh sai sót khi tính nhẩm và gửi hóa đơn chi tiết để khách thuê thanh toán ngay.",
                SeoTitle = "Hướng dẫn chốt số điện nước và tạo phiếu thu minh bạch | Roomie",
                SeoDescription = "Cách ghi nhận chỉ số cũ - mới, tránh sai sót khi tính nhẩm và gửi hóa đơn chi tiết để khách thuê thanh toán ngay.",
                CtaLabel = "Tạo hóa đơn tự động trên Roomie",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=tinh_dien_nuoc",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Nguyên tắc chốt chỉ số điện nước không bao giờ nhầm

Để khách thuê tin tưởng và thanh toán nhanh, mỗi kỳ hóa đơn cần hiển thị rõ:

- Chỉ số điện kỳ trước (tự động kế thừa từ hóa đơn liền kề hoặc chỉ số bàn giao ban đầu).
- Chỉ số điện kỳ mới và sản lượng tiêu thụ (`Kỳ mới - Kỳ cũ`).
- Đơn giá điện, phí nước và phí dịch vụ đã thỏa thuận trong hợp đồng.

## 2. Phát hành hóa đơn qua liên kết riêng cho từng phòng

Thay vì chụp ảnh sổ viết tay, chủ trọ chỉ cần nhập chỉ số mới trên điện thoại và bấm **Phát hành**. Khách thuê mở liên kết riêng của phòng mình sẽ thấy ngay chi tiết từng khoản và mã VietQR/payOS để chuyển khoản chính xác.
"""
            },
            new MarketingArticle
            {
                Slug = "5-meo-thu-tien-tro-dung-han",
                Title = "5 mẹo nhắc lịch và thu tiền trọ đúng hạn qua Zalo & VietQR",
                Category = "Thu tiền & Công nợ",
                ReadTimeLabel = "6 phút đọc",
                Summary = "Kết hợp tin nhắn nhắc lịch tự động và mã QR thanh toán định danh giúp giảm tình trạng chậm đóng tiền phòng.",
                SeoTitle = "5 mẹo nhắc lịch và thu tiền trọ đúng hạn qua Zalo & VietQR | Roomie",
                SeoDescription = "Kết hợp tin nhắn nhắc lịch tự động và mã QR thanh toán định danh giúp giảm tình trạng chậm đóng tiền phòng.",
                CtaLabel = "Bắt đầu thu tiền trọ qua QR",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=thu_tien_tro",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Gửi thông báo đúng khung giờ vàng

Khách thuê thường kiểm tra điện thoại và thanh toán hóa đơn vào buổi sáng (07:30 - 09:00) hoặc buổi tối sau giờ làm (18:30 - 20:30). Nhắc hóa đơn lịch sự qua Zalo kèm liên kết hóa đơn giúp khách thao tác ngay trong 30 giây.

## 2. Tích hợp mã QR điền sẵn số tiền và nội dung

Khi khách phải tự gõ số tài khoản và số tiền lẻ, họ rất dễ trì hoãn hoặc chuyển nhầm số tiền. Liên kết hóa đơn của Roomie hiển thị sẵn mã QR ngân hàng với đúng số tiền cần đóng, giúp chủ trọ gạch nợ nhanh chóng.
"""
            },
            new MarketingArticle
            {
                Slug = "huong-dan-xuat-bao-cao-doanh-thu-nha-tro",
                Title = "Mẫu bảng kê doanh thu và theo dõi công nợ nhà trọ cuối tháng",
                Category = "Báo cáo & Sổ sách",
                ReadTimeLabel = "5 phút đọc",
                Summary = "Cách tổng hợp tổng thu, dư nợ từng dãy trọ và xuất file Excel chuẩn kế toán chỉ với 1 cú nhấp chuột.",
                SeoTitle = "Mẫu bảng kê doanh thu và theo dõi công nợ nhà trọ cuối tháng | Roomie",
                SeoDescription = "Cách tổng hợp tổng thu, dư nợ từng dãy trọ và xuất file Excel chuẩn kế toán chỉ với 1 cú nhấp chuột.",
                CtaLabel = "Trải nghiệm báo cáo Excel tự động",
                CtaTargetUrl = "/register?utm_source=guide&utm_medium=seo&utm_campaign=bao_cao_doanh_thu",
                Status = MarketingArticleStatus.Published,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                ContentMarkdown = """
## 1. Những chỉ số tài chính chủ trọ cần nắm mỗi cuối tháng

Một báo cáo nhà trọ rõ ràng cần tách bạch:

- **Tổng phát hành trong kỳ**: Tổng tiền thuê, điện, nước, dịch vụ của các hóa đơn đã phát hành.
- **Đã thu thực tế**: Các khoản thanh toán đã được xác nhận qua ngân hàng hoặc thu tiền mặt.
- **Dư nợ còn lại**: Danh sách các phòng chưa thanh toán để ưu tiên nhắc lịch.

## 2. Xuất báo cáo Excel chuẩn hóa chỉ với 1 chạm

Trên Roomie, bạn có thể lọc theo tháng hoặc theo dãy nhà và tải ngay file Excel đầy đủ các sheet: Tổng hợp, Hóa đơn, Phiếu thu, Sổ cọc và Trả phòng mà không cần gõ lại công thức.
"""
            }
        };

        var missing = defaults.Where(d => !existingSlugs.Contains(d.Slug)).ToList();
        if (missing.Count == 0) return;

        db.MarketingArticles.AddRange(missing);
        await db.SaveChangesAsync();
    }

    private static async Task<AppUser> FindOrAddDevUserAsync(AppDbContext db, string name, string email, string password, UserRole role, OwnerStatus status)
    {
        var existing = await db.Users.SingleOrDefaultAsync(user => user.Email == email);
        if (existing is not null) return existing;
        var user = new AppUser { Name = name, Email = email, PasswordHash = "", Role = role, Status = status };
        user.PasswordHash = PasswordHasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static bool ReadFlag(string? value) => bool.TryParse(value, out var enabled) && enabled;
}

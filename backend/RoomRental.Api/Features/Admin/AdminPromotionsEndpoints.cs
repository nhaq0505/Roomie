using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class AdminPromotionsEndpoints
{
    private static PostgresException? GetPostgresException(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is PostgresException pg) return pg;
        }
        return null;
    }

    public static void MapPromotionsRoutes(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/promotions")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        admin.MapGet("", async (AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var welcome = await db.SystemPromotions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == "welcome", ct);
            var defaultMonths = config.GetValue("Promotions:NewUserProMonths", 0);
            var welcomeDto = welcome is not null
                ? new WelcomePromotionDto(welcome.Id, welcome.Name, welcome.IsEnabled, welcome.RewardMonths, welcome.StartsAt, welcome.EndsAt)
                : new WelcomePromotionDto("welcome", "Tặng Pro người đăng ký mới", defaultMonths > 0, defaultMonths > 0 ? defaultMonths : 3, null, null);

            var codes = await db.PromotionCodes.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new PromotionCodeDto(
                    x.Id, x.Code, x.Description, x.RewardMonths, x.MaxRedemptions, x.RedeemedCount, x.IsActive, x.ExpiresAt, x.CreatedAt
                ))
                .ToListAsync(ct);

            return Results.Ok(new AdminPromotionsOverviewDto(welcomeDto, codes));
        });

        admin.MapPut("/welcome", async (WelcomePromotionUpdateRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (request.RewardMonths < 1 || request.RewardMonths > 120)
                throw new ApiException(StatusCodes.Status400BadRequest, "Số tháng tặng thưởng phải từ 1 đến 120 tháng.");

            if (request.StartsAt.HasValue && request.EndsAt.HasValue && request.StartsAt > request.EndsAt)
                throw new ApiException(StatusCodes.Status400BadRequest, "Thời gian bắt đầu không được lớn hơn thời gian kết thúc.");

            var promo = await db.SystemPromotions.SingleOrDefaultAsync(x => x.Id == "welcome", ct);
            if (promo is null)
            {
                promo = new SystemPromotion
                {
                    Id = "welcome",
                    Name = "Tặng Pro người đăng ký mới",
                    IsEnabled = request.IsEnabled,
                    RewardMonths = request.RewardMonths,
                    StartsAt = request.StartsAt,
                    EndsAt = request.EndsAt,
                    UpdatedAt = clock.GetUtcNow()
                };
                db.SystemPromotions.Add(promo);
            }
            else
            {
                promo.IsEnabled = request.IsEnabled;
                promo.RewardMonths = request.RewardMonths;
                promo.StartsAt = request.StartsAt;
                promo.EndsAt = request.EndsAt;
                promo.UpdatedAt = clock.GetUtcNow();
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || GetPostgresException(ex) is { SqlState: "40001" or "23505" or "40P01" or "55P03" })
            {
                throw new ApiException(StatusCodes.Status409Conflict, "Dữ liệu khuyến mãi đang được cập nhật bởi thao tác khác. Vui lòng thử lại.");
            }

            return Results.Ok(new WelcomePromotionDto(promo.Id, promo.Name, promo.IsEnabled, promo.RewardMonths, promo.StartsAt, promo.EndsAt));
        });

        admin.MapPost("/codes", async (PromotionCodeCreateRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) || code.Length < 3 || code.Length > 32)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mã khuyến mãi phải từ 3 đến 32 ký tự.");

            if (request.Description?.Trim().Length > 255)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mô tả mã khuyến mãi tối đa 255 ký tự.");

            if (request.RewardMonths < 1 || request.RewardMonths > 120)
                throw new ApiException(StatusCodes.Status400BadRequest, "Số tháng tặng thưởng phải từ 1 đến 120 tháng.");

            if (request.MaxRedemptions.HasValue && request.MaxRedemptions.Value < 1)
                throw new ApiException(StatusCodes.Status400BadRequest, "Số lượt sử dụng tối đa phải lớn hơn 0.");

            if (await db.PromotionCodes.AnyAsync(x => x.Code == code, ct))
                throw new ApiException(StatusCodes.Status409Conflict, "Mã khuyến mãi này đã tồn tại.");

            var promoCode = new PromotionCode
            {
                Code = code,
                Description = request.Description?.Trim(),
                RewardMonths = request.RewardMonths,
                MaxRedemptions = request.MaxRedemptions,
                ExpiresAt = request.ExpiresAt,
                IsActive = request.IsActive,
                CreatedAt = clock.GetUtcNow()
            };

            db.PromotionCodes.Add(promoCode);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || GetPostgresException(ex) is { SqlState: "40001" or "23505" or "40P01" or "55P03" })
            {
                var pg = GetPostgresException(ex);
                if (pg?.SqlState == "23505")
                    throw new ApiException(StatusCodes.Status409Conflict, "Mã khuyến mãi này đã tồn tại.");
                throw new ApiException(StatusCodes.Status409Conflict, "Dữ liệu khuyến mãi đang được cập nhật bởi thao tác khác. Vui lòng thử lại.");
            }

            return Results.Created($"/api/admin/promotions/codes/{promoCode.Id}", new PromotionCodeDto(
                promoCode.Id, promoCode.Code, promoCode.Description, promoCode.RewardMonths, promoCode.MaxRedemptions,
                promoCode.RedeemedCount, promoCode.IsActive, promoCode.ExpiresAt, promoCode.CreatedAt
            ));
        });

        admin.MapPatch("/codes/{id:guid}", async (Guid id, PromotionCodeUpdateRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var promo = await db.PromotionCodes.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy mã khuyến mãi.");

            if (request.Description?.Trim().Length > 255)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mô tả mã khuyến mãi tối đa 255 ký tự.");

            if (request.RewardMonths < 1 || request.RewardMonths > 120)
                throw new ApiException(StatusCodes.Status400BadRequest, "Số tháng tặng thưởng phải từ 1 đến 120 tháng.");

            if (request.MaxRedemptions.HasValue && request.MaxRedemptions.Value < 1)
                throw new ApiException(StatusCodes.Status400BadRequest, "Số lượt sử dụng tối đa phải lớn hơn 0.");

            if (request.MaxRedemptions.HasValue && request.MaxRedemptions.Value < promo.RedeemedCount)
                throw new ApiException(StatusCodes.Status400BadRequest, $"Số lượt tối đa không được nhỏ hơn số lượt đã dùng ({promo.RedeemedCount}).");

            promo.Description = request.Description?.Trim();
            promo.RewardMonths = request.RewardMonths;
            promo.MaxRedemptions = request.MaxRedemptions;
            promo.ExpiresAt = request.ExpiresAt;
            promo.IsActive = request.IsActive;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || GetPostgresException(ex) is { SqlState: "40001" or "23505" or "40P01" or "55P03" })
            {
                throw new ApiException(StatusCodes.Status409Conflict, "Dữ liệu khuyến mãi đang được cập nhật bởi thao tác khác. Vui lòng thử lại.");
            }

            return Results.Ok(new PromotionCodeDto(
                promo.Id, promo.Code, promo.Description, promo.RewardMonths, promo.MaxRedemptions,
                promo.RedeemedCount, promo.IsActive, promo.ExpiresAt, promo.CreatedAt
            ));
        });

        admin.MapDelete("/codes/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var promo = await db.PromotionCodes.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy mã khuyến mãi.");

            var redemptions = db.PromotionRedemptions.Where(x => x.PromotionCodeId == id);
            db.PromotionRedemptions.RemoveRange(redemptions);
            db.PromotionCodes.Remove(promo);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || GetPostgresException(ex) is { SqlState: "40001" or "23505" or "40P01" or "55P03" })
            {
                throw new ApiException(StatusCodes.Status409Conflict, "Dữ liệu khuyến mãi đang được cập nhật bởi thao tác khác. Vui lòng thử lại.");
            }

            return Results.NoContent();
        });

        // User route to redeem promo code
        app.MapPost("/api/subscription/redeem-code", async (RedeemPromotionCodeRequest request, HttpContext context, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var code = request.Code?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập mã khuyến mãi.");

            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var promo = await db.PromotionCodes.SingleOrDefaultAsync(x => x.Code == code, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Mã khuyến mãi không tồn tại.");

            if (!promo.IsActive)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mã khuyến mãi này hiện đang bị khóa hoặc ngừng áp dụng.");

            var now = clock.GetUtcNow();
            if (promo.ExpiresAt.HasValue && promo.ExpiresAt.Value < now)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mã khuyến mãi này đã hết hạn sử dụng.");

            if (promo.MaxRedemptions.HasValue && promo.RedeemedCount >= promo.MaxRedemptions.Value)
                throw new ApiException(StatusCodes.Status400BadRequest, "Mã khuyến mãi này đã đạt giới hạn lượt sử dụng.");

            var alreadyRedeemed = await db.PromotionRedemptions.AnyAsync(x => x.PromotionCodeId == promo.Id && x.UserId == ownerId, ct);
            if (alreadyRedeemed)
                throw new ApiException(StatusCodes.Status400BadRequest, "Bạn đã sử dụng mã khuyến mãi này rồi.");

            var owner = await db.Users.SingleAsync(x => x.Id == ownerId, ct);
            if (owner.SubscriptionTier == SubscriptionTier.Pro && owner.SubscriptionExpiresAt == null)
                throw new ApiException(StatusCodes.Status409Conflict, "Tài khoản của bạn đang ở gói Pro vĩnh viễn, không cần sử dụng thêm mã khuyến mãi.");

            var baseExpiry = (owner.SubscriptionTier == SubscriptionTier.Pro && owner.SubscriptionExpiresAt.HasValue && owner.SubscriptionExpiresAt.Value > now)
                ? owner.SubscriptionExpiresAt.Value
                : now;

            owner.SubscriptionTier = SubscriptionTier.Pro;
            owner.SubscriptionExpiresAt = baseExpiry.AddMonths(promo.RewardMonths);
            promo.RedeemedCount++;

            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                PromotionCodeId = promo.Id,
                UserId = ownerId,
                RedeemedAt = now
            });

            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || GetPostgresException(ex) is { SqlState: "40001" or "23505" or "40P01" or "55P03" })
            {
                var pg = GetPostgresException(ex);
                if (pg?.SqlState == "23505")
                    throw new ApiException(StatusCodes.Status409Conflict, "Bạn đã sử dụng mã khuyến mãi này rồi.");
                throw new ApiException(StatusCodes.Status409Conflict, "Dữ liệu khuyến mãi đang được cập nhật bởi thao tác khác. Vui lòng thử lại.");
            }

            return Results.Ok(new RedeemPromotionResultDto(
                Success: true,
                Message: $"Kích hoạt thành công! Bạn nhận được {promo.RewardMonths} tháng sử dụng gói Pro.",
                SubscriptionTier: owner.SubscriptionTier.ToString(),
                SubscriptionExpiresAt: owner.SubscriptionExpiresAt,
                AddedMonths: promo.RewardMonths
            ));
        }).RequireAuthorization(policy => policy.RequireRole("Owner")).RequireRateLimiting("tenant-payment");
    }
}

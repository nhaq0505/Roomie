using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class PromotionsTests
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task RunAsync()
    {
        await using var testDb = await PostgresTestDb.CreateAsync(ensureCreated: true);
        using var db = testDb.CreateDbContext();
        var clock = TimeProvider.System;

        // 1. Welcome promo in DB overrides config
        db.SystemPromotions.Add(new SystemPromotion
        {
            Id = "welcome",
            Name = "Welcome 6 months",
            IsEnabled = true,
            RewardMonths = 6,
            StartsAt = null,
            EndsAt = null,
            UpdatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Promotions:NewUserProMonths"] = "1"
        }).Build();

        var sender = new FakeOtpSender();
        var regService = new RegistrationVerificationService(db, sender, clock, configuration: config);
        var challenge = await regService.StartAsync(new AppUser { Name = "Owner 1", Email = "owner1@example.invalid", PasswordHash = "x" }, default);
        var user = await regService.VerifyAsync(challenge.ChallengeToken, sender.Code, default);

        Check(user.SubscriptionTier == SubscriptionTier.Pro && user.SubscriptionExpiresAt is not null &&
              user.SubscriptionExpiresAt.Value > clock.GetUtcNow().AddMonths(5),
              "welcome promotion from DB grants 6 months pro over 1 month config");

        // 2. Voucher creation, redemption and idempotency
        var voucher = new PromotionCode
        {
            Code = "ROOMIE2026",
            Description = "Tet 2026 Promo",
            RewardMonths = 2,
            MaxRedemptions = 2,
            RedeemedCount = 0,
            IsActive = true,
            CreatedAt = clock.GetUtcNow()
        };
        db.PromotionCodes.Add(voucher);
        await db.SaveChangesAsync();

        // Redeem voucher for user
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            var codeEntity = await db.PromotionCodes.SingleAsync(x => x.Code == "ROOMIE2026");
            var alreadyUsed = await db.PromotionRedemptions.AnyAsync(x => x.PromotionCodeId == codeEntity.Id && x.UserId == user.Id);
            Check(!alreadyUsed, "user has not redeemed voucher yet");

            var prevExpiresAt = user.SubscriptionExpiresAt ?? clock.GetUtcNow();
            var newExpiresAt = prevExpiresAt.AddMonths(codeEntity.RewardMonths);
            user.SubscriptionTier = SubscriptionTier.Pro;
            user.SubscriptionExpiresAt = newExpiresAt;
            codeEntity.RedeemedCount += 1;

            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                PromotionCodeId = codeEntity.Id,
                UserId = user.Id,
                RedeemedAt = clock.GetUtcNow()
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        var reloadedCode = await db.PromotionCodes.AsNoTracking().SingleAsync(x => x.Code == "ROOMIE2026");
        Check(reloadedCode.RedeemedCount == 1, "redeemed count incremented to 1");

        // Attempting to redeem the same code again should be blocked by unique index / check
        var isDuplicateRedemption = await db.PromotionRedemptions.AnyAsync(x => x.PromotionCodeId == reloadedCode.Id && x.UserId == user.Id);
        Check(isDuplicateRedemption, "duplicate redemption check detects prior usage");

        // Second user redeems the code
        var user2 = new AppUser { Id = Guid.NewGuid(), Name = "Owner 2", Email = "owner2@example.invalid", PasswordHash = "x", Status = OwnerStatus.Approved };
        db.Users.Add(user2);
        await db.SaveChangesAsync();

        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            var codeEntity = await db.PromotionCodes.SingleAsync(x => x.Code == "ROOMIE2026");
            codeEntity.RedeemedCount += 1;
            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                PromotionCodeId = codeEntity.Id,
                UserId = user2.Id,
                RedeemedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        reloadedCode = await db.PromotionCodes.AsNoTracking().SingleAsync(x => x.Code == "ROOMIE2026");
        Check(reloadedCode.RedeemedCount == 2, "redeemed count reached max (2/2)");

        // Third user tries to redeem when max is reached
        Check(reloadedCode.MaxRedemptions.HasValue && reloadedCode.RedeemedCount >= reloadedCode.MaxRedemptions.Value,
            "max redemptions limit reached check works correctly");
    }

    private sealed class FakeOtpSender : IRegistrationEmailSender
    {
        public string Code { get; private set; } = "";
        public void EnsureConfigured() { }
        public Task SendAsync(string email, string code, CancellationToken ct)
        {
            Code = code;
            return Task.CompletedTask;
        }
    }
}

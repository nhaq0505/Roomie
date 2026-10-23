using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;
using RoomRental.Tests;

internal static class PromotionsTests
{
    private static readonly Guid AdminId = Guid.Parse("90000000-0000-0000-0000-000000000001");
    private static readonly Guid FreeOwnerId = Guid.Parse("90000000-0000-0000-0000-000000000002");
    private static readonly Guid ExpiredProOwnerId = Guid.Parse("90000000-0000-0000-0000-000000000003");
    private static readonly Guid LifetimeProOwnerId = Guid.Parse("90000000-0000-0000-0000-000000000004");

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

        // 1b. Welcome promo outside date window or disabled grants Free tier even when config has NewUserProMonths > 0
        var welcomeRow = await db.SystemPromotions.SingleAsync(x => x.Id == "welcome");
        welcomeRow.StartsAt = clock.GetUtcNow().AddDays(2);
        welcomeRow.EndsAt = clock.GetUtcNow().AddDays(10);
        await db.SaveChangesAsync();

        var futureChallenge = await regService.StartAsync(new AppUser { Name = "Future Promo Owner", Email = "future@example.invalid", PasswordHash = "x" }, default);
        var futureUser = await regService.VerifyAsync(futureChallenge.ChallengeToken, sender.Code, default);
        Check(futureUser.SubscriptionTier == SubscriptionTier.Free && futureUser.SubscriptionExpiresAt is null,
            "welcome promotion with StartsAt in the future does not grant Pro");

        welcomeRow.StartsAt = clock.GetUtcNow().AddDays(-10);
        welcomeRow.EndsAt = clock.GetUtcNow().AddMinutes(-5);
        await db.SaveChangesAsync();

        var expiredPromoChallenge = await regService.StartAsync(new AppUser { Name = "Expired Promo Owner", Email = "expiredpromo@example.invalid", PasswordHash = "x" }, default);
        var expiredPromoUser = await regService.VerifyAsync(expiredPromoChallenge.ChallengeToken, sender.Code, default);
        Check(expiredPromoUser.SubscriptionTier == SubscriptionTier.Free && expiredPromoUser.SubscriptionExpiresAt is null,
            "welcome promotion with EndsAt in the past does not grant Pro");

        welcomeRow.StartsAt = null;
        welcomeRow.EndsAt = null;
        welcomeRow.IsEnabled = false;
        await db.SaveChangesAsync();

        var disabledPromoChallenge = await regService.StartAsync(new AppUser { Name = "Disabled Promo Owner", Email = "disabledpromo@example.invalid", PasswordHash = "x" }, default);
        var disabledPromoUser = await regService.VerifyAsync(disabledPromoChallenge.ChallengeToken, sender.Code, default);
        Check(disabledPromoUser.SubscriptionTier == SubscriptionTier.Free && disabledPromoUser.SubscriptionExpiresAt is null,
            "disabled welcome promotion in DB overrides config NewUserProMonths and grants Free");

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

        // 3. Lifetime Pro user (SubscriptionTier.Pro, SubscriptionExpiresAt = null) is rejected when redeeming voucher
        var lifetimeUser = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Lifetime Owner",
            Email = "lifetime@example.invalid",
            PasswordHash = "x",
            Status = OwnerStatus.Approved,
            SubscriptionTier = SubscriptionTier.Pro,
            SubscriptionExpiresAt = null
        };
        db.Users.Add(lifetimeUser);

        var lifetimeVoucher = new PromotionCode
        {
            Code = "PRO_LIFETIME_TEST",
            Description = "Test Voucher",
            RewardMonths = 1,
            MaxRedemptions = 5,
            RedeemedCount = 0,
            IsActive = true,
            CreatedAt = clock.GetUtcNow()
        };
        db.PromotionCodes.Add(lifetimeVoucher);
        await db.SaveChangesAsync();

        var lifetimeRedeemRejected = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var promo = await db.PromotionCodes.SingleOrDefaultAsync(x => x.Code == "PRO_LIFETIME_TEST")
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Mã khuyến mãi không tồn tại.");

            var owner = await db.Users.SingleAsync(x => x.Id == lifetimeUser.Id);
            if (owner.SubscriptionTier == SubscriptionTier.Pro && owner.SubscriptionExpiresAt == null)
                throw new ApiException(StatusCodes.Status409Conflict, "Tài khoản của bạn đang ở gói Pro vĩnh viễn, không cần sử dụng thêm mã khuyến mãi.");

            owner.SubscriptionTier = SubscriptionTier.Pro;
            owner.SubscriptionExpiresAt = clock.GetUtcNow().AddMonths(promo.RewardMonths);
            promo.RedeemedCount++;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status409Conflict)
        {
            lifetimeRedeemRejected = true;
        }

        Check(lifetimeRedeemRejected, "lifetime pro user is rejected with 409 Conflict when redeeming voucher");

        var reloadedLifetimeUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == lifetimeUser.Id);
        Check(reloadedLifetimeUser.SubscriptionTier == SubscriptionTier.Pro && reloadedLifetimeUser.SubscriptionExpiresAt == null,
            "lifetime pro user retains SubscriptionTier.Pro and SubscriptionExpiresAt == null");

        var reloadedLifetimeVoucher = await db.PromotionCodes.AsNoTracking().SingleAsync(x => x.Code == "PRO_LIFETIME_TEST");
        Check(reloadedLifetimeVoucher.RedeemedCount == 0, "voucher RedeemedCount was not incremented when lifetime user was rejected");

        // 4. Payment webhook for lifetime Pro user retains SubscriptionExpiresAt == null
        var dummyProtection = new EphemeralDataProtectionProvider();
        var dummyHttpFactory = new FakeHttpClientFactory();
        var webhookService = new PaymentWebhookService(db, dummyProtection, dummyHttpFactory, config);

        var subAttempt = new PayOsAttempt
        {
            Id = Guid.NewGuid(),
            OwnerId = lifetimeUser.Id,
            OrderCode = 77665544,
            Amount = 99000,
            Kind = "Subscription",
            SubscriptionDays = 30,
            Status = PaymentAttemptStatus.Pending,
            CreatedAt = clock.GetUtcNow()
        };
        db.PayOsAttempts.Add(subAttempt);
        await db.SaveChangesAsync();

        var paymentHandled = await webhookService.HandleSubscriptionPaymentSuccessAsync(subAttempt, 99000, "ref-lifetime-sub-pay", default);
        Check(paymentHandled, "HandleSubscriptionPaymentSuccessAsync succeeds for lifetime user");

        var reloadedAfterSubPayment = await db.Users.AsNoTracking().SingleAsync(x => x.Id == lifetimeUser.Id);
        Check(reloadedAfterSubPayment.SubscriptionTier == SubscriptionTier.Pro && reloadedAfterSubPayment.SubscriptionExpiresAt == null,
            "lifetime pro user retains SubscriptionTier.Pro and SubscriptionExpiresAt == null after payment webhook");

        // 5. Full HTTP Endpoint & Edge Case Suite for AdminPromotionsEndpoints
        await RunHttpEndpointsAndEdgeCasesAsync();
    }

    private static async Task RunHttpEndpointsAndEdgeCasesAsync()
    {
        await using var fixture = await PostgresTestDb.CreateAsync(ensureCreated: false);
        await using var app = CreatePromotionsTestApp(fixture.ConnectionString);
        await app.StartAsync();
        using var client = app.GetTestClient();

        // Role guards: Owner cannot access /api/admin/promotions; Admin cannot call /api/subscription/redeem-code
        using (var ownerAdminReq = CreateAuthenticatedRequest(HttpMethod.Get, "/api/admin/promotions", FreeOwnerId, "Owner"))
        using (var ownerAdminResp = await client.SendAsync(ownerAdminReq))
        {
            Check(ownerAdminResp.StatusCode == HttpStatusCode.Forbidden, "Owner is forbidden (403) from GET /api/admin/promotions");
        }

        using (var adminRedeemReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", AdminId, "Admin", new RedeemPromotionCodeRequest("ANYCODE")))
        using (var adminRedeemResp = await client.SendAsync(adminRedeemReq))
        {
            Check(adminRedeemResp.StatusCode == HttpStatusCode.Forbidden, "Admin is forbidden (403) from POST /api/subscription/redeem-code");
        }

        // GET /api/admin/promotions default overview before welcome row exists
        using (var getInitialReq = CreateAuthenticatedRequest(HttpMethod.Get, "/api/admin/promotions", AdminId, "Admin"))
        using (var getInitialResp = await client.SendAsync(getInitialReq))
        {
            Check(getInitialResp.StatusCode == HttpStatusCode.OK, "Admin GET /api/admin/promotions returns 200 OK");
            var overview = await getInitialResp.Content.ReadFromJsonAsync<AdminPromotionsOverviewDto>();
            Check(overview != null && overview.Welcome.Id == "welcome" && overview.Codes.Count == 0,
                "initial promotions overview returns default welcome campaign and empty codes");
        }

        // PUT /api/admin/promotions/welcome validation boundaries
        foreach (var badMonths in new[] { 0, -3, 121 })
        {
            using var badWelcomeReq = CreateAuthenticatedRequest(HttpMethod.Put, "/api/admin/promotions/welcome", AdminId, "Admin",
                new WelcomePromotionUpdateRequest(true, badMonths, null, null));
            using var badWelcomeResp = await client.SendAsync(badWelcomeReq);
            Check(badWelcomeResp.StatusCode == HttpStatusCode.BadRequest, $"PUT /welcome rejects RewardMonths = {badMonths} with 400");
        }

        var now = DateTimeOffset.UtcNow;
        using (var invertedDatesReq = CreateAuthenticatedRequest(HttpMethod.Put, "/api/admin/promotions/welcome", AdminId, "Admin",
            new WelcomePromotionUpdateRequest(true, 3, now.AddDays(5), now.AddDays(1))))
        using (var invertedDatesResp = await client.SendAsync(invertedDatesReq))
        {
            Check(invertedDatesResp.StatusCode == HttpStatusCode.BadRequest, "PUT /welcome rejects StartsAt > EndsAt with 400");
        }

        using (var validWelcomeReq = CreateAuthenticatedRequest(HttpMethod.Put, "/api/admin/promotions/welcome", AdminId, "Admin",
            new WelcomePromotionUpdateRequest(true, 3, now.AddDays(-1), now.AddDays(30))))
        using (var validWelcomeResp = await client.SendAsync(validWelcomeReq))
        {
            Check(validWelcomeResp.StatusCode == HttpStatusCode.OK, "PUT /welcome creates welcome promotion with 200 OK");
            var dto = await validWelcomeResp.Content.ReadFromJsonAsync<WelcomePromotionDto>();
            Check(dto != null && dto.IsEnabled && dto.RewardMonths == 3, "PUT /welcome returns updated DTO");
        }

        // POST /api/admin/promotions/codes validation boundaries
        foreach (var badCode in new[] { "", "  ", "AB", new string('A', 33) })
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
                new PromotionCodeCreateRequest(badCode, "Desc", 1, 10, null, true));
            using var resp = await client.SendAsync(req);
            Check(resp.StatusCode == HttpStatusCode.BadRequest, $"POST /codes rejects invalid code length '{badCode}' with 400");
        }

        using (var longDescReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("VALIDCODE", new string('D', 256), 1, 10, null, true)))
        using (var longDescResp = await client.SendAsync(longDescReq))
        {
            Check(longDescResp.StatusCode == HttpStatusCode.BadRequest, "POST /codes rejects Description > 255 chars with 400");
        }

        foreach (var badMonths in new[] { 0, 121 })
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
                new PromotionCodeCreateRequest("VALIDCODE", "Desc", badMonths, 10, null, true));
            using var resp = await client.SendAsync(req);
            Check(resp.StatusCode == HttpStatusCode.BadRequest, $"POST /codes rejects RewardMonths = {badMonths} with 400");
        }

        using (var zeroMaxReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("VALIDCODE", "Desc", 1, 0, null, true)))
        using (var zeroMaxResp = await client.SendAsync(zeroMaxReq))
        {
            Check(zeroMaxResp.StatusCode == HttpStatusCode.BadRequest, "POST /codes rejects MaxRedemptions = 0 with 400");
        }

        // Create valid code (trimmed + uppercase) and verify duplicate 409
        PromotionCodeDto? createdCode;
        using (var createReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("  tet2026  ", "  Tết Bính Ngọ  ", 3, 2, now.AddDays(10), true)))
        using (var createResp = await client.SendAsync(createReq))
        {
            Check(createResp.StatusCode == HttpStatusCode.Created, "POST /codes creates voucher with 201 Created");
            createdCode = await createResp.Content.ReadFromJsonAsync<PromotionCodeDto>();
            Check(createdCode != null && createdCode.Code == "TET2026" && createdCode.Description == "Tết Bính Ngọ",
                "POST /codes normalizes code to uppercase and trims description");
        }

        using (var dupReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("tet2026", "Duplicate", 1, null, null, true)))
        using (var dupResp = await client.SendAsync(dupReq))
        {
            Check(dupResp.StatusCode == HttpStatusCode.Conflict, "POST /codes with duplicate code returns 409 Conflict");
        }

        // Create helper codes for inactive, expired, and stacking tests
        using (var inactiveCreate = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("LOCKED_CODE", "Locked", 2, 5, null, false)))
        {
            await client.SendAsync(inactiveCreate);
        }

        using (var expiredCreate = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("EXPIRED_CODE", "Expired", 2, 5, now.AddMinutes(-10), true)))
        {
            await client.SendAsync(expiredCreate);
        }

        using (var stackCreate = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("STACK_2M", "Stack 2 months", 2, 10, null, true)))
        {
            await client.SendAsync(stackCreate);
        }

        // POST /api/subscription/redeem-code error & edge cases
        using (var blankRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("   ")))
        using (var blankResp = await client.SendAsync(blankRedeem))
        {
            Check(blankResp.StatusCode == HttpStatusCode.BadRequest, "redeem-code rejects blank code with 400");
        }

        using (var unknownRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("DOES_NOT_EXIST")))
        using (var unknownResp = await client.SendAsync(unknownRedeem))
        {
            Check(unknownResp.StatusCode == HttpStatusCode.NotFound, "redeem-code rejects non-existent code with 404");
        }

        using (var inactiveRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("LOCKED_CODE")))
        using (var inactiveResp = await client.SendAsync(inactiveRedeem))
        {
            Check(inactiveResp.StatusCode == HttpStatusCode.BadRequest, "redeem-code rejects inactive code with 400");
        }

        using (var expiredRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("EXPIRED_CODE")))
        using (var expiredResp = await client.SendAsync(expiredRedeem))
        {
            Check(expiredResp.StatusCode == HttpStatusCode.BadRequest, "redeem-code rejects expired code with 400");
        }

        using (var lifetimeRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", LifetimeProOwnerId, "Owner",
            new RedeemPromotionCodeRequest("TET2026")))
        using (var lifetimeResp = await client.SendAsync(lifetimeRedeem))
        {
            Check(lifetimeResp.StatusCode == HttpStatusCode.Conflict, "redeem-code rejects Lifetime Pro owner with 409 Conflict");
        }

        // FreeOwner redeems TET2026 (+3 months) -> upgraded to Pro
        DateTimeOffset? firstExpiry;
        using (var redeem1Req = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("  tet2026 ")))
        using (var redeem1Resp = await client.SendAsync(redeem1Req))
        {
            Check(redeem1Resp.StatusCode == HttpStatusCode.OK, "FreeOwner redeems TET2026 with 200 OK");
            var res = await redeem1Resp.Content.ReadFromJsonAsync<RedeemPromotionResultDto>();
            Check(res != null && res.Success && res.AddedMonths == 3 && res.SubscriptionTier == "Pro",
                "FreeOwner upgraded to Pro with AddedMonths = 3");
            firstExpiry = res!.SubscriptionExpiresAt;
        }

        // Duplicate redemption by same owner -> 400 BadRequest
        using (var dupRedeemReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("TET2026")))
        using (var dupRedeemResp = await client.SendAsync(dupRedeemReq))
        {
            Check(dupRedeemResp.StatusCode == HttpStatusCode.BadRequest, "duplicate voucher redemption by same owner returns 400");
        }

        // Stacking: FreeOwner (now active Pro) redeems STACK_2M (+2 months) -> adds 2 months onto firstExpiry
        using (var stackReq = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("STACK_2M")))
        using (var stackResp = await client.SendAsync(stackReq))
        {
            Check(stackResp.StatusCode == HttpStatusCode.OK, "active Pro owner stacks second voucher with 200 OK");
            var stackRes = await stackResp.Content.ReadFromJsonAsync<RedeemPromotionResultDto>();
            Check(stackRes?.SubscriptionExpiresAt is not null && Math.Abs((stackRes.SubscriptionExpiresAt.Value - firstExpiry!.Value.AddMonths(2)).TotalSeconds) < 1,
                "stacking voucher adds months onto existing future SubscriptionExpiresAt");
        }

        // ExpiredProOwner (whose SubscriptionExpiresAt was in the past) redeems TET2026 (2nd of 2 max redemptions) -> base resets to now
        using (var expiredOwnerRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", ExpiredProOwnerId, "Owner",
            new RedeemPromotionCodeRequest("TET2026")))
        using (var expiredOwnerResp = await client.SendAsync(expiredOwnerRedeem))
        {
            Check(expiredOwnerResp.StatusCode == HttpStatusCode.OK, "ExpiredProOwner redeems 2nd slot of TET2026 with 200 OK");
            var expRes = await expiredOwnerResp.Content.ReadFromJsonAsync<RedeemPromotionResultDto>();
            Check(expRes != null && expRes.SubscriptionExpiresAt > now.AddMonths(2).AddDays(25),
                "ExpiredProOwner base expiry resets to now instead of past date");
        }

        using (var exhaustedRedeem = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", Guid.Parse("90000000-0000-0000-0000-000000000005"), "Owner",
            new RedeemPromotionCodeRequest("TET2026")))
        using (var exhaustedResp = await client.SendAsync(exhaustedRedeem))
        {
            Check(exhaustedResp.StatusCode == HttpStatusCode.BadRequest, "3rd owner redeeming exhausted voucher (2/2) receives 400 BadRequest");
        }

        // PATCH /api/admin/promotions/codes/{id}: cannot set MaxRedemptions < RedeemedCount (2)
        using (var lowMaxPatch = CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/admin/promotions/codes/{createdCode!.Id}", AdminId, "Admin",
            new PromotionCodeUpdateRequest("Updated", 3, 1, null, true)))
        using (var lowMaxResp = await client.SendAsync(lowMaxPatch))
        {
            Check(lowMaxResp.StatusCode == HttpStatusCode.BadRequest, "PATCH /codes/{id} rejects MaxRedemptions < RedeemedCount with 400");
        }

        // PATCH valid update
        using (var validPatch = CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/admin/promotions/codes/{createdCode.Id}", AdminId, "Admin",
            new PromotionCodeUpdateRequest("Updated Desc", 4, 2, null, true)))
        using (var validPatchResp = await client.SendAsync(validPatch))
        {
            Check(validPatchResp.StatusCode == HttpStatusCode.OK, "PATCH /codes/{id} updates voucher with 200 OK");
        }

        // PATCH & DELETE unknown ID -> 404 NotFound
        var unknownId = Guid.NewGuid();
        using (var patch404 = CreateAuthenticatedRequest(HttpMethod.Patch, $"/api/admin/promotions/codes/{unknownId}", AdminId, "Admin",
            new PromotionCodeUpdateRequest("Desc", 1, null, null, true)))
        using (var patch404Resp = await client.SendAsync(patch404))
        {
            Check(patch404Resp.StatusCode == HttpStatusCode.NotFound, "PATCH unknown code ID returns 404 NotFound");
        }

        using (var del404 = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/admin/promotions/codes/{unknownId}", AdminId, "Admin"))
        using (var del404Resp = await client.SendAsync(del404))
        {
            Check(del404Resp.StatusCode == HttpStatusCode.NotFound, "DELETE unknown code ID returns 404 NotFound");
        }

        // DELETE existing code with redemptions -> 204 NoContent
        using (var delReq = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/admin/promotions/codes/{createdCode.Id}", AdminId, "Admin"))
        using (var delResp = await client.SendAsync(delReq))
        {
            Check(delResp.StatusCode == HttpStatusCode.NoContent, "DELETE /codes/{id} removes voucher and redemptions with 204 NoContent");
        }

        // Concurrent redemption of a 1-slot voucher under Serializable isolation allows at most 1 redemption
        using (var singleSlotCreate = CreateAuthenticatedRequest(HttpMethod.Post, "/api/admin/promotions/codes", AdminId, "Admin",
            new PromotionCodeCreateRequest("RACE_1SLOT", "Single slot race test", 1, 1, null, true)))
        using (var singleSlotResp = await client.SendAsync(singleSlotCreate))
        {
            Check(singleSlotResp.StatusCode == HttpStatusCode.Created, "POST /codes creates RACE_1SLOT with MaxRedemptions = 1");
        }

        var thirdOwnerId = Guid.Parse("90000000-0000-0000-0000-000000000005");
        using var raceReq1 = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", FreeOwnerId, "Owner",
            new RedeemPromotionCodeRequest("RACE_1SLOT"));
        using var raceReq2 = CreateAuthenticatedRequest(HttpMethod.Post, "/api/subscription/redeem-code", thirdOwnerId, "Owner",
            new RedeemPromotionCodeRequest("RACE_1SLOT"));
        var raceResponses = await Task.WhenAll(client.SendAsync(raceReq1), client.SendAsync(raceReq2));
        var okCount = raceResponses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejectedCount = raceResponses.Count(r => r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
        foreach (var r in raceResponses) r.Dispose();
        Check(okCount == 1 && rejectedCount == 1,
            "concurrent redemptions of 1-slot voucher under Serializable isolation succeed for exactly 1 owner and reject the other with 400/409");
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, Guid userId, string role, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Fixture-User", userId.ToString());
        req.Headers.Add("X-Fixture-Role", role);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private static WebApplication CreatePromotionsTestApp(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication(PromoFixtureAuth.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, PromoFixtureAuth>(PromoFixtureAuth.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddPolicy("tenant-payment",
            _ => RateLimitPartition.GetNoLimiter("fixture")));

        var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(
                new AppUser { Id = AdminId, Name = "Admin", Email = "admin@example.com", PasswordHash = "x", Role = UserRole.Admin, Status = OwnerStatus.Approved },
                new AppUser { Id = FreeOwnerId, Name = "Free Owner", Email = "free@example.com", PasswordHash = "x", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Free },
                new AppUser { Id = ExpiredProOwnerId, Name = "Expired Pro Owner", Email = "expired@example.com", PasswordHash = "x", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Pro, SubscriptionExpiresAt = DateTimeOffset.UtcNow.AddDays(-30) },
                new AppUser { Id = LifetimeProOwnerId, Name = "Lifetime Pro Owner", Email = "lifetime@example.com", PasswordHash = "x", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Pro, SubscriptionExpiresAt = null },
                new AppUser { Id = Guid.Parse("90000000-0000-0000-0000-000000000005"), Name = "Third Owner", Email = "third@example.com", PasswordHash = "x", Role = UserRole.Owner, Status = OwnerStatus.Approved, SubscriptionTier = SubscriptionTier.Free }
            );
            db.SaveChanges();
        }

        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPromotionsRoutes();
        return app;
    }

    private sealed class PromoFixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PromoFixtureAuth";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Fixture-User"].FirstOrDefault() ?? FreeOwnerId.ToString();
            var role = Request.Headers["X-Fixture-Role"].FirstOrDefault() ?? "Owner";
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            ], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
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

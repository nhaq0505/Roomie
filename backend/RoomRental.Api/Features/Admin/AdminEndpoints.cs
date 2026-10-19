using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/admin").RequireAuthorization(policy => policy.RequireRole("Admin"));

        routes.MapGet("/owners", async (string? status, int? page, int? pageSize, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var query = db.Users.AsNoTracking().Where(row => row.Role == UserRole.Owner);
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<OwnerStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    throw new ApiException(StatusCodes.Status400BadRequest, "Unknown owner status.");
                query = query.Where(row => row.Status == parsed);
            }
            var paging = PageRequest.Parse(page, pageSize);
            var total = await query.CountAsync(cancellationToken);
            var pendingCount = await db.Users.CountAsync(x => x.Role == UserRole.Owner && x.Status == OwnerStatus.Pending, cancellationToken);
            var owners = await query.OrderBy(row => row.Status).ThenBy(row => row.CreatedAt).ThenBy(row => row.Id)
                .Skip(paging.Offset).Take(paging.Size).ToListAsync(cancellationToken);
            var ownerIds = owners.Select(row => row.Id).ToList();
            var roomCounts = await (from r in db.Rooms.AsNoTracking()
                                    join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                                    where ownerIds.Contains(b.OwnerId) && r.ArchivedAt == null && b.ArchivedAt == null
                                    group r by b.OwnerId into g
                                    select new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count, cancellationToken);

            var result = owners.Select(row =>
            {
                var count = roomCounts.GetValueOrDefault(row.Id, 0);
                return new OwnerAdminDto(
                    row.Id, row.Name, row.Email, row.Phone, row.Status.ToString(), row.StatusReason,
                    row.CreatedAt, row.SubscriptionTier.ToString(), row.SubscriptionExpiresAt,
                    row.IsPro, count, row.MaxRooms);
            }).ToList();
            return Results.Ok(page is null ? (object)result : new { items = result, total, pendingCount, page = paging.Number, pageSize = paging.Size });
        });

        routes.MapPost("/owners/{id:guid}/reset-password", async (Guid id, AdminResetPasswordRequest request,
            HttpContext context, AppDbContext db, Microsoft.AspNetCore.Identity.IPasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            await AccountLoginLimiter.ReserveAsync(db, "admin-recovery:" + ApiSupport.UserId(context), app.Environment.IsDevelopment() ? 1000 : 10, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var admin = await db.Users.SingleAsync(x => x.Id == ApiSupport.UserId(context), ct);
            PasswordSecurity.VerifyCurrentPassword(admin, request.CurrentPassword, hasher);
            var owner = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Role == UserRole.Owner, ct)
                ?? throw new ApiException(404, "Owner was not found.");
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            owner.PasswordResetHash = PasswordSecurity.HashToken(token);
            owner.PasswordResetExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            await PasswordSecurity.RevokeSessionsAsync(db, owner.Id, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token, expiresAt = owner.PasswordResetExpiresAt });
        }).RequireRateLimiting("auth");

        routes.MapPost("/owners/{id:guid}/status", async (Guid id, OwnerStatusRequest request, AppDbContext db, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<OwnerStatus>(request.Status, ignoreCase: false, out var nextStatus) || !Enum.IsDefined(nextStatus))
                throw new ApiException(StatusCodes.Status400BadRequest, "Status must be Pending, Approved, Rejected, or Suspended.");
            var owner = await db.Users.SingleOrDefaultAsync(row => row.Id == id && row.Role == UserRole.Owner, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason is { Length: > 1000 }) throw new ApiException(StatusCodes.Status400BadRequest, "Reason must be 1000 characters or fewer.");
            owner.Status = nextStatus;
            owner.StatusReason = reason;
            if (nextStatus != OwnerStatus.Approved)
            {
                var now = DateTimeOffset.UtcNow;
                var sessions = await db.AuthSessions.Where(row => row.UserId == owner.Id && row.RevokedAt == null).ToListAsync(cancellationToken);
                foreach (var session in sessions) session.RevokedAt = now;
            }
            await db.SaveChangesAsync(cancellationToken);
            var roomCount = await (from r in db.Rooms.AsNoTracking()
                                   join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                                   where b.OwnerId == owner.Id && r.ArchivedAt == null && b.ArchivedAt == null
                                   select r.Id).CountAsync(cancellationToken);
            return Results.Ok(new OwnerAdminDto(
                owner.Id, owner.Name, owner.Email, owner.Phone, owner.Status.ToString(), owner.StatusReason,
                owner.CreatedAt, owner.SubscriptionTier.ToString(), owner.SubscriptionExpiresAt,
                owner.IsPro, roomCount, owner.MaxRooms));
        });

        routes.MapPost("/owners/{id:guid}/subscription", async (Guid id, AdminSubscriptionRequest request, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var owner = await db.Users.SingleOrDefaultAsync(row => row.Id == id && row.Role == UserRole.Owner, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");

            if (!Enum.TryParse<SubscriptionTier>(request.Tier, ignoreCase: true, out var nextTier) || !Enum.IsDefined(nextTier))
                throw new ApiException(StatusCodes.Status400BadRequest, "Tier must be Free or Pro.");

            owner.SubscriptionTier = nextTier;
            if (nextTier == SubscriptionTier.Pro)
            {
                if (request.ExpiresAt is not null)
                {
                    owner.SubscriptionExpiresAt = request.ExpiresAt.Value;
                }
                else if (request.Days is > 0)
                {
                    var baseDate = (owner.SubscriptionExpiresAt is not null && owner.SubscriptionExpiresAt > DateTimeOffset.UtcNow)
                        ? owner.SubscriptionExpiresAt.Value
                        : DateTimeOffset.UtcNow;
                    owner.SubscriptionExpiresAt = baseDate.AddDays(request.Days.Value);
                }
                else if (request.Days is 0)
                {
                    owner.SubscriptionExpiresAt = null; // Unlimited / Lifetime
                }
                else
                {
                    var baseDate = (owner.SubscriptionExpiresAt is not null && owner.SubscriptionExpiresAt > DateTimeOffset.UtcNow)
                        ? owner.SubscriptionExpiresAt.Value
                        : DateTimeOffset.UtcNow;
                    owner.SubscriptionExpiresAt = baseDate.AddDays(30);
                }
            }
            else
            {
                owner.SubscriptionExpiresAt = null;
            }

            if (!string.IsNullOrWhiteSpace(request.Reason))
                owner.StatusReason = request.Reason.Trim();

            await db.SaveChangesAsync(cancellationToken);

            var count = await (from r in db.Rooms.AsNoTracking()
                               join b in db.Buildings.AsNoTracking() on r.BuildingId equals b.Id
                               where b.OwnerId == owner.Id && r.ArchivedAt == null && b.ArchivedAt == null
                               select r.Id).CountAsync(cancellationToken);

            return Results.Ok(new OwnerAdminDto(
                owner.Id, owner.Name, owner.Email, owner.Phone, owner.Status.ToString(), owner.StatusReason,
                owner.CreatedAt, owner.SubscriptionTier.ToString(), owner.SubscriptionExpiresAt,
                owner.IsPro, count, owner.MaxRooms));
        });
    }
}

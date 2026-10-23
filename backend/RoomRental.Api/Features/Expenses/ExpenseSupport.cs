using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

/// <summary>Validation, plan checks and lookups shared by expense, recurring-expense and report endpoints.</summary>
public static class ExpenseSupport
{
    public const string ProRequiredMessage = "Tính năng này dành cho gói Pro. Vui lòng nâng cấp để sử dụng.";

    public static async Task<bool> IsProAsync(AppDbContext db, Guid ownerId, DateTimeOffset now, CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId && user.SubscriptionTier == SubscriptionTier.Pro &&
            (user.SubscriptionExpiresAt == null || user.SubscriptionExpiresAt > now), ct);

    public static async Task EnsureProAsync(AppDbContext db, Guid ownerId, DateTimeOffset now, CancellationToken ct,
        string message = ProRequiredMessage)
    {
        if (!await IsProAsync(db, ownerId, now, ct))
            throw new ApiException(StatusCodes.Status403Forbidden, message);
    }

    public static string NormalizeCategory(string? category)
    {
        var value = category?.Trim() ?? "";
        if (!ExpenseCategories.All.Contains(value))
            throw new ApiException(StatusCodes.Status400BadRequest, "Danh mục chi phí không hợp lệ.");
        return value;
    }

    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > ExpenseLimits.MaxAmount || amount != decimal.Truncate(amount))
            throw new ApiException(StatusCodes.Status400BadRequest, "Số tiền chi phải là số VND nguyên lớn hơn 0 và không quá 10 tỷ.");
    }

    public static string? NormalizeDescription(string? description)
    {
        var value = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (value is { Length: > ExpenseLimits.MaxDescriptionLength })
            throw new ApiException(StatusCodes.Status400BadRequest, "Ghi chú chi phí tối đa 500 ký tự.");
        return value;
    }

    public static string? NormalizeIdempotencyKey(string? key)
    {
        var value = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (value is { Length: > 100 })
            throw new ApiException(StatusCodes.Status400BadRequest, "Idempotency key must be 100 characters or fewer.");
        return value;
    }

    /// <summary>
    /// Resolves an optional building/room link. A link that is unchanged from the stored record may point at an
    /// archived building/room so old records stay editable; any new link must be active and owned by the caller.
    /// </summary>
    public static async Task<(Guid? BuildingId, Guid? RoomId)> ResolveLocationAsync(AppDbContext db, Guid ownerId,
        Guid? buildingId, Guid? roomId, Guid? currentBuildingId, Guid? currentRoomId, CancellationToken ct)
    {
        var unchanged = buildingId == currentBuildingId && roomId == currentRoomId && (buildingId is not null || roomId is not null);
        if (roomId is Guid requestedRoom)
        {
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, requestedRoom, ct, allowArchived: unchanged);
            if (buildingId is Guid requestedBuilding && requestedBuilding != building.Id)
                throw new ApiException(StatusCodes.Status400BadRequest, "Phòng không thuộc dãy trọ đã chọn.");
            return (building.Id, room.Id);
        }
        if (buildingId is Guid onlyBuilding)
        {
            if (unchanged)
            {
                var exists = await db.Buildings.AsNoTracking().AnyAsync(row => row.Id == onlyBuilding && row.OwnerId == ownerId, ct);
                if (!exists) throw new ApiException(StatusCodes.Status404NotFound, "Building was not found.");
                return (onlyBuilding, null);
            }
            return ((await PropertyLookup.FindBuildingAsync(db, ownerId, onlyBuilding, ct)).Id, null);
        }
        return (null, null);
    }

    public static string CurrentPeriod(DateOnly today) => today.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string AddMonths(string period, int months)
    {
        var date = new DateOnly(int.Parse(period.AsSpan(0, 4)), int.Parse(period.AsSpan(5, 2)), 1).AddMonths(months);
        return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    public static int MonthSpan(string fromPeriod, string toPeriod)
    {
        var from = int.Parse(fromPeriod.AsSpan(0, 4)) * 12 + int.Parse(fromPeriod.AsSpan(5, 2));
        var to = int.Parse(toPeriod.AsSpan(0, 4)) * 12 + int.Parse(toPeriod.AsSpan(5, 2));
        return to - from + 1;
    }

    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: "23505" }) return true;
        return false;
    }

    public static async Task<(Dictionary<Guid, string> Buildings, Dictionary<Guid, string> Rooms)> LoadNamesAsync(
        AppDbContext db, Guid ownerId, IEnumerable<Guid?> buildingIds, IEnumerable<Guid?> roomIds, CancellationToken ct)
    {
        var buildingKeys = buildingIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var roomKeys = roomIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var buildings = buildingKeys.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Buildings.AsNoTracking().Where(row => row.OwnerId == ownerId && buildingKeys.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Name, ct);
        var rooms = roomKeys.Count == 0
            ? new Dictionary<Guid, string>()
            : await (from room in db.Rooms.AsNoTracking()
                     join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                     where building.OwnerId == ownerId && roomKeys.Contains(room.Id)
                     select new { room.Id, room.Name }).ToDictionaryAsync(row => row.Id, row => row.Name, ct);
        return (buildings, rooms);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapDashboardRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/dashboard", async (HttpContext context, AppDbContext db, TimeProvider clock, [FromServices] IMemoryCache? cache = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var cacheKey = $"dashboard:{ownerId}";
            var bypassCache = context.Request.Headers.CacheControl.ToString().Contains("no-cache", StringComparison.OrdinalIgnoreCase);
            if (cache is not null && !bypassCache && cache.TryGetValue(cacheKey, out object? cached))
            {
                return Results.Ok(cached);
            }

            var rooms = from room in db.Rooms.AsNoTracking()
                        join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                        where building.OwnerId == ownerId && building.ArchivedAt == null && room.ArchivedAt == null
                        select room.Id;
            var activeOccupancies = db.Occupancies.AsNoTracking().Where(row => row.IsActive && rooms.Contains(row.RoomId));
            var occupiedRooms = await activeOccupancies.CountAsync(cancellationToken);
            var period = ApiSupport.VietnamDate(clock.GetUtcNow()).ToString("yyyy-MM");
            var invoices = db.Invoices.AsNoTracking().Where(row => row.OwnerId == ownerId &&
                row.LifecycleState == InvoiceLifecycleState.Issued && row.Kind != "Deposit");
            var summary = await invoices.GroupBy(_ => 1).Select(group => new
            {
                billed = group.Sum(x => x.Total),
                paid = group.Sum(x => x.Paid),
                monthlyBilled = group.Sum(x => x.Period == period ? x.Total : 0m),
                monthlyPaid = group.Sum(x => x.Period == period ? x.Paid : 0m),
                monthlyOutstanding = group.Sum(x => x.Period == period && x.Total > x.Paid ? x.Total - x.Paid : 0m)
            }).SingleOrDefaultAsync(cancellationToken);
            var billed = summary?.billed ?? 0m;
            var paid = summary?.paid ?? 0m;
            var monthlyBilled = summary?.monthlyBilled ?? 0m;
            var monthlyPaid = summary?.monthlyPaid ?? 0m;
            var monthlyOutstanding = summary?.monthlyOutstanding ?? 0m;
            var roomCount = await rooms.CountAsync(cancellationToken);
            var pendingReadings = await activeOccupancies.CountAsync(occupancy =>
                !db.Readings.Any(reading => reading.OccupancyId == occupancy.Id && reading.Period == period), cancellationToken);
            var response = new
            {
                totalRooms = roomCount,
                occupiedRooms,
                totalBilled = billed,
                totalPaid = paid,
                totalOutstanding = Math.Max(0, billed - paid),
                pendingReadings,
                period,
                monthlyBilled,
                monthlyPaid,
                monthlyOutstanding,
                collectionRate = monthlyBilled == 0 ? 0 : decimal.Round(monthlyPaid / monthlyBilled * 100, 2)
            };
            // ponytail: 10s per-process in-memory cache; upgrade to distributed cache or event bus if scaled across replicas.
            cache?.Set(cacheKey, response, TimeSpan.FromSeconds(10));
            return Results.Ok(response);
        });

    }
}

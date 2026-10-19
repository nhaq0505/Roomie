using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapDashboardRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/dashboard", async (HttpContext context, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var rooms = from room in db.Rooms.AsNoTracking()
                        join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                        where building.OwnerId == ownerId && building.ArchivedAt == null && room.ArchivedAt == null
                        select room.Id;
            var activeOccupancies = db.Occupancies.AsNoTracking().Where(row => row.IsActive && rooms.Contains(row.RoomId));
            var occupiedRooms = await activeOccupancies.CountAsync(cancellationToken);
            var invoices = db.Invoices.AsNoTracking().Where(row => row.OwnerId == ownerId &&
                row.LifecycleState == InvoiceLifecycleState.Issued && row.Kind != "Deposit");
            var billed = await QueryAggregates.SumMoneyAsync(db, invoices.Select(row => row.Total), cancellationToken);
            var paid = await QueryAggregates.SumMoneyAsync(db, invoices.Select(row => row.Paid), cancellationToken);
            var period = ApiSupport.VietnamDate(clock.GetUtcNow()).ToString("yyyy-MM");
            var monthly = invoices.Where(row => row.Period == period);
            var monthlyBilled = await QueryAggregates.SumMoneyAsync(db, monthly.Select(row => row.Total), cancellationToken);
            var monthlyPaid = await QueryAggregates.SumMoneyAsync(db, monthly.Select(row => row.Paid), cancellationToken);
            var monthlyOutstanding = await QueryAggregates.SumMoneyAsync(db,
                monthly.Select(row => row.Total > row.Paid ? row.Total - row.Paid : 0m), cancellationToken);
            var roomCount = await rooms.CountAsync(cancellationToken);
            var pendingReadings = await activeOccupancies.CountAsync(occupancy =>
                !db.Readings.Any(reading => reading.OccupancyId == occupancy.Id && reading.Period == period), cancellationToken);
            return Results.Ok(new
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
            });
        });

    }
}

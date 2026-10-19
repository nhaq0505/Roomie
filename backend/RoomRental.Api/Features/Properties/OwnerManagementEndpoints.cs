using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    public static void MapOwnerManagementEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api").RequireAuthorization(policy => policy.RequireRole("Owner"));

        MapDashboardRoutes(routes);
        MapBuildingsRoutes(routes);
        MapRoomsRoutes(routes);
        MapOccupanciesRoutes(routes);
    }

    private static async Task<string?> RoomArchiveBlockReasonAsync(AppDbContext db, Guid ownerId, Guid roomId, CancellationToken ct)
    {
        if (await db.Occupancies.AnyAsync(row => row.RoomId == roomId && row.IsActive, ct))
            return "còn người thuê đang ở.";
        if (await db.Invoices.AnyAsync(invoice => invoice.OwnerId == ownerId && invoice.RoomId == roomId &&
                invoice.LifecycleState == InvoiceLifecycleState.Draft, ct))
            return "còn hóa đơn nháp.";
        if (await db.Invoices.AnyAsync(invoice => invoice.OwnerId == ownerId && invoice.RoomId == roomId &&
                invoice.LifecycleState == InvoiceLifecycleState.Issued && invoice.Kind != "Deposit" && invoice.Total > invoice.Paid, ct))
            return "còn hóa đơn đã phát hành chưa thu đủ.";

        var depositRows = await db.Invoices.AsNoTracking()
            .Where(invoice => invoice.OwnerId == ownerId && invoice.RoomId == roomId &&
                              invoice.LifecycleState == InvoiceLifecycleState.Issued && invoice.Kind == "Deposit")
            .Select(invoice => new { invoice.Id, invoice.Total, invoice.Paid }).ToListAsync(ct);
        if (depositRows.Any(invoice => invoice.Paid < invoice.Total))
            return "còn khoản cọc chưa thu đủ.";
        var depositIds = depositRows.Select(invoice => invoice.Id).ToList();
        if (depositIds.Count > 0)
        {
            var settlements = await db.DepositSettlements.AsNoTracking().Where(row => depositIds.Contains(row.InvoiceId))
                .Select(row => new { row.InvoiceId, row.Amount }).ToListAsync(ct);
            var settledByInvoice = settlements.GroupBy(row => row.InvoiceId)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
            if (depositRows.Any(invoice => invoice.Paid > settledByInvoice.GetValueOrDefault(invoice.Id)))
                return "còn tiền cọc đang giữ.";
        }
        if (await (from attempt in db.PayOsAttempts.AsNoTracking()
                   join invoice in db.Invoices.AsNoTracking() on attempt.InvoiceId equals invoice.Id
                   where invoice.OwnerId == ownerId && invoice.RoomId == roomId && attempt.Status == PaymentAttemptStatus.Pending
                   select attempt.Id).AnyAsync(ct))
            return "còn giao dịch payOS đang chờ đối soát.";
        return null;
    }

    internal static async Task<RoomDto> ToRoomDtoAsync(AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, Room room, Building building, Occupancy? occupancy, string period, CancellationToken cancellationToken)
    {
        var readings = await db.Readings.AsNoTracking().Where(row => row.RoomId == room.Id && (row.Period == period ||
                row.Id == db.Readings.Where(prior => prior.RoomId == room.Id && string.Compare(prior.Period, period) < 0)
                    .OrderByDescending(prior => prior.Period).ThenByDescending(prior => prior.RecordedAt)
                    .Select(prior => prior.Id).FirstOrDefault()))
            .OrderByDescending(row => row.Period).ThenByDescending(row => row.RecordedAt).ToListAsync(cancellationToken);
        var maxReading = await QueryAggregates.MaxReadingAsync(db,
            db.Readings.AsNoTracking().Where(x => x.RoomId == room.Id).Select(x => x.CurrentReading), cancellationToken);
        var maxFinal = await QueryAggregates.MaxReadingAsync(db,
            db.Invoices.AsNoTracking().Where(x => x.RoomId == room.Id && x.IsFinalMeterReading).Select(x => x.CurrentReading), cancellationToken);
        var deposit = occupancy is null
            ? null
            : await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking()
                .SingleOrDefaultAsync(x => x.OccupancyId == occupancy.Id && x.Kind == "Deposit", cancellationToken);
        var canShareTenantLink = occupancy is not null && await tokenService.CanShareAsync(building.OwnerId, webhookService, cancellationToken);
        return BuildRoomDto(tokenService, room, building, occupancy, period, readings, maxFinal, deposit, canShareTenantLink, maxReading);
    }

    internal static RoomDto BuildRoomDto(TenantTokenService tokenService, Room room, Building building, Occupancy? occupancy, string period,
        IReadOnlyList<MonthlyReading> readings, decimal maxFinalReading, Invoice? deposit, bool canShareTenantLink, decimal? maxPhysicalReading = null)
    {
        // The physical room can have one reading per occupancy in the same month.
        // Prefer the active occupancy's reading for the room's current-reading field;
        // when it has none yet, use only an earlier period as the prior reading.
        var currentPeriodReadings = readings.Where(row => row.Period == period);
        var currentReading = occupancy is null
            ? currentPeriodReadings.FirstOrDefault()
            : currentPeriodReadings.SingleOrDefault(row => row.OccupancyId == occupancy.Id);
        var priorReading = readings.FirstOrDefault(row => string.CompareOrdinal(row.Period, period) < 0);
        var previousReading = currentReading is not null ? currentReading.PreviousReading : priorReading?.CurrentReading ?? room.InitialReading;
        if (currentReading is null)
        {
            var maxReading = maxPhysicalReading ?? (readings.Count > 0 ? readings.Max(r => r.CurrentReading) : room.InitialReading);
            var baseline = Math.Max(Math.Max(room.InitialReading, maxReading), maxFinalReading);
            previousReading = Math.Max(previousReading, baseline);
        }
        OccupancyDto? occupancyDto = null;
        if (occupancy is not null)
        {
            var token = canShareTenantLink ? tokenService.Unprotect(occupancy) : null;
            occupancyDto = new OccupancyDto(occupancy.Id, occupancy.TenantName, occupancy.Phone, occupancy.People, occupancy.StartDate,
                token, token is null ? null : tokenService.Url(token), deposit?.Id, deposit?.Total ?? 0, deposit?.Paid ?? 0, deposit is null ? 0 : Math.Max(0, deposit.Paid - deposit.DepositSettlements.Sum(x => x.Amount)));
        }
        return new RoomDto(room.Id, room.BuildingId, building.Name, room.Name, room.Rent, room.ElectricityRate, room.WaterFee,
            room.WaterPerPerson, room.ServiceFee, room.InitialReading, previousReading, occupancyDto);
    }

    private static void ValidateFees(string name, decimal rent, decimal electricityRate, decimal waterFee, decimal serviceFee, decimal initialReading)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ApiException(StatusCodes.Status400BadRequest, "Room name is required and must be 100 characters or fewer.");
        if (rent < 0 || electricityRate < 0 || waterFee < 0 || serviceFee < 0 || initialReading < 0 ||
            rent > 1_000_000_000 || electricityRate > 10_000_000 || waterFee > 100_000_000 || serviceFee > 100_000_000 || initialReading > 1_000_000_000)
            throw new ApiException(StatusCodes.Status400BadRequest, "Room fees and meter readings must be non-negative values within supported limits.");
    }
}

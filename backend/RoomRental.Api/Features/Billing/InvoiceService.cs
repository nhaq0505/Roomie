using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class InvoiceService(AppDbContext db)
{
    public async Task<(int Created, int Skipped)> GenerateForOwnerAsync(Guid ownerId, string period, CancellationToken cancellationToken = default)
    {
        await using var ownedTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
        var alreadyInvoiced = await db.Invoices.AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.Period == period && x.Kind == "Monthly")
            .Select(x => x.OccupancyId)
            .ToListAsync(cancellationToken);
        var invoicedOccupancies = alreadyInvoiced.ToHashSet();

        var eligible = await (from reading in db.Readings.AsNoTracking()
                              join room in db.Rooms on reading.RoomId equals room.Id
                              join building in db.Buildings on room.BuildingId equals building.Id
                              join occupancy in db.Occupancies on reading.OccupancyId equals occupancy.Id
                              where building.OwnerId == ownerId && building.ArchivedAt == null && room.ArchivedAt == null &&
                                    occupancy.IsActive && reading.Period == period &&
                                    !db.Invoices.Any(invoice => invoice.OccupancyId == occupancy.Id && invoice.IsFinalMeterReading)
                              select new { Reading = reading, Room = room, Building = building, Occupancy = occupancy })
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var row in eligible)
        {
            if (!invoicedOccupancies.Add(row.Occupancy.Id)) continue;
            // Legacy readings have no fee/date snapshot. Leave them alone instead of
            // deriving historical charges from today's room settings during a sweep.
            if (row.Reading.MonthlyRentRate is null || row.Reading.ElectricityRate is null ||
                row.Reading.WaterRate is null || row.Reading.WaterPerPerson is null ||
                row.Reading.ServiceRate is null || row.Reading.BillingStartDate is null ||
                row.Reading.BillingEndDate is null ||
                (row.Reading.WaterPerPerson == true && row.Reading.OccupantsCount is null)) continue;

            db.Invoices.Add(CreateMonthlyInvoice(ownerId, row.Room, row.Building, row.Occupancy, row.Reading, InvoiceLifecycleState.Issued));
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(cancellationToken);
        if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
        return (created, eligible.Count - created);
        }
        catch
        {
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<(int Created, int Skipped)> GenerateForAllApprovedOwnersAsync(string period, CancellationToken cancellationToken = default)
    {
        var owners = await db.Users.AsNoTracking()
            .Where(user => user.Role == UserRole.Owner && user.Status == OwnerStatus.Approved)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        var created = 0;
        var skipped = 0;
        foreach (var ownerId in owners)
        {
            var result = await GenerateForOwnerAsync(ownerId, period, cancellationToken);
            created += result.Created;
            skipped += result.Skipped;
        }
        return (created, skipped);
    }

    public static Invoice CreateMonthlyInvoice(Guid ownerId, Room room, Building building, Occupancy occupancy,
        MonthlyReading reading, InvoiceLifecycleState lifecycleState)
    {
        if (reading.MonthlyRentRate is null || reading.ElectricityRate is null || reading.WaterRate is null ||
            reading.WaterPerPerson is null || reading.ServiceRate is null || reading.BillingStartDate is null ||
            reading.BillingEndDate is null || reading.BillableDays is null || reading.MonthDays is null ||
            (reading.WaterPerPerson == true && reading.OccupantsCount is null))
            throw new ApiException(409, "Bảng phí đã chốt không đầy đủ. Không thể tạo hóa đơn tự động.");

        var rent = ApiSupport.RoundVnd(reading.MonthlyRentRate.Value);
        var electricity = ApiSupport.RoundVnd(reading.Consumption * reading.ElectricityRate.Value);
        var water = ApiSupport.RoundVnd(reading.WaterPerPerson.Value
            ? reading.WaterRate.Value * reading.OccupantsCount!.Value
            : reading.WaterRate.Value);
        var service = ApiSupport.RoundVnd(reading.ServiceRate.Value);
        var total = ValidateTotal(rent, electricity, water, service);
        return new Invoice
        {
            OwnerId = ownerId,
            RoomId = room.Id,
            BuildingId = building.Id,
            OccupancyId = occupancy.Id,
            RoomName = room.Name,
            BuildingName = building.Name,
            Period = reading.Period,
            TenantName = occupancy.TenantName,
            PreviousReading = reading.PreviousReading,
            CurrentReading = reading.CurrentReading,
            Consumption = reading.Consumption,
            Rent = rent,
            ElectricityAmount = electricity,
            WaterAmount = water,
            ServiceAmount = service,
            Total = total,
            Paid = 0,
            Status = total == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid,
            LifecycleState = lifecycleState,
            DueDate = ApiSupport.PeriodDueDate(reading.Period),
            IssuedAt = DateTimeOffset.UtcNow,
            MonthlyRentRate = reading.MonthlyRentRate,
            ElectricityRate = reading.ElectricityRate,
            WaterRate = reading.WaterRate,
            WaterPerPerson = reading.WaterPerPerson,
            ServiceRate = reading.ServiceRate,
            BillingStartDate = reading.BillingStartDate,
            BillingEndDate = reading.BillingEndDate,
            BillableDays = reading.BillableDays,
            MonthDays = reading.MonthDays
        };
    }

    public static (DateOnly Start, DateOnly End, int MonthDays) PeriodDates(string period)
    {
        var year = int.Parse(period.AsSpan(0, 4));
        var month = int.Parse(period.AsSpan(5, 2));
        var start = new DateOnly(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1), DateTime.DaysInMonth(year, month));
    }

    public static int InclusiveDays(DateOnly start, DateOnly end) => Math.Max(0, end.DayNumber - start.DayNumber + 1);

    public static decimal ValidateTotal(params decimal[] amounts)
    {
        if (amounts.Any(amount => amount < 0 || amount != ApiSupport.RoundVnd(amount)))
            throw new ApiException(400, "Khoản thu phải là số VND nguyên không âm.");
        var total = amounts.Sum();
        if (total > int.MaxValue) throw new ApiException(400, "Tổng tiền vượt giới hạn thanh toán.");
        return total;
    }

    public static InvoiceDto ToDto(Invoice invoice, bool hasPendingPayment = false, bool canCorrectMeter = false, string? meterCorrectionBlockedReason = null, string? tenantToken = null) => new(
        invoice.Id,
        invoice.RoomId,
        invoice.RoomName,
        invoice.BuildingName,
        invoice.Period,
        invoice.TenantName,
        invoice.PreviousReading,
        invoice.CurrentReading,
        invoice.Consumption,
        invoice.Rent,
        invoice.ElectricityAmount,
        invoice.WaterAmount,
        invoice.ServiceAmount,
        invoice.Total,
        invoice.Paid,
        Math.Max(0, invoice.Total - invoice.Paid),
        invoice.Status.ToString(),
        invoice.DueDate,
        invoice.IssuedAt, invoice.Kind, invoice.OccupancyId, invoice.Kind == "Deposit" ? Math.Max(0, invoice.Paid - invoice.DepositSettlements.Sum(x => x.Amount)) : 0,
        invoice.DepositSettlements, invoice.Description, invoice.IsFinalMeterReading, invoice.LifecycleState.ToString(), invoice.Version, invoice.BuildingId,
        invoice.MonthlyRentRate, invoice.ElectricityRate, invoice.WaterRate, invoice.WaterPerPerson, invoice.ServiceRate,
        invoice.BillingStartDate, invoice.BillingEndDate, invoice.BillableDays, invoice.MonthDays, hasPendingPayment, canCorrectMeter, meterCorrectionBlockedReason, tenantToken);

    public static void RecalculateStatus(Invoice invoice)
    {
        invoice.Paid = ApiSupport.RoundVnd(invoice.Paid);
        invoice.Status = invoice.Paid >= invoice.Total ? InvoiceStatus.Paid
            : invoice.Paid > 0 ? InvoiceStatus.Partial
            : InvoiceStatus.Unpaid;
    }
}

public sealed class InvoiceSweepService(IServiceScopeFactory scopeFactory, ILogger<InvoiceSweepService> logger, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<InvoiceService>();
                var period = timeProvider.GetUtcNow().ToOffset(VietnamTimeZone.GetUtcOffset(timeProvider.GetUtcNow())).ToString("yyyy-MM");
                var result = await service.GenerateForAllApprovedOwnersAsync(period, stoppingToken);
                if (result.Created > 0) logger.LogInformation("Published {Count} invoices for {Period} by daily sweep.", result.Created, period);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Daily invoice sweep failed."); }

            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}

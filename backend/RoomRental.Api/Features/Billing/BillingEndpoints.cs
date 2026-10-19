using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api").RequireAuthorization(policy => policy.RequireRole("Owner"));

        MapSubscriptionRoutes(routes);
        MapReadingsRoutes(routes);
        MapInvoiceListRoutes(routes);
        MapCorrectionsRoutes(routes);
        MapReceiptsRoutes(routes);
    }

    private sealed record ReadingWork(Room Room, Building Building, Occupancy Occupancy, string Period,
        decimal PreviousReading, decimal CurrentReading, decimal Consumption, DateOnly BillingStartDate,
        DateOnly BillingEndDate, int BillableDays, int MonthDays);

    private static async Task<ReadingWork> PrepareReadingAsync(AppDbContext db, Guid ownerId, ReadingRequest request, CancellationToken ct)
    {
        var period = ApiSupport.NormalizePeriod(request.Period);
        if (request.CurrentReading < 0 || request.CurrentReading > 1_000_000_000 || decimal.Round(request.CurrentReading, 3) != request.CurrentReading)
            throw new ApiException(400, "Chỉ số điện phải là số không âm tối đa ba chữ số thập phân trong giới hạn cho phép.");
        var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, request.RoomId, ct);
        if (room.ArchivedAt is not null || building.ArchivedAt is not null)
            throw new ApiException(409, "Không thể ghi chỉ số cho phòng hoặc dãy đã lưu trữ.");
        var occupancy = await db.Occupancies.SingleOrDefaultAsync(row => row.RoomId == room.Id && row.IsActive, ct)
            ?? throw new ApiException(409, "Cần có người thuê đang ở để ghi chỉ số điện.");
        if (await db.Invoices.AnyAsync(x => x.OccupancyId == occupancy.Id && x.IsFinalMeterReading, ct))
            throw new ApiException(409, "Đã chốt điện trả phòng; không thể phát hành thêm hóa đơn tháng cho lượt thuê này.");
        var (periodStart, periodEnd, monthDays) = InvoiceService.PeriodDates(period);
        if (periodStart > VietnamCalendar.TodayVietnam())
            throw new ApiException(400, "Không thể nhập chỉ số cho tháng trong tương lai.");
        if (string.CompareOrdinal(period, occupancy.StartDate.ToString("yyyy-MM")) < 0)
            throw new ApiException(400, "Tháng ghi chỉ số không thể trước tháng bắt đầu thuê.");
        if (await db.Readings.AnyAsync(row => row.OccupancyId == occupancy.Id && row.Period == period, ct))
            throw new ApiException(409, "Lượt thuê đã có chỉ số điện cho tháng này.");
        if (await db.Invoices.AnyAsync(row => row.OccupancyId == occupancy.Id && row.Period == period && row.Kind == "Monthly", ct))
            throw new ApiException(409, "Hóa đơn của lượt thuê trong tháng này đã được tạo.");

        var priorReadings = await db.Readings.AsNoTracking().Where(row => row.RoomId == room.Id)
            .OrderByDescending(row => row.Period).ThenByDescending(row => row.RecordedAt).ToListAsync(ct);
        var newest = priorReadings.FirstOrDefault();
        if (newest is not null && string.CompareOrdinal(newest.Period, period) > 0)
            throw new ApiException(409, "Vui lòng nhập chỉ số theo thứ tự tháng.");

        // Equal-period readings are valid when a different occupancy moved into the
        // same physical room later that month. MeterBaselineAsync carries the old
        // tenant's final anchor forward instead of resetting the room meter.
        var previousReading = Math.Max(newest?.CurrentReading ?? room.InitialReading,
            await MoveoutEndpoints.MeterBaselineAsync(db, room, ct));
        if (request.CurrentReading < previousReading)
            throw new ApiException(400, $"Chỉ số mới không thể thấp hơn chỉ số trước ({previousReading}).");

        var days = monthDays;
        return new ReadingWork(room, building, occupancy, period, previousReading, request.CurrentReading,
            request.CurrentReading - previousReading, periodStart, periodEnd, days, monthDays);
    }

    private static decimal ValidateCharge(decimal amount)
    {
        if (amount < 0 || amount > int.MaxValue || amount != decimal.Truncate(amount))
            throw new ApiException(400, "Khoản thu phải là số VND nguyên từ 0 đến giới hạn thanh toán.");
        return amount;
    }

    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    internal static string SerializeSnapshot(Invoice invoice) => JsonSerializer.Serialize(new
    {
        invoice.Id,
        version = invoice.Version,
        lifecycleState = invoice.LifecycleState.ToString(),
        invoice.Kind,
        invoice.Period,
        invoice.BuildingId,
        invoice.RoomId,
        invoice.OccupancyId,
        invoice.Rent,
        invoice.ElectricityAmount,
        invoice.WaterAmount,
        invoice.ServiceAmount,
        invoice.Total,
        invoice.CurrentReading,
        invoice.PreviousReading,
        invoice.Consumption,
        invoice.DueDate,
        invoice.Description,
        invoice.BillingStartDate,
        invoice.BillingEndDate,
        invoice.BillableDays,
        invoice.MonthDays,
        invoice.MonthlyRentRate,
        invoice.ElectricityRate,
        invoice.WaterRate,
        invoice.WaterPerPerson,
        invoice.ServiceRate
    }, AuditJsonOptions);

    internal static async Task<InvoiceRevision> AddRevisionAsync(AppDbContext db, Invoice invoice, Guid requestId, string fingerprint,
        string reason, Guid actorId, string before, string after, CancellationToken ct)
    {
        var actorName = await db.Users.AsNoTracking().Where(x => x.Id == actorId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            ?? "Unknown user";
        var lastRevision = await db.InvoiceRevisions.Where(x => x.InvoiceId == invoice.Id)
            .OrderByDescending(x => x.Revision).Select(x => (int?)x.Revision).FirstOrDefaultAsync(ct) ?? 0;
        var revision = new InvoiceRevision
        {
            InvoiceId = invoice.Id,
            Revision = checked(lastRevision + 1),
            RequestId = requestId,
            RequestFingerprint = fingerprint,
            Reason = reason,
            ActorId = actorId,
            ActorName = actorName,
            CreatedAt = DateTimeOffset.UtcNow,
            Version = invoice.Version,
            BeforeJson = before,
            AfterJson = after
        };
        db.InvoiceRevisions.Add(revision);
        return revision;
    }

    private static InvoiceCorrectionDto ToCorrectionDto(InvoiceRevision revision) => new(revision.Id, revision.Revision,
        revision.RequestId, revision.Reason, revision.ActorId, revision.ActorName, revision.CreatedAt, revision.Version,
        revision.BeforeJson, revision.AfterJson);

    private static async Task<(bool Allowed, string? Reason)> MeterCorrectionPolicyAsync(AppDbContext db, Invoice invoice, CancellationToken ct)
    {
        if (invoice.LifecycleState != InvoiceLifecycleState.Issued) return (false, "Draft invoices must be published before correction.");
        if (invoice.Paid != 0) return (false, "An invoice with a recorded payment cannot be corrected.");
        if (invoice.Kind != "Monthly" && !invoice.IsFinalMeterReading) return (false, "This invoice has no editable meter anchor.");

        DateTimeOffset anchorAt;
        if (invoice.Kind == "Monthly")
        {
            var reading = await db.Readings.AsNoTracking().SingleOrDefaultAsync(x => x.OccupancyId == invoice.OccupancyId && x.Period == invoice.Period, ct);
            if (reading is null) return (false, "The source meter reading is missing.");
            anchorAt = reading.RecordedAt;
        }
        else anchorAt = invoice.IssuedAt;

        var readingAnchors = await db.Readings.AsNoTracking().Where(x => x.RoomId == invoice.RoomId)
            .Select(x => new { x.Period, x.RecordedAt }).ToListAsync(ct);
        var hasLaterReading = readingAnchors.Any(x => string.CompareOrdinal(x.Period, invoice.Period) > 0 ||
            (x.Period == invoice.Period && x.RecordedAt > anchorAt));
        if (hasLaterReading) return (false, "A later meter reading already anchors this room. Correcting this reading is blocked.");
        var finalAnchors = await db.Invoices.AsNoTracking().Where(x => x.RoomId == invoice.RoomId && x.IsFinalMeterReading && x.Id != invoice.Id)
            .Select(x => new { x.Period, x.IssuedAt }).ToListAsync(ct);
        var hasLaterFinal = finalAnchors.Any(x => string.CompareOrdinal(x.Period, invoice.Period) > 0 ||
            (x.Period == invoice.Period && x.IssuedAt > anchorAt));
        if (hasLaterFinal) return (false, "A later move-out reading already anchors this room. Correcting this reading is blocked.");
        return (true, null);
    }
}

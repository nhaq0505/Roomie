using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class ReportsEndpoints
{
    private const int MaxPageSize = 200;
    private const int MaxExportRows = 20_000;
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    public static void MapReportsEndpoints(this WebApplication app)
    {
        var routes = app.MapGroup("/api/reports").RequireAuthorization(policy => policy.RequireRole("Owner"));

        routes.MapGet("/receipts", async (DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, Guid? roomId,
            int? page, int? pageSize, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var filters = ParseFilters(dateFrom, dateTo, buildingId, roomId, page, pageSize);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            var report = await GetReceiptsAsync(db, ApiSupport.OwnerId(context), filters, export: false, ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(report);
        });

        routes.MapGet("/debt", async (DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, Guid? roomId,
            int? page, int? pageSize, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var filters = ParseFilters(dateFrom, dateTo, buildingId, roomId, page, pageSize);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            var report = await GetDebtAsync(db, ApiSupport.OwnerId(context), filters, export: false, ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(report);
        });

        routes.MapGet("/deposits", async (DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, Guid? roomId,
            int? page, int? pageSize, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var filters = ParseFilters(dateFrom, dateTo, buildingId, roomId, page, pageSize);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            var report = await GetDepositsAsync(db, ApiSupport.OwnerId(context), filters, export: false, ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(report);
        });

        routes.MapGet("/{reportType}/export.xlsx", async (string reportType, DateOnly? dateFrom, DateOnly? dateTo,
            Guid? buildingId, Guid? roomId, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var filters = ParseFilters(dateFrom, dateTo, buildingId, roomId, page: 1, pageSize: MaxPageSize);
            var ownerId = ApiSupport.OwnerId(context);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            using var workbook = new XLWorkbook();
            switch (reportType.ToLowerInvariant())
            {
                case "receipts":
                {
                    var report = await GetReceiptsAsync(db, ownerId, filters, export: true, ct);
                    AddSummarySheet(workbook, "Thu tiền", filters,
                        ("Thu hóa đơn", report.Totals.InvoiceCash),
                        ("Thu tiền cọc", report.Totals.DepositCash),
                        ("Tổng tiền thu", report.Totals.TotalCash));
                    AddReceiptDetails(workbook, report.Items);
                    break;
                }
                case "debt":
                {
                    var report = await GetDebtAsync(db, ownerId, filters, export: true, ct);
                    AddSummarySheet(workbook, "Công nợ hiện tại", filters,
                        ("Công nợ hiện tại", report.Totals.CurrentOutstanding),
                        ("Công nợ quá hạn", report.Totals.OverdueOutstanding),
                        ("Số hóa đơn còn nợ", report.Totals.InvoiceCount),
                        ("Số hóa đơn quá hạn", report.Totals.OverdueInvoiceCount));
                    AddDebtDetails(workbook, report.Items);
                    break;
                }
                case "deposits":
                {
                    var report = await GetDepositsAsync(db, ownerId, filters, export: true, ct);
                    AddSummarySheet(workbook, "Tiền cọc", filters,
                        ("Tiền cọc nhận trong kỳ", report.Totals.PeriodReceived),
                        ("Tiền cọc hoàn trong kỳ", report.Totals.PeriodRefunded),
                        ("Tiền cọc khấu trừ trong kỳ", report.Totals.PeriodDeducted),
                        ("Tiền cọc đang giữ", report.Totals.CurrentHeld));
                    AddDepositDetails(workbook, report.Items);
                    break;
                }
                default:
                    throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy loại báo cáo.");
            }

            await snapshot.CommitAsync(ct);
            await using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"roomie-{reportType.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";
            return Results.File(stream.ToArray(), XlsxContentType, fileName);
        });
    }

    private static async Task<decimal> SumMoneyAsync(AppDbContext db, IQueryable<decimal> values, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) return await values.SumAsync(ct);
        // SQLite cannot aggregate decimal values. Keep the fallback to one decimal projection per matching row.
        return (await values.ToListAsync(ct)).Sum();
    }

    private static Task<IDbContextTransaction> BeginReadSnapshotAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.BeginTransactionAsync(db.Database.IsNpgsql()
            ? System.Data.IsolationLevel.RepeatableRead
            : System.Data.IsolationLevel.Serializable, ct);

    private static ReportFilters ParseFilters(DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, Guid? roomId,
        int? page, int? pageSize)
    {
        if (dateFrom is DateOnly fromDate && dateTo is DateOnly toDate && fromDate > toDate)
            throw new ApiException(StatusCodes.Status400BadRequest, "dateFrom must be on or before dateTo.");
        if (page is < 1 or > 1_000_000)
            throw new ApiException(StatusCodes.Status400BadRequest, "page must be between 1 and 1,000,000.");
        if (pageSize is < 1 or > MaxPageSize)
            throw new ApiException(StatusCodes.Status400BadRequest, $"pageSize must be between 1 and {MaxPageSize}.");
        if (dateTo == DateOnly.MaxValue)
            throw new ApiException(StatusCodes.Status400BadRequest, "dateTo is outside the supported range.");

        var fromUtc = dateFrom is DateOnly start ? StartOfVietnamDate(start) : (DateTimeOffset?)null;
        var beforeToUtc = dateTo is DateOnly end ? StartOfVietnamDate(end.AddDays(1)) : (DateTimeOffset?)null;
        var periodFrom = dateFrom?.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var periodTo = dateTo?.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var actualPage = page ?? 1;
        var actualPageSize = pageSize ?? 50;
        if ((long)(actualPage - 1) * actualPageSize > int.MaxValue)
            throw new ApiException(StatusCodes.Status400BadRequest, "The requested page is too large.");
        return new ReportFilters(dateFrom, dateTo, fromUtc, beforeToUtc, buildingId, roomId,
            actualPage, actualPageSize, (actualPage - 1) * actualPageSize, periodFrom, periodTo);
    }

    private static DateTimeOffset StartOfVietnamDate(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, VietnamTimeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static bool IsInSelectedDates(DateTimeOffset value, ReportFilters filters) =>
        (filters.FromUtc is null || value >= filters.FromUtc.Value) &&
        (filters.BeforeToUtc is null || value < filters.BeforeToUtc.Value);

    private static DateTimeOffset ParseRecordedAt(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return DateTimeOffset.MinValue;
    }

    private static void EnsureExportSize(long count)
    {
        if (count > MaxExportRows)
            throw new ApiException(StatusCodes.Status413PayloadTooLarge,
                $"Excel export contains {count:N0} rows. Add filters to reduce it to {MaxExportRows:N0} rows or fewer.");
    }


}

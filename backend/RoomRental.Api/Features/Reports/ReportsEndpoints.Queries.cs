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
    private static async Task<PagedReportDto<ReceiptReportRow, ReceiptReportTotals>> GetReceiptsAsync(AppDbContext db, Guid ownerId,
        ReportFilters filters, bool export, CancellationToken ct)
    {
        var query = from receipt in db.Receipts.AsNoTracking()
                    join invoice in db.Invoices.AsNoTracking() on receipt.InvoiceId equals invoice.Id
                    where receipt.OwnerId == ownerId && invoice.OwnerId == ownerId &&
                          invoice.LifecycleState == InvoiceLifecycleState.Issued && receipt.Source != "Deposit"
                    select new { Receipt = receipt, Invoice = invoice };
        if (filters.BuildingId is Guid buildingId) query = query.Where(row => row.Invoice.BuildingId == buildingId);
        if (filters.RoomId is Guid roomId) query = query.Where(row => row.Invoice.RoomId == roomId);
        if (filters.FromUtc is DateTimeOffset from) query = query.Where(row => row.Receipt.CreatedAt >= from);
        if (filters.BeforeToUtc is DateTimeOffset beforeTo) query = query.Where(row => row.Receipt.CreatedAt < beforeTo);

        var totalCount = await query.LongCountAsync(ct);
        if (export) EnsureExportSize(totalCount);
        var invoiceCash = await SumMoneyAsync(db, query.Where(row => row.Invoice.Kind != "Deposit").Select(row => row.Receipt.Amount), ct);
        var depositCash = await SumMoneyAsync(db, query.Where(row => row.Invoice.Kind == "Deposit").Select(row => row.Receipt.Amount), ct);
        var totals = new ReceiptReportTotals(invoiceCash, depositCash, invoiceCash + depositCash);
        var ordered = query.OrderByDescending(row => row.Receipt.CreatedAt).ThenBy(row => row.Receipt.Id)
            .Select(row => new ReceiptReportRow(row.Receipt.Id, row.Receipt.CreatedAt, row.Receipt.Amount,
                row.Receipt.Source, row.Receipt.Note, row.Invoice.Id, row.Invoice.Kind, row.Invoice.Period,
                row.Invoice.OccupancyId, row.Invoice.RoomId, row.Invoice.RoomName, row.Invoice.BuildingId,
                row.Invoice.BuildingName, row.Invoice.TenantName));
        var items = export
            ? await ordered.ToListAsync(ct)
            : await ordered.Skip(filters.Offset).Take(filters.PageSize).ToListAsync(ct);
        return new PagedReportDto<ReceiptReportRow, ReceiptReportTotals>(items, filters.Page, filters.PageSize, totalCount, totals);
    }

    private static async Task<PagedReportDto<DebtReportRow, DebtReportTotals>> GetDebtAsync(AppDbContext db, Guid ownerId,
        ReportFilters filters, bool export, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking().Where(invoice => invoice.OwnerId == ownerId &&
            invoice.LifecycleState == InvoiceLifecycleState.Issued && invoice.Total > invoice.Paid);
        if (filters.BuildingId is Guid buildingId) query = query.Where(invoice => invoice.BuildingId == buildingId);
        if (filters.RoomId is Guid roomId) query = query.Where(invoice => invoice.RoomId == roomId);
        if (filters.PeriodFrom is string fromPeriod) query = query.Where(invoice => invoice.Period.CompareTo(fromPeriod) >= 0);
        if (filters.PeriodTo is string toPeriod) query = query.Where(invoice => invoice.Period.CompareTo(toPeriod) <= 0);

        var totalCount = await query.LongCountAsync(ct);
        if (export) EnsureExportSize(totalCount);
        var today = VietnamCalendar.TodayVietnam();
        var overdueQuery = query.Where(invoice => invoice.DueDate < today);
        var overdueCount = await overdueQuery.LongCountAsync(ct);
        var totals = new DebtReportTotals(
            await SumMoneyAsync(db, query.Select(invoice => invoice.Total - invoice.Paid), ct),
            await SumMoneyAsync(db, overdueQuery.Select(invoice => invoice.Total - invoice.Paid), ct),
            totalCount,
            overdueCount);
        var ordered = query.OrderByDescending(invoice => invoice.Period).ThenBy(invoice => invoice.Id)
            .Select(invoice => new DebtReportRow(invoice.Id, invoice.Period, invoice.Kind, invoice.IssuedAt,
                invoice.DueDate, invoice.RoomId, invoice.RoomName, invoice.BuildingId, invoice.BuildingName,
                invoice.OccupancyId, invoice.TenantName, invoice.Total, invoice.Paid,
                invoice.Total - invoice.Paid));
        var items = export
            ? await ordered.ToListAsync(ct)
            : await ordered.Skip(filters.Offset).Take(filters.PageSize).ToListAsync(ct);
        return new PagedReportDto<DebtReportRow, DebtReportTotals>(items, filters.Page, filters.PageSize, totalCount, totals);
    }

    private static async Task<PagedReportDto<DepositReportRow, DepositReportTotals>> GetDepositsAsync(AppDbContext db, Guid ownerId,
        ReportFilters filters, bool export, CancellationToken ct)
    {
        var invoiceQuery = db.Invoices.AsNoTracking().Where(invoice => invoice.OwnerId == ownerId &&
            invoice.LifecycleState == InvoiceLifecycleState.Issued && invoice.Kind == "Deposit");
        if (filters.BuildingId is Guid buildingId) invoiceQuery = invoiceQuery.Where(invoice => invoice.BuildingId == buildingId);
        if (filters.RoomId is Guid roomId) invoiceQuery = invoiceQuery.Where(invoice => invoice.RoomId == roomId);

        var totalCount = await invoiceQuery.LongCountAsync(ct);
        if (export) EnsureExportSize(totalCount);
        var settlementQuery = from settlement in db.DepositSettlements.AsNoTracking()
                              join invoice in invoiceQuery on settlement.InvoiceId equals invoice.Id
                              select settlement;
        var periodSettlementQuery = settlementQuery;
        if (filters.FromUtc is DateTimeOffset settlementFrom)
        {
            var lowerBound = settlementFrom.ToString("O", CultureInfo.InvariantCulture);
            periodSettlementQuery = periodSettlementQuery.Where(row => row.RecordedAt.CompareTo(lowerBound) >= 0);
        }
        if (filters.BeforeToUtc is DateTimeOffset settlementBeforeTo)
        {
            var upperBound = settlementBeforeTo.ToString("O", CultureInfo.InvariantCulture);
            periodSettlementQuery = periodSettlementQuery.Where(row => row.RecordedAt.CompareTo(upperBound) < 0);
        }
        var receiptQuery = from receipt in db.Receipts.AsNoTracking()
                           join invoice in invoiceQuery on receipt.InvoiceId equals invoice.Id
                           where receipt.OwnerId == ownerId && receipt.Source != "Deposit"
                           select receipt;
        if (filters.FromUtc is DateTimeOffset from) receiptQuery = receiptQuery.Where(row => row.CreatedAt >= from);
        if (filters.BeforeToUtc is DateTimeOffset beforeTo) receiptQuery = receiptQuery.Where(row => row.CreatedAt < beforeTo);
        var heldQuery = from invoice in invoiceQuery
                        let settledAmount = db.DepositSettlements.Where(settlement => settlement.InvoiceId == invoice.Id)
                            .Select(settlement => (decimal?)settlement.Amount).Sum() ?? 0m
                        select invoice.Paid > settledAmount ? invoice.Paid - settledAmount : 0m;
        var currentHeld = await SumMoneyAsync(db, heldQuery, ct);
        var totals = new DepositReportTotals(
            await SumMoneyAsync(db, receiptQuery.Select(row => row.Amount), ct),
            await SumMoneyAsync(db, periodSettlementQuery.Where(row => row.Kind == "Refund").Select(row => row.Amount), ct),
            await SumMoneyAsync(db, periodSettlementQuery.Where(row => row.Kind == "Deduction").Select(row => row.Amount), ct),
            currentHeld);

        var ordered = invoiceQuery.OrderByDescending(invoice => invoice.Period).ThenBy(invoice => invoice.Id);
        IQueryable<Invoice> pageQuery = export ? ordered : ordered.Skip(filters.Offset).Take(filters.PageSize);
        var pageInvoices = await pageQuery.ToListAsync(ct);
        var pageInvoiceIds = pageInvoices.Select(invoice => invoice.Id).ToList();
        var pageSettlements = pageInvoiceIds.Count == 0
            ? []
            : await db.DepositSettlements.AsNoTracking().Where(row => pageInvoiceIds.Contains(row.InvoiceId))
                .Select(row => new { row.InvoiceId, row.Amount, row.Kind, row.RecordedAt }).ToListAsync(ct);
        var pageReceipts = pageInvoiceIds.Count == 0
            ? []
            : await receiptQuery.Where(row => pageInvoiceIds.Contains(row.InvoiceId))
                .Select(row => new { row.InvoiceId, row.Amount }).ToListAsync(ct);
        var pageSettledByInvoice = pageSettlements.GroupBy(row => row.InvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var heldByInvoice = pageInvoices.ToDictionary(invoice => invoice.Id,
            invoice => Math.Max(0, invoice.Paid - pageSettledByInvoice.GetValueOrDefault(invoice.Id)));
        var periodReceivedByInvoice = pageReceipts.GroupBy(row => row.InvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var inPeriodSettlements = pageSettlements.Where(row => IsInSelectedDates(ParseRecordedAt(row.RecordedAt), filters));
        var periodRefundedByInvoice = inPeriodSettlements.Where(row => row.Kind == "Refund").GroupBy(row => row.InvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var periodDeductedByInvoice = inPeriodSettlements.Where(row => row.Kind == "Deduction").GroupBy(row => row.InvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var items = pageInvoices.Select(invoice => new DepositReportRow(invoice.Id, invoice.Period, invoice.RoomId,
            invoice.RoomName, invoice.BuildingId, invoice.BuildingName, invoice.OccupancyId, invoice.TenantName,
            invoice.Total, periodReceivedByInvoice.GetValueOrDefault(invoice.Id),
            periodRefundedByInvoice.GetValueOrDefault(invoice.Id), periodDeductedByInvoice.GetValueOrDefault(invoice.Id),
            heldByInvoice.GetValueOrDefault(invoice.Id))).ToList();
        return new PagedReportDto<DepositReportRow, DepositReportTotals>(items, filters.Page, filters.PageSize, totalCount, totals);
    }


}

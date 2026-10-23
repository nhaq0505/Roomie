using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

/// <summary>
/// Pro-only profit/loss and EVN electricity reconciliation. Income uses exactly the "invoice cash" definition of the
/// receipts report (non-deposit receipts on issued non-deposit invoices, by Vietnam receipt date) so both reports agree.
/// </summary>
public static partial class ReportsEndpoints
{
    private const string ProfitLossProMessage = "Báo cáo lãi/lỗ dành cho gói Pro. Vui lòng nâng cấp để sử dụng.";
    private const string ElectricityProMessage = "Đối chiếu điện EVN dành cho gói Pro. Vui lòng nâng cấp để sử dụng.";
    // Vietnam has used a fixed UTC+7 offset without daylight saving since 1975.
    private const int VietnamOffsetHours = 7;

    private static void MapProfitLossRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/profit-loss", async (DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId, HttpContext context,
            AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await ExpenseSupport.EnsureProAsync(db, ownerId, clock.GetUtcNow(), ct, ProfitLossProMessage);
            var filters = ParseProfitLossFilters(dateFrom, dateTo, buildingId);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            var report = await GetProfitLossAsync(db, ownerId, filters, ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(report);
        });

        routes.MapGet("/electricity-reconciliation", async (string? periodFrom, string? periodTo, Guid? buildingId,
            HttpContext context, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await ExpenseSupport.EnsureProAsync(db, ownerId, clock.GetUtcNow(), ct, ElectricityProMessage);
            var (from, to) = ParsePeriodRange(periodFrom, periodTo);
            await using var snapshot = await BeginReadSnapshotAsync(db, ct);
            var report = await GetElectricityReconciliationAsync(db, ownerId, from, to, buildingId, ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(report);
        });
    }

    private static ReportFilters ParseProfitLossFilters(DateOnly? dateFrom, DateOnly? dateTo, Guid? buildingId)
    {
        if (dateFrom is not DateOnly from || dateTo is not DateOnly to)
            throw new ApiException(StatusCodes.Status400BadRequest, "dateFrom and dateTo are required.");
        var filters = ParseFilters(from, to, buildingId, null, page: 1, pageSize: MaxPageSize);
        if (ExpenseSupport.MonthSpan(from.ToString("yyyy-MM", CultureInfo.InvariantCulture), to.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            > ExpenseLimits.MaxReportMonths)
            throw new ApiException(StatusCodes.Status400BadRequest, "Khoảng báo cáo tối đa 24 tháng.");
        return filters;
    }

    private static (string From, string To) ParsePeriodRange(string? periodFrom, string? periodTo)
    {
        var from = ApiSupport.NormalizePeriod(periodFrom);
        var to = ApiSupport.NormalizePeriod(periodTo);
        if (string.CompareOrdinal(from, to) > 0)
            throw new ApiException(StatusCodes.Status400BadRequest, "periodFrom must be on or before periodTo.");
        if (ExpenseSupport.MonthSpan(from, to) > ExpenseLimits.MaxReportMonths)
            throw new ApiException(StatusCodes.Status400BadRequest, "Khoảng báo cáo tối đa 24 tháng.");
        return (from, to);
    }

    private static IQueryable<Expense> FilteredExpenses(AppDbContext db, Guid ownerId, ReportFilters filters)
    {
        var from = filters.DateFrom!.Value;
        var to = filters.DateTo!.Value;
        var query = db.Expenses.AsNoTracking().Where(row => row.OwnerId == ownerId && row.DeletedAt == null &&
            row.ExpenseDate >= from && row.ExpenseDate <= to);
        if (filters.BuildingId is Guid buildingId) query = query.Where(row => row.BuildingId == buildingId);
        return query;
    }

    private static async Task<ProfitLossReportDto> GetProfitLossAsync(AppDbContext db, Guid ownerId, ReportFilters filters, CancellationToken ct)
    {
        var receipts = from receipt in db.Receipts.AsNoTracking()
                       join invoice in db.Invoices.AsNoTracking() on receipt.InvoiceId equals invoice.Id
                       where receipt.OwnerId == ownerId && invoice.OwnerId == ownerId &&
                             invoice.LifecycleState == InvoiceLifecycleState.Issued && receipt.Source != "Deposit" &&
                             invoice.Kind != "Deposit" && receipt.CreatedAt >= filters.FromUtc!.Value && receipt.CreatedAt < filters.BeforeToUtc!.Value
                       select new { receipt.CreatedAt, receipt.Amount, invoice.BuildingId };
        if (filters.BuildingId is Guid buildingId) receipts = receipts.Where(row => row.BuildingId == buildingId);

        var incomeRows = await receipts
            .Select(row => new { Local = row.CreatedAt.AddHours(VietnamOffsetHours), row.Amount })
            .GroupBy(row => new { row.Local.Year, row.Local.Month })
            .Select(group => new { group.Key.Year, group.Key.Month, Amount = group.Sum(row => row.Amount) })
            .ToListAsync(ct);

        var expenses = FilteredExpenses(db, ownerId, filters);
        var expenseRows = await expenses
            .GroupBy(row => new { row.ExpenseDate.Year, row.ExpenseDate.Month })
            .Select(group => new { group.Key.Year, group.Key.Month, Amount = group.Sum(row => row.Amount) })
            .ToListAsync(ct);
        var byCategory = await expenses.GroupBy(row => row.Category)
            .Select(group => new ExpenseCategoryTotal(group.Key, group.Sum(row => row.Amount), group.LongCount()))
            .ToListAsync(ct);

        var sharedExcluded = 0m;
        if (filters.BuildingId is not null)
        {
            var from = filters.DateFrom!.Value;
            var to = filters.DateTo!.Value;
            sharedExcluded = await db.Expenses.AsNoTracking()
                .Where(row => row.OwnerId == ownerId && row.DeletedAt == null && row.BuildingId == null &&
                              row.ExpenseDate >= from && row.ExpenseDate <= to)
                .SumAsync(row => (decimal?)row.Amount, ct) ?? 0m;
        }

        var income = incomeRows.ToDictionary(row => $"{row.Year:D4}-{row.Month:D2}", row => row.Amount);
        var spent = expenseRows.ToDictionary(row => $"{row.Year:D4}-{row.Month:D2}", row => row.Amount);
        var months = new List<ProfitLossMonth>();
        var lastPeriod = filters.DateTo!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        for (var period = filters.DateFrom!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture);
             string.CompareOrdinal(period, lastPeriod) <= 0; period = ExpenseSupport.AddMonths(period, 1))
        {
            var monthIncome = income.GetValueOrDefault(period);
            var monthExpense = spent.GetValueOrDefault(period);
            months.Add(new ProfitLossMonth(period, monthIncome, monthExpense, monthIncome - monthExpense));
        }
        var totalIncome = months.Sum(row => row.Income);
        var totalExpenses = months.Sum(row => row.Expenses);
        return new ProfitLossReportDto(filters.DateFrom.Value, filters.DateTo.Value, filters.BuildingId,
            new ProfitLossTotals(totalIncome, totalExpenses, totalIncome - totalExpenses), months,
            byCategory.OrderByDescending(row => row.Amount).ThenBy(row => row.Category, StringComparer.Ordinal).ToList(),
            sharedExcluded);
    }

    private static async Task<ElectricityReconciliationDto> GetElectricityReconciliationAsync(AppDbContext db, Guid ownerId,
        string from, string to, Guid? buildingId, CancellationToken ct)
    {
        var evnQuery = db.Expenses.AsNoTracking().Where(row => row.OwnerId == ownerId && row.DeletedAt == null &&
            row.Category == ExpenseCategories.Electricity && row.BuildingId != null && row.UtilityPeriod != null &&
            string.Compare(row.UtilityPeriod, from) >= 0 && string.Compare(row.UtilityPeriod, to) <= 0);
        var invoiceQuery = db.Invoices.AsNoTracking().Where(row => row.OwnerId == ownerId && row.Kind != "Deposit" &&
            string.Compare(row.Period, from) >= 0 && string.Compare(row.Period, to) <= 0);
        if (buildingId is Guid building)
        {
            evnQuery = evnQuery.Where(row => row.BuildingId == building);
            invoiceQuery = invoiceQuery.Where(row => row.BuildingId == building);
        }

        var evn = await evnQuery.GroupBy(row => new { BuildingId = row.BuildingId!.Value, Period = row.UtilityPeriod! })
            .Select(group => new { group.Key.BuildingId, group.Key.Period, Kwh = group.Sum(row => row.UtilityKwh ?? 0m),
                Amount = group.Sum(row => row.Amount), Count = group.Count() })
            .ToListAsync(ct);
        var billed = await invoiceQuery.Where(row => row.LifecycleState == InvoiceLifecycleState.Issued)
            .GroupBy(row => new { row.BuildingId, row.Period })
            .Select(group => new { group.Key.BuildingId, group.Key.Period, Kwh = group.Sum(row => row.Consumption),
                Amount = group.Sum(row => row.ElectricityAmount) })
            .ToListAsync(ct);
        var drafts = await invoiceQuery.Where(row => row.LifecycleState == InvoiceLifecycleState.Draft)
            .GroupBy(row => new { row.BuildingId, row.Period })
            .Select(group => new { group.Key.BuildingId, group.Key.Period, Count = group.Count() })
            .ToListAsync(ct);

        var keys = evn.Select(row => (row.BuildingId, row.Period))
            .Concat(billed.Where(row => row.Kwh != 0 || row.Amount != 0).Select(row => (row.BuildingId, row.Period)))
            .Distinct().ToList();
        var buildingIds = keys.Select(key => key.BuildingId).Distinct().ToList();
        var names = buildingIds.Count == 0 ? new Dictionary<Guid, string>() : await db.Buildings.AsNoTracking()
            .Where(row => row.OwnerId == ownerId && buildingIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, ct);
        var evnByKey = evn.ToDictionary(row => (row.BuildingId, row.Period));
        var billedByKey = billed.ToDictionary(row => (row.BuildingId, row.Period));
        var draftsByKey = drafts.ToDictionary(row => (row.BuildingId, row.Period), row => row.Count);

        var rows = keys.Where(key => names.ContainsKey(key.BuildingId)).Select(key =>
        {
            evnByKey.TryGetValue(key, out var bill);
            billedByKey.TryGetValue(key, out var invoiced);
            var billedKwh = invoiced?.Kwh ?? 0m;
            var billedAmount = invoiced?.Amount ?? 0m;
            decimal? evnKwh = bill?.Kwh;
            decimal? lossKwh = evnKwh is decimal kwh ? kwh - billedKwh : null;
            decimal? lossPercent = evnKwh is decimal total && total > 0 ? decimal.Round((total - billedKwh) / total * 100m, 2) : null;
            var reason = bill is null ? "NoEvnBill" : evnKwh == 0 ? "ZeroEvnKwh" : null;
            return new ElectricityReconciliationRow(key.BuildingId, names[key.BuildingId], key.Period, evnKwh, bill?.Amount ?? 0m,
                bill?.Count ?? 0, billedKwh, billedAmount, lossKwh, lossPercent, reason, billedAmount - (bill?.Amount ?? 0m),
                draftsByKey.GetValueOrDefault(key));
        })
        .OrderByDescending(row => row.Period, StringComparer.Ordinal)
        .ThenBy(row => row.BuildingName, StringComparer.CurrentCulture).ThenBy(row => row.BuildingId)
        .ToList();
        return new ElectricityReconciliationDto(from, to, buildingId, rows);
    }

    private static async Task AddProfitLossWorkbookAsync(XLWorkbook workbook, AppDbContext db, Guid ownerId, ReportFilters filters, CancellationToken ct)
    {
        var report = await GetProfitLossAsync(db, ownerId, filters, ct);
        var expenseQuery = FilteredExpenses(db, ownerId, filters);
        EnsureExportSize(await expenseQuery.LongCountAsync(ct));
        var expenses = await expenseQuery.OrderBy(row => row.ExpenseDate).ThenBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync(ct);
        var (buildings, rooms) = await ExpenseSupport.LoadNamesAsync(db, ownerId, expenses.Select(row => row.BuildingId), expenses.Select(row => row.RoomId), ct);
        var expenseIds = expenses.Select(row => row.Id).ToList();
        var attachmentCounts = expenseIds.Count == 0 ? new Dictionary<Guid, int>() : await db.ExpenseAttachments.AsNoTracking()
            .Where(row => row.OwnerId == ownerId && expenseIds.Contains(row.ExpenseId))
            .GroupBy(row => row.ExpenseId).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, ct);

        AddSummarySheet(workbook, "Lãi/lỗ", filters,
            ("Thực thu (không gồm tiền cọc)", report.Totals.Income),
            ("Chi phí", report.Totals.Expenses),
            ("Lãi ròng", report.Totals.Net),
            ("Chi chung không tính khi lọc dãy", report.SharedExpensesExcluded));

        var monthly = workbook.Worksheets.Add("Theo tháng");
        var monthHeaders = new[] { "Tháng", "Thực thu", "Chi phí", "Lãi ròng" };
        AddHeaders(monthly, monthHeaders);
        for (var index = 0; index < report.Months.Count; index++)
        {
            var row = index + 2;
            var month = report.Months[index];
            SetPeriod(monthly.Cell(row, 1), month.Month);
            SetMoney(monthly.Cell(row, 2), month.Income);
            SetMoney(monthly.Cell(row, 3), month.Expenses);
            SetMoney(monthly.Cell(row, 4), month.Net);
        }
        FinishDetails(monthly, monthHeaders.Length);

        var details = workbook.Worksheets.Add("Chi tiết chi phí");
        var headers = new[] { "Ngày chi", "Danh mục", "Số tiền", "Dãy", "Phòng", "Ghi chú", "Kỳ đối chiếu EVN", "kWh EVN", "Số chứng từ", "Mã khoản chi" };
        AddHeaders(details, headers);
        for (var index = 0; index < expenses.Count; index++)
        {
            var row = index + 2;
            var item = expenses[index];
            details.Cell(row, 1).Value = item.ExpenseDate.ToDateTime(TimeOnly.MinValue);
            details.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            SetSafeText(details.Cell(row, 2), ExpenseCategoryLabel(item.Category));
            SetMoney(details.Cell(row, 3), item.Amount);
            SetSafeText(details.Cell(row, 4), item.BuildingId is Guid b ? buildings.GetValueOrDefault(b) : "Chi chung");
            SetSafeText(details.Cell(row, 5), item.RoomId is Guid r ? rooms.GetValueOrDefault(r) : null);
            SetSafeText(details.Cell(row, 6), item.Description);
            if (item.UtilityPeriod is not null) SetPeriod(details.Cell(row, 7), item.UtilityPeriod);
            if (item.UtilityKwh is decimal kwh) SetMoney(details.Cell(row, 8), kwh);
            details.Cell(row, 9).Value = attachmentCounts.GetValueOrDefault(item.Id);
            SetSafeText(details.Cell(row, 10), item.Id.ToString("D"));
        }
        FinishDetails(details, headers.Length);
    }

    private static async Task AddElectricityWorkbookAsync(XLWorkbook workbook, AppDbContext db, Guid ownerId, string from, string to,
        Guid? buildingId, CancellationToken ct)
    {
        var report = await GetElectricityReconciliationAsync(db, ownerId, from, to, buildingId, ct);
        EnsureExportSize(report.Rows.Count);
        var start = new DateOnly(int.Parse(from.AsSpan(0, 4)), int.Parse(from.AsSpan(5, 2)), 1);
        var endMonth = new DateOnly(int.Parse(to.AsSpan(0, 4)), int.Parse(to.AsSpan(5, 2)), 1);
        var filters = ParseFilters(start, endMonth.AddMonths(1).AddDays(-1), buildingId, null, 1, MaxPageSize);
        AddSummarySheet(workbook, "Đối chiếu điện EVN", filters,
            ("kWh EVN", report.Rows.Sum(row => row.EvnKwh ?? 0m)),
            ("Tiền điện EVN", report.Rows.Sum(row => row.EvnAmount)),
            ("kWh đã lập hóa đơn", report.Rows.Sum(row => row.BilledKwh)),
            ("Tiền điện đã lập hóa đơn", report.Rows.Sum(row => row.BilledAmount)),
            ("Chênh lệch tiền (đã lập − EVN)", report.Rows.Sum(row => row.Margin)));

        var sheet = workbook.Worksheets.Add("Chi tiết");
        var headers = new[] { "Kỳ", "Dãy", "kWh EVN", "Tiền EVN", "Số hóa đơn EVN", "kWh đã lập", "Tiền điện đã lập", "kWh thất thoát", "% thất thoát", "Chênh lệch tiền", "Hóa đơn nháp", "Ghi chú" };
        AddHeaders(sheet, headers);
        for (var index = 0; index < report.Rows.Count; index++)
        {
            var row = index + 2;
            var item = report.Rows[index];
            SetPeriod(sheet.Cell(row, 1), item.Period);
            SetSafeText(sheet.Cell(row, 2), item.BuildingName);
            if (item.EvnKwh is decimal evnKwh) SetMoney(sheet.Cell(row, 3), evnKwh);
            SetMoney(sheet.Cell(row, 4), item.EvnAmount);
            sheet.Cell(row, 5).Value = item.EvnBillCount;
            SetMoney(sheet.Cell(row, 6), item.BilledKwh);
            SetMoney(sheet.Cell(row, 7), item.BilledAmount);
            if (item.LossKwh is decimal lossKwh) SetMoney(sheet.Cell(row, 8), lossKwh);
            if (item.LossPercent is decimal lossPercent) SetMoney(sheet.Cell(row, 9), lossPercent);
            SetMoney(sheet.Cell(row, 10), item.Margin);
            sheet.Cell(row, 11).Value = item.DraftInvoiceCount;
            SetSafeText(sheet.Cell(row, 12), item.LossNullReason switch
            {
                "NoEvnBill" => "Chưa nhập hóa đơn EVN cho kỳ này",
                "ZeroEvnKwh" => "Số kWh EVN bằng 0",
                _ => null
            });
        }
        FinishDetails(sheet, headers.Length);
    }

    private static string ExpenseCategoryLabel(string category) => category switch
    {
        "Electricity" => "Điện tổng (EVN)",
        "Water" => "Nước tổng",
        "Internet" => "Internet / truyền hình",
        "Repair" => "Sửa chữa, bảo trì",
        "Cleaning" => "Vệ sinh, rác",
        "Salary" => "Lương quản lý / nhân công",
        "LeaseRent" => "Tiền thuê nhà (cho thuê lại)",
        "Tax" => "Thuế, phí, lệ phí",
        _ => "Khác"
    };
}

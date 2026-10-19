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
    private static void AddSummarySheet(XLWorkbook workbook, string title, ReportFilters filters,
        params (string Label, object Value)[] totals)
    {
        var sheet = workbook.Worksheets.Add("Tổng hợp");
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(2, 1).Value = "Xuất lúc";
        sheet.Cell(2, 2).Value = ToVietnamLocal(DateTimeOffset.UtcNow);
        sheet.Cell(2, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        sheet.Cell(4, 1).Value = "Bộ lọc";
        SetSafeText(sheet.Cell(4, 2), DescribeFilters(filters));
        sheet.Cell(6, 1).Value = "Chỉ số";
        sheet.Cell(6, 2).Value = "Giá trị";
        StyleHeader(sheet.Range(6, 1, 6, 2));
        for (var index = 0; index < totals.Length; index++)
        {
            var row = index + 7;
            SetSafeText(sheet.Cell(row, 1), totals[index].Label);
            if (totals[index].Value is decimal decimalValue)
            {
                sheet.Cell(row, 2).Value = decimalValue;
                sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0.##";
            }
            else if (totals[index].Value is int intValue)
            {
                sheet.Cell(row, 2).Value = intValue;
                sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
            }
            else if (totals[index].Value is long longValue)
            {
                sheet.Cell(row, 2).Value = Convert.ToDecimal(longValue, CultureInfo.InvariantCulture);
                sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
            }
            else SetSafeText(sheet.Cell(row, 2), Convert.ToString(totals[index].Value, CultureInfo.InvariantCulture));
        }
        sheet.Columns().AdjustToContents();
        sheet.Column(2).Width = Math.Min(70, Math.Max(18, sheet.Column(2).Width));
    }

    private static void AddReceiptDetails(XLWorkbook workbook, IReadOnlyList<ReceiptReportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Chi tiết");
        var headers = new[] { "Ngày thu", "Số tiền", "Nguồn", "Ghi chú", "Mã hóa đơn", "Loại hóa đơn", "Kỳ", "Mã dãy", "Dãy", "Mã phòng", "Phòng", "Mã lượt thuê", "Người thuê" };
        AddHeaders(sheet, headers);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = index + 2;
            var item = rows[index];
            sheet.Cell(row, 1).Value = ToVietnamLocal(item.ReceiptDate);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            SetMoney(sheet.Cell(row, 2), item.Amount);
            SetSafeText(sheet.Cell(row, 3), item.Source);
            SetSafeText(sheet.Cell(row, 4), item.Note);
            SetSafeText(sheet.Cell(row, 5), item.InvoiceId.ToString("D"));
            SetSafeText(sheet.Cell(row, 6), item.InvoiceKind);
            SetPeriod(sheet.Cell(row, 7), item.Period);
            SetSafeText(sheet.Cell(row, 8), item.BuildingId.ToString("D"));
            SetSafeText(sheet.Cell(row, 9), item.BuildingName);
            SetSafeText(sheet.Cell(row, 10), item.RoomId.ToString("D"));
            SetSafeText(sheet.Cell(row, 11), item.RoomName);
            SetSafeText(sheet.Cell(row, 12), item.OccupancyId.ToString("D"));
            SetSafeText(sheet.Cell(row, 13), item.TenantName);
        }
        FinishDetails(sheet, headers.Length);
    }

    private static void AddDebtDetails(XLWorkbook workbook, IReadOnlyList<DebtReportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Chi tiết");
        var headers = new[] { "Kỳ hóa đơn", "Loại hóa đơn", "Ngày phát hành", "Hạn thanh toán", "Tổng hóa đơn", "Đã thu", "Còn nợ hiện tại", "Mã hóa đơn", "Mã dãy", "Dãy", "Mã phòng", "Phòng", "Mã lượt thuê", "Người thuê" };
        AddHeaders(sheet, headers);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = index + 2;
            var item = rows[index];
            SetPeriod(sheet.Cell(row, 1), item.Period);
            SetSafeText(sheet.Cell(row, 2), item.InvoiceKind);
            sheet.Cell(row, 3).Value = ToVietnamLocal(item.IssuedAt);
            sheet.Cell(row, 3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            sheet.Cell(row, 4).Value = item.DueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 4).Style.DateFormat.Format = "dd/MM/yyyy";
            SetMoney(sheet.Cell(row, 5), item.Total);
            SetMoney(sheet.Cell(row, 6), item.Paid);
            SetMoney(sheet.Cell(row, 7), item.Outstanding);
            SetSafeText(sheet.Cell(row, 8), item.InvoiceId.ToString("D"));
            SetSafeText(sheet.Cell(row, 9), item.BuildingId.ToString("D"));
            SetSafeText(sheet.Cell(row, 10), item.BuildingName);
            SetSafeText(sheet.Cell(row, 11), item.RoomId.ToString("D"));
            SetSafeText(sheet.Cell(row, 12), item.RoomName);
            SetSafeText(sheet.Cell(row, 13), item.OccupancyId.ToString("D"));
            SetSafeText(sheet.Cell(row, 14), item.TenantName);
        }
        FinishDetails(sheet, headers.Length);
    }

    private static void AddDepositDetails(XLWorkbook workbook, IReadOnlyList<DepositReportRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Chi tiết");
        var headers = new[] { "Kỳ cọc", "Tiền cọc ban đầu", "Nhận trong kỳ", "Hoàn trong kỳ", "Khấu trừ trong kỳ", "Đang giữ hiện tại", "Mã hóa đơn cọc", "Mã dãy", "Dãy", "Mã phòng", "Phòng", "Mã lượt thuê", "Người thuê" };
        AddHeaders(sheet, headers);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = index + 2;
            var item = rows[index];
            SetPeriod(sheet.Cell(row, 1), item.Period);
            SetMoney(sheet.Cell(row, 2), item.DepositAmount);
            SetMoney(sheet.Cell(row, 3), item.PeriodReceived);
            SetMoney(sheet.Cell(row, 4), item.PeriodRefunded);
            SetMoney(sheet.Cell(row, 5), item.PeriodDeducted);
            SetMoney(sheet.Cell(row, 6), item.CurrentHeld);
            SetSafeText(sheet.Cell(row, 7), item.InvoiceId.ToString("D"));
            SetSafeText(sheet.Cell(row, 8), item.BuildingId.ToString("D"));
            SetSafeText(sheet.Cell(row, 9), item.BuildingName);
            SetSafeText(sheet.Cell(row, 10), item.RoomId.ToString("D"));
            SetSafeText(sheet.Cell(row, 11), item.RoomName);
            SetSafeText(sheet.Cell(row, 12), item.OccupancyId.ToString("D"));
            SetSafeText(sheet.Cell(row, 13), item.TenantName);
        }
        FinishDetails(sheet, headers.Length);
    }

    private static void AddHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++) SetSafeText(sheet.Cell(1, index + 1), headers[index]);
        StyleHeader(sheet.Range(1, 1, 1, headers.Count));
        sheet.SheetView.FreezeRows(1);
    }

    private static void FinishDetails(IXLWorksheet sheet, int columnCount)
    {
        sheet.Range(1, 1, Math.Max(1, sheet.LastRowUsed()?.RowNumber() ?? 1), columnCount).SetAutoFilter();
        sheet.Columns(1, columnCount).AdjustToContents(8, 48);
        sheet.Row(1).Height = 28;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE8F5");
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetMoney(IXLCell cell, decimal value)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0.##";
    }

    private static void SetPeriod(IXLCell cell, string period)
    {
        if (DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            cell.Value = parsed.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = "MM/yyyy";
        }
        else SetSafeText(cell, period);
    }

    private static void SetSafeText(IXLCell cell, string? value)
    {
        var text = value ?? string.Empty;
        var firstNonWhitespace = text.AsSpan().TrimStart();
        if (firstNonWhitespace.Length > 0 && firstNonWhitespace[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            text = "'" + text;
        cell.Value = text;
        cell.Style.NumberFormat.Format = "@";
    }

    private static string DescribeFilters(ReportFilters filters)
    {
        var values = new List<string>();
        if (filters.DateFrom is DateOnly from) values.Add($"Từ {from:dd/MM/yyyy}");
        if (filters.DateTo is DateOnly to) values.Add($"đến {to:dd/MM/yyyy}");
        if (filters.BuildingId is Guid buildingId) values.Add($"buildingId={buildingId:D}");
        if (filters.RoomId is Guid roomId) values.Add($"roomId={roomId:D}");
        return values.Count == 0 ? "Toàn bộ dữ liệu" : string.Join(" · ", values);
    }

    private static DateTime ToVietnamLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, VietnamTimeZone).DateTime;

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }

    private sealed record ReportFilters(DateOnly? DateFrom, DateOnly? DateTo, DateTimeOffset? FromUtc,
        DateTimeOffset? BeforeToUtc, Guid? BuildingId, Guid? RoomId, int Page, int PageSize, int Offset,
        string? PeriodFrom, string? PeriodTo);
}

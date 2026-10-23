import { request, requestBlob } from '../../shared/http/client';
import type { DebtReportRow, DebtReportTotals, DepositReportRow, DepositsReportTotals, ElectricityReconciliation, ExportReportType, ProfitLossReport, ReceiptReportRow, ReceiptsReportTotals, ReportFilters, ReportPage } from './types';


export const reportsApi = {
    receipts: (filters: ReportFilters) => request<ReportPage<ReceiptReportRow, ReceiptsReportTotals>>(`/api/reports/receipts?${reportQuery(filters)}`),
    debt: (filters: ReportFilters) => request<ReportPage<DebtReportRow, DebtReportTotals>>(`/api/reports/debt?${reportQuery(filters)}`),
    deposits: (filters: ReportFilters) => request<ReportPage<DepositReportRow, DepositsReportTotals>>(`/api/reports/deposits?${reportQuery(filters)}`),
    profitLoss: (filters: ReportFilters) => request<ProfitLossReport>(`/api/reports/profit-loss?${reportQuery({ ...filters, roomId: undefined, page: undefined, pageSize: undefined })}`),
    electricity: (filters: ReportFilters) => request<ElectricityReconciliation>(`/api/reports/electricity-reconciliation?${periodQuery(filters)}`),
    export: (type: ExportReportType, filters: ReportFilters) => requestBlob(`/api/reports/${type}/export.xlsx?${type === 'electricity'
      ? periodQuery(filters)
      : reportQuery({ ...filters, roomId: type === 'profit-loss' ? undefined : filters.roomId, page: undefined, pageSize: undefined })}`),
  };

function reportQuery(filters: ReportFilters) {
  const query = new URLSearchParams();
  query.set('dateFrom', filters.dateFrom); query.set('dateTo', filters.dateTo);
  if (filters.buildingId) query.set('buildingId', filters.buildingId);
  if (filters.roomId) query.set('roomId', filters.roomId);
  if (filters.page !== undefined) query.set('page', String(filters.page));
  if (filters.pageSize !== undefined) query.set('pageSize', String(filters.pageSize));
  return query.toString();
}

/** Electricity reconciliation is per billing month; the selected date range is widened to whole months. */
function periodQuery(filters: ReportFilters) {
  const query = new URLSearchParams();
  query.set('periodFrom', filters.dateFrom.slice(0, 7)); query.set('periodTo', filters.dateTo.slice(0, 7));
  if (filters.buildingId) query.set('buildingId', filters.buildingId);
  return query.toString();
}

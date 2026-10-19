import { request, requestBlob } from '../../shared/http/client';
import type { DebtReportRow, DebtReportTotals, DepositReportRow, DepositsReportTotals, ReceiptReportRow, ReceiptsReportTotals, ReportFilters, ReportPage } from './types';


export const reportsApi = {
    receipts: (filters: ReportFilters) => request<ReportPage<ReceiptReportRow, ReceiptsReportTotals>>(`/api/reports/receipts?${reportQuery(filters)}`),
    debt: (filters: ReportFilters) => request<ReportPage<DebtReportRow, DebtReportTotals>>(`/api/reports/debt?${reportQuery(filters)}`),
    deposits: (filters: ReportFilters) => request<ReportPage<DepositReportRow, DepositsReportTotals>>(`/api/reports/deposits?${reportQuery(filters)}`),
    export: (type: 'receipts' | 'debt' | 'deposits', filters: ReportFilters) => requestBlob(`/api/reports/${type}/export.xlsx?${reportQuery({ ...filters, page: undefined, pageSize: undefined })}`),
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

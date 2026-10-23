
export interface ReportPage<T, TTotals> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totals: TTotals;
}

export interface ReceiptReportRow {
  receiptId: string; receiptDate: string; amount: number; source: string; note: string; invoiceId: string;
  invoiceKind: string; period: string; occupancyId: string; roomId: string; roomName: string;
  buildingId: string; buildingName: string; tenantName: string;
}

export interface DebtReportRow {
  invoiceId: string; period: string; invoiceKind: string; issuedAt: string; dueDate: string; roomId: string;
  roomName: string; buildingId: string; buildingName: string; occupancyId: string; tenantName: string;
  total: number; paid: number; outstanding: number;
}

export interface DepositReportRow {
  invoiceId: string; period: string; roomId: string; roomName: string; buildingId: string;
  buildingName: string; occupancyId: string; tenantName: string; depositAmount: number;
  periodReceived: number; periodRefunded: number; periodDeducted: number; currentHeld: number;
}

export interface ReceiptsReportTotals { invoiceCash: number; depositCash: number; totalCash: number; }

export interface DebtReportTotals { currentOutstanding: number; overdueOutstanding: number; invoiceCount: number; overdueInvoiceCount: number; }

export interface DepositsReportTotals { periodReceived: number; periodRefunded: number; periodDeducted: number; currentHeld: number; }

export interface ReportFilters { dateFrom: string; dateTo: string; buildingId?: string; roomId?: string; page?: number; pageSize?: number; }

export interface ProfitLossMonth { month: string; income: number; expenses: number; net: number; }

export interface ProfitLossReport {
  dateFrom: string; dateTo: string; buildingId: string | null;
  totals: { income: number; expenses: number; net: number };
  months: ProfitLossMonth[];
  expensesByCategory: { category: string; amount: number; count: number }[];
  sharedExpensesExcluded: number;
}

export interface ElectricityReconciliationRow {
  buildingId: string; buildingName: string; period: string;
  evnKwh: number | null; evnAmount: number; evnBillCount: number;
  billedKwh: number; billedAmount: number;
  lossKwh: number | null; lossPercent: number | null; lossNullReason: 'NoEvnBill' | 'ZeroEvnKwh' | null;
  margin: number; draftInvoiceCount: number;
}

export interface ElectricityReconciliation { periodFrom: string; periodTo: string; buildingId: string | null; rows: ElectricityReconciliationRow[]; }

export type ExportReportType = 'receipts' | 'debt' | 'deposits' | 'profit-loss' | 'electricity';

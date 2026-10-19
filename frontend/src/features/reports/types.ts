
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

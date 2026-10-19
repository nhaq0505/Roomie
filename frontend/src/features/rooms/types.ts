import type { Invoice } from '../invoices';
export interface Occupancy {
  id: string;
  depositInvoiceId?: string;
  depositRequired: number;
  depositPaid: number;
  depositHeld: number;
  tenantName: string;
  phone?: string | null;
  people: number;
  startDate: string;
  token?: string | null;
  url?: string | null;
}

export interface Room {
  id: string;
  buildingId: string;
  buildingName?: string;
  name: string;
  rent: number;
  electricityRate: number;
  waterFee: number;
  waterPerPerson?: boolean;
  serviceFee: number;
  previousReading?: number;
  initialReading?: number;
  occupancy: Occupancy | null;
  isArchived?: boolean;
  archivedAt?: string | null;
  archiveBatchId?: string | null;
}

export interface MoveoutDepositSummary { invoiceId: string; required: number; paid: number; settled: number; held: number; outstanding: number; version: number | null; }

export interface MoveoutSummary { occupancyId: string; tenantName: string; roomName: string; startDate: string; endDate: string; invoices: Invoice[]; pendingInvoiceIds: string[]; deposit: MoveoutDepositSummary | null; heldDeposit: number; canComplete: boolean; blockedReason?: string | null; }

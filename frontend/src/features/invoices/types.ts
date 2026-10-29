
export interface DepositSettlement {
  id: string; amount: number; kind: 'Refund' | 'Deduction'; reason: string; recordedAt: string; targetInvoiceId?: string;
}

export interface Invoice {
  hasMonthlyOverlap?: boolean;
  kind: 'Monthly' | 'Deposit' | 'Supplemental';
  description?: string;
  isFinalMeterReading: boolean;
  occupancyId: string;
  depositHeld: number;
  depositSettlements: DepositSettlement[];
  id: string;
  roomId: string;
  roomName: string;
  buildingName?: string;
  period: string;
  tenantName?: string;
  previousReading: number;
  currentReading: number;
  consumption: number;
  waterByMeter?: boolean | null;
  previousWaterReading?: number | null;
  currentWaterReading?: number | null;
  waterConsumption?: number | null;
  rent: number;
  electricityAmount: number;
  waterAmount: number;
  serviceAmount: number;
  total: number;
  paid: number;
  outstanding: number;
  status: string;
  dueDate?: string;
  issuedAt?: string;
  lifecycleState?: 'Draft' | 'Issued' | string;
  version?: number;
  billableDays?: number;
  monthDays?: number;
  billingStartDate?: string;
  billingEndDate?: string;
  periodStart?: string;
  pendingPayOsCount?: number;
  hasPendingPayment?: boolean;
  canCorrectMeter?: boolean;
  meterCorrectionBlockedReason?: string | null;
  tenantToken?: string | null;
}

export interface InvoiceCorrection {
  id: string;
  reason: string;
  createdAt?: string;
  recordedAt?: string;
  actorName?: string;
  actorEmail?: string;
  before?: { rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; total?: number; amount?: number; [key: string]: unknown };
  after?: { rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; total?: number; amount?: number; [key: string]: unknown };
  beforeJson?: string;
  afterJson?: string;
  revision?: number;
}

export interface ReceiptResult {
  id: string;
  invoiceId: string;
  amount: number;
  paid: number;
  outstanding: number;
  status: 'Unpaid' | 'Partial' | 'Paid';
}

export type InvoiceKindFilter = 'All' | Invoice['kind'];
export type InvoiceStatusFilter = 'All' | 'Unpaid' | 'Partial' | 'Paid';
export type IssuanceFilter = 'All' | 'Issued' | 'Draft';

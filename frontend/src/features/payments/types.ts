import type { Invoice } from '../invoices';
export interface PaymentSettings {
  confirmationMode?: 'Automatic' | 'Manual';
  manualBank?: ManualBank | null;
  webhookUrl: string | null;
  webhookStatus: 'NotConfigured' | 'Unverified' | 'Verified' | 'Failed';
  webhookVerifiedAt: string | null;
  webhookError: string | null;
  configured: boolean;
  clientId?: string;
}

export interface PaymentReconcileResult {
  status: string;
  invoice: Invoice;
}

export interface TenantCheckout {
  checkoutUrl: string;
  qrCode: string | null;
  amount: number;
  orderCode: number;
  accountName: string | null;
  accountNumber: string | null;
  bankBin: string | null;
  description: string | null;
  status: string;
  expiresAt: number | null;
}

export interface ManualBank { bankBin: string; accountNumber: string; accountName: string; }
export interface PaymentPreferences extends Partial<ManualBank> { confirmationMode: 'Automatic' | 'Manual'; }
export interface ManualPayment {
  roomName?: string | null; tenantName?: string | null;
  id: string; occupancyId: string; amount: number; bankBin: string; accountNumber: string; accountName: string;
  description: string; qrUrl: string; createdAt: string; expiresAt: string; confirmedAt: string | null;
  invoices: { invoiceId: string; amount: number }[];
}
export interface PaymentBank { bin: string; name: string; shortName: string; }

import type { Invoice } from '../invoices';
export interface Reading {
  id?: string;
  roomId: string;
  occupancyId?: string;
  roomName: string;
  buildingName?: string;
  period: string;
  previousReading: number;
  currentReading?: number;
  consumption?: number;
  isSubmitted?: boolean;
  invoiceId?: string;
  issuanceStatus?: 'Draft' | 'Issued' | string;
}

export interface ReadingSaveResult {
  reading?: Reading;
  invoice?: Invoice | null;
  draftInvoice?: Invoice | null;
  invoiceId?: string;
  lifecycleState?: 'Draft' | 'Issued' | string;
  [key: string]: unknown;
}

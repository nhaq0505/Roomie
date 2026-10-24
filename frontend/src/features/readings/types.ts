import type { Invoice } from '../invoices';

export interface TenantMeterSubmission {
  id: string;
  roomId: string;
  occupancyId: string;
  period: string;
  previousReading: number;
  submittedReading: number;
  approvedReading?: number | null;
  tenantNote?: string | null;
  rejectReason?: string | null;
  status: 'Pending' | 'Approved' | 'Rejected';
  photoFileName: string;
  photoContentType: string;
  photoSizeBytes: number;
  submittedAt: string;
  reviewedAt?: string | null;
  readingId?: string | null;
  invoiceId?: string | null;
}

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

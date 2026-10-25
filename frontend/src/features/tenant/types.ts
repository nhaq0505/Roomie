import type { Invoice } from '../invoices';
import type { Occupancy, Room } from '../rooms';

export interface TenantSelfCheckinRequest {
  target: 'tenant' | 'coResident';
  fullName: string;
  citizenId: string;
  dateOfBirth?: string | null;
  gender?: string | null;
  citizenIdIssuedDate?: string | null;
  citizenIdIssuedPlace?: string | null;
  permanentAddress?: string | null;
  phone?: string | null;
  email?: string | null;
  relation?: string | null;
}

export interface TenantIdentityPhotoDto {
  targetKey: string;
  photoFileName: string;
  photoContentType: string;
  photoSizeBytes: number;
  uploadedAt: string;
}

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

export interface TenantOverview {
  confirmationMode?: 'Automatic' | 'Manual';
  total: number;
  totals: {outstanding: number; unpaidCount?: number};
  room: Room;
  occupancy: Occupancy;
  invoices: Invoice[];
  currentPeriod?: string;
  currentPeriodHasReading?: boolean;
  meterSubmission?: TenantMeterSubmission | null;
}

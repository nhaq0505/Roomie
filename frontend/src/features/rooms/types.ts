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
  tenantCitizenId?: string | null;
  coResidents?: CoResident[];
  tenantDateOfBirth?: string | null;
  tenantGender?: string | null;
  tenantCitizenIdIssuedDate?: string | null;
  tenantCitizenIdIssuedPlace?: string | null;
  tenantPermanentAddress?: string | null;
  tenantEmail?: string | null;
  contractEndDate?: string | null;
  paymentDueDay?: number;
  roomAreaSqm?: number | null;
  handoverAssets?: string | null;
  additionalTerms?: string | null;
  householdHeadRelation?: string | null;
  tenantSelfCheckinAt?: string | null;
  hasTenantIdentityPhoto?: boolean;
  tenantIdentityPhotoFileName?: string | null;
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
  waterByMeter?: boolean;
  previousWaterReading?: number;
  serviceFee: number;
  previousReading?: number;
  initialReading?: number;
  initialWaterReading?: number;
  occupancy: Occupancy | null;
  isArchived?: boolean;
  archivedAt?: string | null;
  archiveBatchId?: string | null;
}

export interface OccupancyRequest {
  tenantName: string;
  phone?: string;
  people: number;
  startDate: string;
  depositAmount: number;
  initialReading?: number;
  initialWaterReading?: number;
  tenantCitizenId?: string | null;
  coResidents?: CoResident[];
  tenantDateOfBirth?: string | null;
  tenantGender?: string | null;
  tenantCitizenIdIssuedDate?: string | null;
  tenantCitizenIdIssuedPlace?: string | null;
  tenantPermanentAddress?: string | null;
  tenantEmail?: string | null;
  contractEndDate?: string | null;
  paymentDueDay?: number | null;
  roomAreaSqm?: number | null;
  handoverAssets?: string | null;
  additionalTerms?: string | null;
  householdHeadRelation?: string | null;
}

export interface OccupancyUpdatePayload {
  occupancyId: string;
  initialReading?: number;
  initialWaterReading?: number;
  tenantName: string;
  phone?: string;
  people: number;
  tenantCitizenId?: string | null;
  coResidents?: CoResident[];
  tenantDateOfBirth?: string | null;
  tenantGender?: string | null;
  tenantCitizenIdIssuedDate?: string | null;
  tenantCitizenIdIssuedPlace?: string | null;
  tenantPermanentAddress?: string | null;
  tenantEmail?: string | null;
  contractEndDate?: string | null;
  paymentDueDay?: number | null;
  roomAreaSqm?: number | null;
  handoverAssets?: string | null;
  additionalTerms?: string | null;
  householdHeadRelation?: string | null;
}

export interface MoveoutDepositSummary { invoiceId: string; required: number; paid: number; settled: number; held: number; outstanding: number; version: number | null; }

export interface MoveoutSummary { occupancyId: string; tenantName: string; roomName: string; startDate: string; endDate: string; invoices: Invoice[]; pendingInvoiceIds: string[]; deposit: MoveoutDepositSummary | null; heldDeposit: number; canComplete: boolean; blockedReason?: string | null; }

export interface CoResident {
  fullName: string;
  dateOfBirth?: string | null;
  gender?: string | null;
  citizenId?: string | null;
  relation?: string | null;
}

export interface OccupancyDocumentData {
  roomId: string;
  buildingId: string;
  occupancyId: string;
  ownerName: string;
  ownerPhone?: string | null;
  ownerEmail: string;
  ownerCitizenId?: string | null;
  ownerDateOfBirth?: string | null;
  ownerCitizenIdIssuedDate?: string | null;
  ownerCitizenIdIssuedPlace?: string | null;
  ownerPermanentAddress?: string | null;
  bankBin?: string | null;
  bankName?: string | null;
  bankShortName?: string | null;
  bankAccountNumber?: string | null;
  bankAccountName?: string | null;
  buildingName: string;
  buildingAddress?: string | null;
  policeStationName?: string | null;
  roomName: string;
  roomAreaSqm?: number | null;
  rent: number;
  rentInWords: string;
  electricityRate: number;
  waterFee: number;
  waterPerPerson: boolean;
  waterByMeter?: boolean;
  initialWaterReading?: number;
  serviceFee: number;
  initialReading: number;
  tenantName: string;
  tenantPhone?: string | null;
  tenantEmail?: string | null;
  people: number;
  tenantCitizenId?: string | null;
  tenantDateOfBirth?: string | null;
  tenantGender?: string | null;
  tenantCitizenIdIssuedDate?: string | null;
  tenantCitizenIdIssuedPlace?: string | null;
  tenantPermanentAddress?: string | null;
  startDate: string;
  contractEndDate?: string | null;
  paymentDueDay: number;
  depositRequired: number;
  depositInWords: string;
  depositPaid: number;
  handoverAssets?: string | null;
  additionalTerms?: string | null;
  householdHeadRelation: string;
  coResidents: CoResident[];
  tenantSelfCheckinAt?: string | null;
  hasTenantIdentityPhoto?: boolean;
  tenantIdentityPhotoFileName?: string | null;
}

export interface UpdateOccupancyDocumentRequest {
  occupancyId: string;
  initialReading?: number;
  initialWaterReading?: number;
  ownerName?: string | null;
  ownerPhone?: string | null;
  ownerCitizenId?: string | null;
  ownerDateOfBirth?: string | null;
  ownerCitizenIdIssuedDate?: string | null;
  ownerCitizenIdIssuedPlace?: string | null;
  ownerPermanentAddress?: string | null;
  buildingAddress?: string | null;
  policeStationName?: string | null;
  tenantName?: string | null;
  tenantPhone?: string | null;
  tenantEmail?: string | null;
  tenantCitizenId?: string | null;
  tenantDateOfBirth?: string | null;
  tenantGender?: string | null;
  tenantCitizenIdIssuedDate?: string | null;
  tenantCitizenIdIssuedPlace?: string | null;
  tenantPermanentAddress?: string | null;
  contractEndDate?: string | null;
  paymentDueDay?: number | null;
  roomAreaSqm?: number | null;
  handoverAssets?: string | null;
  additionalTerms?: string | null;
  householdHeadRelation?: string | null;
  coResidents?: CoResident[];
}

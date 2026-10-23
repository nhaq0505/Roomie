import type { SubscriptionTier } from '../subscription';
export type Role = 'Admin' | 'Marketer' | 'Owner';
export interface PasswordRecoveryChallenge { challengeToken: string; email: string; expiresAt: string; resendAvailableAt: string; }
export interface PasswordRecoveryGrant { resetToken: string; expiresAt: string; }

export type AccountStatus = 'Approved' | 'Pending' | 'Rejected' | 'Suspended';

export interface User {
  id: string;
  name: string;
  email: string;
  role: Role;
  mfaEnabled?: boolean;
  mfaRequired?: boolean;
  status?: AccountStatus;
  phone?: string | null;
  citizenId?: string | null;
  dateOfBirth?: string | null;
  citizenIdIssuedDate?: string | null;
  citizenIdIssuedPlace?: string | null;
  permanentAddress?: string | null;
  rejectionReason?: string | null;
  subscriptionTier?: SubscriptionTier;
  subscriptionExpiresAt?: string | null;
  isPro?: boolean;
  roomCount?: number;
  maxRooms?: number | null;
}

export interface UpdateProfilePayload {
  name: string;
  phone?: string | null;
  citizenId?: string | null;
  dateOfBirth?: string | null;
  citizenIdIssuedDate?: string | null;
  citizenIdIssuedPlace?: string | null;
  permanentAddress?: string | null;
}

export interface RegistrationVerification {
  verificationRequired: true;
  challengeToken: string;
  email: string;
  expiresAt: string;
  resendAvailableAt: string;
}

import type { AccountStatus } from '../auth';
import type { SubscriptionTier } from '../subscription';
export interface OwnerRequest {
  id: string;
  name: string;
  email: string;
  phone?: string;
  status: AccountStatus;
  reason?: string | null;
  createdAt?: string;
  subscriptionTier?: SubscriptionTier;
  subscriptionExpiresAt?: string | null;
  isPro?: boolean;
  roomCount?: number;
  maxRooms?: number | null;
}

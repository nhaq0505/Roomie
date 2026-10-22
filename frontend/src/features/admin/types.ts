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

export interface WelcomePromotion {
  id: string;
  name: string;
  isEnabled: boolean;
  rewardMonths: number;
  startsAt: string | null;
  endsAt: string | null;
}

export interface PromotionCode {
  id: string;
  code: string;
  description: string | null;
  rewardMonths: number;
  maxRedemptions: number | null;
  redeemedCount: number;
  isActive: boolean;
  expiresAt: string | null;
  createdAt: string;
}

export interface AdminPromotionsOverview {
  welcome: WelcomePromotion;
  codes: PromotionCode[];
}

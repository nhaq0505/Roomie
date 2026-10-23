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

export interface MarketerAccount {
  id: string;
  name: string;
  email: string;
  phone?: string | null;
  status: AccountStatus;
  reason?: string | null;
  createdAt?: string;
}

export interface CreateMarketerRequest {
  name: string;
  email: string;
  password: string;
  phone?: string | null;
}

export interface PromotionRedemptionItem {
  id: string;
  userId: string;
  ownerName: string;
  ownerEmail: string;
  ownerPhone?: string | null;
  redeemedAt: string;
  subscriptionTier: string;
  subscriptionExpiresAt?: string | null;
  isActivated: boolean;
}

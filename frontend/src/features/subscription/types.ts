
export type SubscriptionTier = 'Free' | 'Pro';

export interface SubscriptionPackage {
  id: string;
  name: string;
  days: number;
  priceVnd: number;
  description: string;
  maxRooms: number;
}

export interface SubscriptionStatus {
  tier: SubscriptionTier;
  expiresAt: string | null;
  isPro: boolean;
  currentRooms: number;
  maxRooms: number | null;
  availablePackages: SubscriptionPackage[];
}

export interface RedeemResult {
  success: boolean;
  message: string;
  subscriptionTier: string;
  subscriptionExpiresAt: string | null;
  addedMonths: number;
}

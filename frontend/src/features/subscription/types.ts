export type SubscriptionTier = 'Free' | 'Pro';

export interface SubscriptionPackage {
  id: string;
  name: string;
  days: number;
  price: number;
  priceVnd?: number;
  description: string;
  maxRooms?: number;
}

export interface SubscriptionStatus {
  tier: SubscriptionTier;
  expiresAt: string | null;
  isPro: boolean;
  roomCount: number;
  currentRooms?: number;
  maxRooms: number | null;
  availablePackages: SubscriptionPackage[];
}

export interface ReconcileSubscriptionResult {
  status: string;
  checkoutUrl?: string | null;
  isPro?: boolean;
}

export interface RedeemResult {
  success: boolean;
  message: string;
  subscriptionTier: string;
  subscriptionExpiresAt: string | null;
  addedMonths: number;
}

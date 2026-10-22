import { post, request } from '../../shared/http/client';
import type { TenantCheckout } from '../payments';
import type { RedeemResult, SubscriptionStatus } from './types';

export const subscriptionApi = {
    getStatus: () => request<SubscriptionStatus>('/api/subscription'),
    upgrade: (packageId?: string) => request<TenantCheckout>('/api/subscription/upgrade', post({ packageId })),
    reconcile: (orderCode: number | string) => request<SubscriptionStatus>(`/api/subscription/reconcile/${encodeURIComponent(orderCode)}`, post()),
    redeemCode: (code: string) => request<RedeemResult>('/api/subscription/redeem-code', post({ code })),
  };

import { json, listFrom, post, request } from '../../shared/http/client';
import type { AccountStatus } from '../auth';
import type { SubscriptionTier } from '../subscription';
import type {
  AdminPromotionsOverview,
  CreateMarketerRequest,
  MarketerAccount,
  OwnerRequest,
  PromotionCode,
  PromotionRedemptionItem,
  WelcomePromotion,
} from './types';

export const ownersApi = {
    page: (page: number, status: string) => request<{items:OwnerRequest[]; total:number; pendingCount:number}>(`/api/admin/owners?page=${page}&pageSize=10${status === 'All' ? '' : `&status=${status}`}`),
    resetPassword: (id: string, currentPassword: string) => request<{ token: string; expiresAt: string }>(`/api/admin/owners/${encodeURIComponent(id)}/reset-password`, post({ currentPassword })),
    list: () => request<unknown>('/api/admin/owners').then(listFrom<OwnerRequest>),
    updateStatus: (id: string, body: { status: AccountStatus; reason?: string }) => request<OwnerRequest>(`/api/admin/owners/${encodeURIComponent(id)}/status`, post(body)),
    updateSubscription: (id: string, body: { tier: SubscriptionTier; days?: number; expiresAt?: string; reason?: string }) => request<OwnerRequest>(`/api/admin/owners/${encodeURIComponent(id)}/subscription`, post(body)),
  };

export const marketersApi = {
  list: () => request<MarketerAccount[]>('/api/admin/marketers'),
  create: (body: CreateMarketerRequest) => request<MarketerAccount>('/api/admin/marketers', post(body)),
  updateStatus: (id: string, body: { status: AccountStatus; reason?: string }) =>
    request<MarketerAccount>(`/api/admin/marketers/${encodeURIComponent(id)}/status`, post(body)),
  resetPassword: (id: string, currentPassword: string) =>
    request<{ token: string; expiresAt: string }>(`/api/admin/marketers/${encodeURIComponent(id)}/reset-password`, post({ currentPassword })),
};

export const promotionsApi = {
  get: () => request<AdminPromotionsOverview>('/api/admin/promotions'),
  updateWelcome: (body: { isEnabled: boolean; rewardMonths: number; startsAt?: string | null; endsAt?: string | null }) =>
    request<WelcomePromotion>('/api/admin/promotions/welcome', { method: 'PUT', body: json(body) }),
  createCode: (body: { code: string; description?: string; rewardMonths: number; maxRedemptions?: number | null; expiresAt?: string | null; isActive?: boolean }) =>
    request<PromotionCode>('/api/admin/promotions/codes', post(body)),
  updateCode: (id: string, body: { description?: string; rewardMonths: number; maxRedemptions?: number | null; expiresAt?: string | null; isActive: boolean }) =>
    request<PromotionCode>(`/api/admin/promotions/codes/${encodeURIComponent(id)}`, { method: 'PATCH', body: json(body) }),
  deleteCode: (id: string) =>
    request<void>(`/api/admin/promotions/codes/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  redemptions: (id: string) =>
    request<PromotionRedemptionItem[]>(`/api/admin/promotions/codes/${encodeURIComponent(id)}/redemptions`),
};

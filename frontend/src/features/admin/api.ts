import { listFrom, post, request } from '../../shared/http/client';
import type { AccountStatus } from '../auth';
import type { SubscriptionTier } from '../subscription';
import type { OwnerRequest } from './types';

export const ownersApi = {
    page: (page: number, status: string) => request<{items:OwnerRequest[]; total:number; pendingCount:number}>(`/api/admin/owners?page=${page}&pageSize=10${status === 'All' ? '' : `&status=${status}`}`),
    resetPassword: (id: string, currentPassword: string) => request<{ token: string; expiresAt: string }>(`/api/admin/owners/${encodeURIComponent(id)}/reset-password`, post({ currentPassword })),
    list: () => request<unknown>('/api/admin/owners').then(listFrom<OwnerRequest>),
    updateStatus: (id: string, body: { status: AccountStatus; reason?: string }) => request<OwnerRequest>(`/api/admin/owners/${encodeURIComponent(id)}/status`, post(body)),
    updateSubscription: (id: string, body: { tier: SubscriptionTier; days?: number; expiresAt?: string; reason?: string }) => request<OwnerRequest>(`/api/admin/owners/${encodeURIComponent(id)}/subscription`, post(body)),
  };

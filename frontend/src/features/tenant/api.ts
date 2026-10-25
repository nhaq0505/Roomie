import { post, request } from '../../shared/http/client';
import type { PaymentReconcileResult, TenantCheckout, ManualPayment } from '../payments';
import type { Occupancy } from '../rooms';
import type { TenantIdentityPhotoDto, TenantMeterSubmission, TenantOverview, TenantSelfCheckinRequest } from './types';

export const tenantApi = {
  overview: (token: string, page = 1, invoiceId?: string) => request<TenantOverview>(`/api/tenant/${encodeURIComponent(token)}?page=${page}&pageSize=10${invoiceId ? `&invoiceId=${encodeURIComponent(invoiceId)}` : ''}`),
  manualPaymentStatus: (token: string, id: string) => request<ManualPayment>(`/api/tenant/${encodeURIComponent(token)}/manual-payments/${encodeURIComponent(id)}`),
  manualPayment: (token: string, invoiceId?: string) => request<ManualPayment>(`/api/tenant/${encodeURIComponent(token)}/manual-payments`, post({ invoiceId: invoiceId ?? null })),
  createAllPayment: (token: string) => request<TenantCheckout>(`/api/tenant/${encodeURIComponent(token)}/payment`, post()),
  reconcileOrder: (token: string, orderCode: number) => request<{status: string}>(`/api/tenant/${encodeURIComponent(token)}/orders/${orderCode}/reconcile`, post()),
  createPayment: (token: string, invoiceId: string) => request<TenantCheckout>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/payment`, post()),
  reconcile: (token: string, invoiceId: string) => request<PaymentReconcileResult>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/reconcile`, post()),
  selfCheckin: (token: string, body: TenantSelfCheckinRequest) => request<Occupancy>(`/api/tenant/${encodeURIComponent(token)}/self-checkin`, post(body)),
  removeCoResident: (token: string, citizenId: string) => request<Occupancy>(`/api/tenant/${encodeURIComponent(token)}/co-residents/${encodeURIComponent(citizenId)}`, { method: 'DELETE' }),
  uploadIdentityPhoto: (token: string, file: File, target = 'tenant') => {
    const form = new FormData();
    form.append('photo', file);
    form.append('target', target);
    return request<TenantIdentityPhotoDto>(`/api/tenant/${encodeURIComponent(token)}/identity-photo`, { method: 'POST', body: form });
  },
  identityPhotoUrl: (token: string, target = 'tenant') => `/api/tenant/${encodeURIComponent(token)}/identity-photo?target=${encodeURIComponent(target)}`,
  submitMeterReading: (token: string, file: File, submittedReading: number, note?: string, period?: string) => {
    const form = new FormData();
    form.append('file', file);
    form.append('submittedReading', String(submittedReading));
    if (note) form.append('note', note);
    if (period) form.append('period', period);
    return request<TenantMeterSubmission>(`/api/tenant/${encodeURIComponent(token)}/meter-submission`, { method: 'POST', body: form });
  },
  meterPhotoUrl: (token: string, period?: string) => `/api/tenant/${encodeURIComponent(token)}/meter-submission/photo${period ? `?period=${encodeURIComponent(period)}` : ''}`,
};

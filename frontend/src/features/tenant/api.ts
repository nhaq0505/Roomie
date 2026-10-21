import { post, request } from '../../shared/http/client';
import type { PaymentReconcileResult, TenantCheckout, ManualPayment } from '../payments';
import type { TenantOverview } from './types';

export const tenantApi = {
    overview: (token: string, page = 1, invoiceId?: string) => request<TenantOverview>(`/api/tenant/${encodeURIComponent(token)}?page=${page}&pageSize=10${invoiceId ? `&invoiceId=${encodeURIComponent(invoiceId)}` : ''}`),
    manualPaymentStatus: (token: string, id: string) => request<ManualPayment>(`/api/tenant/${encodeURIComponent(token)}/manual-payments/${encodeURIComponent(id)}`),
    manualPayment: (token: string, invoiceId?: string) => request<ManualPayment>(`/api/tenant/${encodeURIComponent(token)}/manual-payments`, post({ invoiceId: invoiceId ?? null })),
    createAllPayment: (token: string) => request<TenantCheckout>(`/api/tenant/${encodeURIComponent(token)}/payment`, post()),
    reconcileOrder: (token: string, orderCode: number) => request<{status: string}>(`/api/tenant/${encodeURIComponent(token)}/orders/${orderCode}/reconcile`, post()),
    createPayment: (token: string, invoiceId: string) => request<TenantCheckout>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/payment`, post()),
    reconcile: (token: string, invoiceId: string) => request<PaymentReconcileResult>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/reconcile`, post()),
  };

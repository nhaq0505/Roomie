import { post, request } from '../../shared/http/client';
import type { PaymentReconcileResult, TenantCheckout } from '../payments';
import type { TenantOverview } from './types';

export const tenantApi = {
    overview: (token: string, page = 1, invoiceId?: string) => request<TenantOverview>(`/api/tenant/${encodeURIComponent(token)}?page=${page}&pageSize=10${invoiceId ? `&invoiceId=${encodeURIComponent(invoiceId)}` : ''}`),
    createPayment: (token: string, invoiceId: string) => request<TenantCheckout>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/payment`, post()),
    reconcile: (token: string, invoiceId: string) => request<PaymentReconcileResult>(`/api/tenant/${encodeURIComponent(token)}/invoices/${encodeURIComponent(invoiceId)}/reconcile`, post()),
  };

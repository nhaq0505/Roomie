import { json, post, request } from '../../shared/http/client';
import type { PaymentSettings, PaymentPreferences, PaymentBank, ManualPayment } from './types';


export const paymentSettingsApi = {
    banks: () => request<PaymentBank[]>('/api/settings/payment/banks'),
    preferences: (body: PaymentPreferences) => request<PaymentSettings>('/api/settings/payment/preferences', { method: 'PUT', body: json(body) }),
    manualRequests: (page = 1) => request<{items: ManualPayment[]; total: number}>(`/api/manual-payments?page=${page}&pageSize=20`),
    confirmManual: (id: string, amount: number) => request<ManualPayment>(`/api/manual-payments/${encodeURIComponent(id)}/confirm`, post({ amount })),
    dismissManual: (id: string) => request<ManualPayment>(`/api/manual-payments/${encodeURIComponent(id)}/dismiss`, post()),
    get: () => request<PaymentSettings>('/api/settings/payment'),
    update: (body: { clientId: string; apiKey: string; checksumKey: string }) => request<PaymentSettings>('/api/settings/payment', { method: 'PUT', body: json(body) }),
    verifyWebhook: () => request<PaymentSettings>('/api/settings/payment/verify-webhook', post()),
  };

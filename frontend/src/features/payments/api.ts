import { json, post, request } from '../../shared/http/client';
import type { PaymentSettings } from './types';


export const paymentSettingsApi = {
    get: () => request<PaymentSettings>('/api/settings/payment'),
    update: (body: { clientId: string; apiKey: string; checksumKey: string }) => request<PaymentSettings>('/api/settings/payment', { method: 'PUT', body: json(body) }),
    verifyWebhook: () => request<PaymentSettings>('/api/settings/payment/verify-webhook', post()),
  };

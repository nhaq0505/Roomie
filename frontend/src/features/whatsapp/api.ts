import { json, post, request } from '../../shared/http/client';
import type { WhatsAppConnection, WhatsAppHistory, WhatsAppSettings } from './types';
export const whatsAppApi = {
  settings: () => request<WhatsAppSettings>('/api/whatsapp/settings'),
  save: (body: WhatsAppSettings) => request<void>('/api/whatsapp/settings', { method: 'PUT', body: json(body) }),
  connection: () => request<WhatsAppConnection>('/api/whatsapp/connection'),
  connect: () => request<WhatsAppConnection>('/api/whatsapp/connect', post()),
  disconnect: () => request<WhatsAppConnection>('/api/whatsapp/disconnect', post()),
  queue: () => request<{ queued: number }>('/api/whatsapp/queue', post()),
  history: (page: number) => request<WhatsAppHistory>(`/api/whatsapp/history?page=${page}`),
};

import { json, post, request } from '../../shared/http/client';
import type { ZaloConnection, ZaloHistory, ZaloSettings } from './types';
export const zaloApi = {
  settings: () => request<ZaloSettings>('/api/zalo/settings'),
  save: (body: ZaloSettings) => request<void>('/api/zalo/settings', { method: 'PUT', body: json(body) }),
  connection: () => request<ZaloConnection>('/api/zalo/connection'),
  connect: () => request<ZaloConnection>('/api/zalo/connect', post()),
  disconnect: () => request<ZaloConnection>('/api/zalo/disconnect', post()),
  queue: () => request<{ queued: number }>('/api/zalo/queue', post()),
  history: (page: number) => request<ZaloHistory>(`/api/zalo/history?page=${page}`),
};

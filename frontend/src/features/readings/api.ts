import { listFrom, post, request } from '../../shared/http/client';
import type { Reading, ReadingSaveResult, TenantMeterSubmission } from './types';

export const readingsApi = {
  list: (period: string) => request<unknown>(`/api/readings?period=${encodeURIComponent(period)}`).then(listFrom<Reading>),
  save: (body: { roomId: string; period: string; currentReading: number }) => request<ReadingSaveResult>('/api/readings', post(body)),
  listSubmissions: (period?: string) => request<TenantMeterSubmission[]>(`/api/readings/submissions${period ? `?period=${encodeURIComponent(period)}` : ''}`),
  submissionPhotoUrl: (id: string) => `/api/readings/submissions/${encodeURIComponent(id)}/photo`,
  approveSubmission: (id: string, currentReading?: number) => request<TenantMeterSubmission>(`/api/readings/submissions/${encodeURIComponent(id)}/approve`, post({ currentReading: currentReading ?? null })),
  rejectSubmission: (id: string, reason?: string) => request<TenantMeterSubmission>(`/api/readings/submissions/${encodeURIComponent(id)}/reject`, post({ reason: reason ?? null })),
};

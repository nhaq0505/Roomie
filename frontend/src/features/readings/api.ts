import { listFrom, post, request } from '../../shared/http/client';
import type { Reading, ReadingSaveResult } from './types';

export const readingsApi = {
  list: (period: string) => request<unknown>(`/api/readings?period=${encodeURIComponent(period)}`).then(listFrom<Reading>),
  save: (body: { roomId: string; period: string; currentReading: number }) => request<ReadingSaveResult>('/api/readings', post(body)),
};

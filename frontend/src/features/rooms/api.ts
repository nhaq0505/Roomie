import { json, listFrom, post, request } from '../../shared/http/client';
import type { MoveoutSummary, OccupancyRequest, Room } from './types';


export const roomsApi = {
    moveout: (id: string) => request<MoveoutSummary>(`/api/rooms/${encodeURIComponent(id)}/moveout`),
    completeMoveout: (id: string, body: { occupancyId: string; requestId: string; refundAmount: number; expectedHeld: number; expectedDepositVersion: number | null }) => request<{ success: boolean; refundAmount: number; refundedAmount: number; retainedAmount: number; endDate: string; completedAt: string }>(`/api/rooms/${encodeURIComponent(id)}/complete-moveout`, post(body)),
    list: (period?: string, includeArchived = false) => { const query = new URLSearchParams(); if (period) query.set('period', period); if (includeArchived) query.set('includeArchived', 'true'); return request<unknown>(`/api/rooms${query.size ? `?${query}` : ''}`).then(listFrom<Room>); },
    get: (id: string) => request<Room>(`/api/rooms/${encodeURIComponent(id)}`),
    create: (body: { buildingId: string; name: string; rent: number; electricityRate: number; waterFee: number; waterPerPerson: boolean; serviceFee: number; initialReading?: number }) => request<Room>('/api/rooms', post({ initialReading: 0, ...body })),
    update: (id: string, body: Partial<Room>) => request<Room>(`/api/rooms/${encodeURIComponent(id)}`, { method: 'PATCH', body: json(body) }),
    editOccupancy: (id: string, body: { occupancyId: string; tenantName: string; phone?: string; people: number }) => request<Room>(`/api/rooms/${encodeURIComponent(id)}/occupancy`, { method: 'PATCH', body: json(body) }),
    startOccupancy: (id: string, body: OccupancyRequest) => request<Room>(`/api/rooms/${encodeURIComponent(id)}/occupancy`, post(body)),
    rotateLink: (id: string) => request<{ token: string; url: string }>(`/api/rooms/${encodeURIComponent(id)}/rotate-link`, post()),
    archive: (id: string) => request<{ archived: boolean; archiveBatchId: string }>(`/api/rooms/${encodeURIComponent(id)}`, { method: 'DELETE' }),
    restore: (id: string) => request<{ restored: boolean }>(`/api/rooms/${encodeURIComponent(id)}/restore`, post()),
  };

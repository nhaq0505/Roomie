import { json, listFrom, post, request } from '../../shared/http/client';
import type { Building } from './types';


export const buildingsApi = {
    list: (includeArchived = false) => request<unknown>(`/api/buildings${includeArchived ? '?includeArchived=true' : ''}`).then(listFrom<Building>),
    create: (body: { name: string; address: string }) => request<Building>('/api/buildings', post(body)),
    update: (id: string, body: { name: string; address?: string | null }) => request<Building>(`/api/buildings/${encodeURIComponent(id)}`, { method: 'PATCH', body: json(body) }),
    archive: (id: string) => request<{ archived: boolean; archiveBatchId: string }>(`/api/buildings/${encodeURIComponent(id)}`, { method: 'DELETE' }),
    restore: (id: string) => request<{ restored: boolean; restoredRoomIds: string[] }>(`/api/buildings/${encodeURIComponent(id)}/restore`, post()),
  };

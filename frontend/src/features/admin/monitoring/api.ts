import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { json, post, request } from '../../../shared/http/client';
import type {
  AdminCampaignCostItemDto,
  AdminMonitoringGrowthResponse,
  AdminMonitoringIncidentsResponse,
  AdminMonitoringOperationsResponse,
  AdminMonitoringRevenueResponse,
  IncidentDto,
  MonitoringFilterRequest,
  UpdateIncidentRequest,
  UpsertCampaignCostRequest,
} from './types';

const patch = (body?: unknown): RequestInit => ({
  method: 'PATCH',
  ...(body === undefined ? {} : { body: json(body) }),
});

function buildQueryString(params: Record<string, string | number | undefined | null>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') {
      q.set(k, String(v));
    }
  }
  const str = q.toString();
  return str ? `?${str}` : '';
}

export const monitoringApi = {
  getGrowth: (filter: MonitoringFilterRequest) => {
    const qs = buildQueryString({
      fromDate: filter.fromDate,
      toDate: filter.toDate,
      source: filter.source,
      channel: filter.channel,
      campaign: filter.campaign,
    });
    return request<AdminMonitoringGrowthResponse>(`/api/admin/monitoring/growth${qs}`);
  },

  getRevenue: (filter: MonitoringFilterRequest) => {
    const qs = buildQueryString({
      fromDate: filter.fromDate,
      toDate: filter.toDate,
      source: filter.source,
      channel: filter.channel,
      campaign: filter.campaign,
    });
    return request<AdminMonitoringRevenueResponse>(`/api/admin/monitoring/revenue${qs}`);
  },

  getOperations: () => {
    return request<AdminMonitoringOperationsResponse>('/api/admin/monitoring/operations');
  },

  getIncidents: () => {
    return request<AdminMonitoringIncidentsResponse>('/api/admin/monitoring/incidents');
  },

  updateIncident: (id: string, body: UpdateIncidentRequest) => {
    return request<IncidentDto>(`/api/admin/monitoring/incidents/${encodeURIComponent(id)}`, patch(body));
  },

  getCampaignCosts: (fromDate?: string, toDate?: string) => {
    const qs = buildQueryString({ fromDate, toDate });
    return request<AdminCampaignCostItemDto[]>(`/api/admin/monitoring/campaigns${qs}`);
  },

  upsertCampaignCost: (body: UpsertCampaignCostRequest) => {
    return request<AdminCampaignCostItemDto>('/api/admin/monitoring/campaigns', post(body));
  },
};

// React Query Hooks
export function useGrowthMonitoring(filter: MonitoringFilterRequest, enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'growth', filter],
    queryFn: () => monitoringApi.getGrowth(filter),
    enabled,
    retry: false,
    staleTime: 60 * 1000,
  });
}

export function useRevenueMonitoring(filter: MonitoringFilterRequest, enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'revenue', filter],
    queryFn: () => monitoringApi.getRevenue(filter),
    enabled,
    retry: false,
    staleTime: 60 * 1000,
  });
}

export function useOperationsMonitoring(enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'operations'],
    queryFn: () => monitoringApi.getOperations(),
    enabled,
    retry: false,
    staleTime: 30 * 1000,
    refetchInterval: 30 * 1000, // Poll live operations telemetry every 30s
  });
}

export function useMonitoringIncidents(enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'incidents'],
    queryFn: () => monitoringApi.getIncidents(),
    enabled,
    retry: false,
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
  });
}

export function useCampaignCosts(fromDate?: string, toDate?: string) {
  return useQuery({
    queryKey: ['monitoring', 'campaigns', fromDate, toDate],
    queryFn: () => monitoringApi.getCampaignCosts(fromDate, toDate),
    staleTime: 60 * 1000,
  });
}

export function useUpsertCampaignCost() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertCampaignCostRequest) => monitoringApi.upsertCampaignCost(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'campaigns'] });
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'revenue'] });
    },
  });
}

export function useUpdateIncident() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateIncidentRequest }) =>
      monitoringApi.updateIncident(id, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'incidents'] });
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'operations'] });
    },
  });
}

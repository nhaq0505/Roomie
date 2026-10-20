import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { json, post, request } from '../../../shared/http/client';
import type {
  AdminCampaignCostItemDto,
  AdminMonitoringGrowthResponse,
  AdminMonitoringIncidentsResponse,
  AdminMonitoringOperationsResponse,
  AdminMonitoringRevenueResponse,
  CampaignCostFilterRequest,
  CreateCampaignRequest,
  IncidentDto,
  MarketingCampaignDto,
  MonitoringFilterRequest,
  OpsWindowPreset,
  UpdateCampaignRequest,
  UpdateIncidentRequest,
  UpsertCampaignCostRequest,
} from './types';

const patch = (body?: unknown): RequestInit => ({
  method: 'PATCH',
  ...(body === undefined ? {} : { body: json(body) }),
});

function buildQueryString(params: Record<string, string | number | boolean | undefined | null>): string {
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

  getOperations: (window: OpsWindowPreset = '1h') => {
    return request<AdminMonitoringOperationsResponse>(
      `/api/admin/monitoring/operations?window=${encodeURIComponent(window)}`
    );
  },

  getIncidents: () => {
    return request<AdminMonitoringIncidentsResponse>('/api/admin/monitoring/incidents');
  },

  updateIncident: (id: string, body: UpdateIncidentRequest) => {
    return request<IncidentDto>(`/api/admin/monitoring/incidents/${encodeURIComponent(id)}`, patch(body));
  },

  getCampaignCosts: (params?: CampaignCostFilterRequest) => {
    const qs = buildQueryString({
      fromDate: params?.fromDate,
      toDate: params?.toDate,
      campaign: params?.campaign,
    });
    return request<AdminCampaignCostItemDto[]>(`/api/admin/monitoring/campaigns/costs${qs}`);
  },

  upsertCampaignCost: (body: UpsertCampaignCostRequest) => {
    return request<AdminCampaignCostItemDto>('/api/admin/monitoring/campaigns/costs', post(body));
  },

  getCampaignMetadata: (params?: { activeOnly?: boolean; channel?: string; source?: string }) => {
    const qs = buildQueryString(params || {});
    return request<MarketingCampaignDto[]>(`/api/admin/monitoring/campaigns/metadata${qs}`);
  },

  createCampaignMetadata: (body: CreateCampaignRequest) => {
    return request<MarketingCampaignDto>('/api/admin/monitoring/campaigns/metadata', post(body));
  },

  updateCampaignMetadata: (id: string, body: UpdateCampaignRequest) => {
    return request<MarketingCampaignDto>(`/api/admin/monitoring/campaigns/metadata/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: json(body),
    });
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

export function useOperationsMonitoring(window: OpsWindowPreset = '1h', enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'operations', window],
    queryFn: () => monitoringApi.getOperations(window),
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

export function useCampaignCosts(params?: CampaignCostFilterRequest, enabled = true) {
  return useQuery({
    queryKey: ['monitoring', 'campaign-costs', params?.fromDate, params?.toDate, params?.campaign],
    queryFn: () => monitoringApi.getCampaignCosts(params),
    enabled,
    staleTime: 60 * 1000,
  });
}

export function useUpsertCampaignCost() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertCampaignCostRequest) => monitoringApi.upsertCampaignCost(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'campaign-costs'] });
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

export function useCampaignMetadata(params?: { activeOnly?: boolean; channel?: string; source?: string }) {
  return useQuery({
    queryKey: ['monitoring', 'campaigns-metadata', params],
    queryFn: () => monitoringApi.getCampaignMetadata(params),
    staleTime: 60 * 1000,
  });
}

export function useCreateCampaignMetadata() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCampaignRequest) => monitoringApi.createCampaignMetadata(body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'campaigns-metadata'] });
    },
  });
}

export function useUpdateCampaignMetadata() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCampaignRequest }) =>
      monitoringApi.updateCampaignMetadata(id, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['monitoring', 'campaigns-metadata'] });
    },
  });
}

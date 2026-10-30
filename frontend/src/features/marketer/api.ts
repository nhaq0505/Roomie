import { json, post, request } from '../../shared/http/client';
import type {
  MarketerArticleItem,
  MarketerBannerItem,
  MarketerEmailCampaignItem,
  MarketerEmailDispatchResponse,
  MarketerLeadNoteItem,
  MarketerLeadsResponse,
  UpsertMarketerArticleRequest,
  UpsertMarketerBannerRequest,
  UpsertMarketerEmailCampaignRequest,
  UpsertMarketerLeadProfileRequest,
} from './types';

const put = (body?: unknown): RequestInit => ({
  method: 'PUT',
  ...(body === undefined ? {} : { body: json(body) }),
});
const del = (): RequestInit => ({ method: 'DELETE' });

function buildQuery(params: Record<string, string | number | undefined | null>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') {
      q.set(k, String(v));
    }
  }
  const str = q.toString();
  return str ? `?${str}` : '';
}

export const marketerApi = {
  // Leads CRM
  listLeads: (params?: { segment?: string; search?: string; leadStatus?: string; page?: number; pageSize?: number }) =>
    request<MarketerLeadsResponse>(`/api/marketer/leads${buildQuery(params || {})}`),

  updateLeadProfile: (ownerId: string, body: UpsertMarketerLeadProfileRequest) =>
    request<{ ownerId: string; leadStatus: string }>(`/api/marketer/leads/${encodeURIComponent(ownerId)}/profile`, put(body)),

  listLeadNotes: (ownerId: string) =>
    request<MarketerLeadNoteItem[]>(`/api/marketer/leads/${encodeURIComponent(ownerId)}/notes`),

  addLeadNote: (ownerId: string, content: string) =>
    request<MarketerLeadNoteItem>(`/api/marketer/leads/${encodeURIComponent(ownerId)}/notes`, post({ content })),

  // Content CMS: Articles
  listArticles: () => request<MarketerArticleItem[]>('/api/marketer/articles'),

  createArticle: (body: UpsertMarketerArticleRequest) =>
    request<MarketerArticleItem>('/api/marketer/articles', post(body)),

  updateArticle: (id: string, body: UpsertMarketerArticleRequest) =>
    request<MarketerArticleItem>(`/api/marketer/articles/${encodeURIComponent(id)}`, put(body)),

  deleteArticle: (id: string) =>
    request<{ message: string }>(`/api/marketer/articles/${encodeURIComponent(id)}`, del()),

  // Public Guides
  listPublicGuides: () => request<MarketerArticleItem[]>('/api/public/guides'),

  getPublicGuideBySlug: (slug: string) =>
    request<MarketerArticleItem>(`/api/public/guides/${encodeURIComponent(slug)}`),

  recordPublicGuideCtaClick: (slug: string) =>
    request<{ ok: boolean }>(`/api/public/guides/${encodeURIComponent(slug)}/cta-click`, post({})),

  // Content CMS: Banners
  listBanners: () => request<MarketerBannerItem[]>('/api/marketer/banners'),

  createBanner: (body: UpsertMarketerBannerRequest) =>
    request<MarketerBannerItem>('/api/marketer/banners', post(body)),

  updateBanner: (id: string, body: UpsertMarketerBannerRequest) =>
    request<MarketerBannerItem>(`/api/marketer/banners/${encodeURIComponent(id)}`, put(body)),

  deleteBanner: (id: string) =>
    request<{ message: string }>(`/api/marketer/banners/${encodeURIComponent(id)}`, del()),

  listActiveBanners: (placement: 'LandingTop' | 'OwnerDashboard') =>
    request<MarketerBannerItem[]>(`/api/banners/active?placement=${encodeURIComponent(placement)}`),

  recordBannerClick: (id: string) =>
    request<{ ok: boolean }>(`/api/banners/${encodeURIComponent(id)}/click`, post({})),

  // Email Campaigns
  listEmails: () => request<MarketerEmailCampaignItem[]>('/api/marketer/emails'),

  createEmail: (body: UpsertMarketerEmailCampaignRequest) =>
    request<MarketerEmailCampaignItem>('/api/marketer/emails', post(body)),

  updateEmail: (id: string, body: UpsertMarketerEmailCampaignRequest) =>
    request<MarketerEmailCampaignItem>(`/api/marketer/emails/${encodeURIComponent(id)}`, put(body)),

  sendTestEmail: (id: string, toEmail: string) =>
    request<{ message: string }>(`/api/marketer/emails/${encodeURIComponent(id)}/send-test`, post({ toEmail })),

  // Returns the campaign state; 202 without a body means the background dispatch is still running.
  sendEmailCampaign: (id: string) =>
    request<MarketerEmailDispatchResponse | undefined>(`/api/marketer/emails/${encodeURIComponent(id)}/send`, post({})),
};

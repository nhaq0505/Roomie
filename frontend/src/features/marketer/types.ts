export type MarketerLeadNoteItem = {
  id: string;
  authorId: string;
  authorName: string;
  content: string;
  createdAt: string;
};

export type MarketerLeadItem = {
  ownerId: string;
  name: string;
  email: string;
  phone?: string | null;
  status: string;
  subscriptionTier: string;
  isPro: boolean;
  maxRooms: number;
  subscriptionExpiresAt?: string | null;
  buildingCount: number;
  roomCount: number;
  isActivated: boolean;
  utmSource?: string | null;
  utmMedium?: string | null;
  utmCampaign?: string | null;
  redeemedPromoCodes: string[];
  createdAt: string;
  lastActiveAt?: string | null;
  leadStatus: 'New' | 'Contacted' | 'Interested' | 'Converted' | 'Churned' | string;
  tags?: string | null;
  nextFollowUpAt?: string | null;
  lastContactedAt?: string | null;
  assignedMarketerId?: string | null;
  assignedMarketerName?: string | null;
  notesCount: number;
};

export type MarketerLeadsResponse = {
  items: MarketerLeadItem[];
  total: number;
  page: number;
  pageSize: number;
  segmentCounts: Record<string, number>;
};

export type UpsertMarketerLeadProfileRequest = {
  leadStatus: string;
  tags?: string | null;
  nextFollowUpAt?: string | null;
  markContactedNow?: boolean;
};

export type MarketerArticleItem = {
  id: string;
  slug: string;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: number;
  contentMarkdown: string;
  seoTitle?: string | null;
  seoDescription?: string | null;
  ctaText?: string | null;
  ctaUrl?: string | null;
  isPublished: boolean;
  publishedAt?: string | null;
  viewCount: number;
  ctaClickCount: number;
  authorName: string;
  updatedAt: string;
};

export type UpsertMarketerArticleRequest = {
  slug: string;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: number;
  contentMarkdown: string;
  seoTitle?: string | null;
  seoDescription?: string | null;
  ctaText?: string | null;
  ctaUrl?: string | null;
  isPublished: boolean;
};

export type MarketerBannerItem = {
  id: string;
  title: string;
  message: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  promoCode?: string | null;
  placement: 'LandingTop' | 'OwnerDashboard' | 'Both' | string;
  targetSegment: 'All' | 'FreeOnly' | 'NearRoomLimit' | 'ProExpiringSoon' | string;
  themeColor: string;
  isActive: boolean;
  startsAt?: string | null;
  endsAt?: string | null;
  impressionsCount: number;
  clicksCount: number;
  updatedAt: string;
};

export type UpsertMarketerBannerRequest = {
  title: string;
  message: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  promoCode?: string | null;
  placement: string;
  targetSegment: string;
  themeColor: string;
  isActive: boolean;
  startsAt?: string | null;
  endsAt?: string | null;
};

export type MarketerEmailCampaignItem = {
  id: string;
  name: string;
  segment: string;
  subject: string;
  bodyHtml: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  promoCode?: string | null;
  status: 'Draft' | 'Sending' | 'Completed' | 'Failed' | string;
  recipientCount: number;
  sentCount: number;
  failedCount: number;
  createdByName: string;
  createdAt: string;
  sentAt?: string | null;
};

export type UpsertMarketerEmailCampaignRequest = {
  name: string;
  segment: string;
  subject: string;
  bodyHtml: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  promoCode?: string | null;
};

export type MarketerEmailDispatchResponse = {
  campaignId: string;
  recipientCount: number;
  sentCount: number;
  failedCount: number;
  status: string;
};

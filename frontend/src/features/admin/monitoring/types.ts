// Exact contract-aligned types for Roomie Monitoring Subsystem
// Matches docs/monitoring/contracts.md and backend/RoomRental.Api/Domain/MonitoringDtos.cs

export type DateRangePreset = '7d' | '30d' | '90d' | 'custom';
export type OpsWindowPreset = '1h' | '6h' | '24h';

export interface MonitoringFilters {
  rangePreset: DateRangePreset;
  dateFrom: string;
  dateTo: string;
  source?: string;
  channel?: string;
  campaign?: string;
  opsWindow: OpsWindowPreset;
}

export interface MonitoringFilterRequest {
  fromDate?: string;
  toDate?: string;
  source?: string;
  channel?: string;
  campaign?: string;
}

// 1. Growth Tab Types
export interface GrowthSummaryDto {
  totalSessions: number;
  uniqueVisitors: number;
  registrations: number;
  activatedOwners: number;
  currentWau: number;
  currentMau: number;
}

export interface TrafficSourceStatsDto {
  source: string;
  medium?: string | null;
  sessions: number;
  registrations: number;
  activatedOwners: number;
}

export interface CtaPerformanceDto {
  ctaId: string;
  path: string;
  clicks: number;
}

export interface CohortRetentionRowDto {
  cohortPeriod: string; // e.g. "2026-08"
  cohortSize: number;
  retentionPercentages: number[];
}

export interface CohortMaturityDto {
  activationRate7Day: number | null;
  activationCohortMaturedCount: number;
  firstPaidRate30Day: number | null;
  paidCohortMaturedCount: number;
}

export interface DailyGrowthPointDto {
  date: string; // YYYY-MM-DD
  sessions: number;
  registrations: number;
  activations: number;
  activeOwners: number;
}

export interface AdminMonitoringGrowthResponse {
  summary: GrowthSummaryDto;
  sources: TrafficSourceStatsDto[];
  topCtas: CtaPerformanceDto[];
  retentionMatrix: CohortRetentionRowDto[];
  cohortMaturity: CohortMaturityDto;
  dailySeries: DailyGrowthPointDto[];
}

// 2. Revenue Tab Types
export type Cac30NullReason =
  | 'MissingCostData'
  | 'ZeroDenominator'
  | 'CohortImmature'
  | 'NoRegistrations'
  | string;

export interface Cac30MetricDto {
  valueVnd: number | null;
  isAvailable: boolean;
  nullReason: Cac30NullReason | null;
}

export interface RevenueSummaryDto {
  totalSubscriptionRevenueVnd: number;
  paidSubscriptionCount: number;
  totalMarketingSpendVnd: number;
  totalMarketingHours: number;
  cac30: Cac30MetricDto;
}

export interface SubscriptionOrderRevenueDto {
  orderCode: number;
  ownerId: string;
  amountVnd: number;
  days: number | null;
  paidAt: string;
}

export interface CampaignCostSummaryDto {
  campaign: string;
  channel?: string | null;
  cashVnd: number;
  personHours: number;
  imputedLaborCostVnd?: number | null;
  totalCostVnd: number;
}

export interface DailyRevenuePointDto {
  date: string; // YYYY-MM-DD
  subscriptionRevenueVnd: number;
  marketingSpendVnd: number;
}

export interface AdminMonitoringRevenueResponse {
  summary: RevenueSummaryDto;
  subscriptionOrders: SubscriptionOrderRevenueDto[];
  campaignCosts: CampaignCostSummaryDto[];
  dailySeries: DailyRevenuePointDto[];
}

// 3. Operations Tab Types
export type HealthStatus = 'Healthy' | 'Warning' | 'Critical' | 'Degraded' | 'Unknown' | 'Stale';

export interface OpsOverviewDto {
  overallStatus: string;
  api5xxRatePercent: number;
  latencyP95Ms: number;
  totalRequestsLastHour: number;
  databaseConnected: boolean;
  databaseStatus: string;
}

export interface RouteLatencyMetricDto {
  routeTemplate: string;
  httpMethod: string;
  requestCount: number;
  status5xxCount: number;
  p95LatencyMs: number;
  avgLatencyMs: number;
  maxLatencyMs: number;
}

export interface HostMetricsDto {
  cpuPercent: number;
  memoryPercent: number;
  diskPercent: number;
  containerCount: number;
  healthEndpointStatus: string;
  hostStatus: string;
  recordedAt: string | null;
  isStale: boolean;
}

export interface WorkerStatusDto {
  workerName: string;
  isEnabled: boolean;
  status: string;
  lastStartedAt: string | null;
  lastSuccessAt: string | null;
  lastErrorAt: string | null;
  lastErrorMessage: string | null;
  isStale: boolean;
}

export interface OperationalQueueStatsDto {
  zaloPendingQueueCount: number;
  zaloEligibleWindowCount: number;
  payOsPendingReconcileCount: number;
  smtpAcceptedNotDeliveredCount: number;
}

export interface AdminMonitoringOperationsResponse {
  overview: OpsOverviewDto;
  routeMetrics: RouteLatencyMetricDto[];
  hostMetrics: HostMetricsDto | null;
  workers: WorkerStatusDto[];
  queues: OperationalQueueStatsDto;
}

// 4. Incidents Types
export interface IncidentDto {
  id: string;
  incidentKey: string;
  ruleName: string;
  severity: 'Warning' | 'Critical' | string;
  state: 'Active' | 'Cooldown' | 'Resolved' | string;
  summary: string;
  details: string;
  startedAt: string;
  lastNotifiedAt: string | null;
  resolvedAt: string | null;
  notificationCount: number;
}

export interface AdminMonitoringIncidentsResponse {
  activeIncidentCount: number;
  incidents: IncidentDto[];
}

export interface UpdateIncidentRequest {
  state: string;
  note?: string | null;
}

// 5. Campaign Cost Management Types
export interface AdminCampaignCostItemDto {
  id: string;
  date: string; // YYYY-MM-DD
  campaign: string;
  channel?: string | null;
  source?: string | null;
  cashVnd: number;
  personHours: number;
  hourlyRate?: number | null;
  note?: string | null;
  updatedByName: string;
  updatedAt: string;
}

export interface UpsertCampaignCostRequest {
  date: string; // YYYY-MM-DD
  campaign: string;
  channel?: string | null;
  source?: string | null;
  cashVnd: number;
  personHours: number;
  hourlyRate?: number | null;
  note?: string | null;
}

// UTM Builder Types
export interface UtmLinkParams {
  baseUrl: string;
  source: string;
  medium?: string;
  campaign?: string;
  term?: string;
  content?: string;
}

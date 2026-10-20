// Modular Monitoring Types
// Aligned for independent frontend preparation prior to Task T2 backend contract wiring.

export type DateRangePreset = '7d' | '30d' | '90d' | 'custom';
export type OpsWindowPreset = '1h' | '6h' | '24h';

export interface MonitoringFilters {
  rangePreset: DateRangePreset;
  dateFrom: string;
  dateTo: string;
  source?: string;
  opsWindow: OpsWindowPreset;
}

// 1. Growth & Usage Types
export interface SessionSummary {
  totalSessions: number | null;
  uniqueVisitors: number | null;
  bounceRatePercent: number | null;
  avgDurationSeconds: number | null;
  isStale?: boolean;
}

export interface SourceTrafficItem {
  source: string;
  medium?: string;
  campaign?: string;
  views: number;
  signups: number;
  conversionRatePercent: number;
}

export interface CtaEngagementItem {
  ctaId: string;
  label: string;
  placement: string;
  clicks: number;
  conversions: number;
  conversionRatePercent: number;
}

export interface ActiveUsersMetric {
  wau7: number | null;
  mau30: number | null;
  breakdown: {
    ownerActions: number;
    adminActions: number;
    systemActions: number;
  };
  isStale?: boolean;
}

export interface ActivationCohortRow {
  cohortMonth: string; // e.g. "2026-09"
  registeredOwners: number;
  activated7DayCount: number;
  activated7DayRate: number;
  isMature7Day: boolean;
  nextMonthRetainedCount: number | null;
  nextMonthRetentionRate: number | null;
  isMatureRetention: boolean;
}

// 2. Revenue Types
export interface RevenueSummary {
  mrrVnd: number | null;
  totalProRevenueVnd: number | null;
  paidSubscriptionCount: number | null;
  isStale?: boolean;
}

export type Cac30MissingReason = 'missing_spend' | 'zero_cohort' | 'unmature_cohort';

export interface Cac30CohortItem {
  cohortMonth: string; // "2026-09"
  totalSpendVnd: number | null;
  newFirstPaidOwnersCount: number;
  cacVnd: number | null;
  missingReason?: Cac30MissingReason;
  isMature30Day: boolean;
}

export interface CampaignCostEntry {
  id: string;
  campaignName: string;
  channel?: string;
  recordedDate: string;
  cashExpenseVnd: number;
  personHours: number;
  hourlyRateVnd?: number;
  totalCostVnd: number;
  updatedBy: string;
  updatedAt: string;
  notes?: string;
}

export interface CreateCampaignCostInput {
  campaignName: string;
  channel?: string;
  recordedDate: string;
  cashExpenseVnd: number;
  personHours: number;
  hourlyRateVnd?: number;
  notes?: string;
  updatedBy: string;
}

// 3. Operations Types
export type HealthStatus = 'healthy' | 'warning' | 'critical' | 'unknown' | 'stale';

export interface SystemdHostMetrics {
  collectedAt: string | null;
  cpuPercent: number | null;
  ramPercent: number | null;
  ramUsedMb: number | null;
  ramTotalMb: number | null;
  diskPercent: number | null;
  diskUsedGb: number | null;
  diskTotalGb: number | null;
  containerStatus: 'running' | 'degraded' | 'stopped' | 'unknown';
  apiHealthOk: boolean | null;
  isStale: boolean;
}

export interface RouteMetricItem {
  routeTemplate: string;
  method: string;
  totalRequests: number;
  error5xxCount: number;
  errorRatePercent: number;
  p95LatencyMs: number | null;
  avgLatencyMs: number | null;
}

export interface DatabaseHealthMetric {
  status: HealthStatus;
  latencyMs: number | null;
  poolActive: number | null;
  poolIdle: number | null;
  lastCheckedAt: string | null;
}

export interface SmtpMetrics {
  status: HealthStatus;
  acceptedCount: number | null;
  deliveredCount: number | null;
  undeliveredCount: number | null;
  isStale?: boolean;
}

export interface PayOsReconcileMetrics {
  status: HealthStatus;
  reconciledCount: number | null;
  pendingCount: number | null;
  errorCount: number | null;
  lastReconciledAt: string | null;
}

export interface ZaloQueueMetrics {
  status: HealthStatus;
  queuedCount: number | null;
  sentCount: number | null;
  cooldownActive: boolean;
  inOperatingHoursWindow: boolean; // 07:00 - 21:00
  rateLimited: boolean;
}

export interface BackgroundWorkerItem {
  name: string;
  key: 'recovery_email' | 'zalo_reminders' | 'payos_reconcile' | 'invoice_sweep';
  enabled: boolean;
  lastStart: string | null;
  lastSuccess: string | null;
  lastError: string | null;
  status: 'idle' | 'running' | 'failed' | 'disabled' | 'unknown';
}

export interface BackupWatchdogMetric {
  status: HealthStatus;
  lastBackupAt: string | null;
  hoursSinceLastBackup: number | null;
  isWithin27Hours: boolean;
}

export interface IncidentAlertItem {
  id: string;
  title: string;
  severity: 'warning' | 'critical';
  component: string;
  description: string;
  triggeredAt: string;
  state: 'active' | 'cooldown' | 'resolved';
  cooldownRemainingMinutes?: number;
}

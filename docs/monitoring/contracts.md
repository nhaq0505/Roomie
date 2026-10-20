# Monitoring Contracts and Metric Definitions

Document Revision: 2026-10-20  
Task: T0 Foundation  
Worktree: `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/monitoring-core`  
Branch: `nhaq0505/monitoring-core`  

---

## 1. Overview and Architecture

The monitoring subsystem provides an internal, privacy-first, first-party analytics and operations tracking system for Roomie (`/admin/monitoring`). It operates without third-party tracking scripts, cookies, or external analytics clouds.

### 1.1 Team Ownership Boundaries

| Component / Task | Owner | Scope & Deliverables |
|---|---|---|
| **T0 Foundation** (Current) | Worker T0 | Central database schema, migrations for SQLite & PostgreSQL, domain entities, DTOs, core client event intake, milestone & activity tracker, DI extension points (`AddMonitoringCore`), base contracts and this contract specification. |
| **T1 Business Metrics** | Worker T1 | Backend implementations of `IGrowthMonitoringQueryService` and `IRevenueMonitoringQueryService`, funnel aggregation, activation cohort retention, CAC30 calculation with null reasons, campaign spend analytics. |
| **T2 Frontend UI** | Worker B | React / Ant Design dashboard UI tabs at `/admin/monitoring` (Growth + Usage, Revenue, Operations), SVG charts (no third-party chart libraries), i18n keys via `t()`. |
| **T3 Operations & Alerting** | Worker C | Backend implementations of `IOperationsMonitoringService`, route latency minute bucketing middleware, worker telemetry instrumentation, systemd host collector JSON mount reader, incident state engine, SMTP alert notifier. |

> [!IMPORTANT]
> T0 owns the central schema and EF Core migrations. Dependent workers (T1, B, C) branch from T0's immutable foundation commit. Worker C consumes the ops tables (`RouteMinuteBuckets`, `WorkerHeartbeats`, `HostMetrics`, `MonitoringIncidents`, `OperationalCounters`) and must not generate competing migrations.

---

## 2. API Contracts & Endpoint Specifications

### 2.1 Public Client Analytics Intake

- **Route:** `POST /api/analytics/events`
- **Authorization:** Anonymous or Authenticated Owner
- **Rate Limit:** Policy `analytics` (120 req/min prod, 600 req/min dev)
- **Same-Origin Enforcement:** Enforced even for anonymous requests (`Origin` or `Referer` header must match server origin; foreign origins rejected with `403 Forbidden`).
- **Privacy & Safety Rules:** 
  - URL paths stripped of query parameters (`?`) and URL fragments (`#`).
  - Absolute raw URLs (`https://...`) normalized to relative paths.
  - Dangerous schemes (`javascript:`, `data:`, `mailto:`) rejected.
  - Reset tokens and challenge tokens in path normalized to `:token`; GUIDs normalized to `:id`. Max path length 128 chars.
  - Event names and CTA identifiers restricted to bounded safe identifiers (max 64 chars; no PII or free-text injections).
  - UTM parameters and labels validated and sanitized: no PII (`@` emails, phone numbers, or tokens); max length 64 chars.
  - Client timestamps bounded and validated against server timing; server authoritative timestamp (`ServerTimestamp`) attached on receipt.
  - Batch size bounded to maximum 100 events per request; null or empty collections handled gracefully.
- **Idempotency & Deduping:**
  - Every event item requires an `eventId` (max 64 chars).
  - Network retries with the same `eventId` are safely deduped without error (`dedupedCount` returned in response).
  - Distinct real clicks/actions with unique `eventId`s are preserved.

#### Request Body (`AnalyticsEventBatchRequest`)
```json
{
  "browserId": "c4b18f8e-d4c3-4d82-841f-11758f504381",
  "sessionId": "s-882f07ab-3f41",
  "events": [
    {
      "eventId": "evt-c4b18f8e-1",
      "eventType": "page_view",
      "eventName": "pricing_page",
      "path": "/pricing",
      "referrerPath": "/home",
      "ctaId": "btn_hero_signup",
      "utmSource": "facebook",
      "utmMedium": "cpc",
      "utmCampaign": "fall_promo",
      "utmContent": "variant_a",
      "utmTerm": "room_management",
      "clientTimestamp": "2026-10-20T08:50:00Z"
    }
  ]
}
```

#### Allowlisted `eventType` Values
- `page_view`
- `cta_click`
- `session_start`
- `landing_view`
- `pricing_view`
- `auth_attempt`
- `registration_submit`

*(Generic `custom` event type is eliminated to prevent allowlist bypass)*

#### Response Body (`AnalyticsEventBatchResponse`) — `200 OK`
```json
{
  "processedCount": 1,
  "rejectedCount": 0,
  "dedupedCount": 0
}
```

---

### 2.2 Registration Attribution Extension & Funnel Lifecycle Facts

To carry campaign attribution through registration, `RegisterRequest` supports an `attribution` object with independent `firstTouch` and `lastTouch` non-direct touchpoints:

```json
{
  "name": "Nguyen Van A",
  "email": "owner@example.com",
  "password": "Password123!",
  "phone": "0901234567",
  "attribution": {
    "browserId": "c4b18f8e-d4c3-4d82-841f-11758f504381",
    "firstTouch": {
      "source": "google",
      "medium": "cpc",
      "campaign": "fall_launch",
      "landingPage": "/pricing",
      "referrer": "https://google.com/search",
      "occurredAt": "2026-10-10T08:00:00Z"
    },
    "lastTouch": {
      "source": "facebook",
      "medium": "retargeting",
      "campaign": "retargeting_q4",
      "landingPage": "/dang-ky",
      "referrer": "https://facebook.com",
      "occurredAt": "2026-10-20T08:50:00Z"
    }
  }
}
```

- **Independent Touches:** First touch and last-known non-direct touch are tracked independently with their own server-validated timing (`occurredAt <= ServerNow`). First touch is never blindly duplicated into last touch.
- **Backward-Compatible Flat Fields:** Flat fields (`utmSource`, `utmCampaign`, `landingPage`, etc.) map to `firstTouch` if nested objects are absent.
- **Durable Funnel Facts (`RegistrationLifecycleFacts`):**
  - Append-only authoritative fact records track registration progress through stages: `Started`, `CodeSent`, `CodeResent`, `Verified`, `Bypassed`.
  - Facts persist independently of challenge token resets or resends.
  - Attribution is linked to the registration attempt and carried through to `RegistrationAttributions` upon verification.
- **Auto-Approval:** All registered owners are auto-approved (`AutoApproved = true`).

---

### 2.3 Admin Monitoring Endpoints (`/api/admin/monitoring/*`)

All `/api/admin/monitoring/*` endpoints require the `Admin` role (`401 Unauthorized` for unauthenticated requests, `403 Forbidden` for non-Admin owners).

#### 2.3.1 Growth Tab Metrics
- **Route:** `GET /api/admin/monitoring/growth`
- **Query Parameters:**
  - `fromDate` (optional, `YYYY-MM-DD`)
  - `toDate` (optional, `YYYY-MM-DD`)
  - `source` (optional, string)
  - `channel` (optional, string)
  - `campaign` (optional, string)
- **Status in T1:** Fully implemented via `IGrowthMonitoringQueryService`.
- **Funnel Facts (`funnel`):** Exposes `RegistrationFunnelDto` (`startedCount`, `codeResentCount`, `verifiedCount`, `bypassedCount`, `unknownAttemptCount`) from server-authoritative `RegistrationFacts`. Funnel counts strictly apply the active `source`, `channel`, and `campaign` filters rather than global counts; `unknownAttemptCount` explicitly tracks unattributed registration attempts in the period.
- **Retention Semantics:** `retentionPercentages` is `IReadOnlyList<double?>`. Incomplete current calendar month and future cohort cells are `null`, never mature `0.0`.
- **Channel Mapping & Touchpoint Credit:** Filter parameters (`source`, `channel`, `campaign`) are evaluated consistently across all growth metrics, retention, WAU, MAU, revenue orders, CAC, and campaign costs. A touchpoint is evaluated as a cohesive unit (`FirstTouch` or `LastTouch`); filters never mix `FirstTouchSource` with unrelated `LastTouchCampaign`. Channel maps to attribution medium or campaign channel metadata.
- **Raw Coverage, 13-Month Continuity & Null Semantics:**
  - Inside the 90-day raw retention window, raw telemetry is proven present; numeric 0 is a real zero and never falls back to aggregates.
  - Beyond 90 days (where raw `AnalyticsEvents` are pruned), aggregates are used only when matching dimensions exist (unfiltered all-traffic queries). When dimension filters (`source`, `channel`, `campaign`) are active, daily series sessions and period total sessions return explicit `null` (unavailable) rather than false zeros or all-traffic counts.
  - Period `uniqueVisitors` returns `null` across ranges with missing raw coverage because daily distinct visitors cannot mathematically sum to period distinct visitors.
  - `TrafficSourceStatsDto.sessions` is `null` when raw coverage is pruned.
- **Date Range & Query Bounds:**
  - Date parameters are strictly clamped: `toDate` cannot exceed `today` (future dates clamped), `fromDate` lookback clamped to 400 days (~13 months), and span clamped to maximum 365 days.
  - Database queries are bounded with explicit `.Take(...)` caps preventing unbounded in-memory collection.
- **Response Shape (`AdminMonitoringGrowthResponse`):**
```json
{
  "summary": {
    "totalSessions": 1420,
    "uniqueVisitors": 980,
    "registrations": 45,
    "activatedOwners": 28,
    "currentWau": 18,
    "currentMau": 32
  },
  "sources": [
    {
      "source": "facebook",
      "medium": "cpc",
      "sessions": 850,
      "registrations": 30,
      "activatedOwners": 20
    }
  ],
  "topCtas": [
    {
      "ctaId": "btn_hero_signup",
      "path": "/",
      "clicks": 142
    }
  ],
  "retentionMatrix": [
    {
      "cohortPeriod": "2026-08",
      "cohortSize": 25,
      "retentionPercentages": [100.0, 72.0, 64.0, null]
    }
  ],
  "cohortMaturity": {
    "activationRate7Day": 62.2,
    "activationCohortMaturedCount": 45,
    "firstPaidRate30Day": 24.4,
    "paidCohortMaturedCount": 45
  },
  "dailySeries": [
    {
      "date": "2026-10-20",
      "sessions": 120,
      "registrations": 4,
      "activations": 3,
      "activeOwners": 15
    }
  ]
}
```

#### 2.3.2 Revenue Tab Metrics
- **Route:** `GET /api/admin/monitoring/revenue`
- **Query Parameters:** `fromDate`, `toDate`, `source`, `channel`, `campaign`
- **Status in T1:** Fully implemented via `IGrowthMonitoringQueryService`.
- **Metric Invariants:**
  - Pro revenue includes ONLY server-confirmed, paid subscription attempts (`PayOsAttempt` with `Kind == "Subscription"` and `Status == PaymentAttemptStatus.Paid`).
  - Unapproved or speculative `mrrVnd` is explicitly removed from `RevenueSummaryDto`.
  - Tenant rent and admin-granted complimentary Pro tiers are strictly excluded from revenue metrics.
  - CAC30 = Spend in signup cohort period / New first-paid owners within 30 days. When costs are missing, labor valuation is missing (person-hours logged without an hourly rate), or the denominator is zero, CAC returns `null` with an explicit reason (`MissingCostData`, `MissingValuation`, `ZeroDenominator`, `CohortImmature`, `NoRegistrations`). Omitted hourly rate cannot invent valuation. Cash is real spend, person-hours are logged separately, and imputed labor is null unless an explicit hourly rate is entered.
- **Response Shape (`AdminMonitoringRevenueResponse`):**
```json
{
  "summary": {
    "totalSubscriptionRevenueVnd": 15000000.0,
    "paidSubscriptionCount": 15,
    "totalMarketingSpendVnd": 5000000.0,
    "totalMarketingHours": 42.5,
    "cac30": {
      "valueVnd": 416666.67,
      "isAvailable": true,
      "nullReason": null
    }
  },
  "subscriptionOrders": [
    {
      "orderCode": 100234,
      "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "amountVnd": 299000,
      "days": 30,
      "paidAt": "2026-10-18T10:30:00Z"
    }
  ],
  "campaignCosts": [
    {
      "campaign": "facebook_october",
      "channel": "social",
      "cashVnd": 3500000.0,
      "personHours": 20.0,
      "imputedLaborCostVnd": 3000000.0,
      "totalCostVnd": 6500000.0
    }
  ],
  "dailySeries": [
    {
      "date": "2026-10-20",
      "subscriptionRevenueVnd": 598000.0,
      "marketingSpendVnd": 250000.0
    }
  ]
}
```

#### 2.3.3 Operations Tab Metrics
- **Route:** `GET /api/admin/monitoring/operations?window=1h`
- **Query Parameters:** `window` (optional, default `"1h"`, e.g. `"1h"`, `"24h"`, `"7d"`)
- **Status in T0:** Explicitly `501 Not Implemented` (implemented in T3).
- **Invariants:** 
  - Missing or unknown host/queue metrics return `null`, never healthy zeros.
  - Host metric doubles (`cpuPercent`, `memoryPercent`, `diskPercent`) and queue counts are nullable (`double?`, `int?`, `long?`).
- **Response Shape (`AdminMonitoringOperationsResponse`):**
```json
{
  "overview": {
    "overallStatus": "Healthy",
    "api5xxRatePercent": 0.02,
    "latencyP95Ms": 145.0,
    "totalRequestsLastHour": 8420,
    "databaseConnected": true,
    "databaseStatus": "ok",
    "window": "1h"
  },
  "routeMetrics": [
    {
      "routeTemplate": "/api/invoices/{id}",
      "httpMethod": "GET",
      "requestCount": 1250,
      "status5xxCount": 0,
      "p95LatencyMs": 85.0,
      "avgLatencyMs": 32.4,
      "maxLatencyMs": 310.0
    }
  ],
  "hostMetrics": {
    "cpuPercent": 24.5,
    "memoryPercent": 48.2,
    "diskPercent": 55.1,
    "containerCount": 3,
    "healthEndpointStatus": "ok",
    "hostStatus": "Healthy",
    "recordedAt": "2026-10-20T08:54:00Z",
    "isStale": false
  },
  "workers": [
    {
      "workerName": "recovery-email",
      "isEnabled": true,
      "status": "Healthy",
      "lastStartedAt": "2026-10-20T08:50:00Z",
      "lastSuccessAt": "2026-10-20T08:50:05Z",
      "lastErrorAt": null,
      "lastErrorMessage": null,
      "isStale": false
    }
  ],
  "queues": {
    "zaloPendingQueueCount": 2,
    "zaloEligibleWindowCount": 2,
    "payOsPendingReconcileCount": 0,
    "smtpAcceptedNotDeliveredCount": 0
  }
}
```

#### 2.3.4 Incidents
- **Routes:** 
  - `GET /api/admin/monitoring/incidents`
  - `PATCH /api/admin/monitoring/incidents/{id}`
- **Status in T0:** Explicitly `501 Not Implemented` (implemented in T3).
- **Incident Rules and Starting Thresholds:**
  1. `app_db_unreachable`: App/DB 3 failures 1 min apart.
  2. `api_5xx_rate`: API 5xx > 5% over 5 min (minimum 100 requests).
  3. `api_p95_latency`: API p95 > 2s over 10 min (minimum 100 requests).
  4. `host_resource_high`: Host CPU or RAM > 85% for 10 min.
  5. `host_disk_high`: Host Disk > 85% for 5 min.
  6. `worker_stalled`: Enabled workers exceeding SLA (recovery-email: 5 min, Zalo: 5 min, reconcile: 10 min, invoice-sweep: 26 h).
- **Reminder & Recovery Rules:** Reminders cooldown <= 1 hour; recovery notice dispatched upon incident resolution.

#### 2.3.5 Campaign Metadata CRUD
- **Routes:**
  - `GET /api/admin/monitoring/campaigns/metadata?activeOnly=true&channel=...&source=...`
  - `POST /api/admin/monitoring/campaigns/metadata`
  - `PUT /api/admin/monitoring/campaigns/metadata/{id}`
- **Status in T0:** Implemented in T0 foundation!
- **Create Campaign Request Body (`CreateCampaignRequest`):**
```json
{
  "code": "google_search_q4",
  "name": "Google Search Q4 Brand",
  "channel": "google",
  "source": "search",
  "utmCampaign": "q4_brand",
  "utmMedium": "cpc",
  "utmSource": "google",
  "targetUrl": "https://roomie.io.vn/pricing",
  "isActive": true
}
```
- **Response Body (`MarketingCampaignDto`):** Returns persisted metadata record with audit info. Allows campaigns to exist before spend data is entered. Unique index enforced on `Code`.

#### 2.3.6 Campaign Costs Management
- **Routes:**
  - `GET /api/admin/monitoring/campaigns/costs?fromDate=...&toDate=...&campaign=...`
  - `POST /api/admin/monitoring/campaigns/costs`
- **Status in T0:** Implemented in T0 foundation!
- **Upsert Request Body (`UpsertCampaignCostRequest`):**
```json
{
  "date": "2026-10-20",
  "campaign": "google_search_q4",
  "channel": "google",
  "source": "search",
  "cashVnd": 1500000,
  "personHours": 3.5,
  "hourlyRate": 200000,
  "note": "A/B test ad group 1"
}
```
- **Response Body (`AdminCampaignCostItemDto`):** Returns persisted entity with audit updater info (`UpdatedByName`, `UpdatedAt`).

#### 2.3.7 Campaign Tracking URL Generation
- **Route:** `GET /api/admin/monitoring/campaigns/generate-url`
- **Query Parameters:** `targetUrl` (required), `source`, `medium`, `campaign`, `term`, `content`
- **Status in T1:** Fully implemented via `IRevenueMonitoringQueryService.BuildTrackingUrl`.
- **Response Shape:**
```json
{
  "url": "https://roomie.io.vn/pricing?utm_source=facebook&utm_medium=cpc&utm_campaign=summer_promo&utm_content=banner_v1"
}
```

#### 2.3.8 Maintenance Operations
- **Routes:**
  - `POST /api/admin/monitoring/maintenance/rollup` - Aggregates daily metrics into `DailyAggregateMetrics` for a date (default: yesterday in Vietnam calendar).
  - `POST /api/admin/monitoring/maintenance/prune` - Prunes raw web analytics older than 90 days, owner daily activities older than 13 months, and daily aggregate metrics older than 13 months. `OwnerMilestones` are preserved indefinitely and never pruned.
- **Status in T1:** Fully implemented via `IMonitoringMaintenanceService`.

---

## 3. Data Semantics and Retention Rules

### 3.1 Timezone & Calendar Semantics
- **Business Date Standard:** All business days, reading periods, and cohorts are anchored to Vietnam Time (`Asia/Ho_Chi_Minh`, UTC+7) using `VietnamCalendar` and `ApiSupport.VietnamDate`.
- **Database Storage:** All instant timestamps are persisted as UTC `DateTimeOffset` (converted to binary integer in SQLite per existing architecture).

### 3.2 Milestones and Activity Definitions
- **Activation Milestone:** An owner activates when their first **Monthly** invoice enters the `Issued` state (either created directly as Issued or published from Draft). Deposit invoices and Draft invoices do not activate an owner. Activation happens once per owner lifecycle and is preserved while the owner exists.
- **First Paid Milestone:** Recorded when an owner's first Pro subscription order is confirmed Paid. Excludes complimentary Admin grants and tenant payments.
- **Milestone Fallback (Historical Owners):** When an un-tracked existing owner triggers their first activation or payment milestone, `RegisteredAt` is set using the authoritative historical registration date from `Users.CreatedAt` (or remains `null` if unknown). `RegisteredAt = now` is NEVER fabricated.
- **Meaningful Owner Activity & Actor Separation:** Daily activity distinguishes `ActorType.Owner`, `ActorType.Admin`, and `ActorType.System`.
  - System background tasks (e.g., automated invoice generation sweeps, payment webhook reconciliations) pass `ActorType.System` to milestone tracking and activity recording.
  - System actions increment `SystemActionCount` and do NOT increment `OwnerActionCount` or set `HasOwnerAction = true`.
  - Only actions performed explicitly by the Owner themselves (`ActorType.Owner`) increment `OwnerActionCount` and set `HasOwnerAction = true`.
  - WAU (last 7 Vietnam calendar days) and MAU (last 30 Vietnam calendar days) count only distinct owners where `HasOwnerAction == true`.
  - Monthly retention matrix calculates whether owners in an activation cohort had `HasOwnerAction == true` in subsequent calendar months.
- **Committed Success Paths & Isolation:** All milestone tracking and activity hooks are dispatched strictly on committed success paths (after `transaction.CommitAsync`). Independent DbContext scopes never execute inside open SQLite transactions, avoiding deadlocks and lock contention. Telemetry failures are suppressed and cannot abort core business workflows.
- **Authoritative Provider Timestamps:** Payment webhooks and reconciliations run under `ActorType.System` and record the provider's authoritative persisted `PaidAt` timestamp rather than a fabricated callback arrival instant. Milestone tracking deduplicates repeated webhook signals and manual reconciliations idempotently.

### 3.3 Data Retention Policies
- **Raw Web Events (`AnalyticsEvents`):** 90 days. Pruned by background maintenance.
- **Operational Minute Buckets (`RouteMinuteBuckets`):** 30 days.
- **Owner Daily Activities & Daily Aggregates (`OwnerDailyActivities`, `DailyAggregateMetrics`):** 13 months.
- **Owner Milestones (`OwnerMilestones`):** Retained as long as the owner user exists (`ON DELETE CASCADE`).

---

## 4. Service Interfaces and Registration Extension Points

Monitoring services are registered via the extension method:
```csharp
builder.Services.AddMonitoringCore(builder.Configuration);
```

### 4.1 Interface Signatures

```csharp
namespace RoomRental.Api.Features.Monitoring;

public interface IClientAnalyticsService
{
    Task<AnalyticsEventBatchResponse> RecordClientEventsAsync(
        AnalyticsEventBatchRequest request,
        Guid? authenticatedOwnerId,
        string? clientIp,
        CancellationToken ct);
}

public interface IOwnerMilestoneTracker
{
    Task TrackRegistrationStartedAsync(
        Guid attemptId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct);

    Task TrackRegistrationCompletedAsync(
        Guid attemptId,
        Guid ownerId,
        string stage,
        CancellationToken ct);

    Task TrackOwnerRegisteredAsync(
        Guid ownerId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct);

    Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        ActorType actor = ActorType.Owner,
        CancellationToken ct = default);

    Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        ActorType actor = ActorType.Owner,
        CancellationToken ct = default);

    Task RecordOwnerActivityAsync(
        Guid ownerId,
        ActorType actorType,
        string actionType,
        CancellationToken ct);
}

public interface IGrowthMonitoringQueryService
{
    Task<AdminMonitoringGrowthResponse> GetGrowthMetricsAsync(
        MonitoringFilterRequest filter,
        CancellationToken ct);
}

public interface IRevenueMonitoringQueryService
{
    Task<AdminMonitoringRevenueResponse> GetRevenueMetricsAsync(
        MonitoringFilterRequest filter,
        CancellationToken ct);

    Task<IReadOnlyList<MarketingCampaignDto>> GetCampaignsAsync(
        bool? activeOnly,
        string? channel,
        string? source,
        CancellationToken ct);

    Task<MarketingCampaignDto> CreateCampaignAsync(
        CreateCampaignRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct);

    Task<MarketingCampaignDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct);

    Task<IReadOnlyList<AdminCampaignCostItemDto>> GetCampaignCostsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? campaign,
        CancellationToken ct);

    Task<AdminCampaignCostItemDto> UpsertCampaignCostAsync(
        UpsertCampaignCostRequest request,
        Guid adminUserId,
        string adminUserName,
        CancellationToken ct);
}

public interface IOperationsMonitoringService
{
    Task<AdminMonitoringOperationsResponse> GetOperationsMetricsAsync(
        string? window,
        CancellationToken ct);

    Task RecordRouteMetricAsync(
        string routeTemplate,
        string httpMethod,
        int statusCode,
        double latencyMs,
        CancellationToken ct);
}

public interface IWorkerStateTracker
{
    Task RecordWorkerHeartbeatAsync(
        string workerName,
        bool isEnabled,
        string status,
        string? errorMessage,
        CancellationToken ct);

    Task<IReadOnlyList<WorkerStatusDto>> GetWorkerStatusesAsync(
        CancellationToken ct);
}

public interface IHostMetricsReader
{
    Task<HostMetricsDto?> ReadHostMetricsAsync(
        CancellationToken ct);
}

public interface IIncidentService
{
    Task<AdminMonitoringIncidentsResponse> GetIncidentsAsync(
        CancellationToken ct);

    Task<IncidentDto> UpdateIncidentStateAsync(
        Guid id,
        UpdateIncidentRequest request,
        CancellationToken ct);

    Task TriggerIncidentCheckAsync(
        CancellationToken ct);
}

public interface IIncidentNotifier
{
    Task SendIncidentAlertAsync(
        MonitoringIncident incident,
        bool isRecovery,
        CancellationToken ct);
}
```

### 4.2 Extension by Worker C and Worker T1

All query and operations service registrations use `TryAddScoped`, allowing downstream workers to register their concrete implementations in `Program.cs` or custom extension methods without conflicting with the foundation registrations:

```csharp
// Example Worker C ops registration
builder.Services.AddScoped<IOperationsMonitoringService, OperationsMonitoringService>();
builder.Services.AddScoped<IHostMetricsReader, LinuxHostCollectorMetricsReader>();
builder.Services.AddScoped<IIncidentService, IncidentEngineService>();
builder.Services.AddScoped<IIncidentNotifier, SmtpIncidentNotifier>();
```

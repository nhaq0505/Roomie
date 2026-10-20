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
  - No PII (emails, phone numbers, passwords, OTPs, raw tokens, IP addresses, bank account numbers) stored.
  - Client timestamps bounded; server authoritative timestamp (`ServerTimestamp`) attached on receipt.
  - Batch size bounded to maximum 50 events per request.

#### Request Body (`AnalyticsEventBatchRequest`)
```json
{
  "browserId": "c4b18f8e-d4c3-4d82-841f-11758f504381",
  "sessionId": "s-882f07ab-3f41",
  "events": [
    {
      "eventType": "page_view",
      "eventName": "Pricing Page",
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
- `custom`

#### Response Body (`AnalyticsEventBatchResponse`) — `200 OK`
```json
{
  "processedCount": 1,
  "rejectedCount": 0
}
```

---

### 2.2 Registration Attribution Extension

To carry campaign attribution through registration, `RegisterRequest` supports an optional backward-compatible `attribution` object:

```json
{
  "name": "Nguyen Van A",
  "email": "owner@example.com",
  "password": "Password123!",
  "phone": "0901234567",
  "attribution": {
    "browserId": "c4b18f8e-d4c3-4d82-841f-11758f504381",
    "utmSource": "facebook",
    "utmMedium": "cpc",
    "utmCampaign": "fall_promo",
    "utmContent": "variant_a",
    "utmTerm": "room_management",
    "landingPage": "/landing",
    "referrer": "https://facebook.com"
  }
}
```

- **OTP Carry-Through:** When email verification is required, `RegistrationVerification` reserves the attribution context. Upon successful OTP verification (`/api/auth/verify-registration`), attribution is committed with `VerificationMethod = "Code"`.
- **OTP Bypass Tracking:** When `EmailVerification:Enabled = false` (in test or bypass mode), attribution is committed with `VerificationMethod = "Bypass"`.
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
- **Status in T0:** Explicitly `501 Not Implemented` (implemented in T1).
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
      "retentionPercentages": [100.0, 72.0, 64.0]
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
- **Query Parameters:** `fromDate`, `toDate`, `source`, `channel`
- **Status in T0:** Explicitly `501 Not Implemented` (implemented in T1).
- **Metric Invariants:**
  - Pro revenue includes ONLY server-confirmed, paid subscription attempts (`PayOsAttempt` with `Kind == "Subscription"` and `Status == PaymentAttemptStatus.Paid`).
  - Central webhook and manual reconciliation deduplicate receipts.
  - Tenant rent and admin-granted complimentary Pro tiers are strictly excluded from revenue metrics.
  - CAC30 = Spend in signup cohort period / New first-paid owners within 30 days. When costs are missing or the denominator is zero, CAC returns `null` with an explicit reason (`MissingCostData`, `ZeroDenominator`, `CohortImmature`, `NoRegistrations`).
- **Response Shape (`AdminMonitoringRevenueResponse`):**
```json
{
  "summary": {
    "totalSubscriptionRevenueVnd": 15000000.0,
    "paidSubscriptionCount": 15,
    "mrrVnd": 15000000.0,
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
- **Route:** `GET /api/admin/monitoring/operations`
- **Status in T0:** Explicitly `501 Not Implemented` (implemented in T3).
- **Invariants:** Missing metrics return `isStale = true` or `null`, never healthy zeros.
- **Response Shape (`AdminMonitoringOperationsResponse`):**
```json
{
  "overview": {
    "overallStatus": "Healthy",
    "api5xxRatePercent": 0.02,
    "latencyP95Ms": 145.0,
    "totalRequestsLastHour": 8420,
    "databaseConnected": true,
    "databaseStatus": "ok"
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

#### 2.3.5 Campaign Costs Management
- **Routes:**
  - `GET /api/admin/monitoring/campaigns?fromDate=...&toDate=...`
  - `POST /api/admin/monitoring/campaigns`
- **Status in T0:** Implemented in T0 foundation!
- **Upsert Request Body (`UpsertCampaignCostRequest`):**
```json
{
  "date": "2026-10-20",
  "campaign": "facebook_launch",
  "channel": "facebook",
  "source": "fb_ads",
  "cashVnd": 1500000,
  "personHours": 3.5,
  "hourlyRate": 200000,
  "note": "A/B test ad group 1"
}
```
- **Response Body (`AdminCampaignCostItemDto`):** Returns persisted entity with audit updater info (`UpdatedByName`, `UpdatedAt`).

---

## 3. Data Semantics and Retention Rules

### 3.1 Timezone & Calendar Semantics
- **Business Date Standard:** All business days, reading periods, and cohorts are anchored to Vietnam Time (`Asia/Ho_Chi_Minh`, UTC+7) using `VietnamCalendar` and `ApiSupport.VietnamDate`.
- **Database Storage:** All instant timestamps are persisted as UTC `DateTimeOffset` (converted to binary integer in SQLite per existing architecture).

### 3.2 Milestones and Activity Definitions
- **Activation Milestone:** An owner activates when their first **Monthly** invoice enters the `Issued` state (either created directly as Issued or published from Draft). Deposit invoices and Draft invoices do not activate an owner. Activation happens once per owner lifecycle and is preserved while the owner exists.
- **First Paid Milestone:** Recorded when an owner's first Pro subscription order is confirmed Paid. Excludes complimentary Admin grants and tenant payments.
- **Meaningful Owner Activity:** Daily activity distinguishes `ActorType.Owner`, `ActorType.Admin`, and `ActorType.System`.
  - Only actions performed by the Owner themselves set `HasOwnerAction = true`.
  - WAU (last 7 Vietnam calendar days) and MAU (last 30 Vietnam calendar days) count only distinct owners where `HasOwnerAction == true`.
  - Monthly retention matrix calculates whether owners in an activation cohort had `HasOwnerAction == true` in subsequent calendar months.

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
    Task TrackOwnerRegisteredAsync(
        Guid ownerId,
        RegistrationAttributionDto? attribution,
        string verificationMethod,
        CancellationToken ct);

    Task TrackInvoiceIssuedAsync(
        Guid ownerId,
        Guid invoiceId,
        string kind,
        CancellationToken ct);

    Task TrackSubscriptionPaidAsync(
        Guid ownerId,
        long orderCode,
        int amount,
        CancellationToken ct);

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

    Task<IReadOnlyList<AdminCampaignCostItemDto>> GetCampaignCostsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
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

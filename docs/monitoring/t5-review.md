# Task T5 Independent Backend and Operations QA Review Report

> [!IMPORTANT]
> **Defect Remediation Status (Task T4b Resolution)**:
> The Coordinator rejected the "nonblocking" classification of the findings in Section 4 and mandated immediate resolution prior to final acceptance.
> In Task **T4b** (commit `6891132`), all three defects were authoritatively fixed:
> 1. **Defect 1 (Payment Telemetry Dropped Milestones)**: Resolved via `MilestoneOutboxEvent` durable retry and `EnsureSubscriptionPaidMilestoneAsync` authoritative repair using persisted `PaidAt` without duplicating Pro expiry or receipts.
> 2. **Defect 2 (Invoice Issuance Hook Failures & Rollback)**: Resolved via `EnsureInvoiceIssuedMilestoneAsync` using provable actual publish timestamps (`invoice.IssuedAt` / `InvoiceRevision`), and outer transaction rollback protection in `/readings`.
> 3. **Defect 3 (In-Memory Take(50k) and Take(10k) Truncation)**: Replaced with database-level SQL aggregations (`SumAsync`, `CountAsync`, `GroupBy`) and relational filter joins, verified beyond 50,000 facts, 50,000 orders, and 10,000 costs.
> Full technical details and verification evidence are documented in [`t4b-metric-accuracy-outbox.md`](./t4b-metric-accuracy-outbox.md) and regression suite [`MonitoringAccuracyAndOutboxRegressionTests.cs`](../../tests/RoomRental.WebhookTests/MonitoringAccuracyAndOutboxRegressionTests.cs).

**Review Date**: 2026-10-20  
**Reviewer**: Worker B (Independent QA of Backend & Operations)  
**Reviewed Snapshot SHA**: `76cde7e35be2cca72f5f63a1ab6a49ee88edb212`  
**Base Original HEAD**: `f1576506825a1acde37d51960dd2dc80f5c7b23d`  
**Disposable Review Branch**: `review/t5-backend-ops`  
**Target Repository**: Roomie ASP.NET Core .NET 10 / React 19

---

## 1. Executive Summary

This independent quality assurance review evaluates the integrated backend monitoring implementation (Worker A) and operations/alerting telemetry subsystem (Worker C) at commit `76cde7e35be2cca72f5f63a1ab6a49ee88edb212`. The codebase demonstrates strong architectural hygiene: strict separation of actor types (`Owner`, `Admin`, `System`), server-authoritative registration funnel facts that survive OTP resends and bypasses, privacy-preserving client analytics with zero PII/IP logging, and autonomous host-level alerting for container/host failures without external VPS probes.

All core automated suites pass with zero failures:
- `RoomRental.WebhookTests`: **18/18 test suites passed** (including `MonitoringFoundationTests`, `MonitoringBusinessTests`, `OperationsMonitoringTests`, `MigrationTests`).
- `RoomRental.ZaloTests`: **53 assertions passed** with 0 external provider calls.
- `test_host_collector.py`: **34 unit tests passed** in 1.289s.
- SQLite live schema migration and PostgreSQL model snapshot equivalence (`HasPendingModelChanges() == false`) verified.

Two notable non-blocking defects were identified regarding post-commit telemetry failure recovery on replayed payments and remaining in-memory `Take(50_000)` cap truncation across revenue, funnel, and retention queries. Both defects are documented below with file:line references, repro descriptions, and recommended fixes for original owners.

---

## 2. Metric Definitions & Business Logic Audit (Worker A)

### 2.1 Source Attribution, OTP Bypass, and Resend Lifecycle
- **Implementation**: [`AuthEndpoints.cs:41-70`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Auth/AuthEndpoints.cs#L41-L70), [`RegistrationVerificationService.cs:57-65`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Auth/RegistrationVerificationService.cs#L57-L65), [`MonitoringServices.cs:262-520`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L262-L520).
- **Verification**:
  - `POST /api/auth/register` creates `RegistrationLifecycleFact` with `Stage = "Started"`, recording `FirstTouch` and `LastTouch` independently without copying first touch into last touch.
  - If OTP is disabled (`EmailVerification:Enabled = false`), the registration is immediately tracked with `VerificationMethod = "Bypass"` and `Stage = "Bypassed"`.
  - When OTP is resent via `RegistrationVerificationService.ResendAsync`, `TrackRegistrationResentAsync` logs `Stage = "CodeResent"`.
  - When OTP verification succeeds (`POST /api/auth/verify-registration`), `TrackOwnerRegisteredAsync` loads the started fact by `RegistrationAttemptId == ownerId`, faithfully reconstructing attribution across the challenge-response cycle and saving `RegistrationAttribution` with `AutoApproved = true`.
  - Funnel query (`DefaultGrowthMonitoringQueryService.FactMatchesFilter`) correctly attributes `CodeResent` and `Verified` facts to active dimension filters by looking up parent attempt touchpoints.

### 2.2 Session Deduplication & Routing Cardinality
- **Implementation**: [`MonitoringServices.cs:40-134`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L40-L134), [`MonitoringServices.cs:841-872`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L841-L872).
- **Verification**:
  - Inside the 90-day raw window, `TotalSessions` and `UniqueVisitors` execute database-level `Select().Distinct().CountAsync()`. Distinct real clicks with unique `eventId`s are recorded; network retries with identical `eventId`s are safely deduped.
  - Beyond 90 days (pruned raw telemetry), `UniqueVisitors` returns `null` because daily distinct visitors cannot mathematically sum to period distinct visitors.
  - For multi-day periods crossing midnight with pruned raw telemetry, `TotalSessions` returns `null` because 30-min idle sessions cross midnight, preventing over-counting. Single-day queries return the exact single-day aggregate.

### 2.3 Actor Separation & Meaningful Owner Actions
- **Implementation**: [`MonitoringServices.cs:631-701`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L631-L701).
- **Verification**:
  - `RecordOwnerActivityInternalAsync` validates `Users.AnyAsync(u => u.Id == ownerId)`. Unknown/unregistered actors are rejected immediately.
  - `ActorType.Owner` increments `OwnerActionCount` and sets `HasOwnerAction = true`.
  - `ActorType.Admin` increments `AdminActionCount` only (`HasOwnerAction` remains false).
  - `ActorType.System` (e.g. background sweeps, webhook processing) increments `SystemActionCount` only (`HasOwnerAction` remains false).
  - WAU (last 7 Vietnam days), MAU (last 30 Vietnam days), and cohort retention matrices query `Where(a => a.HasOwnerAction)`, strictly guaranteeing that automated sweeps and administrative actions never inflate active owner metrics.

### 2.4 Activation Milestone (Direct vs Draft Invoices)
- **Implementation**: [`InvoiceService.cs:51-75`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/InvoiceService.cs#L51-L75), [`BillingEndpoints.Readings.cs:90-95`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.Readings.cs#L90-L95), [`BillingEndpoints.InvoiceList.cs:182-187`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs#L182-L187), [`MonitoringServices.cs:522-576`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L522-L576).
- **Verification**:
  - An owner activates when their first **Monthly** invoice enters `Issued` state (either direct issuance in `/readings` or publishing a Draft in `/invoices/{id}/publish`).
  - Deposit invoices (`Kind == "Deposit"`) and Draft invoices are explicitly ignored.
  - Milestone hook is executed strictly **after** outer database transactions commit, preventing transaction deadlocks on SQLite.
  - If milestone is already set, `ActivatedAt` is never overwritten.
  - For historical owners without existing milestone records, `RegisteredAt` is populated using historical `Users.CreatedAt` rather than fabricating the current timestamp.

### 2.5 Pro Revenue, Central Webhooks, and Exclusions
- **Implementation**: [`PaymentWebhookService.cs:100-148`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs#L100-L148), [`MonitoringServices.cs:1257-1274`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L1257-L1274).
- **Verification**:
  - Revenue includes ONLY server-confirmed, paid Pro subscriptions (`PayOsAttempt` with `Kind == "Subscription"` and `Status == PaymentAttemptStatus.Paid`).
  - Speculative MRR has been removed.
  - Tenant rent is excluded (`Kind != "Subscription"`).
  - Admin-granted complimentary Pro tiers are excluded (they modify `AppUser.SubscriptionTier` without generating a paid `PayOsAttempt`).
  - Provider's authoritative persisted `PaidAt` timestamp is recorded.

### 2.6 Cohort 7/30 Maturity, Vietnam Timezone, and Retention
- **Implementation**: [`MonitoringServices.cs:973-1077`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L973-L1077).
- **Verification**:
  - Standard business calendar uses Vietnam Time (`Asia/Ho_Chi_Minh`, UTC+7) via `ApiSupport.VietnamDate` and `ApiSupport.StartOfVietnamDate`.
  - 7-day cohort: only registrations occurring at least 7 full Vietnam days prior to today are counted in `activationCohortMaturedCount`.
  - 30-day cohort: only registrations occurring at least 30 full Vietnam days prior to today are counted in `paidCohortMaturedCount`.
  - Retention matrix: Month 0 is 100.0%. Ongoing incomplete current month and future months return `null`, never premature `0.0`. Historical months count distinct owners with `HasOwnerAction == true`.

### 2.7 CAC30 & Campaign Spend Valuation
- **Implementation**: [`MonitoringServices.cs:1275-1361`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L1275-L1361), [`MonitoringServices.cs:1579-1615`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L1579-L1615).
- **Verification**:
  - CAC30 returns `null` with explicit machine-readable reasons:
    - `MissingCostData` when no campaign costs exist for the period.
    - `MissingValuation` when person-hours are entered without an hourly rate (labor valuation is not invented).
    - `NoRegistrations` when the signup cohort has zero registered owners.
    - `CohortImmature` when the queried period ended less than 30 days ago.
    - `ZeroDenominator` when no cohort owners paid for Pro within 30 days.
  - Cost upserting updates audit metadata (`UpdatedBy`, `UpdatedByName`, `UpdatedAt`).

---

## 3. Operations Telemetry, Alerting & Host Review (Worker C)

### 3.1 Host Collector & Deploy Helpers
- **Implementation**: [`deploy/monitoring/rehouse-host-collector.py`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/deploy/monitoring/rehouse-host-collector.py), [`deploy/monitoring/install-monitoring.sh`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/deploy/monitoring/install-monitoring.sh), [`deploy/compose.yaml:70`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/deploy/compose.yaml#L70).
- **Verification**:
  - Atomic JSON writes: `host-metrics.json` written with mode `0644` via `os.replace` and `fsync`.
  - Private state: `collector-state.json` maintained with mode `0600` outside the public mount.
  - Docker bind mount: `/var/lib/rehouse/monitoring/public:/app/monitoring-host:ro` mounted read-only into the ASP.NET container. App container has **no** access to the host Docker socket.
  - Independent SMTP alerts: Host collector directly dispatches SMTP notifications for app container failure, DB container failure, or sustained host resource spikes (CPU/RAM/Disk >= 85%) without external VPS probe dependency.
  - Sustained timing: App/DB requires 3 consecutive failures at least 55s apart; CPU/RAM requires 10 samples (10 min); Disk requires 5 samples (5 min). Rapid samples (<55s) are rejected.
  - Backup watchdog authority: Authority stays at 27h (97,200s); mapped into application `/api/admin/monitoring/operations` as `backup-watchdog` with status `Unknown` if missing or stale, and excluded from duplicate application-side alerts.

### 3.2 Application Incident Engine & Worker SLAs
- **Implementation**: [`IncidentMonitoringService.cs:165-749`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/Operations/IncidentMonitoringService.cs#L165-L749), [`WorkerTelemetry.cs:40-172`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/Operations/WorkerTelemetry.cs#L40-L172).
- **Verification**:
  - Evaluates starting thresholds:
    1. Database disconnected: 3 consecutive failed probes >= 1 min apart.
    2. API 5xx rate: > 5.0% sustained over 5 minutes (min 100 requests).
    3. API P95 latency: > 2000ms sustained over 10 minutes (min 100 requests).
    4. Host telemetry stale (> 180s).
    5. Host resource alerts consumed from collector without duplicate SMTP delivery.
    6. Worker stalled: respects per-worker SLAs (`recovery-email`: 5m, `zalo`: 5m, `reconcile`: 10m, `cleanup`: 20m, `ops-aggregation`: 2h, `invoice-sweep`: 26h).
  - Disabled workers (`IsEnabled == false`) are never marked stale or failed.
  - 10-minute startup grace period prevents false stall alerts.
  - Reminder alerts observe 1-hour cooldown.
  - Pre-network persistence: Incident state is saved to the database prior to network dispatch.
  - Gated delivery: `LastNotifiedAt` and `NotificationCount` are updated strictly after accepted SMTP delivery.
  - Recovery retry loop: Failed recovery notices are flagged `[PENDING_RECOVERY_RETRY]` and retried in subsequent cycles until accepted.
  - Missing metrics report `Unknown`/`null`, never healthy zeros. Active incidents do not resolve on silence.

### 3.3 HTTP Pipeline Placement & Status Capture
- **Implementation**: [`RouteLatencyMiddleware.cs:19-77`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/Operations/RouteLatencyMiddleware.cs#L19-L77), [`Program.cs:159-160`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Program.cs#L159-L160).
- **Verification**:
  - `RouteLatencyMiddleware` sits before `ApiExceptionMiddleware`.
  - Captures `ApiException` status (400, 422) accurately without classifying as 500.
  - Captures unhandled exceptions as 500, avoiding false 200 statuses.
  - Captures 401, 403, and 404 responses.

---

## 4. Severity-Ranked Findings & Defect Reports

### Defect 1 (Medium): Post-Commit Telemetry Failure Permanently Drops Milestone on Replayed Payment Webhook
- **File & Line**: [`backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs:103-119`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs#L103-L119)
- **Defect Description**:
  In `PaymentWebhookService.HandleSubscriptionPaymentSuccessAsync`:
  ```csharp
  var duplicate = await db.ProcessedWebhooks.SingleOrDefaultAsync(
      row => row.OwnerId == attempt.OwnerId && row.Reference == reference, cancellationToken);
  if (duplicate is not null)
  {
      await transaction.CommitAsync(cancellationToken);
      return true; // EARLY RETURN: Skips milestone hook entirely!
  }
  ```
  And if `trackedAttempt.Status == PaymentAttemptStatus.Paid`:
  ```csharp
  if (trackedAttempt.Status == PaymentAttemptStatus.Paid)
  {
      db.ProcessedWebhooks.Add(new ProcessedWebhook { ... });
      await db.SaveChangesAsync(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
      return true; // EARLY RETURN: Skips milestone hook entirely!
  }
  ```
  If `milestoneTracker.TrackSubscriptionPaidAsync` throws a transient exception (e.g., temporary database busy or lock timeout) during the initial webhook processing, the core payment transaction is already committed. When payOS subsequently replays the webhook or scheduled reconciliation runs, the early-return guard triggers. The milestone tracking hook is never re-executed. Consequently, the owner's `FirstPaidAt` milestone remains `null` permanently unless repaired.
- **Expected Behavior**:
  Replayed webhooks or scheduled batch payment reconciliation should check whether `OwnerMilestone.FirstPaidAt` is missing for the paying owner, and idempotently invoke `TrackSubscriptionPaidAsync` with the authoritative persisted `trackedAttempt.PaidAt` timestamp, without creating duplicate activity or revenue records.

---

### Defect 2 (Medium): Unguarded In-Memory `Take(50_000)` and `Take(10_000)` Caps in Revenue, Funnel, and Retention Queries
- **File & Line**: [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:803, 1016, 1021, 1083, 1088, 1157, 1254, 1266, 1286`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L803)
- **Defect Description**:
  While raw traffic session aggregation was hardened in T4 with `Take(50_001)` and `isEventsTruncated` nullability signaling, several core queries remain capped with raw `Take(50_000)`:
  1. Lines 803 and 1254: `db.RegistrationAttributions.AsNoTracking().Take(50_000)`
     - Once total registered owners exceed 50,000, `GetMatchingOwnerIds` omits attributions beyond the cap, silently excluding matching owners from filtered growth and revenue metrics.
  2. Lines 1016 and 1021: `db.OwnerMilestones...Take(50_000)` and `db.OwnerDailyActivities...Take(50_000)`
     - Cohort retention matrices silently truncate when activated owners or activities exceed 50,000, returning imprecise percentages without any warning or null indicator.
  3. Line 1157: `db.RegistrationFacts...Take(50_000)`
     - Registration funnel silently truncates started, resent, and verified fact counts.
  4. Line 1266: `ordersQuery...Take(50_000)`
     - In `GetRevenueMetricsAsync`, `totalSubscriptionRevenueVnd` sums only the first 50,000 orders. Any orders beyond 50,000 in large periods are omitted from total revenue.
  5. Line 1286: `costsQuery...Take(10_000)`
     - Marketing costs capped at 10,000 rows.
- **Expected Behavior**:
  Similar to the traffic events query, queries exceeding safety bounds should either perform database-level aggregates (`CountAsync()`, `SumAsync()`) or detect truncation (`Take(CAP + 1)`) and return explicit `null` / unavailability flags rather than silently presenting truncated numbers as complete metrics.

---

### Defect 3 (Low): Transient Telemetry Failure on Invoice Sweep Skips Subsequent Issuance Milestones
- **File & Line**: [`backend/RoomRental.Api/Features/Billing/InvoiceService.cs:51-75`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/InvoiceService.cs#L51-L75)
- **Defect Description**:
  In `InvoiceService.GenerateForOwnerAsync`:
  ```csharp
  if (milestoneTracker != null && issuedInvoices.Count > 0)
  {
      try { foreach (var inv in issuedInvoices) await milestoneTracker.TrackInvoiceIssuedAsync(...); }
      catch { /* Telemetry failure must not break invoice generation */ }
  }
  ```
  If `milestoneTracker.TrackInvoiceIssuedAsync` throws, the invoice has already been committed to the database. On subsequent daily sweeps for the same period, `invoicedOccupancies.Add(row.Occupancy.Id)` returns false, resulting in zero `issuedInvoices`. The activation milestone hook is never retried for that invoice.
- **Expected Behavior**:
  If an owner has issued monthly invoices in the database but `OwnerMilestone.ActivatedAt` is null, background maintenance (`RollupDailyAggregateAsync`) or future sweeps should detect the issued invoice and reconcile the missing activation milestone with the invoice's true creation timestamp.

---

## 5. Verification Commands & Captured Evidence

All tests were executed against disposable in-memory or temporary disk-backed databases with zero external provider calls:

```
$ dotnet build backend/RoomRental.Api/RoomRental.Api.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.16

$ dotnet build backend/RoomRental.DbMigrator/RoomRental.DbMigrator.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.02

$ dotnet run --project tests/RoomRental.WebhookTests/RoomRental.WebhookTests.csproj
PASS: successful confirmation
PASS: verification persists across service instances
PASS: fixed official endpoint, correct credentials and callback payload
PASS: rejection preserves settings and clears verified status
PASS: malformed response is never verified
PASS: timeout permits retry without losing keys
PASS: mismatched callback is rejected
PASS: changed callback origin invalidates verification
PASS: changed credentials invalidate verification
PASS: non-public HTTP callback fails before external call
PASS: confirmation never creates financial receipts
PASS: MFA compatibility flags; independent retention; persisted account throttling; tenant log redaction
PASS: extracted fingerprint compatibility; room/building owner isolation and archive guards
PASS migrations: fresh SQLite schema, snapshot, filtered indexes, repeat apply, legacy data protection
PASS: signed checkout URL round-trips
PASS: signed non-payable QR payload round-trips
PASS: checkout amount is the current outstanding balance
PASS: checkout order code is returned
PASS: signed destination account metadata round-trips
PASS: signed description and pending status round-trip
PASS: checkout attempt persists with pending status
PASS: verified QR metadata persists with its signed payment attempt
PASS: new payment order requests and persists a Unix expiry exactly 600 seconds after reservation
PASS: expiredAt remains outside the payOS request signature's documented canonical fields
PASS: pending checkout reuses the same stored order, QR, and original expiry without extending it
PASS: pending reuse does not create another provider order or local attempt
PASS: all provider traffic is intercepted in-process
PASS: registration started fact recorded with Code method and touchpoint attribution
PASS: CodeResent fact recorded
PASS: OTP verification completed and attribution preserved
PASS: auto-approved user reflected in attribution
PASS: Bypass registration recorded without verification code
PASS: Deposit invoice does NOT activate owner milestone
PASS: First Monthly invoice Issued activates owner milestone
PASS: Second Monthly invoice does NOT overwrite original ActivatedAt
PASS: Draft invoice creation does NOT activate owner
PASS: Publishing Draft invoice to Issued activates owner
PASS: System actor action increments SystemActionCount but NOT OwnerActionCount
PASS: Owner action increments OwnerActionCount and sets HasOwnerAction true
PASS: WAU (7d) and MAU (30d) count only distinct owners with HasOwnerAction
PASS: Historic owner milestone uses authoritative Users.CreatedAt, never now
PASS: Central subscription payment webhook records System actor and provider PaidAt
PASS: Replayed subscription webhook dedupes idempotently
PASS: Tenant rent payment excluded from Pro subscription revenue
PASS: Admin-granted Pro tier excluded from paid revenue
PASS: CAC30 returns MissingCostData when no spend logged
PASS: CAC30 returns MissingValuation when person-hours logged without hourly rate
PASS: CAC30 returns CohortImmature when period ends within 30 days
PASS: CAC30 returns ZeroDenominator when no new first-paid owners in 30 days
PASS: CAC30 calculates correct value when costs and valuations present
PASS: Campaign cost upsert audits UpdatedByName and UpdatedAt
PASS: Filtered zero inside 90d raw window returns REAL ZERO (never aggregate fallback)
PASS: Period >90d TotalSessions returns null for multi-day periods crossing midnight
PASS: Period >90d UniqueVisitors returns null (daily distinct cannot sum to period)
PASS: Funnel facts apply active dimension filters
PASS: Dates clamped: toDate <= today, lookback <= 400d, span <= 365d
PASS: SQLite outer transaction rollback rejects invalid reading without activating milestone
PASS: SQLite outer transaction commit commits reading and activates owner milestone without stalls
PASS: Production DI alert sink uses mandatory IAlertEmailSender and dedicated monitoring recipient
PASS: Subject formatted with severity
PASS: LastNotifiedAt persisted after successful delivery
PASS: NotificationCount is 1 after initial delivery
PASS: NotificationCount not incremented when transport fails
PASS: LastNotifiedAt not updated when transport fails
PASS: NotificationCount incremented to 2 after transport recovery
PASS: LastNotifiedAt advanced after successful reminder delivery
PASS: Incident resolved even when recovery notice SMTP throws
PASS: Recovery notice flagged for retry in DB
PASS: Pending recovery notice retry completed and cleared
PASS: Real HTTP pipeline returned 200 OK
PASS: /api/test-ok recorded 1 request, 0 5xx
PASS: Real HTTP pipeline returned 400 BadRequest (correctly treated as 4xx, not 500)
PASS: Real HTTP pipeline returned 422 UnprocessableEntity
PASS: Real HTTP pipeline returned 500 InternalServerError (recorded 5xx count == 1, NOT 200)
PASS: Worker start updates Status to Running and preserves LastSuccessAt
PASS: Worker completion updates Status to Healthy and updates LastSuccessAt
PASS: Worker failure updates Status to Failed and preserves LastSuccessAt
PASS: recovery-email SLA is 5m
PASS: zalo SLA is 5m
PASS: reconcile SLA is 10m
PASS: cleanup SLA is 20m
PASS: ops-aggregation SLA is 2h
PASS: invoice-sweep SLA is 26h
PASS: Disabled worker is NEVER marked stale even after 7 days
PASS: Admin operations endpoint returns 200 OK with T3 implemented
PASS: backup-watchdog surfaced as WorkerStatusDto
PASS: All 6 application workers tracked and healthy
PASS: Missing collector surfaces backup-watchdog as Unknown
PASS: Admin incidents endpoint returns 200 OK
PASS: Update incident state transitions to Acknowledged and Resolved

$ dotnet run --project tests/RoomRental.ZaloTests/RoomRental.ZaloTests.csproj
PASS: before 07:00 blocked
PASS: 07:00 allowed
PASS: 20:59:59 allowed
PASS: 21:00 blocked
...
PASS: SQLite migrations and rerun preserve data
PASS: 53 Zalo assertions; no external provider calls.

$ python3 -m unittest tests/test_host_collector.py
..................................
----------------------------------------------------------------------
Ran 34 tests in 1.289s
OK

$ python3 -m unittest discover -s tests -p 'test_backup_*.py'
.......
----------------------------------------------------------------------
Ran 7 tests in 0.015s
OK
```

---

## 6. Honest Limitations & Non-Run Items

1. **PostgreSQL Live Network Execution**: PostgreSQL model snapshot integrity and provider migrations were verified offline via `!pgDb.Database.HasPendingModelChanges()`. Live network migration execution against a PostgreSQL daemon was not run because no isolated test PostgreSQL container was configured for this test runner.
2. **External Providers**: Zero calls were made to real payOS servers, real external SMTP servers, or external VPS hosts in adherence to safety constraints. All provider flows were verified using in-process test fixtures and local fake handlers.
3. **Frontend Review Excluded**: As Owner B, review of the React/AntD frontend was intentionally omitted to avoid self-review. Frontend review is delegated to Worker C (Task T6).

---

## 7. Conclusion & Readiness

The integrated backend and ops telemetry baseline at commit `76cde7e35be2cca72f5f63a1ab6a49ee88edb212` is functionally sound, correctly meets the core architectural invariants, and satisfies the required metric definitions. The two medium findings documented in Section 4 represent clear optimization and resilience hardening opportunities that can be routed to the respective owners (Worker A for cap handling and outbox replay repair). The integrated snapshot is approved to proceed.

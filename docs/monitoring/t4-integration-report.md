# Task T4 Integration Report: Monitoring Subsystem

## 1. Executive Summary
- **Integrated Branch**: `nhaq0505/monitoring-core`
- **Integrated Commit SHA**: `bd27eb19e82de77b2b843dc585567632fc8114a3`
- **Original Base Checkout**: `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/LandingPage` on branch `nhaq0505/LandingPage` (HEAD `f1576506825a1acde37d51960dd2dc80f5c7b23d`) remained completely untouched.
- **Provider Status**: In-process test fakes only; zero external calls to payOS, SMTP, or VPS; zero production or shared database modifications.

---

## 2. Commit Manifest & Import Sequence
Imported cleanly onto base `8a9e764` (T1 settled core):
1. **Frontend Commits**:
   - `1c33355` (from `dcf2df3`): Admin monitoring page shell, tabs, types and browser tracker primitive (T2a)
   - `66196f8` (from `cd2a7df`): Admin monitoring UI, native charts, UTM builder, and hardened tracker (T2)
   - `d94882b` (from `5679f22`): Tracker eventId, campaign metadata CRUD, and touchpoint DTO alignment
   - `aa90c19` (from `789b8eb`): Synthetic metrics removal, null telemetry, ops window, campaign costs endpoint, retention explanation (T2b)
2. **Host Collector Commits**:
   - `f534a5c` (from `d25be1f`): Host collector, systemd units, contract, and test suite
   - `83a0c94` (from `8df5524`): Directory bind mount, container health, sustained timing, and SMTP reliability
   - `536d5c8` (from `01af817`): Incident identity keying, non-discarding retry queue, monotonic budget, and pre-network persistence
   *(Skipped duplicate Worker C foundation commits `04fc05c` and `e081ce9`)*
3. **Operations Backend Commits**:
   - `b661ea2` (from `5a56552`): T3 operations telemetry, host reader, worker tracking, incident alerts, and test suite
   - `ca2f5e0` (from `22e06ae`): Host collector reader, backup-watchdog display mapping, and alert engine alignment
   - `609ec64` (from `cf81ea5`): Mandatory SMTP alert email sender, dedicated recipient, and restart retry
   - `63f0128` (from `5ce3f39`): RouteLatencyMiddleware pipeline placement and exception status capture
   - `0dc4d62` (from `c2cc52b`): Record Running on worker start and preserve LastSuccessAt until completion
   - `f30b838` (from `d0e5779`): Production DI alert sink, HTTP pipeline status, and worker telemetry regression tests
4. **Integration & Acceptance Hardening Commits**:
   - `8ec3a97`: Add Postgres model snapshot pending changes assertion in foundation tests
   - `bd27eb1`: Complete T4 integration of nullable metrics, server registration funnel, date bounds, and 4-catalog i18n

---

## 3. Reconciled Architectural Invariants
- **Concurrency & Transacted Invoices**: Preserved `8a9e764` invariant where `InvoiceService.GenerateForOwnerAsync` detects outer transactions and defers milestone tracking strictly until after `transaction.CommitAsync()` in `/readings`.
- **System Actor vs Owner Actions**: System sweep increments `SystemActionCount` without incrementing `OwnerActionCount`. Webhooks are recorded with central System actor and explicit `PaidAt`.
- **Worker Telemetry**: Worker transitions to `Running` upon tick start, preserves `LastSuccessAt` during execution, and clears consecutive failures on healthy completion.
- **HTTP Pipeline & Diagnostics**: `RouteLatencyMiddleware` sits before exception handlers; unhandled 500s are captured accurately without false 200 statuses; 4xx errors are distinguished from server errors.
- **Watchdog Authority**: 27h backup watchdog is authoritative; missing collector reports `Unknown` rather than synthetic healthy.
- **Provider Migration Equivalence**: SQLite and PostgreSQL EF Core model snapshots both have 0 pending model changes. Database runtime is strictly DML-only.

---

## 4. Verified Acceptance Gaps Resolved
1. **Truncation & Server Aggregation Truth**:
   - `GrowthSummaryDto.TotalSessions` and `UniqueVisitors` within 90d raw window use database-level `Select().Distinct().CountAsync()` queries, completely avoiding 50k in-memory cap truncation.
   - Raw queries use `.Take(50_001)` to actively detect safety cap saturation; when saturated, partial counts are not returned as complete metrics.
2. **Multi-Day Session Crossing Truth**:
   - For multi-day periods where raw data is pruned (>90d), `TotalSessions` reports explicit `null` because 30-min idle sessions cross midnight and daily distinct sums overestimate true period distinct sessions. Single-day queries continue to return the single-day aggregate accurately.
3. **Server Registration Funnel Wiring**:
   - `RegistrationFunnelDto` (`StartedCount`, `CodeResentCount`, `VerifiedCount`, `BypassedCount`, `UnknownAttemptCount`) is fully wired through backend, DTOs, frontend types, `GrowthTab`, and native charts.
4. **Effective Date Period**:
   - `AdminMonitoringGrowthResponse` and `AdminMonitoringRevenueResponse` expose `EffectiveFromDate` and `EffectiveToDate`. Custom date range picker in UI disallows future dates (`disabledDate` and `maxDate`).
5. **Campaign Valuation & CAC30**:
   - When person-hours > 0 but hourly rate is omitted, CAC30 reports `MissingValuation`, and campaign cost tables display an explicit `Thiếu đơn giá giờ công` tag rather than zeroing out labor.
6. **4-Catalog i18n Completeness**:
   - All 8 new metric and status labels are compiled across `vi`, `en`, `zh`, `ko`, `hi` (1,353 keys total), verified with 0 untranslated strings.

---

## 5. Executable Test Evidence
| Suite | Command | Result |
| :--- | :--- | :--- |
| **C# WebhookTests** | `dotnet run --project tests/RoomRental.WebhookTests/RoomRental.WebhookTests.csproj` | **ALL PASS** (OperationalHardening, SharedSupport, Migrations, PayOsPayment, BatchPayment, TenantLinkHttp, AccountOccupancy, RegistrationEmailTemplate, RegistrationVerification, PasswordRecovery, ManualReceipt, AuthSecurityHttp, RoomTenantLifecycleHttp, AccountingBillingHttp, PayOsOperationsHttp, MonitoringFoundation, MonitoringBusiness, OperationsMonitoring) |
| **C# ZaloTests** | `dotnet run --project tests/RoomRental.ZaloTests/RoomRental.ZaloTests.csproj` | **PASS** (53 assertions; 0 provider calls) |
| **Python Host Collector** | `python3 -m unittest tests/test_host_collector.py` | **PASS** (34 unit tests; 1.18s) |
| **Frontend Architecture** | `npm run check:architecture` | **PASS** (108 source modules; 0 dependency cycles) |
| **Frontend i18n Catalogs** | `npm run i18n:check` | **PASS** (1,353 keys × 4 catalogs; UI coverage confirmed) |
| **Frontend Analytics Tracker** | `npm run test:analytics` | **PASS** (first-party browser tracker; 30m idle session; PII scrubbing) |
| **Frontend HTTP Client** | `npm run test:http` | **PASS** (transport envelopes, headers, idempotency) |
| **Frontend API Paging** | `npm run test:api` | **PASS** (bounded pagination) |
| **Frontend Production Build** | `npm run build` | **PASS** (`tsc -b && vite build && node scripts/prerender.mjs`) |
| **Playwright E2E Suite** | `npm run test:e2e` | **PASS** (60 passed across 6 workers, 15.0s) |
| **DbMigrator Build** | `dotnet build backend/RoomRental.DbMigrator/RoomRental.DbMigrator.csproj` | **PASS** (0 warnings, 0 errors) |

---

## 6. Readiness for T5/T6 Independent Reviews
The core integration worktree is fully clean on branch `nhaq0505/monitoring-core` at commit `bd27eb19e82de77b2b843dc585567632fc8114a3`. All code is formatted, fully documented, and ready for independent peer reviews.

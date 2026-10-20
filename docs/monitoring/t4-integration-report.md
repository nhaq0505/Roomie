# Task T4 Integration Report: Monitoring Subsystem

> [!NOTE]
> **Historical Record & Superseding Resolutions**: This document records the initial Task T4 integration. Subsequent independent reviews and remediation tasks have superseded preliminary findings:
> - **Backend Accuracy & Outbox Fixes**: The 3 metric accuracy defects identified in [`t5-review.md`](./t5-review.md) were resolved authoritatively in Task T4b via durable outbox retry and database aggregations; see [`t4b-metric-accuracy-outbox.md`](./t4b-metric-accuracy-outbox.md).
> - **Frontend UI & Mobile Hardening**: The 7 UI/UX edge cases identified in [`t6-review.md`](./t6-review.md) were resolved and verified in Task T6b; see [`t2c-frontend-corrections.md`](./t2c-frontend-corrections.md) and [`t6b-review.md`](./t6b-review.md).
> - **Final Assembly & Verification**: For final combined assembly, test evidence, and operational runbook, see [`t4c-assembly-report.md`](./t4c-assembly-report.md) and [`runbook-and-metric-dictionary.md`](./runbook-and-metric-dictionary.md).

## 1. Executive Summary
- **Integrated Branch**: `nhaq0505/monitoring-core`
- **Integrated Commit SHA**: `d30fe7716e95c3f450cb488f2a482acf28918b36`
- **Target Repository & Base Checkout**: `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/LandingPage` on branch `nhaq0505/LandingPage` (original HEAD `f1576506825a1acde37d51960dd2dc80f5c7b23d`) remained completely untouched and will remain untouched until T7.
- **Provider Status**: In-process test fakes and disposable QA databases only; zero calls to production/real payOS, real SMTP, or external VPS hosts; zero modifications to shared or production databases.

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
   - `d30fe77`: Publish T4 integration verification report

---

## 3. Reconciled Architectural Invariants
- **Concurrency & Transacted Invoices**: Preserved `8a9e764` invariant where `InvoiceService.GenerateForOwnerAsync` detects outer caller transactions and defers milestone tracking strictly until after `await transaction.CommitAsync()` in `/readings`. Tested on real disk-backed disposable SQLite DB with foreign keys enabled.
- **System Actor vs Owner Actions**: System background sweep increments `SystemActionCount` without incrementing `OwnerActionCount`. Webhooks are recorded with central System actor and explicit `PaidAt`.
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

## 5. Distinction Between Test Layers
1. **Mocked UI Scenarios (Playwright E2E)**:
   - `frontend/e2e/monitoring.spec.ts` intercepts browser HTTP requests via `page.route` to test user-facing responsive viewports (375px mobile, 1440px desktop), native SVG charts, navigation gate guards, null/zero states, and client form validation (60 test cases passed).
2. **Real In-Process HTTP Endpoints (ASP.NET Core TestHost / WebApplicationFactory)**:
   - `tests/RoomRental.WebhookTests/MonitoringFoundationTests.cs`, `MonitoringBusinessTests.cs`, and `OperationsMonitoringTests.cs` run the actual compiled ASP.NET Core web server and pipeline.
   - They send real HTTP requests (`GetAsync`, `PostAsJsonAsync`) to `/api/admin/monitoring/growth`, `/api/admin/monitoring/revenue`, `/api/admin/monitoring/operations`, `/api/admin/monitoring/incidents`, `/api/admin/monitoring/campaigns`, and `/api/analytics/events`.
   - All DI services (`DefaultGrowthMonitoringQueryService`, `DefaultRevenueMonitoringQueryService`, `OperationsMonitoringService`, `IncidentMonitoringService`, `WorkerTelemetry`, `RouteLatencyAggregator`, `SmtpIncidentNotifier`) are wired and executed against real disposable SQLite databases.
3. **Real SQLite Concurrency & Rollback Regression**:
   - `MonitoringBusinessTests.TestRealSqlitePostReadingsSuccessRollbackAndNoStallAsync` initializes a real disk-backed SQLite database file (`monitoring_sqlite_readings_regression_*.db`) with foreign keys enabled, executes an outer transaction, commits, and asserts that reading + issued invoice + owner milestone activate transactionally without stalls or false milestones.
4. **Provider Migration Execution vs Offline Checks**:
   - **SQLite**: Full live schema migration (`MigrateAsync()`), table checks, index checks, repeat migration checks executed in `MigrationTests.cs` and `MonitoringFoundationTests.cs`.
   - **PostgreSQL**: Model equivalence confirmed via `!pgDb.Database.HasPendingModelChanges()`. Live PostgreSQL network migration execution was not run because no local PostgreSQL test daemon was provisioned in this Mac worker environment (`ZALO_TEST_POSTGRES` was not set).

---

## 6. Executable Test Evidence & Commands
| Suite | Command | Execution Evidence | Status |
| :--- | :--- | :--- | :--- |
| **Backend API Build** | `dotnet build backend/RoomRental.Api/RoomRental.Api.csproj` | 0 Warning(s), 0 Error(s) | **PASS** |
| **DbMigrator Build** | `dotnet build backend/RoomRental.DbMigrator/RoomRental.DbMigrator.csproj` | 0 Warning(s), 0 Error(s) | **PASS** |
| **C# WebhookTests** | `dotnet run --project tests/RoomRental.WebhookTests/RoomRental.WebhookTests.csproj` | All 18 sub-suites passed (including MonitoringFoundation, MonitoringBusiness, OperationsMonitoring, MigrationTests) | **ALL PASS** |
| **C# ZaloTests** | `dotnet run --project tests/RoomRental.ZaloTests/RoomRental.ZaloTests.csproj` | 53 assertions passed; 0 external provider calls | **PASS** |
| **Python Host Collector** | `python3 -m unittest tests/test_host_collector.py` | 34 unit tests in 1.18s | **PASS** |
| **Frontend Architecture** | `npm run check:architecture` | 108 source modules; 0 cycles | **PASS** |
| **Frontend i18n Catalogs** | `npm run i18n:check` | 1,353 keys × 4 catalogs; UI coverage verified | **PASS** |
| **Frontend Analytics Tracker** | `npm run test:analytics` | First-party browser tracker; 30m idle session; PII scrubbing | **PASS** |
| **Frontend HTTP Client** | `npm run test:http` | Transport envelopes, headers, single financial submission | **PASS** |
| **Frontend API Paging** | `npm run test:api` | Bounded pagination past 100 rows | **PASS** |
| **Frontend Production Build** | `npm run build` | `tsc -b && vite build && node scripts/prerender.mjs` succeeded | **PASS** |
| **Playwright E2E Suite** | `npm run test:e2e` | 60 passed across 6 workers in 15.0s | **PASS** |

---

## 7. Honest Limitations & Not-Run Items
1. **Live PostgreSQL Network Execution**: Live connection against PostgreSQL was not executed because no local PostgreSQL test database daemon (`roomie_qa_zalo`) was provisioned on loopback in this test worker machine. Snapshot and migration model integrity was verified via `HasPendingModelChanges() == false`.
2. **External Production Providers**: Real payOS merchant API, real external SMTP servers, and external Ubuntu host probes were not executed in accordance with zero-external-provider constraints. All provider flows were tested using in-process test fixtures and fakes.

---

## 8. Readiness for Independent T5/T6 Reviews
The integrated worktree is completely clean on branch `nhaq0505/monitoring-core` at commit `d30fe7716e95c3f450cb488f2a482acf28918b36`. All code is formatted, fully documented, and ready for independent peer reviews by Worker B (backend/ops) and Worker C (frontend).

# Task T4c Final Assembly and Pre-Review Gate Report

## 1. Executive Summary
- **Target Worktree**: `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/monitoring-core`
- **Branch**: `nhaq0505/monitoring-core`
- **Assembly Scope**: Final assembly of all independently reviewed and approved monitoring components prior to the final review gate:
  - Incorporates Task **T4b** backend metric accuracy, durable milestone outbox, and database aggregation corrections (`6891132`).
  - Cherry-picks Task **T5** independent backend & ops review doc (`867bbf0` -> `a227e6c`).
  - Cherry-picks Task **T6b** independent frontend UI/UX corrections resolving all 7 mobile/date/hourly-rate issues (`e1d90c8` -> `5e9bd7a`).
  - Cherry-picks Task **T6/T6b** review reports and Playwright QA evidence (`c98de63` -> `45e8fe2`).
  - Establishes historical crosslinks across all review documents, authoring the canonical operational runbook and metric dictionary.
- **Repository Safety**: The original checkout `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/LandingPage` on branch `nhaq0505/LandingPage` remains pristine at HEAD `f1576506825a1acde37d51960dd2dc80f5c7b23d`. Zero commits, pushes, or deployments were made to the original repository.
- **Provider & Database Safety**: Zero external calls to real payOS, real SMTP, or external VPS probes; all tests executed on in-process fakes, loopback SMTP sink, and disposable SQLite databases.

---

## 2. Integrated Commit Manifest

| Commit SHA | Source / Reference | Author | Summary |
|---|---|---|---|
| `76cde7e` | Origin base | Worker A | T4 Integration Baseline (T0 foundation + T1 business metrics + T2 frontend + T3 operations) |
| `6891132` | Task T4b | Worker A | Durable milestone outbox, authoritative idempotent repair, and database aggregations for accurate metrics |
| `a227e6c` | `867bbf0` (review/t5-backend-ops) | Worker B | Publish T5 independent backend and ops QA review report |
| `5e9bd7a` | `e1d90c8` (fix/t2c-frontend-corrections) | Worker B | Resolve T6 mobile overflow, hourly rate preservation, clamped date tags, and honest funnel (T6b) |
| `45e8fe2` | `c98de63` (review-t6) | Worker C | Publish T6 and T6b independent frontend review reports and QA evidence |
| `8f1e395` | Task T4c (Authorized Correctness Fixes) | Worker A | Outbox durable ack verification, true publish revision filter, and system actor integrity |
| `6846f2b` | `b15d251` (review/t5b-backend-corrections) | Worker B | Publish T5b independent backend correction QA review report |

---

### 2.1 Authorized Correctness Blockers Resolved in Commit `8f1e395`
Per coordinator guidance (`msg_9fd3ff141e7c`), three correctness blockers identified during T5/T6 were resolved and verified with regression tests:
1. **True Publish Revision Filter**:
   - `EnsureInvoiceIssuedMilestoneAsync` now searches for revisions matching `EF.Functions.Like(r.Reason, "%publish%")` prior to `IssuedAt`, guaranteeing draft edit revisions (e.g. reading corrections prior to publication) are never selected as the activation milestone timestamp.
2. **Durable Outbox Acknowledgement & Sanitized Errors**:
   - `ProcessPendingOutboxEventsAsync` strictly checks `db.OwnerMilestones.AnyAsync(m => m.OwnerId == ev.OwnerId && ... != null)` before setting `ev.Status = "Completed"`. If the milestone is not durably recorded, the event remains `"Pending"` with an incremented `RetryCount` and `LastError = "ERR_MILESTONE_NOT_RECORDED"`.
   - All caught exceptions map to sanitized codes (`ERR_CONCURRENCY_CONFLICT`, `ERR_DB_UPDATE`, `ERR_TIMEOUT`, `ERR_INVALID_OP`, `ERR_TELEMETRY_FAILED`), guaranteeing zero raw SQL or PII leakage into error logs or database records.
3. **System Actor Integrity**:
   - Outbox event targets format `{targetId}:{actor}` (e.g., `{invoiceId}:System` vs `{invoiceId}:Owner`). Automated system sweeps record `ActorType.System`, incrementing `SystemActionCount` without incrementing `OwnerActionCount` or setting `HasOwnerAction = true`, ensuring zero false owner WAU/MAU inflation.

---

### 2.2 Task T4d Final Review Resolutions: Relational Subqueries & Outbox Retention
Per Task T4d review directives, two remaining findings from `t5b-review.md` were resolved:
1. **Relational Subqueries for Owner Attribution Filtering (Defect 4)**:
   - `GetMatchingOwnerIdsAsync` previously materialized matching owner IDs to memory (`HashSet<Guid>`), causing unbounded owner ID materialization and high memory overhead across 8 metric queries.
   - Introduced `GetMatchingOwnerIdsQuery` returning `IQueryable<Guid>?`. EF Core compiles this to relational SQL subqueries (`WHERE m.OwnerId IN (SELECT a.OwnerId FROM RegistrationAttributions a WHERE ...)`), eliminating in-memory list allocations.
   - Verified offline via `ToQueryString()` for both SQLite and PostgreSQL (Npgsql), and verified in tests with 20 matching / 10 non-matching owners.
2. **Completed Milestone Outbox Retention Pruning (Defect 6)**:
   - Added 30-day retention pruning for `Status == "Completed"` outbox events via `ExecuteDeleteAsync` in `DefaultMonitoringMaintenanceService.PruneExpiredTelemetryAsync`.
   - Verified that older `Completed` events (>30 days) are deleted, while fresh `Completed` (<=30 days), all `Pending`, and all `Failed` events are strictly preserved for retry and audit. Durable `OwnerMilestones` are preserved indefinitely.

---

## 3. Dual-Provider Schema and Model Equivalence

Both database providers (SQLite and PostgreSQL) have been verified for additive schema alignment and zero pending model changes:
- `SqliteAppDbContext`: Verified with `dotnet ef migrations has-pending-model-changes --context SqliteAppDbContext` -> **0 pending changes**.
- `PostgresAppDbContext`: Verified with `dotnet ef migrations has-pending-model-changes --context PostgresAppDbContext` -> **0 pending changes**.
- **Additive Migrations Applied**:
  - `20261020085451_MonitoringFoundation` (both SQLite & Postgres)
  - `20261020092153_MonitoringCorrection` (both SQLite & Postgres)
  - `20261020122224_MilestoneOutbox` (SQLite) / `20261020122233_MilestoneOutbox` (Postgres)
- Model snapshots (`SqliteAppDbContextModelSnapshot.cs` and `PostgresAppDbContextModelSnapshot.cs`) are completely up to date.

---

## 4. Verification Evidence & Test Summary

All automated test suites across backend, frontend, host collector, and integration layers were executed against the combined assembly:

| Test Suite | Command | Total Tests / Assertions | Passed | Failed | Skipped / Not Run | Notes |
|---|---|---|---|---|---|---|
| **C# Webhook & Monitoring Suite** | `dotnet run --project tests/RoomRental.WebhookTests/RoomRental.WebhookTests.csproj` | 19 test classes (hundreds of assertions) | **19/19** | 0 | 0 | Includes `MonitoringAccuracyAndOutboxRegressionTests` verifying outbox, replay, and 50k+ boundary tests. |
| **C# Zalo Worker Suite** | `dotnet run --project tests/RoomRental.ZaloTests/RoomRental.ZaloTests.csproj` | 53 assertions | **53/53** | 0 | 0 | Verifies 07-21 window, leap month, 20h cooldown, SQLite migrations, and database leases. |
| **Python Host Collector** | `python3 -m unittest tests/test_host_collector.py` | 34 unit tests | **34/34** | 0 | 0 | Verifies streaks, JSON schema, bind mount permissions, and SMTP queueing. |
| **Python Backup Watchdog** | `python3 -m unittest discover -s tests -p 'test_backup_*.py'` | 7 unit tests | **7/7** | 0 | 0 | Verifies 27h backup watchdog authority and filesystem marker evaluation. |
| **Frontend Architecture** | `npm --prefix frontend run check:architecture` | 108 source modules | **PASS** | 0 | 0 | Verifies shared/feature boundaries, public imports, and zero circular dependencies. |
| **Frontend I18N Check** | `npm --prefix frontend run i18n:check` | 1,354 keys × 4 catalogs | **PASS** | 0 | 0 | Verifies Vietnamese, English, Korean, and Chinese catalogs with UI string coverage. |
| **Frontend Dates & Timezone** | `npm --prefix frontend run test:dates` | Full suite | **PASS** | 0 | 0 | Verifies Vietnam calendar clamping (`Asia/Ho_Chi_Minh` UTC+7) and negative timezone resilience. |
| **Frontend HTTP Client** | `npm --prefix frontend run test:http` | Full suite | **PASS** | 0 | 0 | Verifies header propagation, envelope handling, and error mapping. |
| **Frontend Secondary API** | `npm --prefix frontend run test:api` | Full suite | **PASS** | 0 | 0 | Verifies secondary consumer pagination past 100 rows. |
| **Frontend Browser Analytics** | `npm --prefix frontend run test:analytics` | Full suite | **PASS** | 0 | 0 | Verifies first-party browser ID, 30m idle session, PII scrubbing, and allowlisted events. |
| **Frontend Production Build** | `npm --prefix frontend run build` | Full build + prerender | **PASS** | 0 | 0 | TypeScript check (`tsc -b`), Vite build, and SSG prerendering completed in 2.44s with 0 errors. |
| **Playwright Full E2E Suite** | `cd frontend && npx playwright test` | 67 E2E tests | **67/67** | 0 | 0 | Complete test run across desktop (1440px) and mobile (375px); zero horizontal scroll overflow. |

### Non-Run Reasons & Honest Architecture Scope
1. **Direct In-Process Services vs HTTP Pipeline**: Regression tests in `MonitoringAccuracyAndOutboxRegressionTests.cs` exercise business logic, outbox retries, failure injection, and manual database transaction rollbacks directly via in-process service instances (`revenueQueryService.GetRevenueMetricsAsync`, `growthService.GetGrowthMetricsAsync`, `webhookService.HandleSubscriptionPaymentSuccessAsync`). Full HTTP routing, middleware pipeline, exception mapping, and status code propagation are verified in the separate existing real HTTP integration tests (e.g. `RealHttpPipelineRecords5xxCount`, `TestAdminOperationsEndpointReturns200` in `OperationsMonitoringTests.cs`).
2. **Offline PostgreSQL Verification**: PostgreSQL migrations and model snapshots were verified offline via EF Core design-time tooling (`!pgDb.Database.HasPendingModelChanges() == true`) and offline `ToQueryString()` SQL relational subquery translation. Live network execution against a running PostgreSQL server was omitted because disposable SQLite is the isolated provider in this container.
3. **External Providers**: Zero calls were made to production payOS or live SMTP servers (disposable loopback fake handlers only).
4. **No Whole-VPS Outage Probe**: The internal host collector cannot alert if the entire VPS host suffers network/power death; external monitoring is required for host death.

---

## 5. Documentation Manifest

The following documents in `docs/monitoring/` constitute the canonical monitoring documentation:
1. [`contracts.md`](./contracts.md): Core metrics definitions, DTO schemas, and public client event contracts.
2. [`operations.md`](./operations.md): Detailed specification of route latency aggregation, worker SLAs, host ingestion, and incident engine.
3. [`host-contract.md`](./host-contract.md): Host collector systemd setup, file schema, and bind mount specification.
4. [`frontend.md`](./frontend.md): Admin dashboard UI layout, SVG charting, and i18n localization.
5. [`runbook-and-metric-dictionary.md`](./runbook-and-metric-dictionary.md): **Canonical operational runbook**, cutover steps, retention windows, unknown value handling, outbox retries, alert thresholds, and rollback procedures.
6. [`t4-integration-report.md`](./t4-integration-report.md): Historical record of initial T4 assembly with superseding resolution links.
7. [`t5-review.md`](./t5-review.md): Historical independent backend/ops review with resolution links to T4b.
8. [`t5b-review.md`](./t5b-review.md): Historical independent backend QA re-review with resolution links to T4c/T4d.
9. [`t6-review.md`](./t6-review.md): Historical independent frontend review with resolution links to T6b.
10. [`t6b-review.md`](./t6b-review.md): Independent verification report approving all 7 frontend corrections.
11. [`t2c-frontend-corrections.md`](./t2c-frontend-corrections.md): Detailed implementation record of frontend UI/UX fixes.
12. [`t4b-metric-accuracy-outbox.md`](./t4b-metric-accuracy-outbox.md): Detailed implementation record of backend accuracy, outbox, and repair mechanisms.
13. [`t4c-assembly-report.md`](./t4c-assembly-report.md): **This document**, representing the final pre-review assembly verification.

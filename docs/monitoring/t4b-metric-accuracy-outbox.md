# T4b Metric Accuracy & Replay Defects Resolution Report

## 1. Executive Summary
This document records the resolution of accuracy and replay defects identified in `t5-review.md` and mandated for Task T4b. The implementation introduces durable outbox retry and authoritative idempotent repair mechanisms across the monitoring and telemetry layers, completely replaces arbitrary in-memory `Take(50_000)` and `Take(10_000)` truncations with exact database aggregations and relational filter joins, and ensures telemetry failures never corrupt financial state or discard business milestones.

---

## 2. Defect 1: Payment Telemetry Dropped Milestones & Replay Repair
### Problem
`PaymentWebhookService` dropped `FirstPaidAt` permanently if initial postcommit telemetry threw an unhandled exception. On webhook retry, duplicate reference checks and already-`Paid` guards returned early without completing milestone tracking. Furthermore, scheduled reconciliation queried only `Status == PaymentAttemptStatus.Pending`, permanently excluding `Paid` orders from telemetry recovery.

### Solution & Invariants
1. **Durable Outbox Event in Same Database Transaction**:
   When a subscription payment succeeds, `PaymentWebhookService` writes a `MilestoneOutboxEvent` (`EventType = "SubscriptionPaid"`, `OccurredAt = paidAt`, `Status = "Pending"`) within the product transaction before committing.
2. **Authoritative Idempotent Repair**:
   - On duplicate webhook reference replay or already-`Paid` return paths, `PaymentWebhookService` immediately invokes `milestoneTracker.EnsureSubscriptionPaidMilestoneAsync(attempt.OwnerId)` and `milestoneTracker.ProcessPendingOutboxEventsAsync()`.
   - `EnsureSubscriptionPaidMilestoneAsync` authoritatively finds the earliest `Paid` subscription attempt from the database (`db.PayOsAttempts.Where(Kind == "Subscription" && Status == Paid)`) and sets `milestone.FirstPaidAt = attempt.PaidAt` and `milestone.FirstPaidOrderCode = attempt.OrderCode`.
   - `EnsureSubscriptionPaidMilestoneAsync` never fabricates timestamps; it uses the exact persisted `PaidAt` timestamp from the authoritative transaction record.
   - Financial invariants preserved: Pro tier duration is never doubled, receipts are not duplicated, and `ProcessedWebhooks` contains exactly 1 row per reference.
3. **Reconciliation & Background Worker Recovery**:
   - `PayOsService.ReconcileSubscriptionOrderAsync` triggers `EnsureSubscriptionPaidMilestoneAsync` upon confirming a paid order.
   - `PaymentReconciliationHostedService` executes `RepairMissingMilestonesAsync` and `ProcessPendingOutboxEventsAsync` during scheduled cycles, ensuring survival across application restarts.

---

## 3. Defect 2: Invoice Issuance Hook Failures & True Transition Timestamps
### Problem
Hook failures during invoice issuance permanently skipped `ActivatedAt` because subsequent billing sweeps excluded already-issued invoices. Furthermore, draft invoices published later could not use `draft.CreatedAt` because draft creation does not prove the actual issuance/publish time.

### Solution & Invariants
1. **Durable Outbox in Product Transaction**:
   - In `InvoiceService.IssueInvoiceAsync`, a `MilestoneOutboxEvent` (`EventType = "InvoiceIssued"`) is enqueued before saving changes.
   - In `BillingEndpoints.InvoiceList.cs` (`/publish`), the draft invoice transition sets `invoice.IssuedAt = now`, creates an audit `InvoiceRevision`, and enqueues a `MilestoneOutboxEvent` with the true publish timestamp.
   - In `BillingEndpoints.Readings.cs`, meter reading creation, invoice generation, and milestone outbox creation all share the outer transaction. If the transaction rolls back after meaningful writes, the outbox event and milestone are cleanly rolled back, ensuring zero fake milestones.
2. **Authoritative Repair from Provable Transition Records**:
   - `EnsureInvoiceIssuedMilestoneAsync` locates the earliest `Monthly` invoice in `Issued` state for the owner.
   - For drafts published later, the true publish time is provable from `InvoiceRevision` records (`Reason` matching publish or earliest revision `CreatedAt`) or `invoice.IssuedAt`. Draft `CreatedAt` is strictly never used as publication time.
   - Existing milestones are preserved: subsequent invoice issuances never overwrite an existing `ActivatedAt` milestone.

---

## 4. Defect 3: Removal of In-Memory `Take(50k)` and `Take(10k)` Truncation
### Problem
`MonitoringServices.cs` previously applied in-memory `.Take(50_000)` and `.Take(10_000)` limits across attributions (lines 803/1254), retention cohort owner lookups (1016/1021), daily growth series (1083/1088), registration facts (1157), revenue subscription orders (1266), and campaign costs (1286). Datasets exceeding 50,000 records silently dropped data and produced exact-looking false partial values.

### Solution
1. **Database Relational Joins & Subqueries for Dimensions**:
   - `GetMatchingOwnerIdsAsync` executes an SQL query directly against `RegistrationAttributions` filtered by UTM Source, Medium, and Campaign, returning only matching owner IDs.
   - Filtered facts queries use SQL subqueries matching `RegistrationAttemptId` against attribution-matched attempts.
2. **Database Aggregations**:
   - `TotalSubscriptionRevenueVnd` uses SQL `ordersQuery.SumAsync(o => (decimal?)o.Amount)`.
   - `PaidSubscriptionCount` uses SQL `ordersQuery.CountAsync()`.
   - Daily revenue points group and aggregate via database queries rather than in-memory LINQ over truncated slices.
   - Campaign marketing costs use SQL `costsQuery.GroupBy(...).Select(...)` and `costsQuery.SumAsync(...)`.
   - Funnel facts (`Started`, `CodeResent`, `Verified`, `Bypassed`) execute database `CountAsync()` queries directly.
   - Retention matrix computes cohort size and active users using SQL `Distinct().CountAsync()` grouped by month.

---

## 5. Automated Verification Evidence
A comprehensive regression suite was authored in `tests/RoomRental.WebhookTests/MonitoringAccuracyAndOutboxRegressionTests.cs` and executed with disposable SQLite databases:

1. **Fail-First Replay & Reconcile Test**:
   - Webhook telemetry post-commit hook artificially threw `InvalidOperationException`.
   - Verified payment marked `Paid`, `PaidAt` persisted, Pro tier activated, outbox `MilestoneOutboxEvent` created with `Status = Pending`.
   - Webhook replayed with same reference: `ProcessedWebhooks` remained 1, Pro expiry was not duplicated, and `FirstPaidAt` was authoritatively restored to `persistedPaidAt`.
   - Webhook replayed with different reference on already-paid order: restored `FirstPaidAt` without extending Pro expiry.
   - Reconciliation of paid order: authoritatively repaired `FirstPaidAt`.
   - Process restart: `RepairMissingMilestonesAsync` successfully restored `FirstPaidAt` and drained outbox to `Completed`.
   - Admin revenue endpoint verified: `PaidSubscriptionCount == 1`, `TotalSubscriptionRevenueVnd == 299,000 VND`.

2. **Invoice Issuance Transition & Readings Rollback Test**:
   - Draft monthly invoice created: `ActivatedAt` remained `null`.
   - Draft published: `IssuedAt` updated to publish time; repair verified `ActivatedAt` matched publish time, not draft creation time 20 days prior.
   - Subsequent invoice issuance: original `ActivatedAt` preserved.
   - `/readings` outer transaction rollback: reading and invoice written to DB inside transaction, then transaction rolled back; verified reading, invoice, and outbox event were completely rolled back, leaving `ActivatedAt` null.

3. **Boundary Tests Seeding Beyond 50,000 / 10,000 Caps**:
   - Seeded 50,000 dummy direct attributions, then added the 50,001st owner with matching UTM parameters. Verified `GetMatchingOwnerIdsAsync` matched row 50,001 without truncation.
   - Seeded 50,010 registration lifecycle facts (25,005 Started, 25,005 Verified). Verified `growthService.GetGrowthMetricsAsync` returned exact counts: `StartedCount == 25,005`, `VerifiedCount == 25,005`.
   - Seeded 50,005 paid subscription orders. Verified `revenueService.GetRevenueMetricsAsync` returned exact counts: `PaidSubscriptionCount == 50,005`, `TotalSubscriptionRevenueVnd == 500,050,000 VND`.
   - Seeded 10,005 marketing campaign costs. Verified `revenueService.GetRevenueMetricsAsync` returned exact sum: `TotalMarketingSpendVnd == 10,005,000 VND`.

4. **All Existing Test Suites Passed**:
   - `RoomRental.WebhookTests`: 19/19 test classes passed (including `MonitoringAccuracyAndOutboxRegressionTests`).
   - `RoomRental.ZaloTests`: 53/53 assertions passed.
   - `test_host_collector.py`: 34/34 unit tests passed.
   - `test_backup_*.py`: 7/7 tests passed.

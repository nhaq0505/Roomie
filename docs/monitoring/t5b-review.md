# Task T5b Independent Backend Correction Re-Review Report

**Review Date**: 2026-10-20  
**Reviewer**: Worker B (Independent QA of Backend & Telemetry Systems)  
**Reviewed Snapshot SHA**: `68911329abdbc325e0a4eb49d58a998fa5c56c76`  
**Base Integrated SHA**: `76cde7e35be2cca72f5f63a1ab6a49ee88edb212`  
**Review Branch**: `review/t5b-backend-corrections`  
**Target Repository**: Roomie ASP.NET Core .NET 10 / React 19  
**Final Verdict**: **REVISE / CHANGES REQUIRED (NON-PASS)** — 4 High/Blocking Defects, 2 Medium/Low Defect Findings

---

## 1. Executive Summary

This independent quality assurance re-review evaluates the backend metric accuracy and outbox repair correction authored by Worker A in commit [`6891132`](https://github.com/nhaq0505/monitoring-core/commit/68911329abdbc325e0a4eb49d58a998fa5c56c76), following findings documented in the prior T5 report ([`867bbf0`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/docs/monitoring/t5-review.md)).

### What Was Done Well
1. **Schema & Migration Hygiene**:
   - `MilestoneOutboxEvent` was introduced additively with appropriate dual-column indexes (`Status, OccurredAt` and `OwnerId, EventType`).
   - Additive migrations were generated for both providers: SQLite [`20261020122224_MilestoneOutbox.cs`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Data/Migrations/Sqlite/20261020122224_MilestoneOutbox.cs) and PostgreSQL [`20261020122233_MilestoneOutbox.cs`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Data/Migrations/Postgres/20261020122233_MilestoneOutbox.cs).
   - PostgreSQL model snapshot integrity verified offline (`!pgDb.Database.HasPendingModelChanges() == true`).
2. **Transactional Outbox Event Creation**:
   - Webhook processing in [`PaymentWebhookService.cs`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs#L145-L155) enqueues a `MilestoneOutboxEvent` (`EventType = "SubscriptionPaid"`) inside the product database transaction prior to commit.
   - Draft publishing in [`BillingEndpoints.InvoiceList.cs`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs#L182-L192) and meter reading issuance in [`BillingEndpoints.Readings.cs`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.Readings.cs#L87-L97) enqueue outbox events within their respective transactions. Outer transaction rollback cleanly rolls back outbox rows without leaving dangling milestones.
3. **Removal of In-Memory Truncations**:
   - Raw `Take(50_000)` and `Take(10_000)` caps were removed from total subscription revenue (`SumAsync`), subscription counts (`CountAsync`), campaign spend sums, and registration funnel stage counts.

### Why Commit 6891132 Is Non-Passing (Blocking Defects)
Despite the structural improvements, deep inspection revealed 4 blocking defects that break the fundamental outbox and metric accuracy guarantees:
1. **Outbox False-Completion via Swallowed Exceptions**: `TrackSubscriptionPaidAsync` and `TrackInvoiceIssuedAsync` catch and swallow all exceptions internally. Consequently, both caller hooks and `ProcessPendingOutboxEventsAsync` mark events as `Completed` even when the database write throws, permanently dropping un-milestoned events from retry.
2. **Activation Timestamp Backdated by Draft-Edit Revisions**: `EnsureInvoiceIssuedMilestoneAsync` queries `InvoiceRevisions` ordered by `CreatedAt` ascending without filtering for publish transitions. For drafts edited before publication (e.g. occupancy occupant updates), the activation milestone is backdated to the draft-edit date rather than the provable publication date (`IssuedAt`).
3. **System Sweeps Hardcoded as Owner Activity**: In both outbox processing and milestone repair, `TrackInvoiceIssuedAsync` is invoked with hardcoded `ActorType.Owner`. When background sweeps (`InvoiceSweepService`) issue invoices, this logs `HasOwnerAction = true` for the owner, artificially inflating WAU (7d) and MAU (30d) with automated system actions.
4. **Unbounded In-Memory Materialization of Owner IDs**: `GetMatchingOwnerIdsAsync` materializes all filtered owner IDs into an in-memory `HashSet<Guid>` (`ToListAsync()`) and feeds them into 8 queries via `Contains(...)`. In SQLite, matching owners exceeding parameter limits (32,766) will throw `too many SQL variables`.

---

## 2. Detailed Findings & Concrete Repros

### Defect 1 (High / Blocking): Exception Swallowing in `Track*` Methods Triggers Unconditional Outbox "Completed" Status
- **File & Lines**:
  - [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:581-584, 641-644, 748-758`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L581)
  - [`backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs:161-171`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Payments/PaymentWebhookService.cs#L161)
  - [`backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs:195-203`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs#L195)
  - [`backend/RoomRental.Api/Features/Billing/BillingEndpoints.Readings.cs:101-109`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.Readings.cs#L101)
- **Defect Description**:
  In `OwnerMilestoneTracker`, both `TrackInvoiceIssuedAsync` and `TrackSubscriptionPaidAsync` wrap their internal database operations in `try { ... } catch { }` blocks that swallow all exceptions:
  ```csharp
  // MonitoringServices.cs:640-644
  catch
  {
      // Payment tracking failure must not block webhook processing
  }
  ```
  In `PaymentWebhookService.cs`:
  ```csharp
  try
  {
      await milestoneTracker.TrackSubscriptionPaidAsync(attempt.OwnerId, trackedAttempt.OrderCode, (int)amount, ActorType.System, trackedAttempt.PaidAt, cancellationToken);
      outbox.Status = "Completed";
      outbox.ProcessedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync(cancellationToken);
  }
  catch
  {
      // Durable outbox remains Pending for replay/reconcile/restart
  }
  ```
  Because `TrackSubscriptionPaidAsync` swallows the exception and returns cleanly, `PaymentWebhookService` never enters its `catch` block. It proceeds immediately to line 164, sets `outbox.Status = "Completed"`, and commits the change.
  Even worse, in `ProcessPendingOutboxEventsAsync`:
  ```csharp
  // MonitoringServices.cs:725-758
  foreach (var ev in pendingEvents)
  {
      try
      {
          if (ev.EventType == "SubscriptionPaid")
          {
              await TrackSubscriptionPaidAsync(...); // Swallow exception internally!
          }
          ev.Status = "Completed"; // Runs even if milestone write failed!
          processed++;
      }
      catch (Exception ex)
      {
          ev.RetryCount++; // DEAD CODE: never reached for milestone DB failures!
      }
  }
  ```
  The outbox processor marks the event `Completed` unconditionally. The `catch (Exception ex)` block that increments `RetryCount` and sets `Status = "Failed"` is unreachable for milestone DB errors.
- **Why Worker A's Test Missed It**:
  In `MonitoringAccuracyAndOutboxRegressionTests.cs:732-736`, Worker A created a mock `FailingMilestoneTracker` that threw `InvalidOperationException` *before* calling the real tracker. It simulated an exception from the tracker interface itself rather than an internal database failure inside the real `OwnerMilestoneTracker`.
- **Actionable Remediation for Worker A**:
  Either:
  1. Have `TrackSubscriptionPaidAsync` and `TrackInvoiceIssuedAsync` return a `bool` indicating whether the database write succeeded (`true`) or caught an error (`false`), and only set `outbox.Status = "Completed"` if `true`.
  2. Or allow exceptions to propagate when called in context of outbox/repair operations so that `catch (Exception ex)` in `ProcessPendingOutboxEventsAsync` can record `RetryCount` and `LastError`.

---

### Defect 2 (High / Blocking): Earliest `InvoiceRevision` Selection Backdates Activation from Pre-Publish Draft Edits
- **File & Lines**:
  - [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:685-699`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L685-L699)
  - [`backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs:176`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs#L176)
  - [`backend/RoomRental.Api/Features/Properties/OwnerManagementEndpoints.Occupancies.cs:46`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Properties/OwnerManagementEndpoints.Occupancies.cs#L46)
- **Defect Description**:
  In `EnsureInvoiceIssuedMilestoneAsync`:
  ```csharp
  var earliestInvoice = await db.Invoices.AsNoTracking()
      .Where(x => x.OwnerId == ownerId && x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Issued)
      .OrderBy(x => x.IssuedAt)
      .FirstOrDefaultAsync(ct);

  if (earliestInvoice is not null)
  {
      var publishRevision = await db.InvoiceRevisions.AsNoTracking()
          .Where(r => r.InvoiceId == earliestInvoice.Id)
          .OrderBy(r => r.CreatedAt)
          .FirstOrDefaultAsync(ct);

      var transitionTime = publishRevision?.CreatedAt ?? earliestInvoice.IssuedAt;
      await TrackInvoiceIssuedAsync(ownerId, earliestInvoice.Id, earliestInvoice.Kind, ActorType.Owner, transitionTime, ct);
  }
  ```
  The query grabs the earliest `InvoiceRevision` by `CreatedAt` with zero filtering on `Reason` or lifecycle state change.
  In Roomie, draft invoices can have revisions before publication. For example, updating the room's occupant count while a draft exists calls `BillingEndpoints.AddRevisionAsync` with Reason `"Cập nhật số người ở cho hóa đơn nháp."` ([`OwnerManagementEndpoints.Occupancies.cs:46`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Properties/OwnerManagementEndpoints.Occupancies.cs#L46)).
  If a draft is created on Day 1, edited on Day 5, and published to `Issued` on Day 20, `publishRevision` selects the Day 5 draft-edit revision. `ActivatedAt` is erroneously backdated to Day 5 instead of the true publication date (Day 20).
- **Authoritative Invariant**:
  When an invoice is published via `/invoices/{id}/publish`, `invoice.IssuedAt = now;` is explicitly set to the publication timestamp ([`InvoiceList.cs:176`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/BillingEndpoints.InvoiceList.cs#L176)). The fallback should use `earliestInvoice.IssuedAt` directly, or filter `InvoiceRevisions` where `Reason` matches publish transitions.

---

### Defect 3 (High / Blocking): Hardcoded `ActorType.Owner` on Outbox Drain & Repair Falsely Inflates WAU/MAU on System Sweeps
- **File & Lines**:
  - [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:698, 743`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L698)
  - [`backend/RoomRental.Api/Features/Billing/InvoiceService.cs:60-68, 244-250`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Billing/InvoiceService.cs#L60)
- **Defect Description**:
  Both `ProcessPendingOutboxEventsAsync` and `EnsureInvoiceIssuedMilestoneAsync` hardcode `ActorType.Owner` when calling `TrackInvoiceIssuedAsync`:
  ```csharp
  // Line 743:
  await TrackInvoiceIssuedAsync(ev.OwnerId, invoice.Id, invoice.Kind, ActorType.Owner, ev.OccurredAt, ct);

  // Line 698:
  await TrackInvoiceIssuedAsync(ownerId, earliestInvoice.Id, earliestInvoice.Kind, ActorType.Owner, transitionTime, ct);
  ```
  `TrackInvoiceIssuedAsync` invokes `RecordOwnerActivityInternalAsync(db, ownerId, actor, "InvoiceIssued", ct)`. When `actor == ActorType.Owner`, this executes:
  ```csharp
  // Line 849-854:
  activity.OwnerActionCount = actorType == ActorType.Owner ? 1 : 0;
  activity.HasOwnerAction = activity.OwnerActionCount > 0;
  ```
  When the automated daily sweep (`InvoiceSweepService`) runs, invoices are generated with `ActorType.System`. However, if the milestone outbox is drained or repaired, the hardcoded `ActorType.Owner` parameter sets `HasOwnerAction = true` for every invoiced owner.
  Because WAU (7d) and MAU (30d) strictly count owners where `HasOwnerAction == true`, automated background invoice generation falsely counts owners as active users who took meaningful product actions.
- **Actionable Remediation for Worker A**:
  - In `InvoiceService.GenerateForOwnerAsync`, outbox events created during sweeps should preserve actor type, or outbox drain should not record human owner activity if the invoice was generated by a system sweep.
  - `EnsureInvoiceIssuedMilestoneAsync` should only set `OwnerMilestone.ActivatedAt` and `FirstActivatedInvoiceId`, without creating artificial `OwnerDailyActivity` owner action records for historic sweeps.

---

### Defect 4 (High / Blocking): In-Memory Materialization of All Matching Owner IDs Causes Parameter Overflow & Scalability Failure
- **File & Lines**:
  - [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:1008, 1121, 1145, 1156, 1276, 1323, 1436, 1511`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L1008)
- **Defect Description**:
  In `GetMatchingOwnerIdsAsync`:
  ```csharp
  var matchingIds = await query.Select(a => a.OwnerId).ToListAsync(ct);
  return matchingIds.ToHashSet();
  ```
  This immediately loads all matching owner IDs from `RegistrationAttributions` into an in-memory `HashSet<Guid>`.
  Then, across 8 subsequent queries in `GetGrowthMetricsAsync` and `GetRevenueMetricsAsync`, the query applies:
  ```csharp
  milestonesQuery = milestonesQuery.Where(m => matchingOwnerIds.Contains(m.OwnerId));
  wauQuery = wauQuery.Where(a => matchingOwnerIds.Contains(a.OwnerId));
  mauQuery = mauQuery.Where(a => matchingOwnerIds.Contains(a.OwnerId));
  cohortOwnersQuery = cohortOwnersQuery.Where(m => matchingOwnerIds.Contains(m.OwnerId));
  activeDaysQuery = activeDaysQuery.Where(a => matchingOwnerIds.Contains(a.OwnerId));
  filteredFacts = factsBaseQuery.Where(f => (f.OwnerId.HasValue && matchingOwnerIds.Contains(f.OwnerId.Value)) || ...);
  ordersQuery = ordersQuery.Where(o => matchingOwnerIds.Contains(o.OwnerId));
  ```
  EF Core translates `.Where(x => matchingOwnerIds.Contains(x.OwnerId))` by generating SQL `IN (@p0, @p1, ... @pN)`.
  - In SQLite: The maximum variable limit (`SQLITE_LIMIT_VARIABLE_NUMBER`) is 32,766. If a popular channel or source has >32,766 owners, SQLite throws an unhandled exception: `SQLite Error 1: 'too many SQL variables'`.
  - In PostgreSQL: Npgsql has a maximum parameter limit of 65,535 parameters (`NpgsqlException: The number of parameters exceeded 65535`).
  - Memory & Query Planner: Passing tens of thousands of parameters across 8 queries causes huge GC pressure and high database query planning latency.
- **Actionable Remediation for Worker A**:
  Do not materialize `matchingOwnerIds` to memory. Keep it as an `IQueryable<Guid>`:
  ```csharp
  var matchingOwnerIdsQuery = query.Select(a => a.OwnerId);
  milestonesQuery = milestonesQuery.Where(m => matchingOwnerIdsQuery.Contains(m.OwnerId));
  ```
  EF Core compiles this directly to a relational SQL subquery:
  ```sql
  WHERE m.OwnerId IN (SELECT a.OwnerId FROM RegistrationAttributions a WHERE ...)
  ```
  This requires 0 in-memory list allocations and 0 SQL variables.

---

### Defect 5 (Medium): Potential PII / Sensitive Payload Leak in Outbox `LastError`
- **File & Line**: [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:755`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L755)
- **Defect Description**:
  ```csharp
  ev.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
  ```
  Unsanitized `ex.Message` from database or HTTP exceptions can contain raw SQL parameter values, database connection string snippets, or customer information.
- **Actionable Remediation for Worker A**:
  Sanitize or categorize `LastError` (e.g. record exception type and generic message, stripping query parameters and sensitive tokens) before persisting.

---

### Defect 6 (Low): Missing Retention Pruning for `MilestoneOutboxEvents`
- **File & Line**: [`backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs:1906-1949`](file:///Users/nguyenhoanhquan/orca/workspaces/Quản%20Lý%20Phòng/monitoring-ui/backend/RoomRental.Api/Features/Monitoring/MonitoringServices.cs#L1906)
- **Defect Description**:
  `PruneExpiredTelemetryAsync` prunes `AnalyticsEvents` (90 days), `OwnerDailyActivities` (13 months), and `DailyAggregateMetrics` (13 months). It contains no pruning logic for `MilestoneOutboxEvents`. Completed and failed outbox events will accumulate unbounded in the database.
- **Actionable Remediation for Worker A**:
  Add cleanup of completed outbox events older than 30 or 90 days in `PruneExpiredTelemetryAsync`.

---

## 3. Test Coverage Audit: Claim vs. Reality

`docs/monitoring/t4b-metric-accuracy-outbox.md` reports extensive verification in `tests/RoomRental.WebhookTests/MonitoringAccuracyAndOutboxRegressionTests.cs`. A critical audit of the test suite reveals:

| Category | Claimed in T4b Report | Actual Implementation in Test Suite |
| :--- | :--- | :--- |
| **HTTP Pipeline vs. Direct Helper** | "Admin revenue endpoint verified", "Draft published", "/readings outer transaction rollback" | Tests do **not** use `WebApplicationFactory` or `TestServer`. All assertions run through direct in-process C# service method calls (`revenueQueryService.GetRevenueMetricsAsync`, `webhookService.HandleSubscriptionPaymentSuccessAsync`) and manual `db.Database.BeginTransactionAsync()` rollbacks. Routing, middleware, and HTTP response serialization were not exercised by the new test class. |
| **Mock Wrapper vs. Production Telemetry** | "Fail-First Replay & Reconcile Test" | Test wraps `OwnerMilestoneTracker` with `FailingMilestoneTracker` which throws from the outer wrapper. It did not test internal DB exceptions inside `OwnerMilestoneTracker`, which masked Defect 1 (exception swallowing). |
| **50k Boundary Seeding** | "Owner at row 50,001 is correctly matched by database filter join" | Test seeded 50,000 direct rows and 1 matching row, so `matchedIds.Count == 1`. It never tested what happens when 50,000 rows match the filter, which masked Defect 4 (parameter limit overflow). |
| **Draft Publish Transition** | "Repair verified ActivatedAt matched publish time, not draft creation time 20 days prior" | Test seeded only 1 `InvoiceRevision` created at publish time. It never seeded a pre-publish draft edit revision, which masked Defect 2 (backdating). |

### All Automated Suites Execution Evidence (Reviewed Snapshot 6891132)
All four test suites execute cleanly on the reviewed commit:
1. `dotnet run --project tests/RoomRental.WebhookTests/RoomRental.WebhookTests.csproj`:
   - **19/19 test suites passed** (including `MonitoringFoundationTests`, `MonitoringBusinessTests`, `OperationsMonitoringTests`, `MigrationTests`, `MonitoringAccuracyAndOutboxRegressionTests`).
2. `dotnet run --project tests/RoomRental.ZaloTests/RoomRental.ZaloTests.csproj`:
   - **53/53 assertions passed** with 0 external provider calls.
3. `python3 -m unittest tests/test_host_collector.py`:
   - **34/34 tests passed** in 1.284s.
4. `python3 -m unittest discover -s tests -p 'test_backup_*.py'`:
   - **7/7 tests passed** in 0.011s.

### Dual-Provider Model Integrity & Limitations
- **SQLite Live Migration**: Verified against in-memory SQLite (`SqliteAppDbContext.Database.MigrateAsync()`), with repeat apply preserving data.
- **PostgreSQL Model Snapshot**: Verified offline via `!pgDb.Database.HasPendingModelChanges() == true`.
- **Honest Limitation**: Live PostgreSQL network execution was **not run** because no local PostgreSQL server is configured in this test environment. No external network or shared production databases were touched.

---

## 4. Prioritized Action Items for Worker A (Task T4c)

To achieve passing verification, Worker A should implement the following targeted corrections:
1. **Un-swallow Milestone DB Errors or Signal Result**:
   - In `OwnerMilestoneTracker.TrackSubscriptionPaidAsync` and `TrackInvoiceIssuedAsync`, allow callers to detect whether the milestone write actually succeeded.
   - In `PaymentWebhookService`, `BillingEndpoints.InvoiceList`, and `BillingEndpoints.Readings`, only set `outbox.Status = "Completed"` upon confirmed success.
   - In `ProcessPendingOutboxEventsAsync`, catch exceptions so that `RetryCount++` and `LastError` function properly, and never mark `Status = "Completed"` on failure.
2. **Fix `EnsureInvoiceIssuedMilestoneAsync` Authoritative Timestamp**:
   - Use `earliestInvoice.IssuedAt` directly, or query the revision where `Reason` explicitly indicates publication.
3. **Preserve System Actor for Sweep Invoices**:
   - Do not pass hardcoded `ActorType.Owner` to `TrackInvoiceIssuedAsync` during outbox drain or sweep repair. Only mark `HasOwnerAction = true` when an owner manually issues or publishes an invoice.
4. **Relational Subqueries for Owner Attribution Filtering**:
   - Refactor `GetMatchingOwnerIdsAsync` to return `IQueryable<Guid>` (or use EF Core subquery join directly) instead of `ToListAsync()`, removing parameter limit risks across all 8 metric queries.
5. **Pruning & Sanitization**:
   - Add outbox event pruning in `PruneExpiredTelemetryAsync` and sanitize `LastError`.

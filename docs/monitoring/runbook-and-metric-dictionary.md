# Monitoring Runbook and Metric Dictionary

## 1. Overview and Core Invariants
The Roomie monitoring subsystem provides an internal, privacy-first, first-party analytics and operations tracking system (`/admin/monitoring`). It operates without third-party tracking scripts, external analytics clouds, or cookies.

### Core Architectural Invariants:
1. **Privacy & Anonymity**: No PII, email, phone number, OTP, JWT token, bank account number, IP address, or raw query parameters are ever persisted in analytics tables.
2. **Server-Authoritative Milestones**: Registration, invoice issuance, and subscription payment milestones are recorded server-side inside transactional boundaries. Client telemetry cannot forge or manipulate milestones.
3. **Additive Schema & DML Runtime**: All schema changes are strictly additive. Database migrations are verified for both SQLite and PostgreSQL. Runtime application code executes DML queries only.
4. **No Invented Truth**: Missing, stale, or indeterminate data is explicitly reported as `null` or `Unknown` with an explanatory reason. Synthetic "healthy zeros" or fake backfills are strictly prohibited.
5. **Durable Outbox & Idempotent Repair**: Telemetry and milestone updates use a transactional outbox (`MilestoneOutboxEvent`) and authoritative repair mechanisms (`EnsureSubscriptionPaidMilestoneAsync`, `EnsureInvoiceIssuedMilestoneAsync`). Product transaction rollback cancels uncommitted outbox events, while webhook replays and background workers safely restore dropped milestones without duplicating financial mutations.

---

## 2. Metric Dictionary

### 2.1 Growth & Usage Metrics
- **Time Window**: Default 30-day business window (`startUtc` to `endUtc` in Vietnam Timezone `Asia/Ho_Chi_Minh` UTC+7).
- **Sessions (`TotalSessions`)**: Bounded 30-minute idle session model derived from client analytics events within the 90-day raw retention window. Queries use database-level `Distinct().CountAsync()`. When the 90-day window is exceeded or data is absent, returns `null`.
- **Unique Visitors (`UniqueVisitors`)**: Distinct first-party `BrowserId` count within the active window (maximum 90 days). Returns `null` when filtered outside the raw event retention window.
- **Registration Funnel**:
  - `StartedCount`: Unique registration attempts initiated (`RegistrationLifecycleFact` with `Stage == 'Started'`).
  - `CodeResentCount`: Registration attempts with OTP resend requested (`Stage == 'CodeResent'`).
  - `VerifiedCount`: Successful OTP verification (`Stage == 'Verified'`).
  - `BypassedCount`: Direct verification via auto-approved bypass (`Stage == 'Bypassed'`).
  - `UnknownAttemptCount`: Unattributed registration facts present without matching started context.
  - *Honest Funnel Fallback*: If `TotalSessions` is `null` (e.g. historical range >90d), the funnel displays authentic stage counts without inventing a synthetic session base.
- **Active User Metrics**:
  - **WAU7**: Distinct owners with meaningful owner actions (`OwnerDailyActivity.OwnerActionCount > 0`) in the trailing 7 Vietnam calendar days. Actions performed by `Admin` or `System` background workers are explicitly segregated and do not count toward owner WAU.
  - **MAU30**: Distinct owners with meaningful owner actions in the trailing 30 Vietnam calendar days.
- **Cohort Retention Matrix**:
  - Owners grouped by registration/activation calendar month.
  - Retention percentages for subsequent calendar months (Month 0 to Month 5) computed via SQL grouping.
  - Inactive or unobserved months render explicit `null` ("—"), never defaulting to 100%.

### 2.2 Revenue Metrics
- **Total Subscription Revenue (`TotalSubscriptionRevenueVnd`)**: Sum of confirmed `Paid` subscription order amounts (`PayOsAttempt.Amount` where `Kind == "Subscription" && Status == Paid`). Excludes tenant rent, deposits, and admin-granted complimentary Pro subscriptions.
- **Paid Subscription Count (`PaidSubscriptionCount`)**: Count of confirmed paid subscription orders.
- **Monthly Recurring Pro Revenue (`MonthlyRecurringProRevenueVnd`)**: Active paying Pro subscriptions annualized/normalized to monthly recurring rate.
- **CAC30 (Customer Acquisition Cost within 30 Days)**:
  - Calculation: $\frac{\text{Total Marketing Spend in Signup Cohort Period}}{\text{New First-Paid Owners Converted within 30 Days of Registration}}$
  - Spend includes Cash (`CashVnd`) plus Imputed Labor (`PersonHours * HourlyRate`).
  - **Explicit Null Reasons (`CacNullReason`)**:
    - `MissingCostData`: No marketing cost records entered for the cohort window.
    - `MissingValuation`: Non-zero `PersonHours` entered but `HourlyRate` omitted.
    - `ZeroDenominator`: Cohort period has matured (>30 days elapsed) but 0 owners converted to Paid.
    - `CohortImmature`: Cohort period ended less than 30 days ago; conversion window has not closed.
    - `NoRegistrations`: Zero owner registrations occurred during the period.

### 2.3 Operations & Telemetry Metrics
- **Route Latency P95 (`LatencyP95Ms`)**: Interpolated 95th percentile latency computed from route minute bucket histograms (`RouteMinuteBuckets`).
- **API 5xx Rate (`Api5xxRatePercent`)**: Proportion of requests resulting in HTTP 5xx status codes over the selected window. 4xx client errors (400, 401, 403, 404, 422) are strictly distinguished and do not trigger 5xx alerts.
- **Worker Health Status**:
  - States: `Healthy`, `Running`, `Degraded`, `Failed`, `Disabled`, `Unknown`.
  - Startup Grace Period: 10 minutes from process start before worker staleness triggers.
  - Per-Worker Heartbeat SLAs:
    - `recovery-email`: 5 minutes
    - `zalo`: 5 minutes
    - `reconcile`: 10 minutes
    - `cleanup`: 20 minutes
    - `ops-aggregation`: 2 hours
    - `invoice-sweep`: 26 hours
- **Backup Watchdog Authority**:
  - The 27-hour filesystem backup marker (`backup-watchdog`) managed by Ubuntu host collector is the sole authoritative backup monitor.
  - Mapped into the operations API for unified display, but strictly excluded from application alert rules to prevent duplicate alarms.
  - Absent or stale host collector telemetry surfaces `backup-watchdog` as `Unknown`.

---

## 3. Data Retention Policy

| Table / Entity | Retention Window | Purge Mechanism | Authority / Invariant |
|---|---|---|---|
| `ClientAnalyticsEvents` | 90 days | Scheduled background sweep (`DataMaintenanceService`) | Raw client telemetry pruned; queries past 90d return `null`. |
| `OperationalRouteMinuteBuckets` | 30 days | Scheduled background sweep (`OpsMinuteFlushWorker`) | Aggregated route latencies kept for 30d operational analysis. |
| `OwnerDailyActivities` | 13 months | Scheduled background sweep (`DataMaintenanceService`) | Preserves 13 months of daily owner activity for trailing WAU/MAU. |
| `DailyMonitoringAggregates` | 13 months | Scheduled background sweep (`DataMaintenanceService`) | Daily growth and revenue snapshots preserved for trend reporting. |
| `MonitoringIncidents` | Indefinite (or 13 mo) | Manual audit archive | Preserves historical incident records and resolution audit notes. |
| `OwnerMilestones` | **Permanent** | **Never pruned** | Milestones (`RegisteredAt`, `ActivatedAt`, `FirstPaidAt`) persist as long as owner user exists. |
| `MilestoneOutboxEvents` | 30 days after `Completed` | Scheduled sweep | Processed outbox events retained for 30 days for replay audit. |

---

## 4. Operational Runbook

### 4.1 Required Configuration & Environment Variables

```bash
# Monitoring Alert Recipient (Dedicated Monitoring Mailbox)
MONITORING_ALERT_TO="monitoring-alerts@rehouse.vn"

# Host Collector Bind Mount Path (Read-Only)
MONITORING_HOST_METRICS_PATH="/app/monitoring-host/host-metrics.json"

# Webhook Base URL (Public Callback URL for payOS)
WebhookBaseUrl="https://app.rehouse.vn"

# Rate Limiting Policies
RateLimiting__AnalyticsPermitLimit=120
RateLimiting__AnalyticsWindowSeconds=60
```

### 4.2 Host Collector Installation & Systemd Configuration

1. **Directories & Permissions**:
   ```bash
   sudo mkdir -p /var/lib/rehouse/monitoring/public
   sudo chown -R rehouse:rehouse /var/lib/rehouse/monitoring
   sudo chmod 0755 /var/lib/rehouse/monitoring/public
   ```

2. **Systemd Collector Service** (`/etc/systemd/system/rehouse-host-collector.service`):
   ```ini
   [Unit]
   Description=Rehouse Host Metrics Collector
   After=network.target docker.service

   [Service]
   Type=oneshot
   User=rehouse
   ExecStart=/usr/bin/python3 /opt/rehouse/scripts/host-collector.py
   StandardOutput=journal
   StandardError=journal
   ```

3. **Systemd Collector Timer** (`/etc/systemd/system/rehouse-host-collector.timer`):
   ```ini
   [Unit]
   Description=Trigger Rehouse Host Metrics Collector Every Minute

   [Timer]
   OnBootSec=1min
   OnUnitActiveSec=1min
   AccuracySec=5s

   [Install]
   WantedBy=timers.target
   ```

4. **Docker Container Bind Mount**:
   Mount `/var/lib/rehouse/monitoring/public` into the application container strictly as read-only:
   ```yaml
   volumes:
     - /var/lib/rehouse/monitoring/public:/app/monitoring-host:ro
   ```
   > [!CAUTION]
   > Never mount the Docker daemon socket (`/var/run/docker.sock`) into the application container. The host collector runs autonomously on the host.

### 4.3 Incident Engine & Alert Thresholds

| Rule Key | Condition | Severity | Reminder Cooldown | Recovery Notice |
|---|---|---|---|---|
| `high_5xx_rate` | API 5xx rate > 5.0% sustained 5m (min 100 req) | Critical (>=10%) / Warning (>=5%) | 1 hour | Automatic upon <5% with >=100 req |
| `high_p95_latency` | Route P95 latency > 2000ms sustained 10m (min 100 req) | Critical | 1 hour | Automatic upon <=2000ms with >=100 req |
| `database_disconnected` | 3 consecutive DB health probe failures 1m apart | Critical | 1 hour | Automatic upon successful DB ping |
| `host_collector_stale` | Collector timestamp > 180s old or file missing | Warning | 1 hour | Automatic upon fresh valid JSON |
| `host_cpu_high` | Host CPU utilization >= 85% sustained 10m | Critical (>=90%) / Warning (>=85%) | 1 hour (collector) | Handled by host collector |
| `host_disk_high` | Host disk utilization >= 85% sustained 5m | Critical (>=90%) / Warning (>=85%) | 1 hour (collector) | Handled by host collector |
| `worker_stalled_{name}` | Enabled worker heartbeat older than SLA | Warning | 1 hour | Automatic upon worker healthy tick |

> [!WARNING]
> **No Whole-VPS Outage Detection**:
> The internal host collector and application alerting mechanisms run on the local Ubuntu host. If the entire VPS experiences a total power outage, hypervisor panic, or complete network disconnection, the local processes cannot dispatch SMTP alerts. External synthetic uptime monitoring (HTTP probe outside the VPS targeting `/api/health`) is required for whole-host disaster alerting.

---

## 5. Cutover and Migration Guide

### 5.1 Offline Development vs. Live PostgreSQL Cutover
1. **Offline / Development Database (SQLite)**:
   - Disposable databases automatically apply pending migrations on container startup:
     ```bash
     dotnet ef database update --context SqliteAppDbContext
     ```
2. **Live Production Database (PostgreSQL)**:
   - Migrations are strictly additive and backward-compatible.
   - Run migration prior to deploying the updated application container:
     ```bash
     dotnet ef database update --context PostgresAppDbContext --connection "$PROD_POSTGRES_CONNECTION"
     ```
   - Schema additions (`MilestoneOutboxEvents`, `MonitoringIncidents`, `CampaignCosts`, etc.) do not lock existing product tables (`Users`, `Invoices`, `PayOsAttempts`).
   - The application executes DML queries only.

### 5.2 Rollback Procedure
If a rollback of the application container is required:
1. Revert container image to the prior stable release tag.
2. The database schema is additive and non-breaking: newly added tables and columns remain dormant without conflicting with older code versions.
3. No destructive migration down-scripts should be run against live PostgreSQL during an active incident.

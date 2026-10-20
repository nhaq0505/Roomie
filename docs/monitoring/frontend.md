# Frontend Monitoring Subsystem Documentation

## 1. Overview and Architecture

The Roomie frontend monitoring subsystem provides an internal, privacy-first, first-party analytics and operations tracking dashboard for administrative staff (`/admin/monitoring`), alongside non-blocking first-party browser event and attribution collection on public marketing and authentication pages.

### 1.1 Core Design Principles
- **No Third-Party Analytics:** Operates without Google Analytics, Mixpanel, cookies, or external tracking pixels.
- **Strict Privacy Invariants:** Zero PII (no emails, phone numbers, passwords, OTPs, raw authentication tokens, tenant tokens, or IP addresses) stored or transmitted in analytics payloads.
- **Server-Authoritative Milestones:** Activation, first-paid Pro subscriptions, and verified owner registrations are determined solely by backend domain events. The client tracker never emits or trusts client-claimed activation or payment events.
- **SSR & Prerender Safety:** Tracking logic executes strictly on the browser (`typeof window !== 'undefined'`) within `useEffect` or user event handlers. Static prerendering (`scripts/prerender.mjs`) and Vite build pipelines run without interference.
- **Non-Blocking Best-Effort Delivery:** Collector network or storage failures never interrupt or delay authentication, registration, navigation, or operational workflows.
- **Native SVG Visualizations:** Implemented purely with React and Ant Design 5 components without third-party chart library dependencies (recharts, chart.js, etc.).
- **Vietnam Time & Calendar Standard:** All business dates, cohort periods, and timestamps are displayed in Vietnam Time (`Asia/Ho_Chi_Minh`, UTC+7).

---

## 2. Component Structure and File Organization

```
frontend/
├── src/
│   ├── app/
│   │   ├── App.tsx                     # Lazy route registration for /admin/monitoring with Gate admin
│   │   └── layout/
│   │       └── AppShell.tsx            # Navigation menu entry under Admin section
│   ├── features/
│   │   ├── admin/
│   │   │   └── monitoring/
│   │   │       ├── index.ts            # Public feature exports
│   │   │       ├── types.ts            # Contract-aligned TypeScript types (Growth, Revenue, Ops, Incidents, Costs)
│   │   │       ├── api.ts              # API client methods and React Query hooks
│   │   │       ├── AdminMonitoringPage.tsx # Shell page with PageTitle, filters bar, and 3 tabs
│   │   │       ├── GrowthTab.tsx       # Tab 1: Growth, WAU/MAU, Funnel, Cohort Maturity, Traffic, Top CTAs
│   │   │       ├── RevenueTab.tsx      # Tab 2: Pro Revenue, Paid Subscriptions, Spend, CAC30, Campaign Costs
│   │   │       ├── OperationsTab.tsx   # Tab 3: Host metrics, SLA workers, Queues, Routes, Incidents
│   │   │       ├── MonitoringFiltersBar.tsx # Range presets (7d, 30d, 90d, custom), Source & Campaign filters, Ops window
│   │   │       ├── MonitoringState.tsx # Stat cards, StaleIndicator, UnknownMetric, HealthTag, Error/Empty states
│   │   │       ├── NativeCharts.tsx    # Native SVG Sparkline, BarChart, MultiLineChart, Funnel, LatencyHistogram
│   │   │       ├── CampaignCostModal.tsx # Create & edit modal for manual campaign spend (cash, hours, hourly rate)
│   │   │       └── UtmLinkBuilderModal.tsx # Admin UTM campaign link generator with clipboard copy
│   │   ├── auth/
│   │   │   ├── RegisterPage.tsx        # Carries attribution context into /api/auth/register
│   │   │   └── api.ts                  # RegisterRequest supports optional attribution DTO
│   │   └── marketing/
│   │       └── LandingPage.tsx         # Records first-party attribution and tracks allowlisted landing/CTA events
│   └── shared/
│       ├── analytics/
│       │   ├── index.ts
│       │   └── browserTracker.ts       # First-party tracker primitive (ID, session, route normalization, PII scrub)
│       └── i18n/                       # Catalogs (source.json, translations.tsv, en, zh, ko, hi)
└── scripts/
    └── test-analytics-tracker.mjs      # Comprehensive automated verification suite for browser tracker
```

---

## 3. First-Party Browser Tracker (`browserTracker.ts`)

### 3.1 Identifier & Session Lifecycle
- **Browser ID (`roomie_bid`):** A unique, random identifier generated via `crypto.randomUUID` (with fallback to `crypto.getRandomValues` and pseudo-random byte generator). Stored in `localStorage` indefinitely per browser; never linked across devices.
- **Session ID (`roomie_sid`):** Generated upon initial visit. Persists until **30 minutes of user inactivity** (`now - lastActive >= 30 * 60 * 1000`). If idle for 30 minutes or longer, a fresh session ID is issued.
- **Storage Isolation & Stability:** When `localStorage` is blocked or unavailable (e.g. private browsing, Safari quota errors), an in-memory storage fallback (`inMemoryStorage`) maintains stable browser and session IDs across page lifecycle without generating duplicate IDs.

### 3.2 Attribution Model (First-Touch & Last Non-Direct)
- **First Touch (`roomie_first_touch`):** Captured on the user's initial visit and preserved for up to **30 days**. Subsequent visits (even direct or campaign visits) do NOT overwrite an active first-touch record.
- **Last Non-Direct (`roomie_last_non_direct`):** Recorded whenever the visitor arrives via a non-direct source (e.g. UTM parameters or external referrer). Direct visits do not overwrite the last non-direct source. Expires after **30 days**.
- **Registration Attribution:** When a user registers (`/register`), `getRegistrationAttributionForApi()` attaches a structured `RegistrationAttributionDto` with independent `firstTouch?: AttributionTouchDto` and `lastTouch?: AttributionTouchDto`, alongside flattened UTM fields (`utmSource`, `utmMedium`, `utmCampaign`, `utmContent`, `utmTerm`) for backward compatibility.

### 3.3 Route Normalization and PII Scrubbing
- **Route Allowlisting & Masking:** Paths sent to `/api/analytics/events` are scrubbed of query parameters and hashes. Tenant routes (`/t/:token`, `/tenant/:token/invoices/:id/checkout`) are automatically normalized to `/tenant/[token]` and `/tenant/[token]/invoices/[invoice_id]/checkout`. GUIDs and numeric IDs in arbitrary paths are masked.
- **PII Scrubbing:** Text strings and properties are inspected with regex for email addresses, Vietnamese phone numbers (`03/05/07/08/09...`), OTP codes, and JWT/Bearer tokens.
- **Cycle & Depth Protection:** `scrubPii()` uses a `WeakSet` to detect cyclic object graphs and bounds traversal depth to 5 levels to guarantee zero stack overflow risk.

### 3.4 Bounded Event Batch Payload
Events sent to `POST /api/analytics/events` strictly conform to the backend `AnalyticsEventBatchRequest`:
```json
{
  "browserId": "c4b18f8e-d4c3-4d82-841f-11758f504381",
  "sessionId": "s-882f07ab-3f41",
  "events": [
    {
      "eventId": "evt-7f1a92e4-a12b",
      "eventType": "page_view",
      "eventName": "Landing Page",
      "path": "/",
      "referrerPath": "",
      "ctaId": "btn_hero_signup",
      "utmSource": "facebook",
      "utmMedium": "cpc",
      "utmCampaign": "summer_promo",
      "clientTimestamp": "2026-10-20T09:15:00.000Z"
    }
  ]
}
```

Allowlisted `eventType` values:
`page_view`, `cta_click`, `session_start`, `landing_view`, `pricing_view`, `auth_attempt`, `registration_submit`.

---

## 4. Admin Dashboard Tabs (`/admin/monitoring`)

### 4.1 Growth & Usage Tab (`GrowthTab.tsx`)
- **KPI Summary Cards:**
  - `Total Sessions` (30m idle, first-party ID)
  - `Unique Visitors` (first-party browser ID)
  - `New Registrations`
  - `Activated Owners` (milestone: first Monthly invoice enters `Issued`)
  - `WAU7` (Distinct owners with `HasOwnerAction == true` over the last 7 Vietnam days; excludes Admin and System automated tasks)
  - `MAU30` (Distinct owners with `HasOwnerAction == true` over the last 30 Vietnam days)
- **Conversion Funnel:** Native SVG multi-step funnel showing drop-off and conversion rates from Sessions to Registrations to Activated Owners.
- **Cohort Maturity:**
  - 7-Day Activation Rate (`activationRate7Day`) and matured count.
  - 30-Day First Paid Rate (`firstPaidRate30Day`) and matured count.
  - Clear tag indicators: `Trưởng thành` (Matured) vs `Chưa đủ 7 ngày trưởng thành` / `Chưa đủ 30 ngày trưởng thành` (Immature).
- **Retention Matrix Table:** Month-over-month retention percentages by activation cohort based on next CALENDAR month activity (not fixed 30-day maturity). Month 0 renders an explicit unknown state (`— (Chưa xác định)`) when data is null, never inventing default 100% values.
- **Traffic Sources & Top CTAs:** Paginated tables displaying source/medium/campaign breakdowns and CTA click conversion rates.

### 4.2 Revenue Tab (`RevenueTab.tsx`)
- **Pro Revenue Invariants:**
  - Includes ONLY server-confirmed paid subscription orders (`PayOsAttempt` with `Kind == "Subscription"` and `Status == "Paid"`).
  - Central webhook and manual reconciliation deduplicate payments.
  - Admin-granted complimentary tiers and tenant rent payments are strictly excluded.
- **CAC30 (Customer Acquisition Cost):**
  - Formula: Total marketing spend in signup cohort period / New first-paid owners within 30 days.
  - Distinct Null Reasons: Handled gracefully via `CacNullReasonTag`:
    - `MissingCostData` -> "Chưa nhập chi phí"
    - `ZeroDenominator` -> "Chưa có chủ trọ trả phí"
    - `CohortImmature` -> "Chưa đủ 30 ngày trưởng thành"
    - `NoRegistrations` -> "Chưa có lượt đăng ký"
- **Daily Trends Chart:** Native SVG multi-line chart comparing Subscription Revenue (VND) vs Marketing Spend (VND).
- **Manual Campaign Cost Editor (`CampaignCostModal.tsx`):**
  - Bound to `/api/admin/monitoring/campaigns/costs` (GET & POST) with campaign filter support.
  - Supports Create and Edit operations.
  - Fields: Campaign name, Channel, Source, Date, Cash expense (VND), Person-hours, Hourly rate (VND/hour), Notes.
  - Dynamic cost preview: `Cash + (Hours * Hourly Rate)`.
  - Displays audit metadata: `UpdatedByName` and `UpdatedAt`.
- **UTM Link Builder (`UtmLinkBuilderModal.tsx`):**
  - Generates standardized tracking URLs with presets for channels (`facebook`, `google`, `zalo`, etc.) and medium (`cpc`, `social`, etc.).
  - One-click clipboard copy with user feedback.

### 4.3 Operations Tab (`OperationsTab.tsx`)
- **Overview Stat Cards:** Overall status (`Healthy`, `Warning`, `Critical`, `Degraded`), API 5xx error rate %, p95 latency (ms), requests in last hour, Database connectivity.
- **Configurable Ops Windows:** Supports `1h`, `24h`, and `7d` operating windows dynamically bound through query parameters and queryKey caching.
- **Host Metrics (Systemd JSON Mount):**
  - CPU, RAM, Disk utilization progress bars with alarm thresholds (> 85%).
  - Container active count and `/api/health` status. Null host telemetry renders explicit unknown states (`— (Chưa xác định)`), preserving authentic 0% measurements.
  - Stale data indicator (`StaleIndicator`).
- **Operational Queues:**
  - Zalo Pending Queue & Eligible Window (respects 07:00–21:00 operating window).
  - PayOS Pending Reconciliation count.
  - SMTP Accepted Not Delivered count.
  - Null queue telemetry displays explicit unknown states (`— (Chưa xác định)`), never coalesced to healthy zeros.
- **Authoritative Backup Watchdog:** Surfaces authoritative 27-hour authority watchdog status from backend telemetry; renders an explicit Unknown state when unsupplied by the server (no invented green/healthy status).
- **Background Worker Telemetry:** Status table covering `recovery-email`, `zalo-reminders`, `payos-reconcile`, and `invoice-sweep`.
- **Route Latency Histogram:** Renders an explicit unavailable state (`Chưa có dữ liệu phân phối độ trễ`) when backend bucket telemetry is absent, strictly avoiding fabricated or synthetic metric distributions.
- **Incidents & Resolution:** Surfaces open alerts with severity badges, durations, notification count, and state transition actions (Acknowledge, Resolve).

---

## 5. UI States and Metric Semantics

| State | Visual Treatment | Description |
|---|---|---|
| **Loading** | Skeleton / Spin | Displayed while React Query fetches remote metrics. |
| **Unknown** | `— (Chưa xác định)` | Shown when metric is not computable or has missing dependencies. Accompanied by explanatory tooltip. |
| **Zero** | `0` or `0 VND` | Distinct from Unknown; represents an authentic zero measurement. |
| **Stale** | `Dữ liệu cũ (HH:mm)` Tag | Indicated when collector or host telemetry timestamp exceeds freshness window. |
| **Empty** | Ant Design `Empty` | Shown when no records exist for the selected filters. |
| **501 Not Implemented** | Informational Alert | Displayed when backend contract services are pending downstream worker completion. |
| **Error** | Red Alert with Retry Button | Rendered on network or server 5xx failure with explicit retry capability. |

---

## 6. Verification and Testing

The frontend implementation is verified through automated test suites:
- **`npm run check:architecture`:** Enforces strict boundary rules between `shared/`, `features/`, and `app/`. Prohibits circular dependencies (108 source modules verified).
- **`npm run i18n:check`:** Verifies 100% Vietnamese UI string coverage and placeholder consistency across all 4 language catalogs (`en`, `zh`, `ko`, `hi`) (1345 keys verified).
- **`npm run test:analytics`:** Validates browser ID persistence, exact 30-minute idle session boundaries, in-memory storage fallback, route normalization, token masking, cyclic object scrubbing, first-touch preservation, and batch request payload formats.
- **`npx playwright test e2e/monitoring.spec.ts`:** Validates Admin monitoring rendering on desktop (1440px) and mobile (375px), Gate access control, network request routing with window (`1h`/`24h`/`7d`) and campaign filters, explicit unknown states on null telemetry without synthetic numbers, and preservation of authentic zero values.
- **`npm run build`:** Confirms TypeScript typecheck (`tsc -b`), production Vite bundling with lazy route code-splitting, and static SSR prerendering (`scripts/prerender.mjs`).

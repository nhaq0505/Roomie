# Task T6 Independent Frontend & User Journey Review Report

## 1. Executive Summary
- **Reviewer**: Worker C (Independent Frontend & User Journey QA)
- **Reviewed Snapshot SHA**: `76cde7e35be2cca72f5f63a1ab6a49ee88edb212` (incorporating T4 base `d30fe7716e95c3f450cb488f2a482acf28918b36`)
- **Review Environment**: Isolated review worktree `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/monitoring-review-t6` on branch `review-t6`. Base checkout `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/LandingPage` remained completely untouched.
- **Service & Provider Safety**: In-process test fakes, loopback SMTP sink fixture, and disposable SQLite databases only. Zero external traffic to real payOS, real SMTP, or production servers.
- **Overall Verdict**: **Conditional Approval with High-Severity Mobile Viewport Defect and Four Medium/Minor UI/UX Defects.**
  The architectural foundation, security gates, privacy safeguards, SSR build safety, nonblocking analytics tracker, and connected backend business flows are solid and verified. However, the mobile layout (375px) suffers from severe horizontal document overflow across all three monitoring tabs (reaching up to 777px on the Revenue tab), which was masked in the original test suite by an omitted `scrollWidth` assertion. In addition, `CampaignCostModal` improperly zeroes out missing hourly valuations on edit save, and clamped date window boundaries (`effectiveFromDate`/`effectiveToDate`) are ignored in the UI.

---

## 2. Independent Evaluation Matrix

| Category | Requirement & Invariant | Verification Method | Status | Findings / Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Admin Gate Security** | `/admin/monitoring` restricted to Admin role; non-admins, anonymous, and unapproved users blocked | Playwright E2E & Code Inspection (`App.tsx:32-41,79`) | **PASS** | `Gate admin` redirects anonymous to `/login`, non-admin owners to `/dashboard`, unapproved owners to `/account-status`. |
| **Tab Shell & Layout** | Complete Admin shell, side navigation item, 3 tabs: Growth, Revenue, Operations | Playwright E2E & Visual Inspection (`AppShell.tsx:49`, `AdminMonitoringPage.tsx:118-185`) | **PASS** | Tabs navigate seamlessly; side navigation active state tracked; all strings localized via `t()`. |
| **Data vs Contract** | Exact match with backend API contracts for Growth, Revenue, Operations, Incidents, Campaigns | Contract comparison against `MonitoringDtos.cs` and `MonitoringEndpoints.cs` | **PASS (with minor typing fix)** | All HTTP verbs, query parameters, and endpoints match backend contracts. Minor typing discrepancy on `retentionPercentages`. |
| **SSR / Prerender Safety** | Build (`tsc -b && vite build && node scripts/prerender.mjs`) must not access browser DOM in Node | Production build execution (`npm run build`) | **PASS** | `AdminMonitoringPage` is dynamically imported (`lazy`); `browserTracker.ts` guards with `isBrowser()`. 0 build errors. |
| **Privacy & PII Protection** | Zero email, phone, OTP, token, IP, bank details, or raw query parameters in analytics | Unit tests (`npm run test:analytics`) & Code Audit (`browserTracker.ts:209-270`) | **PASS** | Strict regex scrubbing of emails, phones, OTPs, JWT/hex tokens, passwords, and URL query sanitization with ID masking. |
| **Non-blocking Tracking** | Browser tracker must never throw or break auth/registration upon network failure | Automated error injection & Code Audit (`browserTracker.ts:627-647`) | **PASS** | `sendBeacon` fallback to `fetch` with `credentials: 'same-origin'`, wrapped in `try/catch` and `.catch(() => {})`. |
| **OTP Context & Attribution** | Attribution carried through OTP registration & resend; bypass recorded | Connected E2E test & Unit tests | **PASS** | Attribution context (`browserId`, `firstTouch`, `lastTouch`, UTMs) successfully transmitted to `/api/auth/register` and persisted. |
| **Collector Resilience** | Failure or absence of host collector must not break registration or app | Connected E2E test with broken collector path | **PASS** | Registration, OTP verification, and owner login succeed 100% when host collector file is missing or corrupt. |
| **Timezone & Calendars** | Vietnam calendar (`Asia/Ho_Chi_Minh` UTC+7) default 30d business / 1h ops | Automated inspection (`MonitoringFiltersBar.tsx`, `AdminMonitoringPage.tsx`) | **PASS (with minor edge case)** | Explicit `timeZone: 'Asia/Ho_Chi_Minh'` in formatters. Minor date arithmetic bug in `AdminMonitoringPage.tsx`. |
| **Unknown vs Zero Telemetry** | Missing/stale telemetry renders explicit Unknown ("—"), never synthetic zeros | Playwright E2E (`e2e/monitoring.spec.ts:390-452`) | **PASS** | Null CPU/RAM, absent watchdog, and null month-0 retention display explicit "— (Chưa xác định)", never healthy zeros. |
| **Chart & Visualization** | Zero third-party chart dependencies; native SVG and AntD components | Code audit (`NativeCharts.tsx`) | **PASS** | Sparklines, multi-line charts, bar charts, and histograms implemented purely with native SVG and AntD. Null values split lines cleanly. |
| **Campaign Cost UX** | Manual campaign spend (cash + hours @ rate), audit trail (`updatedByName`) | Component audit (`RevenueTab.tsx`, `CampaignCostModal.tsx`) | **DEFECT** | `CampaignCostModal` coerces missing `hourlyRate` (`null`) to `0`, concealing missing valuation. |
| **Responsive Viewports** | Mobile 375px and Desktop 1440px zero horizontal page overflow | Playwright automated measurement & screenshots | **DEFECT** | Critical horizontal overflow on 375px viewport (381px on Growth, 777px on Revenue, 462px on Operations). |

---

## 3. Severity-Ranked Defects & Deficiencies

### [HIGH] Defect T6-01: Severe Horizontal Page Overflow on Mobile Viewport (375px)
- **Files**:
  - `frontend/src/features/admin/monitoring/GrowthTab.tsx:307-315` (`sourceColumns` table)
  - `frontend/src/features/admin/monitoring/RevenueTab.tsx:293-301` (`costColumns` table, 9 columns)
  - `frontend/src/features/admin/monitoring/RevenueTab.tsx:313-320` (`orderColumns` table, 5 columns)
  - `frontend/src/features/admin/monitoring/OperationsTab.tsx:474-482` (`workerColumns` table, 5 columns)
  - `frontend/src/features/admin/monitoring/OperationsTab.tsx:488-496` (`routeColumns` table, 6 columns)
- **Reproduction**:
  Set Playwright or browser viewport to `{ width: 375, height: 667 }`. Navigate to `/admin/monitoring`.
  Measure `document.documentElement.scrollWidth` across tabs:
  - **Growth Tab**: `scrollWidth = 381px` (6px overflow beyond viewport)
  - **Revenue Tab**: `scrollWidth = 777px` (**402px overflow — more than 2× viewport width!**)
  - **Operations Tab**: `scrollWidth = 462px` (87px overflow beyond viewport)
- **Root Cause**:
  Standard AntD `<Table>` components are instantiated without responsive encapsulation (such as the project's existing `frontend/src/shared/ui/ResponsiveTable.tsx`) and without inner horizontal scroll containers (`scroll={{ x: 'max-content' }}`). On screens narrower than 768px, wide multi-column tables push out the card boundaries and force the root `<html>`/`<body>` element to scroll horizontally.
- **Expected**:
  All monitoring tabs must render within `375px` with strict `document.documentElement.scrollWidth <= 375`. Tables must either use `ResponsiveTable` (rendering vertical card articles on compact screens) or contain scrolling locally via `scroll={{ x: 600 }}` and CSS `overflow-x: auto`.
- **Evidence Screenshots**:
  - `file:///Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/screenshots/mobile-375-growth.png`
  - `file:///Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/screenshots/mobile-375-revenue.png`
  - `file:///Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/screenshots/mobile-375-operations.png`

---

### [MEDIUM] Defect T6-02: Missing Test Assertion Masked Mobile 375px Viewport Overflow
- **File**: `frontend/e2e/monitoring.spec.ts:323-336`
- **Reproduction**:
  Review the existing Playwright test `Mobile 375px: responsive layout renders correctly on mobile viewport`:
  ```ts
  test('Mobile 375px: responsive layout renders correctly on mobile viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/admin/monitoring');
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
  });
  ```
- **Root Cause**:
  Unlike all other mobile tests in the repository (`smoke.spec.ts:132`, `zalo.spec.ts:32`, `payment-modes.spec.ts:70`, `ui-readability.spec.ts:64`), this test only checked that text was visible, omitting the mandatory invariant check:
  `expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);`
  This allowed the test to pass with 60/60 green while hiding the 777px horizontal blowout.
- **Expected**:
  Add `expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);` to each tab step in `e2e/monitoring.spec.ts`.

---

### [MEDIUM] Defect T6-03: `CampaignCostModal` Coerces Missing Hourly Valuation to Zero on Edit
- **File**: `frontend/src/features/admin/monitoring/CampaignCostModal.tsx:33,41,58`
- **Reproduction**:
  1. Have an existing campaign cost item with `personHours = 10` and `hourlyRate = null` (representing unvalued volunteer/founder labor).
  2. In `RevenueTab.tsx:116-121`, the table displays: `<Tag color="warning">Thiếu đơn giá giờ công</Tag>`.
  3. Click "Sửa" (Edit) to open `CampaignCostModal`.
  4. Notice line 33: `setHourlyRate(initialItem.hourlyRate || 0);` and line 41: `hourlyRate: initialItem.hourlyRate || 0`.
  5. The form input populates with `0 VND/giờ` instead of remaining empty/unspecified.
  6. Line 58 calculates: `const totalCost = (cashExpense || 0) + (personHours || 0) * (hourlyRate || 0);` -> displays total cost as `cashExpense + 0` with no indication of missing valuation.
- **Expected**:
  When `hourlyRate` is `null`, the input should remain empty (`undefined`), total cost should display a notice that labor valuation is incomplete, and saving without editing the rate must preserve `null` rather than coercing to `0`.

---

### [LOW] Defect T6-04: Clamped Date Boundaries (`effectiveFromDate` / `effectiveToDate`) Not Rendered
- **File**: `frontend/src/features/admin/monitoring/types.ts:92-93,147-148`
- **Details**:
  The backend contracts return `effectiveFromDate` and `effectiveToDate` on both Growth and Revenue endpoints so that administrators know if a requested 90-day range was clamped by backend data retention or boundary dates.
  While declared in `types.ts`, these fields are never referenced or rendered in `GrowthTab.tsx`, `RevenueTab.tsx`, or `MonitoringFiltersBar.tsx`.
- **Expected**:
  Surface a subtle subtitle or tag when `effectiveFromDate` / `effectiveToDate` differ from requested dates (e.g., "Dữ liệu thực tế: 2026-08-01 đến 2026-10-20").

---

### [LOW] Defect T6-05: Fallback Funnel Suppression on Null/Zero Session Coverage
- **File**: `frontend/src/features/admin/monitoring/GrowthTab.tsx:145,242-246`
- **Details**:
  In line 145: `{ label: t("Phiên truy cập"), count: summary?.totalSessions ?? 0, color: '#17a486' }`.
  In line 242:
  ```tsx
  ) : summary && summary.totalSessions !== null && summary.totalSessions > 0 ? (
    <NativeConversionFunnel stages={funnelStages} />
  ) : (
    <MonitoringEmptyState />
  )
  ```
  When `data.funnel` is null and `summary.totalSessions` is `null` (such as multi-day windows > 90d where raw events are pruned, but registration counts exist in daily aggregates), the UI renders `<MonitoringEmptyState />` instead of showing the registrations and activations that *are* known, discarding valid business data.
- **Expected**:
  Render the fallback funnel stages with an explicit "—" (Unknown) on the Session stage if `totalSessions == null`, while preserving the non-null registration and activation stages.

---

### [MINOR] Defect T6-06: TypeScript `CohortRetentionRowDto` Nullability Discrepancy
- **File**: `frontend/src/features/admin/monitoring/types.ts:58`
- **Details**:
  In `types.ts`, `CohortRetentionRowDto.retentionPercentages` is typed as `number[];`.
  In the backend C# contract (`MonitoringDtos.cs:93`), it is `IReadOnlyList<double?> RetentionPercentages`.
  At runtime, immature cohorts return `null` entries (e.g. `[null, null]`), and `GrowthTab.tsx:80,91,102` correctly checks `!== null && !== undefined`.
- **Expected**:
  Update definition to `retentionPercentages: (number | null)[];` to reflect the contract accurately.

---

### [MINOR] Defect T6-07: Timezone Date Arithmetic in `AdminMonitoringPage.tsx:36-39`
- **File**: `frontend/src/features/admin/monitoring/AdminMonitoringPage.tsx:36-39`
- **Details**:
  `getInitialDates()` creates `new Date(todayStr)` (parsed as UTC midnight) and then executes `from.setDate(from.getDate() - 29)` using local browser time before calling `.toISOString().split('T')[0]`.
  In non-Vietnam negative UTC timezones (e.g., UTC-5 New York), this causes a 1-day off-by-one calculation.
- **Expected**:
  Use `dayjs(todayStr).subtract(29, 'day').format('YYYY-MM-DD')` consistently, as done in `MonitoringFiltersBar.tsx`.

---

## 4. Connected End-to-End User Journey Evidence
To verify beyond mocked HTTP tests, a full connected user journey was executed using the real compiled ASP.NET Core web server (`backend/RoomRental.Api/bin/Debug/net10.0`), a loopback SMTP sink fixture (`tests/otp_smtp_fixture.py`), and a disposable SQLite database.

> [!NOTE]
> Scope Clarification: This connected journey strictly proves the lifecycle through first monthly invoice activation and operations telemetry. It does NOT exercise server-confirmed Pro subscription upgrades (which are tested separately in `tests/RoomRental.WebhookTests`).
> Furthermore, broken host collector resilience (verifying that missing or corrupt host collector JSON on disk does not fail registration or app startup) is distinct from browser analytics network failure resilience (verifying that client tracker beacon/fetch failure does not break auth or navigation); each is verified under its own independent test harness.

### Executed User Story Steps:
1. **UTM Touchpoint Tracking**: Sent `POST /api/analytics/events` with anonymous `BrowserId` (`bid-e2e-1`), `SessionId`, and UTM campaign metadata (`utm_source=facebook&utm_medium=cpc&utm_campaign=summer_promo_2026`). Recorded with 200 OK (`processedCount: 1`).
2. **Registration under Simulated Collector Failure**: Sent `POST /api/auth/register` passing the attribution touchpoints. Configured API with a non-existent host collector path (`/tmp/broken-collector/host-metrics.json`). Registration succeeded with `202 Accepted` and `verificationRequired: true`, proving that host collector file absence does not block user registration.
3. **Loopback OTP Verification**: Local SMTP sink intercepted the registration email on loopback. Extracted 6-digit OTP code (`519968`) and submitted `POST /api/auth/verify-registration`. Received `201 Created`. Authenticated as the new owner (`Owner ID: 10b14a7c-...`, `Status: Approved`).
4. **Deposit Invoice Issuance Invariant**: Owner created a building, room, and checked in a tenant with a 3,000,000 VND deposit. Querying Admin Growth endpoint `/api/admin/monitoring/growth` confirmed `activatedOwners` remained at `0` (Deposit invoice does NOT activate owner).
5. **Monthly Invoice Issuance Activation**: Owner submitted meter reading (`POST /api/readings`), triggering first Monthly invoice issuance. Querying Admin Growth endpoint confirmed `activatedOwners` immediately incremented from `0` to `1` (Owner ACTIVATED).
6. **Operations Telemetry Resilience**: Admin queried `/api/admin/monitoring/operations?window=1h`. System overview returned `Healthy`, routes latency captured, and absent host collector gracefully defaulted to `Unknown` status without throwing 500.

*Verification Script*: `/Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/scratch/test_t6_complete_journey.py`  
*Result*: **100% PASS (attestation bounded through monthly invoice activation & operations; confirmed Pro tested separately in webhook suites)**

---

## 5. Visual Evidence & Screenshot Manifest

Screenshots were captured using Playwright across desktop and mobile viewports, stored in the persistent artifact directory:

| Viewport | Page / State | File Path | Status |
| :--- | :--- | :--- | :--- |
| **Desktop 1440px** | Growth & Usage Tab | `screenshots/desktop-1440-growth.png` | **PASS**: Clean layout, no overflow (`scrollWidth = 1440px`) |
| **Desktop 1440px** | Revenue & CAC30 Tab | `screenshots/desktop-1440-revenue.png` | **PASS**: Clean layout, KPI grid, trends chart |
| **Desktop 1440px** | UTM Link Builder Modal | `screenshots/desktop-1440-utm-modal.png` | **PASS**: Form controls, quick presets, copy button |
| **Desktop 1440px** | Operations & Workers Tab | `screenshots/desktop-1440-operations.png` | **PASS**: Host systemd gauges, queue cards, workers table |
| **Mobile 375px** | Growth Tab | `screenshots/mobile-375-growth.png` | **FAIL**: Horizontal overflow (`scrollWidth = 381px`) |
| **Mobile 375px** | Revenue Tab | `screenshots/mobile-375-revenue.png` | **FAIL**: Massive horizontal blowout (`scrollWidth = 777px`) |
| **Mobile 375px** | Operations Tab | `screenshots/mobile-375-operations.png` | **FAIL**: Horizontal overflow (`scrollWidth = 462px`) |

*Absolute Artifact Directory*: `/Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/screenshots/`

---

## 6. Comprehensive Test Execution Suite Evidence

| Test Suite | Execution Command | Result Evidence | Status |
| :--- | :--- | :--- | :--- |
| **Frontend Architecture** | `npm run check:architecture` | 108 source modules checked; 0 circular dependencies; feature layer boundaries respected | **PASS** |
| **Frontend i18n Catalogs** | `npm run i18n:check` | 1,353 keys × 4 catalogs (`vi`, `en`, `zh`, `ko`, `hi`); 0 missing translations | **PASS** |
| **Analytics Tracker Unit** | `npm run test:analytics` | First-party browser tracker; 30m idle session; PII regex scrubbing; allowlist validation | **PASS** |
| **HTTP Transport Client** | `npm run test:http` | Headers, envelope unwrapping, single financial submission, error types | **PASS** |
| **API Paging Helper** | `npm run test:api` | Bounded pagination past 100 rows | **PASS** |
| **Production Build & Prerender** | `npm run build` | `tsc -b && vite build && node scripts/prerender.mjs` succeeded in 2.36s | **PASS** |
| **Mocked Playwright E2E** | `npm run test:e2e` | 60 passed across 6 workers in 15.3s | **PASS** |
| **Backend Webhook & Operations** | `dotnet run --project tests/RoomRental.WebhookTests` | All 18 sub-suites passed (MonitoringFoundation, MonitoringBusiness, OperationsMonitoring, etc.) | **PASS** |
| **Backend Zalo Integration** | `dotnet run --project tests/RoomRental.ZaloTests` | 53 assertions passed; zero external provider traffic | **PASS** |
| **Python Host Collector** | `python3 -m unittest tests/test_host_collector.py` | 34 unit tests in 1.09s | **PASS** |
| **Connected E2E Journey** | `python3 scratch/test_t6_complete_journey.py` | Complete connected flow (UTM -> signup -> OTP -> deposit -> monthly invoice -> operations) | **PASS** |

---

## 7. Honest Limitations & Non-Tested Scope
1. **Live PostgreSQL Network Execution**: Live connection against PostgreSQL was not executed because no local PostgreSQL test database daemon was provisioned on loopback in this test worker machine. Snapshot and migration model integrity was verified via `HasPendingModelChanges() == false`.
2. **Real payOS Merchant Network Traffic**: Real merchant payOS network calls and webhooks were not sent to external servers; verified using in-process test fixtures and cryptographic signature validators.
3. **Real External SMTP Servers**: Real external email dispatch was prevented; all emails were routed to loopback SMTP sinks.
4. **Confirmed Pro Subscription in Connected Journey**: The connected journey fixture attestation explicitly stops after verifying first monthly invoice activation and operations metrics; server-confirmed Pro subscription upgrades and payment reconciliation were verified separately in backend webhook integration test suites (`tests/RoomRental.WebhookTests`), not in the live loopback script.
5. **Separation of Collector Resilience vs Browser Tracker Resilience**: Broken host collector file resilience (verifying that missing or unreadable `/tmp/.../host-metrics.json` on disk gracefully degrades without failing registration or crashing app endpoints) is architecturally distinct from browser analytics network resilience (verifying that client tracker beacon/fetch drops do not block authentication or form submission). They are governed by separate test fixtures and must not be conflated as a single resilience proof.

---

## 8. Recommendations for Coordinator & Feature Owners
1. **Assign Mobile Overflow Fix (Worker B / Frontend Owner)**:
   - Wrap `<Table>` components in `GrowthTab.tsx`, `RevenueTab.tsx`, and `OperationsTab.tsx` with responsive horizontal scroll containers (`scroll={{ x: 'max-content' }}` or `scroll={{ x: 600 }}`), or adopt `ResponsiveTable.tsx` for compact screens.
   - Update `e2e/monitoring.spec.ts:323-336` to include `expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);`.
2. **Fix `CampaignCostModal` Valuation Preservation**:
   - In `CampaignCostModal.tsx:33,41`, do not default `initialItem.hourlyRate` to `0` when `null`. Maintain an empty/undefined input to preserve the `MissingValuation` state.
3. **Surface Clamped Range in Header**:
   - Display `effectiveFromDate` and `effectiveToDate` when returned by the backend so users know if their requested query range was bounded.
4. **Standardize Date Arithmetic with `dayjs`**:
   - Refactor `AdminMonitoringPage.tsx:36-39` to use `dayjs` for Vietnam date subtraction.

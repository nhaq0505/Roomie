# Task T6b Independent UI Correction Re-Review Report

## 1. Executive Summary
- **Reviewer**: Worker C (Independent Frontend & User Journey QA Reviewer)
- **Reviewed Commit SHA**: `e1d90c8ce0a5cd18ea8d25a7b74d765adcc5880b` (`fix(monitoring): resolve T6 mobile overflow, hourly rate preservation, clamped date tags, and honest funnel`)
- **Base / Parent SHA**: `867bbf055dde074053353369013563bbdd0e9677` (incorporating integrated base `76cde7e35be2cca72f5f63a1ab6a49ee88edb212`)
- **Review Environment**: Isolated worktree `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/monitoring-review-t6` on detached commit `e1d90c8`. Base checkout `/Users/nguyenhoanhquan/orca/workspaces/Quản Lý Phòng/LandingPage` remained completely untouched.
- **Provider & Service Safety**: In-process test fakes, loopback SMTP sink fixture, and disposable SQLite databases only. Zero external traffic to real payOS, real SMTP, or production servers.
- **Final Verdict**: **Full Unconditional Approval**. All 7 defects identified in the prior review report (`docs/monitoring/t6-review.md`) have been verified resolved. Mobile 375px viewport exhibits strict zero document overflow (`scrollWidth <= 375px`) while preserving local table scroll and tab interactivity; hourly labor valuation distinguishes unvalued volunteer labor (`null`) from free labor (`0`); clamped server dates surface clearly without corrupting user filter intent; fallback funnel displays known conversion milestones without synthetic zeros; and Vietnam date arithmetic is completely resilient to negative UTC browser timezones.

---

## 2. Independent Verification Matrix of All 7 Prior Findings

| Finding ID | Severity | Description | Resolution in `e1d90c8` | Independent Verification Result | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **T6-01** | **HIGH** | Mobile 375px viewport document overflow across all 3 tabs (Growth 381px, Revenue 777px, Operations 462px). | Tables wrapped in `.monitoring-table-wrapper` with `overflow-x: auto;` and `-webkit-overflow-scrolling: touch;`. Added `max-width: 100%; overflow-x: hidden;` to `.admin-monitoring-page` and tab contents. Set numeric `scroll.x` on all tables. | Playwright measured `document.documentElement.scrollWidth <= 375` across Growth (375px), Revenue (375px), and Operations (375px). Tables scroll locally without clipping controls. | **RESOLVED** |
| **T6-02** | **MEDIUM** | Missing `scrollWidth` test assertion in `e2e/monitoring.spec.ts` allowed horizontal blowout to pass silently. | Added explicit `expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);` to Growth, Revenue, and Operations tab steps. | Automated test suite enforces `<= 375px` barrier across all tabs. 12/12 monitoring tests and 65/65 full E2E suite pass. | **RESOLVED** |
| **T6-03** | **MEDIUM** | `CampaignCostModal` coerced unvalued `hourlyRate` (`null`) to `0` on edit/save, masking volunteer/founder labor. | Initial state preserves `null`; form input uses `undefined` to leave field empty; submit parser distinguishes empty (`null`) from numeric `0`; total preview warns `Thiếu đơn giá giờ công`. | Verified in `e2e/monitoring.spec.ts` and `e2e/monitoring-qa-evidence.spec.ts`. Submitting unedited form preserves `hourlyRate: null`; inputting 0 preserves `hourlyRate: 0`. | **RESOLVED** |
| **T6-04** | **LOW** | Clamped date boundaries (`effectiveFromDate` / `effectiveToDate`) from backend response were ignored in the UI. | Added subtle `Dữ liệu thực tế: {0} đến {1}` tag with warning status in Growth and Revenue tabs when clamped, while preserving requested preset in filter bar. | Verified in `e2e/monitoring.spec.ts`. When backend clamps 90d query to 2026-08-01 - 2026-10-20, tag displays correctly while filter bar remains on "90 ngày". | **RESOLVED** |
| **T6-05** | **LOW** | Fallback conversion funnel collapsed to empty state when `totalSessions` was null, hiding known registrations & activations. | `NativeConversionFunnel` updated to handle `count: number | null`; renders `" — "` for unknown sessions while displaying 45 registrations, 28 activations, and 62% step rate. | Verified in `e2e/monitoring.spec.ts`. Funnel renders authentic numbers and step percentages without fabricating 0 or suppressing known stages. | **RESOLVED** |
| **T6-06** | **MINOR** | TypeScript `CohortRetentionRowDto.retentionPercentages` was typed as `number[]` instead of `(number | null)[]`. | Updated interface in `frontend/src/features/admin/monitoring/types.ts` to `(number | null)[]` to mirror backend C# `IReadOnlyList<double?>`. | `tsc -b` compiles cleanly with zero type errors. Runtime immature cohorts handled gracefully. | **RESOLVED** |
| **T6-07** | **MINOR** | Timezone date arithmetic in `AdminMonitoringPage.tsx` mixed UTC parsing with local Date mutation, risking off-by-one in negative UTC timezones. | Exported `getInitialDates()` using `dayjs(todayStr).subtract(29, 'day').format('YYYY-MM-DD')` consistently with `MonitoringFiltersBar.tsx`. | Verified across multiple timezones (New York UTC-4/5, Honolulu UTC-10, London UTC, Tokyo UTC+9) in `scripts/test-monitoring-dates.mjs`. | **RESOLVED** |

---

## 3. Coordinator Visual Acceptance & Interactive Reachability Analysis

### 3.1 Investigation of the Tab Navigation Bar on 375px Viewport
During initial review, Root noted that in `mobile-375-revenue.png` the left tab label appeared clipped (`'ăng trưởng & Sử dụng'`). An independent technical audit reveals:
1. **Ant Design Scroll Mechanism**: Ant Design `<Tabs>` renders an internal navigation container (`.ant-tabs-nav-wrap`) with a horizontal scroll track (`.ant-tabs-nav-list`). On compact viewports (< 768px), the total width of the three tab labels exceeds 375px.
2. **Active Tab Centering**: When the test clicked the second tab ("Doanh thu"), Ant Design automatically scrolled the tab track to the right to ensure the newly selected tab and its active ink-bar were fully visible. This intentionally shifted the first tab slightly to the left, displaying an edge fade shadow (`.ant-tabs-nav-wrap-ping-left`).
3. **Interactive Reachability**: When switching back to "Tăng trưởng & Sử dụng", the tab track smoothly scrolls left, bringing the full title `📈 Tăng trưởng & Sử dụng` into full view without any permanent clipping. When navigating to "Vận hành", the track scrolls right, revealing `⚙ Vận hành`. All three tabs are 100% interactive, focusable, and selectable on 375px screens.

### 3.2 Proof of Local Table Horizontal Scrolling & Control Accessibility
A critical concern with CSS `overflow-x: hidden` on parent containers is that offscreen table columns or action buttons might be trapped and unreachable. We independently tested the 9-column Campaign Cost Table on 375px mobile viewport:
1. **Container Architecture**: The card and tab wrapper use `max-width: 100%; overflow-x: hidden;`, while each table is housed inside `.monitoring-table-wrapper` with `width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch;` and `<Table scroll={{ x: 800 }}>`.
2. **Initial State**: On 375px viewport, columns 1–4 ("Tên chiến dịch", "Kênh", "Nguồn", "Ngày ghi nhận") are immediately visible. Columns 5–9 ("Tiền mặt", "Giờ công", "Tổng chi phí", "Người cập nhật", "Hành động") are offscreen to the right.
3. **Scroll Verification**: In Playwright test `Mobile 375px: Table horizontal local scrolling and control reachability (no overflow-hidden clipping)`:
   - The table container was scrolled horizontally to the right.
   - The rightmost "Hành động" column and the "Sửa" (Edit) button came into full view (`mobile-375-table-scrolled.png`).
   - The "Sửa" button was clicked directly via Playwright.
   - The modal `Sửa chi phí chiến dịch` opened successfully (`mobile-375-modal-open.png`), proving the button is fully interactable and not blocked or occluded by `overflow-hidden`.
4. **Document Invariant**: Throughout this table scrolling and modal interaction, `document.documentElement.scrollWidth` remained strictly equal to `375px`.

---

## 4. Valuation, Contract, and Timezone Integrity

### 4.1 Unvalued Labor Valuation (`hourlyRate = null`)
- **Table Cell Rendering**: In `RevenueTab.tsx:120-127`, when `personHours > 0` and `hourlyRate === null`, the total cost column renders the cash portion alongside a warning tag `<Tag color="warning">Thiếu đơn giá giờ công</Tag>`. It does not calculate `cash + hours * 0`, preventing false financial precision.
- **Modal Editing**: In `CampaignCostModal.tsx:33-41`, `hourlyRate` initializes to `undefined` in the form (leaving the input empty with placeholder `Thiếu đơn giá giờ công`). If saved without entering a rate, `handleFinish` outputs `hourlyRate: null`. If the user explicitly enters `0`, `handleFinish` outputs `hourlyRate: 0`.

### 4.2 Clamped Date Boundaries
- **Effective Range Banner**: In `GrowthTab.tsx:160-174` and `RevenueTab.tsx:180-194`, when `data.effectiveFromDate` and `data.effectiveToDate` are returned by the backend, a tag `Dữ liệu thực tế: {0} đến {1}` is rendered with status `warning` if clamped.
- **Preserved User Intent**: The filter radio buttons in `MonitoringFiltersBar.tsx` retain the user's selected preset (e.g. `90 ngày`), ensuring that the user's intent is visible while communicating that historical raw event retention bounded the returned dataset.

### 4.3 Fallback Conversion Funnel
- In `GrowthTab.tsx:250-270` and `NativeCharts.tsx:145-215`, when `data.funnel` is null and `summary.totalSessions` is null (e.g. multi-day window > 90d where raw web events were pruned), the UI renders the fallback funnel stages.
- The "Phiên truy cập" stage displays `—` (Unknown) with a 0% bar, while "Đăng ký" displays `45` and "Chủ trọ kích hoạt" displays `28` with step rate `(62% từ bước trước)`. Known registration and activation records are never discarded or suppressed.

### 4.4 Vietnam Calendar Date Arithmetic
- In `AdminMonitoringPage.tsx:23-38`, `getInitialDates()` uses `Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' })` to establish today's calendar date in Vietnam, followed by `dayjs(todayStr).subtract(29, 'day').format('YYYY-MM-DD')`.
- Tested in `scripts/test-monitoring-dates.mjs` across negative UTC timezones (US Eastern UTC-4/5, Hawaii UTC-10), UTC, and Tokyo (UTC+9). In all environments, the initial range evaluates to exactly 30 calendar days (`2026-09-21` to `2026-10-20`).

---

## 5. Visual Evidence & Screenshot Manifest

Screenshots were captured during automated Playwright runs and archived in the conversation artifact directory:
- **Directory**: `/Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/c5040527-b0ce-4e4d-b67f-05ffc7eb25f4/screenshots/`

| Viewport | Screen / State | Screenshot Path | Status & Visual Verification |
| :--- | :--- | :--- | :--- |
| **Mobile 375px** | Growth & Usage Tab | `screenshots/mobile-375-growth.png` | **PASS**: `scrollWidth = 375px`. Full title `📈 Tăng trưởng & Sử dụng` visible; KPI cards stack vertically. |
| **Mobile 375px** | Revenue & CAC30 Tab | `screenshots/mobile-375-revenue.png` | **PASS**: `scrollWidth = 375px`. Revenue KPI cards, trend line, and table containers fit neatly. |
| **Mobile 375px** | Operations & Workers Tab | `screenshots/mobile-375-operations.png` | **PASS**: `scrollWidth = 375px`. System status, host gauges, worker cards fit within 375px. |
| **Mobile 375px** | Revenue Tab (Table Scrolled) | `screenshots/mobile-375-table-scrolled.png` | **PASS**: `scrollWidth = 375px`. Campaign table scrolled horizontally; "Hành động" and "Sửa" button fully reachable. |
| **Mobile 375px** | Campaign Cost Modal Open | `screenshots/mobile-375-modal-open.png` | **PASS**: Modal centered within 375px; "Thiếu đơn giá giờ công" placeholder and unvalued labor warning visible. |
| **Mobile 375px** | Tab Bar Switched to Revenue | `screenshots/mobile-375-tabs-revenue.png` | **PASS**: Active indicator under "Doanh thu"; left shadow indicates scrollable navigation track. |
| **Mobile 375px** | Tab Bar Switched to Operations | `screenshots/mobile-375-tabs-operations.png` | **PASS**: Active indicator under "Vận hành"; all tabs selectable. |
| **Desktop 1440px** | Growth & Usage Tab | `screenshots/desktop-1440-growth.png` | **PASS**: `scrollWidth = 1440px`. Full grid layout, multi-line trend chart, CTA and retention tables. |
| **Desktop 1440px** | Revenue & CAC30 Tab | `screenshots/desktop-1440-revenue.png` | **PASS**: `scrollWidth = 1440px`. Pro revenue, CAC30 card, full campaign cost table with 9 columns. |
| **Desktop 1440px** | UTM Link Builder Modal | `screenshots/desktop-1440-utm-modal.png` | **PASS**: Modal dialog with presets, custom inputs, and generated link preview. |
| **Desktop 1440px** | Operations & Workers Tab | `screenshots/desktop-1440-operations.png` | **PASS**: `scrollWidth = 1440px`. 4-column host metrics, worker statuses, API route latency histogram. |

---

## 6. Comprehensive Verification Test Suite Results

All verification test suites were executed locally in the isolated review worktree:

| Suite Name | Command | Tests Run | Result | Evidence Details |
| :--- | :--- | :--- | :--- | :--- |
| **Playwright Full Suite** | `cd frontend && npx playwright test` | 65 tests | **PASS (65/65)** | Completed in 16.2s across 6 workers. Zero failures. |
| **Monitoring E2E Suite** | `cd frontend && npx playwright test e2e/monitoring.spec.ts` | 12 tests | **PASS (12/12)** | Responsive 375/1440 viewports, hourly rate null vs 0, clamped date tag, honest fallback funnel, unknown telemetry. |
| **QA Evidence & Local Scroll** | `cd frontend && npx playwright test e2e/monitoring-qa-evidence.spec.ts` | 4 tests | **PASS (4/4)** | Tab navigation reachability, horizontal table scroll, "Sửa" button click, modal valuation display, screenshot generation. |
| **Vietnam Calendar Timezone Unit** | `cd frontend && npm run test:dates` | 4 test cases | **PASS** | Evaluated in US Eastern, Hawaii, London, and Tokyo timezones. Inclusive 30-day window verified. |
| **Frontend Architecture Boundary** | `cd frontend && npm run check:architecture` | 108 modules | **PASS** | Zero circular dependencies; feature-layer boundary invariants respected. |
| **i18n Catalog Completeness** | `cd frontend && npm run i18n:check` | 1,354 keys × 4 catalogs | **PASS** | 100% translation coverage across `vi`, `en`, `zh`, `ko`, `hi`. |
| **Production Build & Prerender** | `cd frontend && npm run build` | `tsc -b && vite build && prerender` | **PASS** | Succeeded in 2.40s; 0 TypeScript errors; SSR and prerendering preserved. |
| **API Paging & Bounded Limits** | `cd frontend && npm run test:api` | Node unit test | **PASS** | Pagination past 100 rows verified. |
| **Analytics Tracker Unit** | `cd frontend && npm run test:analytics` | Node unit test | **PASS** | First-party tracking, 30m idle session, PII regex scrubbing, allowlist validation. |
| **HTTP Transport Client** | `cd frontend && npm run test:http` | Node unit test | **PASS** | Headers, envelope unwrapping, single financial submission, error types. |

---

## 7. Limitations, Architectural Distinctions, and Non-Tested Scope

1. **Connected Journey Attestation Scope**: The prior connected user journey fixture (`scratch/test_t6_complete_journey.py`) proves the user lifecycle from anonymous UTM touchpoint through registration, loopback OTP verification, approved owner state, non-activating deposit invoice, first monthly invoice activation, and operations telemetry under a simulated missing host collector. It does **not** execute server-confirmed Pro subscription upgrades or payOS payment webhooks in that script; those invariants are proven independently by the webhook integration test suite (`tests/RoomRental.WebhookTests`).
2. **Resilience Boundary Separation**: Broken host collector resilience (verifying that missing or unreadable host JSON on disk does not fail registration or cause 500 errors on app endpoints) is architecturally distinct from browser analytics network failure resilience (verifying that client tracker beacon/fetch drops do not block authentication or UI navigation). These two resilience guarantees operate at different layers and were proven under separate, isolated test harnesses.
3. **External Provider Isolation**: No real external network requests were dispatched to live payOS production endpoints, external SMTP servers, or PostgreSQL loopback daemons. In-process test fakes and loopback SMTP fixtures were used exclusively.

---

## 8. Final Conclusion and Recommendation

Commit `e1d90c8ce0a5cd18ea8d25a7b74d765adcc5880b` completely and elegantly resolves all 7 defects identified in the T6 review. The implementation is robust, responsive, and adheres to all project invariants.

**Reviewer Recommendation**: **APPROVED FOR PRODUCTION INTEGRATION**. No remaining defects or deficiencies.

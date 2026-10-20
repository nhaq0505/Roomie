# MVP API contract

Backend: ASP.NET Core .NET 10 on port 5080. Frontend: React/TypeScript/Vite on port 5173, proxy `/api` to backend. JSON camelCase, UUID identifiers, decimal JSON numbers, errors `{ "message": "..." }`. Cookie authentication with HttpOnly cookies. Mutation requests use same-origin checks. No client-generated payment success.

## Authentication and approval

- `POST /api/auth/register` `{name,email,password,phone}` → HTTP202 `{verificationRequired:true,challengeToken,email,expiresAt,resendAvailableAt}`. Registration remains pending until email OTP is confirmed; no user/session is created yet. Missing/failing SMTP returns503, not success.
- `POST /api/auth/verify-registration` `{challengeToken,code}` → HTTP201 user. Six-digit OTP expires after10 minutes; challenge after30 minutes, max5 wrong attempts per code. Correct verification atomically creates an Approved Free Owner; sign in separately. Replayed/invalid/expired codes return400; concurrent loser may return409.
- `POST /api/auth/resend-registration` `{challengeToken}` → refreshed challenge timing. Enforces60-second cooldown and5 sends/email/hour, returning429 when limited. Sending again invalidates the old code. New registration with the same pending email rotates its token/credentials after cooldown and preserves the send budget. Existing accounts cannot be overwritten.
- Verification uses a256-bit challenge secret bound to its code; DB stores only hashes, never raw OTP/challenge. Existing accounts retain their prior login behavior. SMTP/TLS configuration and Gmail steps: [email OTP setup](email-otp-setup.md).
- `POST /api/auth/forgot-password` `{email}` → HTTP202 `{challengeToken,email,expiresAt,resendAvailableAt}` for both known and unknown email; asynchronous SMTP delivery, 60-second cooldown, 5 requests/email/hour.
- `POST /api/auth/verify-password-recovery` `{challengeToken,code}` → `{resetToken,expiresAt}`. Six-digit OTP, 10-minute expiry, max5 incorrect attempts; successful verification consumes the OTP.
- `POST /api/auth/complete-password-recovery` `{resetToken,newPassword,confirmPassword}` → `{success:true}`. Single-use 10-minute grant; revokes all target sessions and clears browser cookie. See [password recovery](password-recovery.md) for delivery and deployment details. Manual admin-issued reset remains supported.
- `POST /api/auth/login` `{email,password}` → user.
- `GET /api/auth/me` → user; unauthenticated returns 401.
- `POST /api/auth/logout`.
- User: `{id,name,email,role,status}`; roles `Admin`, `Owner`; statuses `Pending`, `Approved`, `Rejected`, `Suspended`.
- `GET /api/admin/owners` → users including phone/reason when available.
- `POST /api/admin/owners/{id}/status` `{status,reason}`.

## Owner management

- `GET /api/dashboard` → `{totalRooms,occupiedRooms,totalBilled,totalPaid,totalOutstanding,pendingReadings}`.
- `GET /api/buildings`; `POST /api/buildings` `{name,address}`.
- `GET /api/rooms?period=YYYY-MM` (optional period selects the baseline shown); `GET /api/rooms/{id}`.
- `POST /api/rooms` `{buildingId,name,rent,electricityRate,waterFee,waterPerPerson,serviceFee,initialReading}`.
- `PATCH /api/rooms/{id}` updates name and fee settings.
- Room includes id, buildingId, buildingName, name, fee settings, previousReading and nullable occupancy `{id,tenantName,phone,people,startDate,token,url}`.
- `POST /api/rooms/{id}/occupancy` `{tenantName,phone,people,startDate,depositAmount}` (depositAmount optional, default 0; whole VND 0..2147483647).
- `POST /api/rooms/{id}/end-occupancy` ends active occupancy and revokes its guest token; debts remain attached to ended occupancy.
- `POST /api/rooms/{id}/rotate-link` → `{token,url}`. Requires the owner's current payOS credentials and webhook origin to have `WebhookStatus=Verified`; otherwise returns HTTP400 without rotating/revoking the existing token.
- Until payOS is verified, all room list/detail/create/update/occupancy responses return `occupancy.token=null` and `occupancy.url=null`; invoice list/detail return `tenantToken=null`. This hides sharing links without blocking room/tenant/billing management. Saved but failed/unverified settings, changed keys or changed webhook origin keep links hidden. Previously shared guest links continue to show invoices; they are not revoked by a settings change.
- `RequireVerifiedPaymentForTenantLink=false` is reserved for disposable Development test fixtures. Production refuses to start with this bypass.

## Readings and invoices

- `GET /api/readings?period=YYYY-MM` → readings including roomId, period, previousReading, currentReading, consumption.
- `POST /api/readings` `{roomId,period,currentReading}`. Reject decreases, out-of-order periods, duplicates or periods already invoiced. The reading must belong to the active occupancy and not predate its starting month or be in the future. Electricity calculation is `(currentReading - previousReading) * electricityRate`. A valid final reading automatically publishes its invoice transactionally.
- `GET /api/invoices?period=YYYY-MM&kind=Monthly&lifecycleState=Issued&status=Unpaid&search=...`; all filters optional and combined with AND. Kind is an exact value (`Monthly`, `Deposit`, `Supplemental`); lifecycle (`Draft`, `Issued`) and status (`Unpaid`, `Partial`, `Paid`) are case-insensitive enums, invalid values return 400. Search is a literal, case-insensitive substring of snapshotted room/building/tenant names. Returns the existing JSON array, scoped to the owner. `GET /api/invoices/{id}`.
- `POST /api/invoices/generate` `{period}` generates and publishes eligible invoices; repeat calls cannot duplicate them.
- Invoice: `{id,roomId,roomName,buildingName,period,tenantName,previousReading,currentReading,consumption,rent,electricityAmount,waterAmount,serviceAmount,total,paid,outstanding,status,dueDate,issuedAt}`. Status `Unpaid`, `Partial`, `Paid`. Amounts rounded to VND.
- `POST /api/invoices/{id}/receipts` `{amount,note,idempotencyKey,expectedVersion?}` records cash/external bank transfer; positive amount must equal the entire current remaining balance; partial receipts are rejected with 409 before provider calls. Returns `{id,invoiceId,amount,paid,outstanding,status,version}`. Reusing the key with the same payload returns the existing receipt; a different payload conflicts. With a pending payOS order, the server verifies signed order/amount/amountPaid, cancels an unpaid order and requires signed CANCELLED/EXPIRED with zero received before committing cancellation plus manual receipt atomically. Manual confirmation settles the full remaining balance and marks Paid. Historical partial receipts and deposit offsets are preserved; confirmation collects the entire remaining amount. A verified full online payment wins: reconcile that payment and return409 without a manual receipt; clients must refresh even after an error. Partial online payment, unknown status, failed cancellation or invalid response blocks cash recording. A timeout after remote cancellation is safe to retry with the same idempotency key. Optional expectedVersion rejects stale invoice state before provider calls.

## Payments and guest access

Chế độ tự động/thủ công, VietQR và thanh toán tất cả: [payment confirmation](payment-confirmation.md). Các API cũ bên dưới tiếp tục dùng cho thanh toán từng hóa đơn; chính sách cấp link payOS áp dụng ở chế độ Automatic, còn Manual cần thông tin ngân hàng hợp lệ.

- `GET /api/settings/payment` → `{configured,clientId,webhookUrl,webhookStatus,webhookVerifiedAt,webhookError}` (never return API key/checksum key). Status: `NotConfigured`, `Unverified`, `Verified`, `Failed`.
- `PUT /api/settings/payment` `{clientId,apiKey,checksumKey}`; encrypt and commit settings, then automatically register/verify the server-configured HTTPS callback with payOS. Blank keys retain existing keys. Returns the same DTO; verification failure is reported in the DTO without discarding saved keys.
- `POST /api/settings/payment/verify-webhook` retries confirmation using saved keys, returning the same DTO. Owner authorization, approval, CSRF origin checks and rate limiting apply. Callback URL cannot be supplied by the client. A changed credential revision/origin invalidates earlier verification.
- `GET /api/tenant/{token}` → `{room,occupancy,invoices}` restricted to that occupancy.
- `POST /api/tenant/{token}/invoices/{id}/payment` → `{checkoutUrl,qrCode,amount,orderCode,accountName,accountNumber,bankBin,description,status,expiresAt}`. `qrCode` and account/description fields may be null; `expiresAt` is Unix seconds or null. `amount` is the outstanding VND amount, and `status` is the local attempt state (`pending`, `paid`, `expired`, `cancelled` or `failed`). The checkout link, QR payload and bank metadata are persisted only after the signed payOS create response has the expected order code and amount. Missing real credentials is a clear configuration error, never simulated success.
- Pending orders are reused; the endpoint does not create a second payOS order while one is pending. Older pending rows may have `qrCode:null` and still return their saved `checkoutUrl`. The tenant portal navigates to a dedicated Rehouse checkout page at `/t/{token}/invoices/{id}/checkout` (with `/tenant/{token}/invoices/{id}/checkout` as an alias), preserving the current query string such as `lang`. The route revalidates the tenant overview and scopes the invoice before creating/reusing checkout once per route mount; missing or already-paid invoices do not start checkout. The back link returns to the tenant overview with its query string. A QR payload renders on Rehouse; a QR-less legacy attempt can expose an explicit hosted-checkout link only for an HTTPS `pay.payos.vn` URL. Rehouse never redirects automatically. Expiry hides QR/hosted actions while the order is being checked.
- `POST /api/tenant/{token}/invoices/{id}/reconcile` → `{status,invoice}`. A provider `PAID` status or browser return alone does not mark an invoice paid: reconciliation must verify the expected paid amount and transaction before recording the receipt. The checkout page displays success only from the updated invoice. It polls while the page is active and the tab is visible, and also offers manual checking.
- `POST /api/payments/webhook/{ownerId}` verifies payOS signature and reconciles exact order, owner and amount transactionally with replay protection. Repeated valid callbacks are idempotent; mismatched amounts are rejected without a second receipt.
- `GET /api/health` checks database connectivity: 200 `{status:"ok",database:"ok"}`; connection failure returns 503 `{status:"unavailable",database:"unreachable"}`.

## Delivery defaults

Manual meter entry only. AI and camera are deferred. Valid manual reading submissions automatically publish invoices; batch generation and the daily sweep reuse the same idempotent service. Default due date is the fifth day of the following month. Use PostgreSQL in Docker, SQLite for convenient local development. Seed demo data only via an explicit development flag. No deployment or live money transfers performed by coding agents.


## Deposits

Starting occupancy with depositAmount > 0 atomically creates one unpaid Deposit invoice.
Invoices include kind (Monthly/Deposit), occupancyId, depositHeld and depositSettlements.
Occupancy includes depositInvoiceId, depositRequired, depositPaid and depositHeld.
Monthly invoices are unique per occupancy/period; deposits are unique per occupancy.
Guest payment/manual receipt endpoints support both kinds. Dashboard financial totals use Monthly invoices.

`POST /api/invoices/{id}/deposit-settlements` body:
`{kind:"Refund"|"Deduction",amount,reason,idempotencyKey,targetInvoiceId?}`.
Returns updated InvoiceDto. GUID idempotency key required; exact replay is unchanged,
changed payload with same key conflicts (409). Positive whole VND cannot exceed held funds.
Refund records money already returned externally; it does not initiate a bank transfer.
Deduction optionally credits an outstanding Monthly invoice belonging to the same owner/occupancy;
amount cannot exceed that debt. Pending online attempts must first be reconciled.
Moveout records a manual refund and retains the remaining held deposit as an audited Deduction atomically. Deposit Paid preserves collection history.


## Supplemental invoices

`POST /api/rooms/{id}/supplemental-invoices` body:
`{requestId:GUID,occupancyId:GUID,description:string,amount:integer,dueDate:"YYYY-MM-DD"}`.
Owner must own room, occupancy must be current active tenant. Description trimmed 1..500,
amount 1..2147483647 whole VND, valid nondefault dueDate required.
Returns InvoiceDto with kind=Supplemental, description, paid=0, period=current issue month.
requestId is the invoice UUID: exact retry returns prior invoice; altered payload conflicts (409).
Multiple supplemental invoices per room/month are supported. Old monthly/deposit rules unchanged.
Same receipt/payOS/guest endpoints apply. Dashboard totals include Supplemental and Monthly.
Controlled corrections are available for eligible invoices; deposit debt deductions can target Monthly/Supplemental invoices of the same occupancy.


## Moveout settlement

`GET /api/rooms/{id}/moveout` returns the active occupancy, invoices, deposit balance/version,
pending invoice IDs, blocking reasons and `canComplete`. It is read-only.

`POST /api/rooms/{id}/complete-moveout` (alias `POST /api/rooms/{id}/end-occupancy`):
`{occupancyId, requestId, refundAmount, expectedHeld, expectedDepositVersion}`.
Refund is manually specified whole VND, from zero through the held balance; no bank transfer is initiated.
The serializable transaction checks ownership, active occupancy and expected balance/version;
blocks Draft invoices, unpaid Issued invoices (including Deposit) and pending payOS attempts.
It records the refund and the retained remainder as audited deposit settlements, closes occupancy
on today's Vietnam date, revokes the guest link, and updates the physical meter baseline from existing readings.
No final invoice/reading is created; the former final-invoice and proration routes are removed.
Exact request/payload replay returns the prior completion; changed payload or stale snapshot returns 409.
`GET /api/rooms/{id}/moveout-completions` returns owner-scoped history, including archived rooms.
Monthly readings always issue full monthly rent, regardless of move-in/out day or month length.

## Reports and soft archive

Approved Owner endpoints:
- `GET /api/reports/receipts`, `/api/reports/debt`, `/api/reports/deposits` accept optional
  `dateFrom`, `dateTo` (ISO dates, inclusive), `buildingId`, `roomId`, `page`, `pageSize`.
  Return `{items,page,pageSize,totalCount,totals}`; totals cover all filtered rows, not just the page.
  Receipts filter by receipt date, debt by invoice period, deposits by settlement/receipt date.
  Debt is current Issued outstanding, including unpaid deposits. Drafts are excluded.
  Internal deposit credits do not count as new cash receipts; deposit held is the current balance.
- Each report supports `/export.xlsx` (for example `/api/reports/debt/export.xlsx`), using the same filters.
  Export enforces a row limit with a clear error, writes typed numeric/date cells and protects user text from formulas.
- `GET /api/buildings?includeArchived=true`, `GET /api/rooms?includeArchived=true` include archived items;
  default lists contain active items only.
- `DELETE /api/buildings/{id}`, `DELETE /api/rooms/{id}` soft archive; no physical deletion.
  Active tenants, Draft/unpaid invoices, held deposits or pending payOS block archive (409).
- `POST /api/buildings/{id}/restore`, `POST /api/rooms/{id}/restore` restore with owner/quota checks.
  Building archive is atomic with its rooms; building restore only restores rooms in the same archive batch.
  Restore the parent building before a room. Reports preserve historical names and IDs from invoice snapshots.

## Active occupancy, password recovery and monthly dashboard (14/10/2026)

`PATCH /api/rooms/{roomId}/occupancy` (approved Owner):
`{ occupancyId, tenantName, phone?, people }` → `RoomDto`.
Requires the exact active occupancy ID; rejects inactive/stale occupants with 409,
other owners' rooms with 404, empty/overlong name (160), phone >60 or people outside 1–30 with 400.
Does not change dates, deposit, room fees or tenant links. Newly recorded readings snapshot
`OccupantsCount`. Per-person unpaid Monthly Drafts use their saved WaterRate and the new
headcount, increment Version and record an InvoiceRevision. Drafts with a paid status,
nonzero Paid, receipts or pending payOS block the entire update. All Issued invoices,
including Unpaid/Partial/Paid, remain unchanged. Legacy per-person readings without a
headcount snapshot and without an invoice are skipped by regeneration rather than
reinterpreted using today's occupants.

`POST /api/auth/change-password` (signed-in Owner/Admin, including Pending Owner):
`{ currentPassword, newPassword }` → `{ success: true }`.
New password: 9–256 characters; no character-composition requirements; optional `confirmPassword` must match if supplied.
The password must differ from the current one.
Invalid input/current password returns 400. Hashes with ASP.NET PasswordHasher, clears
outstanding reset codes and revokes all other open sessions, retaining the current `sid` and cookie.
Revoked devices receive 401 on their next request. The account menu opens a three-field Modal.
Existing registration/login contracts and legacy passwords remain supported.

`POST /api/admin/owners/{id}/reset-password` (Admin, current-password reauthentication):
`{ currentPassword }` → `{ token, expiresAt }`, `Cache-Control: no-store`.
Token is a cryptographically random 32-byte value encoded as 64 hex characters; only its
SHA-256 hash and 30-minute UTC expiry are stored. Only Owner targets are accepted (404 otherwise).
Replaces any older code and revokes existing sessions immediately. Login with the old
password returns 403 until a new password is set. The admin displays the code once and
must privately deliver it after verifying the owner's identity; there is no email provider.

`POST /api/auth/reset-password` (public, rate limited):
`{ email, token, newPassword, confirmPassword }` → `{ success: true }`.
Reset uses the same 9–256 character policy without composition requirements, with required matching confirmation; invalid/expired/replayed code or wrong email returns the same 400
message. Successful reset atomically consumes the code and revokes sessions; does not
sign in automatically or change account approval/suspension. Concurrent consumption has
at most one success; the loser returns 400 or 409. An expired admin-issued code requires
the admin to issue another; expiry alone does not restore access with the old password.
All credential endpoints share the existing auth rate limiter, Origin checks for signed-in
mutations and serializable transactions. Passwords and reset tokens are never included in URLs.

`GET /api/dashboard`: existing `totalBilled`, `totalPaid`, `totalOutstanding`, room counts and
`pendingReadings` retain their semantics. Adds `period`, `monthlyBilled`, `monthlyPaid`,
`monthlyOutstanding`, `collectionRate` (0–100 percent, two decimal places; zero when no bills).
The period uses Asia/Ho_Chi_Minh and the server clock. Monthly values include Issued
non-Deposit invoices whose Period matches the current Vietnamese month; Drafts and deposits
are excluded. `monthlyPaid` is paid balance on those invoices, not cash received during
this calendar month. Use the receipt report for cash-flow reporting. Frontend primary KPIs
use the monthly fields; all-time values appear in a separate block. Recent invoices use
the period returned by the dashboard API.

## Operational hardening / pagination contract -16October2026

- New passwords for registration/change/reset/bootstrap use 9–256 characters without uppercase/lowercase/digit/symbol requirements; whitespace-only passwords are rejected; existing credentials are not silently rotated. Production login has both IP and persistent normalized-account budgets. Authenticated password change and admin recovery also consume account budgets even on failure. Budgets reset in a short window; no permanent attacker-triggered account lockout.
- POST `/api/auth/login` authenticates using email/password only. Optional legacy `code` is accepted but ignored. Login and `/api/auth/me` always return `mfaEnabled: false` and `mfaRequired: false`, regardless of stored legacy flags. Admin business routes do not require MFA enrollment.
- MFA setup/confirm endpoints have been removed (404); `/mfa-setup` no longer renders an enrollment page. Existing database columns and applied migrations remain unchanged for compatibility. Registration email OTP and password reset tokens remain active.

- GET `/api/invoices?page=1&pageSize=10` returns `{items,total,totals:{billed,paid,outstanding},draftCount,page,pageSize}`. Existing filters remain; optional GUID `occupancyId` narrows within the authenticated owner. Totals cover the full filtered set. No `page` retains an array but bounds results to100. Page size1-100, page1-100000; invalid values return400. Stable order includes Id.
- GET `/api/admin/owners?page=1&pageSize=10` returns `{items,total,pendingCount,page,pageSize}`; optional status filters the page while pendingCount remains global. No page retains the bounded legacy array.
- GET `/api/tenant/{token}?page=1&pageSize=10` adds total/totals/page/pageSize to room/occupancy/invoices. Optional `invoiceId` selects one issued invoice within that token's occupancy, independently of history page position; mismatched IDs return an empty invoice list. Checkout uses this direct selection, never searches only page1.
- Secondary owner screens needing all invoices for a period/occupancy fetch bounded100-row pages to completion. Deposit targets are occupancy-scoped. Main invoice/admin/tenant lists remain paginated; compact/mobile tables forward the same server state.
- OTP persists a send reservation before SMTP without an open transaction, then compensates only an unchanged reservation on delivery failure. Existing cooldown/hour budget/code replacement/consume guards remain. Delivery and DB cannot form an atomic external transaction; ambiguous SMTP delivery can require resend.

Release/testing evidence: [tech debt remediation](tech-debt-remediation-2026-10-16.md).

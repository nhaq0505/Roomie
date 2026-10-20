# Local API integration tests

## Backup Google Drive — 16/10/2026

`python3 -m unittest discover -s tests -p 'test_backup_retention.py' -v` kiểm tra4ca: các mốc ngày/tuần/tháng, nhiều snapshot cùng ngày, tên/ngày không hợp lệ và kho rỗng. Không kết nối Drive/VPS hoặc xóa dữ liệu. Bước CI mới đã thêm ở source, chưa push/chạy trên GitHub trong đợt này.

Kiểm chứng thực tế riêng đã PASS: upload crypt, download/decrypt4file không khác biệt, SHA256, restore PostgreSQL17 container tạm networknone và kiểm tra archive DataProtection XML; forced token refresh dưới systemd cũng PASS. Đây là kiểm chứng trên dataset tại thời điểm chạy, không phải cam kết phục hồi mọi dữ liệu tương lai. Nhật ký và lệnh phục hồi: [google-drive-backup.md](../docs/backup-operations.md).

## SEO HTTP checks

Build frontend first (`npm --prefix frontend run build`) and backend (`dotnet build backend/RoomRental.Api`). Start a separate API using the disposable database command below, with these additional arguments/configuration:

```sh
# Add to the disposable API environment, never to a shared Development database:
Seo__AllowIndexing=true
# Add to dotnet run's arguments (replace with the absolute project path):
--webroot "/absolute/project/path/frontend/dist"
```

Then run against that local server:

```sh
SEO_BASE_URL=http://localhost:5094 /usr/bin/python3 -m unittest discover -s tests -p 'test_seo_http.py' -v
```

Linux CI can use `python3` instead of `/usr/bin/python3`. These 13 read-only tests check pre-rendered HTML and CSS, canonical/OG/JSON-LD, noindex on private/demo/language routes, preserved redirects, real 404s, auth errors, HEAD, sitemap, the user-requested robots block for all crawlers, and PNG dimensions. Requests use the synthetic Host `roomie.io.vn` against localhost to exercise production index controls; `Seo__AllowIndexing=true` is necessary because Development defaults to noindex. CI runs the same checks inside the disposable application image. `npm run dev`/`preview` do not exercise the .NET routing and HTTP headers.

## Tenant link verification policy

The ordinary business API fixtures set `RequireVerifiedPaymentForTenantLink=false` because they test rotation/history independently of provider setup. Only Development permits this; Production fails at startup if the bypass is set. Do not set it on a shared or live app.

`dotnet run --project tests/RoomRental.WebhookTests` also runs `TenantLinkHttpTests` with the policy enabled. ASP.NET TestServer maps the actual owner/payment/billing route handlers, synthetic authenticated owners, an in-memory SQLite DB and an in-process payOS handler. It covers missing/failed/verified settings; hidden tokens in room/occupancy edits and invoice list/detail; successful rotation and revoked old tokens; webhook-origin/key changes; owner isolation and continued viewing through previously shared links. No real provider/SMTP/payment is used. The previous test that merely repeated an `if` outside the endpoint has been removed.

## Business API checks

The suite creates synthetic owners, rooms, invoices and receipts. **Use only a new disposable database.**
Never use Development PostgreSQL `room_rental` at `127.0.0.1:15432`, production,
or SQLite files/snapshots under `.local-data`.

Create a temporary directory, then start a dedicated API (keep it running in this terminal):

```sh
REHOUSE_QA_DIR=$(mktemp -d /tmp/rehouse-qa.XXXXXX)
printf 'Disposable SQLite path: %s/qa.db\n' "$REHOUSE_QA_DIR"
HTTPS_PROXY=http://127.0.0.1:9 HTTP_PROXY=http://127.0.0.1:9 \
ALL_PROXY=http://127.0.0.1:9 NO_PROXY=localhost,127.0.0.1 \
ASPNETCORE_ENVIRONMENT=Development DatabaseProvider=SQLite \
ConnectionStrings__DefaultConnection="Data Source=$REHOUSE_QA_DIR/qa.db" \
DataProtection__KeysPath="$REHOUSE_QA_DIR/keys" SeedDemo=true EmailVerification__Enabled=false \
RequireVerifiedPaymentForTenantLink=false EnableInvoiceSweep=false EnablePaymentReconciliation=false AuthRequestsPerMinute=1000 \
WebhookBaseUrl=http://localhost:5094 PublicBaseUrl=http://localhost:5094 \
dotnet run --no-launch-profile --project backend/RoomRental.Api --urls http://localhost:5094
```

Port 9 must be unused: this intentionally rejects outbound HTTP(S). HTTP localhost callback
only blocks webhook registration; it does **not** block the subscription checkout test.
Keep the outbound proxy guard on every disposable API. For tracked evidence, use a local
CONNECT-rejecting proxy that logs only destination hosts. No real payOS keys are required.
In a second terminal, use the exact temporary database path printed above:

```sh
API_BASE_URL=http://localhost:5094 TEST_SQLITE_DB=/tmp/rehouse-qa.REPLACE/qa.db \
/usr/bin/python3 -m unittest discover -s tests -v
```

The demo admin defaults to `admin@phongtro.local` / `Admin123!`. Override the
API address and seeded admin credentials with `API_BASE_URL`,
`API_ADMIN_EMAIL`, and `API_ADMIN_PASSWORD` when needed. Mutating requests send
an `Origin` header equal to `API_BASE_URL`; set `API_ORIGIN` if the local server
allows a different development origin.

Keep `WebhookBaseUrl` set to an HTTP localhost URL for this isolated test server:
automatic confirmation rejects it before contacting payOS. Never run the fake-key
suite against an API configured with a real HTTPS callback origin.

The payment tests use synthetic keys. The subscription upgrade test attempts an HTTP request;
the outbound proxy guard above must reject it locally. Callback HTTP localhost alone is insufficient.

An additional SQLite-only webhook replay fixture is opt-in. It inserts
synthetic payment attempts directly into the database used by the local
Development API, then posts signed test webhooks to that localhost API. Use a
disposable local Development database; never point `TEST_SQLITE_DB` at a
production or shared database. The API must be running against the same SQLite
file, and the fixture refuses non-local API URLs and mismatched database files.
It creates random owners and leaves no pending synthetic payment attempts.

For example, with the dedicated disposable API above:

```sh
TEST_SQLITE_DB=/tmp/rehouse-qa.REPLACE/qa.db \
API_BASE_URL=http://localhost:5094 \
python3 -m unittest discover -s tests -p 'test_payos_fixture.py' -v
```

The ordinary suite skips this fixture when `TEST_SQLITE_DB` is unset. The
fixture uses only synthetic credentials and never contacts payOS.

Webhook confirmation service tests use an in-memory SQLite database and fake HTTP handler;
they test success, persistence, provider rejection, timeout, malformed responses,
wrong callback URLs, credential/origin invalidation, and absence of financial receipts:

```sh
dotnet run --project tests/RoomRental.WebhookTests
```


Deposit coverage includes per-occupancy amount validation, zero deposit, separate monthly billing,
manual collection, held balance, refund/deduction, optional monthly debt credit, idempotency,
owner scope, moveout protection and signed deposit webhook replay. Use a disposable database,
never the owner's live data.

Supplemental invoices add creation validation/scope, current occupancy checks, idempotent
creation, multiple items in the same month, guest/manual receipt and signed webhook replay
coverage.

Moveout coverage checks full-month rent remains unchanged, midmonth readings issue immediately,
same-room occupancy history, outstanding-invoice/Draft/pending-payOS guards, manual refund bounds,
audited retained deposit, idempotent replay/conflict, owner scope, token revocation and meter
baseline advancement. Completion no longer requires a final meter invoice.

Billing lifecycle/correction and moveout coverage is in `test_billing_upgrade.py` (13 tests):
full-month rent for midmonth move-in and different calendar lengths, immediate Issued invoices,
removed proration routes, Draft compatibility, guarded/idempotent correction with audit,
integer-VND validation, moveout debt/payment guards, manual refund plus retained-remainder audit,
and same-room occupancy identity. Run it against a disposable API:

```sh
python3 -m unittest discover -s tests -p 'test_billing_upgrade.py' -v
```

## Full-month billing and refund-only moveout — 14/10/2026

The moveout contract and fixtures now follow the full-month billing rule. Saving a monthly
reading creates an Issued invoice at full room rent regardless of move-in day; there is no new
midmonth Draft/proration path. Moveout atomically records the manually specified refund and
retained deposit remainder with audit and idempotency. It blocks unresolved Drafts, unpaid
issued invoices, pending PayOS, and unpaid deposit invoices. The UI was inspected read-only; no
refund or payment mutation was performed from CUA.

On disposable SQLite, focused modules passed separately: `test_api_integration.py` 17/17,
`test_billing_upgrade.py` 13/13, `test_reports_archive.py` 6/6, and `test_payos_fixture.py` 4/4
(40 total across separate runs, not one full-suite run). A repeated full-suite attempt hit the
authentication 429 setup limit; no full-suite pass is claimed. On a cloned PostgreSQL database,
the billing module ran 13 tests: 11 passed and 2 SQLite-only fixtures were skipped. The API
build completed with 0 warnings/errors; frontend build and i18n validation passed with 789 source
keys × 4 catalogs.

Reports/archive coverage is in `test_reports_archive.py`. It checks owner-scoped receipts, debt and
deposit totals across all filtered rows and pages, exclusion of internal deposit credits from cash,
unpaid deposit debt, period/date filters, snapshot history after rename/archive, archive batch
restore, atomic building archive when a room has an active tenant, and all three `.xlsx` exports.
The Excel checks verify typed numeric/date cells and that formula-like receipt notes remain text
with no formula node and an Excel quote-prefix style. Run it against the same disposable API:

```sh
API_BASE_URL=http://localhost:5080 /usr/bin/python3 -m unittest discover -s tests -p 'test_reports_archive.py' -v
```

The report/archive module passed 6/6. With the latest billing guards, the full suite passed 38/38
on SQLite. PostgreSQL clone QA ran 34 tests: 32 passed and two SQLite-only fixture tests were
skipped.
On the current QA Mac, use `/usr/bin/python3` (Python 3.9.6) for this module's XLSX XML assertions:
Python 3.14's Expat reported an XML parse error for the same valid workbook.

QA transparency: an earlier agent run on API port 5082 used a separate disposable database at
`/workspace/.local-data/billing-upgrade-qa/qa.db` but inherited an HTTPS callback origin. Its
synthetic invalid credentials caused four webhook confirmation/registration POST attempts; all
failed, no webhook was verified, and no payment or receipt was created. Do not characterize that
run as having made zero provider requests. Later root QA used the separate
`backend/RoomRental.Api/.local-data/billing-upgrade-qa/qa.db` clone with an HTTP localhost callback
throughout and made no webhook-registration/provider requests. The real application database and
real payment flow were not part of those suites.

## Inline payOS QR checkout (13/10/2026)

`dotnet run --project tests/RoomRental.WebhookTests` now includes an in-process fake
`HttpClient` payment suite. It checks signed QR/account/description/expiry persistence and
response mapping, pending-order reuse, legacy pending attempts with a nullable QR and saved
hosted URL, invalid signature/amount/order rejection, and webhook amount checks/idempotency.
It also verifies that provider `PAID` without a verified matching payment amount does not
create a receipt or credit an invoice. The handler only responds to fake payOS calls in
process; unexpected external calls fail the test. Latest result: 58 assertions passed,
including 10-minute QR expiry persistence/reuse and provider-confirmed terminal-state handling.

An earlier opt-in HTTP API run passed 20/20 against a disposable SQLite copy with
`WebhookBaseUrl=http://localhost:5082`; it did not call the live payOS origin. The latest expanded
focused suite results, including billing, reports, archive and moveout coverage, are recorded
above. A
separate initializer check on another disposable copy confirmed SQLite integrity and unchanged row
counts while adding nullable checkout metadata columns. Do not use the live or shared DB for these
tests.

Browser QA uses a local synthetic-only mock server and CUA, not the live payOS service. The
original inline-modal pass covered QR rendering, QR-less legacy hosted fallback, suppression of
a non-payOS host, provider-paid versus authoritative invoice-paid behavior, and mobile
scrolling/no horizontal overflow at 375 px. The copy button showed its success toast, but
clipboard contents were not independently verified. The payload and all account details are
synthetic and non-payable. Historical modal screenshots are
`tests/artifacts/rehouse-inline-qr-desktop.jpg` and
`tests/artifacts/rehouse-inline-qr-mobile.jpg`.

The follow-up dedicated-page pass (13/10/2026) verified Pay navigation to
`/t/{token}/invoices/{id}/checkout?lang=en`, browser and in-page back navigation to the matching
portal URL, rendered synthetic QR, and the QR-less legacy fixture's explicit safe-host fallback
(the hosted link was not opened). A provider `paid` create response and reconcile response while
the invoice remained Unpaid kept the page waiting, hid QR/hosted checkout, and did not show
success. After the mock invoice itself became Paid, manual Check now showed Paid and
`Amount paid ₫425,000`. One route mount issued exactly one checkout request; reconciliation
requests were separate. At a 375×812 viewport the document/body widths stayed 375 px and the QR
SVG rendered; the desktop 1466 px viewport also had no horizontal overflow. The current route
screenshots are `tests/artifacts/rehouse-checkout-page-desktop.jpg` and
`tests/artifacts/rehouse-checkout-page-mobile.jpg`.

Only local in-memory mock data was changed; no live checkout, bank transfer, real invoice, or
production/shared database was touched. A direct hard refresh, expiry-state UI, and opening the
hosted fallback remain unverified. The latest frontend checks passed: production build (reported
by the frontend agent) and `npm run i18n:check` (554 keys × 4 catalogs).

## Disposable PostgreSQL Docker QA (13/10/2026)

The API integration suite also passed **16/16** against an isolated PostgreSQL 17 Production
Docker deployment over a local TLS proxy. The disposable project used an HTTP localhost
`WebhookBaseUrl`, so no payOS call was possible. This run did not use the SQLite-only direct DB
fixture `test_payos_fixture.py`; run that fixture only against a disposable SQLite DB as described
above. The PostgreSQL schema initializer and repeated startup succeeded, including the seven
nullable checkout metadata columns.

`load_postgres_readonly.py` is a bounded smoke-load helper, not a general benchmark. It requires
`API_LOAD_ALLOW_DISPOSABLE_POSTGRES=1`, accepts only `https://localhost:18443`, verifies TLS with
`API_TLS_CA_FILE`, creates synthetic fixture data before timing, and then runs only guest overview
GETs for 5 seconds at 50 and 100 workers. The project-specific TLS endpoint, CA certificate, and
admin credentials must come from a newly provisioned disposable QA environment; the former
temporary QA project and credentials have been removed. Never substitute a live API URL or
production credentials.

In that one run the 50-worker stage completed 7,085 reads in 5.017 seconds (1,412.3 req/s,
p50 29.8 ms, p95 44.8 ms) and the 100-worker stage completed 7,078 reads in 5.051 seconds
(1,401.4 req/s, p50 57.3 ms, p95 92.0 ms). Both had only HTTP 200 responses, no 429s, and no
transport errors. This was one cache-hot synthetic tenant on the local Docker host and does not
support a production capacity or user-count claim. See [`docs/POSTGRES_QA.md`](../docs/POSTGRES_QA.md)
for the workload, host limits, teardown, and interpretation.

## Occupancy, credential recovery and monthly KPIs — 14/10/2026

`test_account_occupancy_dashboard.py` adds 12 tests for preserved Issued unpaid/paid
snapshots, new headcount and fixed water fees, owner scope/stale occupancy/validation,
Draft revision/version/recalculation and paid/pending guards, saved reading regeneration,
Owner/Admin password changes, session revocation, admin reauthentication, single-use,
replaced/expired/concurrently-consumed reset tokens, hash-only storage and monthly KPI
exclusions/all-time compatibility. SQLite fixtures require `TEST_SQLITE_DB` pointing to the
same disposable local API database. They never contact payOS.

Latest full-suite result: SQLite **52/52 pass**. PostgreSQL disposable database: **48 run,
40 pass, 8 skip** (SQLite-only fixtures). The console service suite passes **71 assertions**,
including Vietnam month/year boundaries and saved-headcount generation. Frontend build
and i18n checks pass with **828 keys × 4 catalogs**. See
[implementation report](../docs/account-occupancy-dashboard-report.md) for migration and UI evidence.

## Backend/security/ops optimization — 14/10/2026

The integration suite adds password changes that keep the current cookie/session, reject wrong
current password/invalid length/foreign Origin, revoke a second device immediately and allow
8-character new passwords without an API confirmation field. Invoice filter tests cover kind,
lifecycle, status, literal case-insensitive room/building/tenant search and the array contract.
Existing account tests now expect the current session to remain active. SQLite payment fixtures
explicitly insert `Kind='Invoice'` required by the current schema.

Latest isolated run: SQLite 59/59; PostgreSQL 55 test methods: 48 pass, 7 SQLite-only skips, plus one skipped webhook fixture class (`OK (skipped=8)`).
The service suite uses an in-process HTTP fake. Full evidence and the initial outbound-request
incident are recorded in `docs/PROJECT_HANDOFF.md` and the upgrade report.

## Registration email OTP (SMTP)

Verification defaults to enabled; Production refuses EmailVerification__Enabled=false.
The legacy business suite explicitly opts out only on its disposable Development API.
`dotnet run --project tests/RoomRental.WebhookTests` includes sender-fake/controlled-clock OTP
checks without external email. A separate real SMTP-protocol sink never relays beyond loopback.

Create `/tmp/rehouse-otp-qa.REPLACE` (a fresh mktemp directory) and start the sink:

```sh
python3 tests/otp_smtp_fixture.py --outbox /tmp/rehouse-otp-qa.REPLACE/outbox --port 5098
```

Start a new dedicated API in another terminal, with these environment overrides (same path):

```sh
ASPNETCORE_ENVIRONMENT=Development DatabaseProvider=SQLite \
ConnectionStrings__DefaultConnection='Data Source=/tmp/rehouse-otp-qa.REPLACE/otp.db' \
DataProtection__KeysPath=/tmp/rehouse-otp-qa.REPLACE/keys \
SeedDemo=false EnableInvoiceSweep=false EnablePaymentReconciliation=false \
EmailVerification__Enabled=true AuthRequestsPerMinute=1000 \
Email__FromAddress=otp@example.invalid Email__Smtp__Host=127.0.0.1 \
Email__Smtp__Port=5098 Email__Smtp__Security=None \
Email__Smtp__Username= Email__Smtp__Password= \
WebhookBaseUrl=http://localhost:5097 PublicBaseUrl=http://localhost:5097 \
HTTPS_PROXY=http://127.0.0.1:9 HTTP_PROXY=http://127.0.0.1:9 \
ALL_PROXY=http://127.0.0.1:9 NO_PROXY=localhost,127.0.0.1 \
dotnet run --no-launch-profile --project backend/RoomRental.Api --urls http://localhost:5097
```

Run OTP tests:

```sh
API_BASE_URL=http://localhost:5097 TEST_SQLITE_DB=/tmp/rehouse-otp-qa.REPLACE/otp.db \
TEST_OTP_OUTBOX=/tmp/rehouse-otp-qa.REPLACE/outbox \
python3 -m unittest discover -s tests -p test_registration_otp.py -v
```

Only fake recipients/codes are stored under /tmp with0700/0600 permissions; never configure a real
SMTP server for these tests. The fixture rejects non-local API/outbox paths. Against a dedicated
PostgreSQL API using the same SMTP sink, run just the provider-independent tests:

```sh
API_BASE_URL=http://localhost:5100 TEST_OTP_OUTBOX=/tmp/rehouse-otp-qa.REPLACE/outbox \
PYTHONPATH=tests python3 -m unittest test_registration_otp.RegistrationOtpHttpTests -v
```

OTP HTTP coverage: pre-verification login rejection, one-time consume, wrong-code lockout,
expiry, per-email resend budget, token replacement/password binding, SMTP failure rollback,
and concurrent consume. Service suite now113 assertions; SQLite OTP10/10; PostgreSQL HTTP3/3.
Existing SQLite business suite59/59 remains green, with opt-in OTP classes skipped.

## Cash receipts with pending payOS (14/10/2026)

`dotnet run --project tests/RoomRental.WebhookTests` includes ManualReceiptTests with an
in-process fake signed payOS handler. Covers atomic full-balance cash cancellation, rejection of partial cash before provider calls, retries,
provider-paid races, partial online money, malformed/signature/amount/order/timeout failures,
rollback recovery, legacy reservations, owner/version/idempotency and terminal reconciliation.
Latest SQLite service suite:165 assertions passed. No live provider calls.

For PostgreSQL service coverage, use a dedicated disposable PostgreSQL instance bound only to
127.0.0.1:15496. Set TEST_ALLOW_DISPOSABLE_PAYMENT_POSTGRES=1 and
TEST_PAYMENT_POSTGRES_CONNECTION to its admin database, then run the same command. The fixture
requires this exact loopback port, creates a fresh random rehouse_payment_qa_* database per
fixture and drops only that generated database afterwards. Never point it at the application DB.
Latest PostgreSQL run:167 assertions passed, including simultaneous cash submissions with
exactly one committed receipt. The disposable HTTP regression API also passed59/59 business
tests (two OTP fixture classes skipped), with outbound proxy blocking and localhost callback.

## Operational hardening16/10/2026

run_disposable.py owns temporary SQLite/PostgreSQL/OTP APIs. PostgreSQL requires TEST_HTTP_POSTGRES_CONTAINER and a loopback TEST_HTTP_POSTGRES_CONNECTION naming roomie_qa_*; it creates/drops its own random database in that explicit disposable container. Never use the app database/container. CI passes --image to test the exact image promoted to GHCR. Skip counts are gated: SQLite0, PostgreSQL8 known SQLite-only cases, OTP0. check_db_roles.py creates a separate network-none PostgreSQL container and executes the real provisioner twice, verifying runtime DML/denied DDL and migration-history writes. test_backup_*.py covers retention, failed verification before rotation, finite subprocess and TLS alert content/cooldown without sending email. frontend/e2e has five synthetic browser smoke cases. See [release record](../docs/tech-debt-remediation-2026-10-16.md).


## Password-only login regression — 16/10/2026

MFA has been removed at the user's request. `test_operational_hardening` now tests login/me compatibility flags, correct/incorrect passwords, ignored legacy `code`, admin access, logout and removed MFA endpoints. Legacy enabled/disabled row mutation is restricted to the runner-owned SQLite `roomie-ci-*/fixture.db` and restores the original values. PostgreSQL retains the explicit SQLite-only skip. Service checks cover DTO flags and authenticated admin middleware access for both legacy states; Playwright covers the removed input/route and ignores stale MFA response flags.


## Password length policy — 16/10/2026

New passwords require9–256characters; no composition rules. Boundary service tests reject empty/whitespace-only/8/257 and accept9lowercase/9digits/uppercase/256. Disposable HTTP tests cover registration, OTP verification/login, password change/session revocation and admin reset with simple nine-character passwords. Playwright reset checks eight-character rejection and nine-character acceptance without composition rules.

## Password recovery

The disposable `otp` provider runs registration and password-recovery HTTP tests against SQLite and a local SMTP sink. `otp-postgres` runs provider-independent registration tests and the same recovery tests against a runner-owned temporary PostgreSQL database. Both retain external-service guards; never supply a production connection. See `python3 tests/run_disposable.py --help` for runner arguments and [password recovery](../docs/password-recovery.md) for behavior.

C# coverage is included in `dotnet run --project tests/RoomRental.WebhookTests`; frontend coverage is in `frontend/e2e/password-recovery.spec.ts`.

Zalo reminder tests (fake provider, no real messages): `dotnet run --project tests/RoomRental.ZaloTests`, `npm --prefix zalo-gateway test`, and `npm --prefix frontend run test:e2e -- zalo.spec.ts --workers=2`. See [Zalo setup](../docs/zalo-setup.md) for disposable PostgreSQL test configuration.

## Payment modes and batch checkout

`tests/test_payment_modes.py` runs in the ordinary `sqlite` and `postgres` disposable harness suites: individual/all-page VietQR selection, owner-only/idempotent confirmation, stale balance rollback and parallel confirmation. C# `BatchPaymentTests` uses a fake signed payOS channel to cover reservation membership, order reuse, invalid amounts, replay and per-invoice allocation. The existing payment fixture can run against an explicitly allowed runner-owned PostgreSQL on port 15496; it creates/drops only its own temporary databases. No real bank transfer or payable QR is used. See [payment confirmation](../docs/payment-confirmation.md).

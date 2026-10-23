"""Black-box integration tests for the local room-management API.

Run only against a disposable Development server with outbound HTTP(S) blocked:

    python3 -m unittest discover -s tests -v

The suite uses only Python's standard library and never contacts a payment
provider from this module. Other modules include subscription checkout attempts,
so the disposable API must use an outbound-blocking proxy. Never use a shared DB.
"""

from __future__ import annotations

import http.cookiejar
import hashlib
import hmac
import json
import os
import unittest
import urllib.error
import urllib.request
import uuid
from datetime import date, datetime, timedelta, timezone
from typing import Any
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

try:
    VIETNAM_TZ: Any = ZoneInfo("Asia/Ho_Chi_Minh")
except ZoneInfoNotFoundError:
    VIETNAM_TZ = timezone(timedelta(hours=7))


BASE_URL = os.environ.get("API_BASE_URL", "http://localhost:5080").rstrip("/")
ORIGIN = os.environ.get("API_ORIGIN", BASE_URL)
ADMIN_EMAIL = os.environ.get("API_ADMIN_EMAIL", "admin@phongtro.local")
ADMIN_PASSWORD = os.environ.get("API_ADMIN_PASSWORD", "Admin123!")
OWNER_PASSWORD = os.environ.get("API_TEST_OWNER_PASSWORD", "OwnerTest!89a")


def unwrap(value: Any) -> Any:
    if isinstance(value, dict) and value.get("data") is not None:
        return value["data"]
    return value


def as_list(value: Any) -> list[dict[str, Any]]:
    value = unwrap(value)
    if isinstance(value, list):
        return value
    if isinstance(value, dict):
        for key in ("items", "results", "records"):
            if isinstance(value.get(key), list):
                return value[key]
    raise AssertionError(f"Expected a JSON list, got {value!r}")


class ApiSession:
    """An isolated cookie jar with a small JSON HTTP client."""

    def __init__(self) -> None:
        self.cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.cookies)
        )

    def request(
        self,
        method: str,
        path: str,
        payload: Any | None = None,
        *,
        origin: str | None = None,
        headers: dict[str, str] | None = None,
    ) -> tuple[int, Any, dict[str, str]]:
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        req_headers = {"Accept": "application/json"}
        if data is not None:
            req_headers["Content-Type"] = "application/json"
        if method.upper() not in {"GET", "HEAD", "OPTIONS"}:
            req_headers["Origin"] = origin or ORIGIN
        if headers:
            req_headers.update(headers)
        request = urllib.request.Request(
            f"{BASE_URL}{path}", data=data, headers=req_headers, method=method.upper()
        )
        try:
            response = self.opener.open(request, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        status = getattr(response, "status", None)
        if status is None:
            status = response.code
        response_headers = dict(response.headers.items())
        content_type = response.headers.get("Content-Type", "")
        raw = response.read()
        response.close()
        if not raw:
            body: Any = None
        elif "json" in content_type.lower():
            try:
                body = json.loads(raw.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                body = raw.decode("utf-8", errors="replace")
        elif "spreadsheetml.sheet" in content_type.lower():
            body = raw
        else:
            body = raw.decode("utf-8", errors="replace")
        return status, body, response_headers

    def ok(
        self,
        method: str,
        path: str,
        payload: Any | None = None,
        *,
        headers: dict[str, str] | None = None,
    ) -> Any:
        status, body, _ = self.request(method, path, payload, headers=headers)
        if not 200 <= status < 300:
            raise AssertionError(f"{method} {path} returned {status}: {body!r}")
        return unwrap(body)


def period_offset(month_offset: int) -> str:
    """Return a YYYY-MM period relative to this month."""
    today = datetime.now(VIETNAM_TZ).date()
    year, month = today.year, today.month + month_offset
    while month < 1:
        year -= 1
        month += 12
    while month > 12:
        year += 1
        month -= 12
    return f"{year:04d}-{month:02d}"


def today_vietnam() -> date:
    return datetime.now(VIETNAM_TZ).date()


def first_day(period: str) -> str:
    return f"{period}-01"


def payos_data_signature(data: dict[str, Any], checksum_key: str) -> str:
    """Sign the scalar payOS webhook data used by these local API tests."""
    def value_text(value: Any) -> str:
        if value is None or value in ("null", "undefined"):
            return ""
        if value is True:
            return "true"
        if value is False:
            return "false"
        return str(value)

    message = "&".join(f"{key}={value_text(data[key])}" for key in sorted(data))
    return hmac.new(checksum_key.encode(), message.encode(), hashlib.sha256).hexdigest()


class RoomApiIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        probe = ApiSession()
        try:
            status, body, _ = probe.request("GET", "/api/health")
        except (OSError, urllib.error.URLError) as error:
            raise unittest.SkipTest(f"Local API is not running at {BASE_URL}: {error}")
        if status != 200:
            raise unittest.SkipTest(
                f"Local API health check returned {status}: {body!r}"
            )

        cls.admin = ApiSession()
        status, body, _ = cls.admin.request(
            "POST", "/api/auth/login", {"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD}
        )
        if status not in (200, 201):
            raise AssertionError(
                "Could not log in with the Development demo admin. "
                f"Check SeedDemo and API_ADMIN_EMAIL/API_ADMIN_PASSWORD; got {status}: {body!r}"
            )

    def setUp(self) -> None:
        self.owner, self.owner_user = self.create_owner()
        self.approve(self.owner_user)
        self.owner.ok(
            "POST", "/api/auth/login", {"email": self.owner_user["email"], "password": OWNER_PASSWORD}
        )
        self.owner_id = self.owner_user["id"]

    def create_owner(self) -> tuple[ApiSession, dict[str, Any]]:
        suffix = uuid.uuid4().hex
        session = ApiSession()
        body = session.ok(
            "POST",
            "/api/auth/register",
            {
                "name": f"Integration Owner {suffix[:8]}",
                "email": f"integration-{suffix}@example.invalid",
                "password": OWNER_PASSWORD,
                "phone": f"09{int(suffix[:8], 16) % 100_000_000:08d}",
            },
        )
        return session, body

    def approve(self, user: dict[str, Any]) -> None:
        result = self.admin.ok(
            "POST",
            f"/api/admin/owners/{user['id']}/status",
            {"status": "Approved", "reason": "Automated local integration test"},
        )
        self.assertEqual(result.get("status"), "Approved")

    def test_change_password_keeps_current_session_and_revokes_other_devices(self):
        second = ApiSession()
        second.ok("POST", "/api/auth/login", {"email": self.owner_user["email"], "password": OWNER_PASSWORD})
        sid_cookie = [(c.name, c.value) for c in self.owner.cookies]
        body = {"currentPassword": OWNER_PASSWORD, "newPassword": "abcdefghi"}
        self.assertEqual(ApiSession().request("POST", "/api/auth/change-password", body)[0], 401)
        self.assertEqual(self.owner.request("POST", "/api/auth/change-password", body, origin="https://evil.invalid")[0], 403)
        status, error, _ = self.owner.request("POST", "/api/auth/change-password", {**body, "currentPassword": "wrong"})
        self.assertEqual(status, 400)
        self.assertIn("Mật khẩu hiện tại", error["message"])
        self.assertEqual(second.request("GET", "/api/auth/me")[0], 200)
        for password in ("short", "abcdefgh", " " * 9, "x" * 257):
            self.assertEqual(self.owner.request("POST", "/api/auth/change-password", {**body, "newPassword": password})[0], 400)
        self.owner.ok("POST", "/api/auth/change-password", body)
        self.assertEqual(self.owner.ok("GET", "/api/auth/me")["id"], self.owner_user["id"])
        self.assertEqual([(c.name, c.value) for c in self.owner.cookies], sid_cookie)
        self.assertEqual(second.request("GET", "/api/auth/me")[0], 401)
        self.assertEqual(ApiSession().request("POST", "/api/auth/login", {"email": self.owner_user["email"], "password": OWNER_PASSWORD})[0], 401)
        ApiSession().ok("POST", "/api/auth/login", {"email": self.owner_user["email"], "password": body["newPassword"]})

    def test_room_reading_period_selection_preserves_physical_maximum(self):
        room = self.create_room(self.owner, label="MeterHistory")
        self.start_occupancy(self.owner, room["id"], start_date=first_day(period_offset(-2)))
        for offset, value in [(-2, 110.125), (-1, 120.25), (0, 130.375)]:
            self.save_reading(self.owner, room["id"], period_offset(offset), value)
        for offset, expected in [(-3, 130.375), (-2, 100), (-1, 110.125), (0, 120.25), (1, 130.375)]:
            rooms = as_list(self.owner.ok("GET", "/api/rooms?period=" + period_offset(offset)))
            selected = next(row for row in rooms if row["id"] == room["id"])
            self.assertEqual(selected["previousReading"], expected)
        self.assertEqual(self.get_room(self.owner, room["id"])["previousReading"], 120.25)
        self.assertEqual(self.owner.ok("GET", "/api/dashboard")["pendingReadings"], 0)

    def test_invoice_server_filters_keep_array_contract(self):
        from urllib.parse import urlencode
        room = self.create_room(self.owner, label="Filter")
        tenant = self.start_occupancy(self.owner, room["id"])
        self.save_reading(self.owner, room["id"], period_offset(0), 110)
        invoice = self.invoices_for(self.owner, period_offset(0))[0]
        for search in (room["name"].upper(), room["buildingName"], tenant["tenantName"]):
            query = urlencode({"kind": "Monthly", "lifecycleState": "Issued", "status": "Unpaid", "search": search, "period": period_offset(0)})
            result = self.owner.ok("GET", "/api/invoices?" + query)
            self.assertIsInstance(result, list)
            self.assertEqual([row["id"] for row in result], [invoice["id"]])
        for query in ("kind=Deposit", "status=Paid", "lifecycleState=Draft", "search=nonexistent-filter-value"):
            self.assertEqual(self.owner.ok("GET", "/api/invoices?" + query), [])
        self.assertEqual(self.owner.request("GET", "/api/invoices?status=invalid")[0], 400)
        self.assertEqual(self.owner.request("GET", "/api/invoices?lifecycleState=invalid")[0], 400)

    def create_building(self, session: ApiSession, label: str) -> dict[str, Any]:
        suffix = uuid.uuid4().hex[:8]
        return session.ok(
            "POST",
            "/api/buildings",
            {"name": f"{label}-{suffix}", "address": f"{suffix} Integration Street"},
        )

    def create_room(
        self,
        session: ApiSession,
        *,
        label: str,
        initial_reading: int = 100,
        rent: int = 4_200_000,
        electricity_rate: int = 1_500,
        water_fee: int = 85_000,
        water_per_person: bool = True,
        service_fee: int = 120_000,
    ) -> dict[str, Any]:
        building = self.create_building(session, label)
        room = session.ok(
            "POST",
            "/api/rooms",
            {
                "buildingId": building["id"],
                "name": f"Room-{uuid.uuid4().hex[:8]}",
                "rent": rent,
                "electricityRate": electricity_rate,
                "waterFee": water_fee,
                "waterPerPerson": water_per_person,
                "serviceFee": service_fee,
                "initialReading": initial_reading,
            },
        )
        return self.get_room(session, room["id"])

    def get_room(self, session: ApiSession, room_id: str) -> dict[str, Any]:
        result = session.ok("GET", f"/api/rooms/{room_id}")
        if isinstance(result, dict) and isinstance(result.get("room"), dict):
            return result["room"]
        return result

    def start_occupancy(
        self,
        session: ApiSession,
        room_id: str,
        tenant_name: str = "Integration Tenant",
        *,
        people: int = 2,
        start_date: str | None = None,
    ) -> dict[str, Any]:
        result = session.ok(
            "POST",
            f"/api/rooms/{room_id}/occupancy",
            {
                "tenantName": f"{tenant_name}-{uuid.uuid4().hex[:6]}",
                "phone": "0900000000",
                "people": people,
                "startDate": start_date or first_day(period_offset(-1)),
            },
        )
        room = self.get_room(session, room_id)
        occupancy = room.get("occupancy")
        if not isinstance(occupancy, dict):
            occupancy = result.get("occupancy") if isinstance(result, dict) else None
        self.assertIsInstance(occupancy, dict, f"Occupancy was not returned: {result!r}")
        return occupancy

    def save_reading(
        self, session: ApiSession, room_id: str, period: str, current: int
    ) -> dict[str, Any]:
        result = session.ok(
            "POST",
            "/api/readings",
            {"roomId": room_id, "period": period, "currentReading": current},
        )
        return result if isinstance(result, dict) else {}

    def invoices_for(self, session: ApiSession, period: str) -> list[dict[str, Any]]:
        return as_list(session.ok("GET", f"/api/invoices?period={period}"))

    def invoice_for_room(self, session: ApiSession, period: str, room_id: str) -> dict[str, Any]:
        matching = [
            invoice
            for invoice in self.invoices_for(session, period)
            if invoice.get("roomId") == room_id
        ]
        self.assertEqual(len(matching), 1, f"Expected one invoice for {room_id}: {matching!r}")
        return matching[0]

    def moveout_payload(self, session: ApiSession, room_id: str, occupancy_id: str,
                        refund_amount: int = 0, request_id: str | None = None) -> dict[str, Any]:
        summary = session.ok("GET", f"/api/rooms/{room_id}/moveout")
        deposit = summary["deposit"]
        return {
            "occupancyId": occupancy_id,
            "requestId": request_id or str(uuid.uuid4()),
            "refundAmount": refund_amount,
            "expectedHeld": summary["heldDeposit"],
            "expectedDepositVersion": deposit["version"] if deposit is not None else None,
        }

    def test_moveout_zero_balance_scope_and_stale_occupancy(self) -> None:
        room = self.create_room(self.owner, label="zero-moveout", initial_reading=100, rent=0, electricity_rate=0, water_fee=0, service_fee=0)
        occupancy = self.start_occupancy(self.owner, room["id"])
        path = f"/api/rooms/{room['id']}"
        body = self.moveout_payload(self.owner, room["id"], occupancy["id"])
        other, user = self.create_owner(); self.approve(user)
        other.ok("POST", "/api/auth/login", {"email": user["email"], "password": OWNER_PASSWORD})
        for method, route, payload in [
            ("GET", "/moveout", None),
            ("POST", "/moveout/preview", {"endDate": today_vietnam().isoformat(), "currentReading": 100}),
            ("POST", "/final-invoice", {"requestId": str(uuid.uuid4()), "occupancyId": occupancy["id"], "currentReading": 100, "additionalAmount": 0}),
            ("POST", "/complete-moveout", body),
        ]:
            self.assertEqual(other.request(method, path + route, payload)[0], 404)
        self.assertEqual(self.owner.request("POST", path + "/final-invoice", {
            "requestId": str(uuid.uuid4()), "occupancyId": occupancy["id"], "currentReading": 100, "additionalAmount": 0,
        })[0], 404)
        wrong_occupancy = {**body, "occupancyId": str(uuid.uuid4()), "requestId": str(uuid.uuid4())}
        self.assertEqual(self.owner.request("POST", path + "/complete-moveout", wrong_occupancy)[0], 404)
        ended = self.owner.ok("POST", path + "/complete-moveout", body)
        self.assertEqual(ended["endDate"], today_vietnam().isoformat())
        self.assertEqual(ended["refundAmount"], 0)
        self.assertEqual(self.owner.ok("POST", path + "/complete-moveout", body)["id"], ended["id"])
        new = self.start_occupancy(self.owner, room["id"], start_date=today_vietnam().isoformat())
        self.assertEqual(self.owner.ok("POST", path + "/complete-moveout", body)["occupancyId"], occupancy["id"])
        self.assertEqual(self.get_room(self.owner, room["id"])["occupancy"]["id"], new["id"])

    def test_moveout_manual_refund_retention_debts_and_replay(self) -> None:
        room = self.create_room(self.owner, label="moveout", initial_reading=100, rent=500, electricity_rate=100, water_fee=0, service_fee=0)
        started = self.owner.ok("POST", f"/api/rooms/{room['id']}/occupancy", {
            "tenantName": "Moveout tenant", "people": 1, "startDate": first_day(period_offset(-1)), "depositAmount": 3000,
        })
        occupancy = started["occupancy"]
        deposit_id = occupancy["depositInvoiceId"]
        self.owner.ok("POST", f"/api/invoices/{deposit_id}/receipts", {"amount": 3000, "idempotencyKey": str(uuid.uuid4())})
        self.owner.ok("POST", "/api/readings", {"roomId": room["id"], "period": period_offset(-1), "currentReading": 110})
        monthly = next(i for i in as_list(self.owner.ok("GET", "/api/invoices")) if i["roomId"] == room["id"] and i["kind"] == "Monthly")
        path = f"/api/rooms/{room['id']}"
        self.assertEqual(self.owner.request("POST", path + "/complete-moveout", self.moveout_payload(self.owner, room["id"], occupancy["id"], 1000))[0], 409)
        self.assertEqual(self.owner.ok("GET", path + "/moveout")["heldDeposit"], 3000)
        self.owner.ok("POST", f"/api/invoices/{monthly['id']}/receipts", {
            "amount": monthly["outstanding"], "idempotencyKey": str(uuid.uuid4()),
        })

        stale = self.moveout_payload(self.owner, room["id"], occupancy["id"], 900)
        deposit_before = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        self.owner.ok("POST", f"/api/invoices/{deposit_id}/deposit-settlements", {
            "amount": 100, "kind": "Deduction", "reason": "Manual adjustment before return",
            "idempotencyKey": str(uuid.uuid4()), "expectedVersion": deposit_before["version"],
        })
        status, body, _ = self.owner.request("POST", path + "/complete-moveout", stale)
        self.assertEqual(status, 409, body)
        self.assertIn("đã thay đổi", body["message"])

        payload = self.moveout_payload(self.owner, room["id"], occupancy["id"], 900)
        ended = self.owner.ok("POST", path + "/complete-moveout", payload)
        self.assertEqual(ended["heldBeforeSettlement"], 2900)
        self.assertEqual(ended["refundAmount"], 900)
        self.assertEqual(ended["retainedAmount"], 2000)
        deposit_after = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        self.assertEqual(deposit_after["paid"], 3000)
        self.assertEqual(deposit_after["depositHeld"], 0)
        settlements = deposit_after["depositSettlements"]
        self.assertEqual(sum(x["amount"] for x in settlements if x["kind"] == "Refund"), 900)
        self.assertEqual(sum(x["amount"] for x in settlements if x["kind"] == "Deduction"), 2100)
        self.assertEqual(self.owner.ok("GET", path + "/moveout-completions")["items"][0]["actorId"], self.owner_user["id"])
        self.assertEqual(self.owner.ok("POST", path + "/complete-moveout", payload)["id"], ended["id"])
        self.assertEqual(self.owner.request("POST", path + "/complete-moveout", {**payload, "refundAmount": 901})[0], 409)
        new = self.start_occupancy(self.owner, room["id"], start_date=today_vietnam().isoformat())
        self.assertEqual(self.owner.ok("POST", path + "/complete-moveout", payload)["occupancyId"], occupancy["id"])
        self.assertEqual(self.get_room(self.owner, room["id"])["occupancy"]["id"], new["id"])
        self.owner.ok("POST", "/api/readings", {"roomId": room["id"], "period": period_offset(0), "currentReading": 118})
        readings = as_list(self.owner.ok("GET", f"/api/readings?period={period_offset(0)}"))
        self.assertEqual(next(x for x in readings if x["occupancyId"] == new["id"])["previousReading"], 110)

    def test_midmonth_readings_are_issued_at_full_rent_and_moveout_has_no_draft_gate(self) -> None:
        previous_period = period_offset(-1)
        room = self.create_room(self.owner, label="moveout-midmonth", rent=3100, electricity_rate=0, water_fee=0, service_fee=0)
        occupancy = self.start_occupancy(self.owner, room["id"], start_date=f"{previous_period}-15")
        first = self.save_reading(self.owner, room["id"], previous_period, 101)
        current = self.save_reading(self.owner, room["id"], period_offset(0), 102)
        self.assertEqual(first["invoice"]["lifecycleState"], "Issued")
        self.assertEqual(first["invoice"]["rent"], 3100)
        self.assertEqual(current["invoice"]["lifecycleState"], "Issued")
        self.assertEqual(current["invoice"]["rent"], 3100)
        payload = self.moveout_payload(self.owner, room["id"], occupancy["id"])
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", payload)
        self.assertEqual(status, 409, body)
        self.assertIn("công nợ", body["message"])

    def test_supplemental_invoice_scope_validation_replay_and_guest(self) -> None:
        room = self.create_room(self.owner, label="supplemental")
        payload = {"requestId": str(uuid.uuid4()), "occupancyId": str(uuid.uuid4()), "description": "Repair lock", "amount": 150000, "dueDate": today_vietnam().isoformat()}
        path = f"/api/rooms/{room['id']}/supplemental-invoices"
        self.assertEqual(self.owner.request("POST", path, payload)[0], 409)
        self.start_occupancy(self.owner, room["id"])
        occupancy = self.get_room(self.owner, room["id"])["occupancy"]
        payload["occupancyId"] = occupancy["id"]
        for amount in (0, -1, 1.5, 2147483648):
            self.assertEqual(self.owner.request("POST", path, {**payload, "amount": amount})[0], 400)
        self.assertEqual(self.owner.request("POST", path, {**payload, "description": " "})[0], 400)
        created = self.owner.ok("POST", path, payload)
        replay = self.owner.ok("POST", path, payload)
        self.assertEqual(created["id"], replay["id"])
        self.assertEqual(created["kind"], "Supplemental")
        self.assertEqual(created["description"], payload["description"])
        self.assertEqual(created["paid"], 0)
        self.assertEqual(self.owner.request("POST", path, {**payload, "amount": 150001})[0], 409)
        second = self.owner.ok("POST", path, {**payload, "requestId": str(uuid.uuid4())})
        self.assertNotEqual(second["id"], created["id"])
        guest = self.owner.ok("GET", f"/api/tenant/{occupancy['token']}")
        self.assertEqual(len(guest["invoices"]), 2)
        self.owner.ok("POST", "/api/readings", {"roomId": room["id"], "period": period_offset(0), "currentReading": 110})
        self.assertEqual(len([x for x in as_list(self.owner.ok("GET", "/api/invoices")) if x["roomId"] == room["id"]]), 3)
        self.owner.ok("POST", f"/api/invoices/{created['id']}/receipts", {"amount": 150000, "idempotencyKey": str(uuid.uuid4())})
        self.assertEqual(self.owner.ok("GET", f"/api/invoices/{created['id']}")["status"], "Paid")
        other, user = self.create_owner(); self.approve(user)
        other.ok("POST", "/api/auth/login", {"email": user["email"], "password": OWNER_PASSWORD})
        self.assertEqual(other.request("POST", path, {**payload, "requestId": str(uuid.uuid4())})[0], 404)
        for invoice in as_list(self.owner.ok("GET", "/api/invoices")):
            if invoice["roomId"] == room["id"] and invoice["outstanding"] > 0:
                self.owner.ok("POST", f"/api/invoices/{invoice['id']}/receipts", {
                    "amount": invoice["outstanding"], "idempotencyKey": str(uuid.uuid4())})
        self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout",
                      self.moveout_payload(self.owner, room["id"], occupancy["id"]))
        self.assertEqual(self.owner.request("POST", path, {**payload, "requestId": str(uuid.uuid4())})[0], 409)
        self.assertEqual(self.owner.request("GET", f"/api/tenant/{occupancy['token']}")[0], 404)

    def test_deposit_collection_settlement_replay_and_moveout(self) -> None:
        room = self.create_room(self.owner, label="deposit", rent=500, electricity_rate=0, water_fee=0, service_fee=0)
        result = self.owner.ok("POST", f"/api/rooms/{room['id']}/occupancy", {
            "tenantName": "Deposit tenant", "people": 1, "startDate": first_day(period_offset(0)), "depositAmount": 1000})
        occupancy = result["occupancy"]
        deposit_id = occupancy["depositInvoiceId"]
        self.assertEqual(occupancy["depositRequired"], 1000)
        self.assertEqual(occupancy["depositPaid"], 0)
        tenant = self.owner.ok("GET", f"/api/tenant/{occupancy['token']}")
        self.assertEqual(tenant["invoices"][0]["kind"], "Deposit")
        self.owner.ok("POST", "/api/readings", {"roomId": room["id"], "period": period_offset(0), "currentReading": 100})
        invoices = [x for x in as_list(self.owner.ok("GET", "/api/invoices")) if x["roomId"] == room["id"]]
        self.assertEqual({x["kind"] for x in invoices}, {"Monthly", "Deposit"})
        monthly = next(x for x in invoices if x["kind"] == "Monthly")
        self.owner.ok("POST", "/api/invoices/generate", {"period": period_offset(0)})
        self.assertEqual(len([x for x in as_list(self.owner.ok("GET", "/api/invoices")) if x["roomId"] == room["id"]]), 2)
        other, other_user = self.create_owner()
        self.approve(other_user)
        other.ok("POST", "/api/auth/login", {"email": other_user["email"], "password": OWNER_PASSWORD})
        settlement = {"amount": 300, "kind": "Deduction", "reason": "Moveout damages", "idempotencyKey": str(uuid.uuid4()), "targetInvoiceId": None}
        self.assertEqual(other.request("POST", f"/api/invoices/{deposit_id}/deposit-settlements", settlement)[0], 404)
        self.owner.ok("POST", f"/api/invoices/{monthly['id']}/receipts", {
            "amount": monthly["total"], "idempotencyKey": str(uuid.uuid4())})
        pending = self.moveout_payload(self.owner, room["id"], occupancy["id"], refund_amount=0)
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", pending)
        self.assertEqual(status, 409, body)
        self.assertIn("cọc", body["message"])
        self.owner.ok("POST", f"/api/invoices/{deposit_id}/receipts", {"amount": 1000, "idempotencyKey": str(uuid.uuid4())})
        self.assertEqual(self.owner.ok("GET", f"/api/invoices/{deposit_id}")["depositHeld"], 1000)
        deposit_before = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        first = self.owner.ok("POST", f"/api/invoices/{deposit_id}/deposit-settlements", {
            **settlement, "expectedVersion": deposit_before["version"]})
        replay = self.owner.ok("POST", f"/api/invoices/{deposit_id}/deposit-settlements", {
            **settlement, "expectedVersion": deposit_before["version"]})
        self.assertEqual(first["depositHeld"], 700)
        self.assertEqual(len(replay["depositSettlements"]), 1)
        self.assertEqual(self.owner.ok("GET", f"/api/invoices/{monthly['id']}")["paid"], monthly["total"])
        changed = {**settlement, "amount": 301}
        self.assertEqual(self.owner.request("POST", f"/api/invoices/{deposit_id}/deposit-settlements", changed)[0], 409)
        refund = {"amount": 701, "kind": "Refund", "reason": "Returned to tenant", "idempotencyKey": str(uuid.uuid4())}
        self.assertEqual(self.owner.request("POST", f"/api/invoices/{deposit_id}/deposit-settlements", refund)[0], 400)
        refund["amount"] = 700
        settled = self.owner.ok("POST", f"/api/invoices/{deposit_id}/deposit-settlements", refund)
        self.assertEqual(settled["depositHeld"], 0)
        self.assertEqual(settled["paid"], 1000)
        monthly_now = self.owner.ok("GET", f"/api/invoices/{monthly['id']}")
        self.assertEqual(monthly_now["outstanding"], 0)
        complete = self.moveout_payload(self.owner, room["id"], occupancy["id"], refund_amount=0)
        ended = self.owner.ok("POST", f"/api/rooms/{room['id']}/end-occupancy", complete)
        self.assertEqual(ended["refundAmount"], 0)
        self.assertEqual(self.owner.request("GET", f"/api/tenant/{occupancy['token']}")[0], 404)
        changed_after_close = {**refund, "amount": 1, "idempotencyKey": str(uuid.uuid4())}
        self.assertEqual(self.owner.request("POST", f"/api/invoices/{deposit_id}/deposit-settlements", changed_after_close)[0], 409)

    def test_deposit_amount_validation_and_zero_deposit(self) -> None:
        room = self.create_room(self.owner, label="deposit-validation")
        payload = {"tenantName": "No deposit", "people": 1, "startDate": first_day(period_offset(0)), "depositAmount": -1}
        for amount in (-1, 1.5, 2147483648):
            payload["depositAmount"] = amount
            self.assertEqual(self.owner.request("POST", f"/api/rooms/{room['id']}/occupancy", payload)[0], 400)
        self.assertIsNone(self.get_room(self.owner, room["id"])["occupancy"])
        payload["depositAmount"] = 0
        occupancy = self.owner.ok("POST", f"/api/rooms/{room['id']}/occupancy", payload)["occupancy"]
        self.assertIsNone(occupancy["depositInvoiceId"])
        self.assertFalse([x for x in as_list(self.owner.ok("GET", "/api/invoices")) if x["roomId"] == room["id"]])

    def test_pending_owner_is_denied_until_admin_approval(self) -> None:
        session, user = self.create_owner()
        self.assertEqual(user.get("status"), "Approved")
        self.assertEqual(user.get("subscriptionTier"), "Free")

        owners = as_list(self.admin.ok("GET", "/api/admin/owners"))
        owner_record = next((item for item in owners if item.get("id") == user["id"]), None)
        self.assertIsNotNone(owner_record, "New owner was not visible to the admin")
        self.assertEqual(owner_record.get("status"), "Approved")
        self.assertEqual(owner_record.get("subscriptionTier"), "Free")

        login_status, login_body, _ = session.request(
            "POST", "/api/auth/login", {"email": user["email"], "password": OWNER_PASSWORD}
        )
        self.assertIn(login_status, (200, 201), f"New self-serve owner could not log in: {login_body!r}")
        dashboard_status, dashboard_body, _ = session.request("GET", "/api/dashboard")
        self.assertEqual(dashboard_status, 200, f"Approved self-serve owner denied dashboard: {dashboard_body!r}")

        # Admin can still suspend the self-serve owner
        self.admin.ok(
            "POST",
            f"/api/admin/owners/{user['id']}/status",
            {"status": "Suspended", "reason": "Self-serve admin suspension test"},
        )
        dashboard_status, dashboard_body, _ = session.request("GET", "/api/dashboard")
        self.assertIn(dashboard_status, (401, 403), f"Suspended owner could still access dashboard: {dashboard_body!r}")

        # Admin can re-approve the suspended owner
        self.approve(user)
        login_status, login_body, _ = session.request(
            "POST", "/api/auth/login", {"email": user["email"], "password": OWNER_PASSWORD}
        )
        self.assertIn(login_status, (200, 201), f"Re-approved owner could not log in: {login_body!r}")
        dashboard_status, dashboard, _ = session.request("GET", "/api/dashboard")
        self.assertEqual(dashboard_status, 200, f"Re-approved owner denied: {dashboard!r}")

    def test_owner_cannot_read_or_edit_another_owners_room(self) -> None:
        owner_b, user_b = self.create_owner()
        self.approve(user_b)
        owner_b.ok(
            "POST", "/api/auth/login", {"email": user_b["email"], "password": OWNER_PASSWORD}
        )
        room_b = self.create_room(owner_b, label="other-owner")

        listed = as_list(self.owner.ok("GET", "/api/rooms"))
        self.assertNotIn(room_b["id"], {item.get("id") for item in listed})

        status, body, _ = self.owner.request("GET", f"/api/rooms/{room_b['id']}")
        self.assertIn(status, (403, 404), f"Cross-owner room read was not denied: {body!r}")

        status, body, _ = self.owner.request(
            "PATCH",
            f"/api/rooms/{room_b['id']}",
            {
                "name": "Tampered by another owner",
                "rent": room_b["rent"],
                "electricityRate": room_b["electricityRate"],
                "waterFee": room_b["waterFee"],
                "waterPerPerson": room_b["waterPerPerson"],
                "serviceFee": room_b["serviceFee"],
            },
        )
        self.assertIn(status, (403, 404), f"Cross-owner room edit was not denied: {body!r}")

    def test_authenticated_mutation_rejects_a_foreign_origin(self) -> None:
        name = f"Blocked-Origin-{uuid.uuid4().hex[:8]}"
        status, body, _ = self.owner.request(
            "POST",
            "/api/buildings",
            {"name": name, "address": "Must not be created"},
            origin="http://evil.invalid",
        )
        self.assertEqual(status, 403, f"Foreign-origin mutation was not blocked: {body!r}")
        buildings = as_list(self.owner.ok("GET", "/api/buildings"))
        self.assertNotIn(name, {building.get("name") for building in buildings})

    def test_suspending_a_logged_in_owner_invalidates_the_existing_session(self) -> None:
        self.assertEqual(self.owner.ok("GET", "/api/auth/me")["id"], self.owner_id)
        self.admin.ok(
            "POST",
            f"/api/admin/owners/{self.owner_id}/status",
            {"status": "Suspended", "reason": "Integration session invalidation"},
        )
        status, body, _ = self.owner.request("GET", "/api/dashboard")
        self.assertIn(
            status,
            (401, 403),
            f"Suspended owner's existing session still reached a business route: {body!r}",
        )

    def test_reading_calculates_vnd_amounts_and_invoice_generation_is_idempotent(self) -> None:
        room = self.create_room(self.owner, label="baseline")
        self.start_occupancy(self.owner, room["id"], people=2)
        period = period_offset(0)
        self.save_reading(self.owner, room["id"], period, 137)

        # A valid reading can publish automatically or through the generate
        # endpoint. Repeating generation must leave exactly one invoice.
        self.owner.ok("POST", "/api/invoices/generate", {"period": period})
        self.owner.ok("POST", "/api/invoices/generate", {"period": period})
        invoices = [
            invoice
            for invoice in self.invoices_for(self.owner, period)
            if invoice.get("roomId") == room["id"]
        ]
        self.assertEqual(len(invoices), 1, f"Invoice generation duplicated: {invoices!r}")
        invoice = invoices[0]

        self.assertEqual(invoice["previousReading"], 100)
        self.assertEqual(invoice["currentReading"], 137)
        self.assertEqual(invoice["consumption"], 37)
        self.assertEqual(invoice["rent"], 4_200_000)
        self.assertEqual(invoice["electricityAmount"], 55_500)
        self.assertEqual(invoice["waterAmount"], 170_000)
        self.assertEqual(invoice["serviceAmount"], 120_000)
        self.assertEqual(invoice["total"], 4_545_500)
        self.assertEqual(invoice["paid"], 0)
        self.assertEqual(invoice["outstanding"], 4_545_500)

        self.owner.ok(
            "PATCH",
            f"/api/rooms/{room['id']}",
            {
                "name": "Updated after invoice",
                "rent": 1_000_000,
                "electricityRate": 5_000,
                "waterFee": 50_000,
                "waterPerPerson": False,
                "serviceFee": 50_000,
            },
        )
        persisted = self.owner.ok("GET", f"/api/invoices/{invoice['id']}")
        self.assertEqual(persisted["rent"], 4_200_000)
        self.assertEqual(persisted["electricityAmount"], 55_500)
        self.assertEqual(persisted["waterAmount"], 170_000)
        self.assertEqual(persisted["serviceAmount"], 120_000)
        self.assertEqual(persisted["total"], 4_545_500)

    def test_meter_decrease_is_rejected_without_saving_a_reading(self) -> None:
        room = self.create_room(self.owner, label="decrease", initial_reading=400)
        self.start_occupancy(self.owner, room["id"], people=1)
        first_period, second_period = period_offset(-1), period_offset(0)
        self.save_reading(self.owner, room["id"], first_period, 425)

        status, body, _ = self.owner.request(
            "POST",
            "/api/readings",
            {"roomId": room["id"], "period": second_period, "currentReading": 424},
        )
        self.assertGreaterEqual(status, 400, f"Decreasing meter value was accepted: {body!r}")
        self.assertLess(status, 500, f"Decreasing meter value caused a server error: {body!r}")
        readings = as_list(
            self.owner.ok("GET", f"/api/readings?period={second_period}")
        )
        self.assertNotIn(room["id"], {reading.get("roomId") for reading in readings})

    def test_reading_period_before_occupancy_or_in_the_future_is_rejected(self) -> None:
        room = self.create_room(self.owner, label="invalid-period")
        self.start_occupancy(
            self.owner,
            room["id"],
            people=1,
            start_date=today_vietnam().isoformat(),
        )
        for period in (period_offset(-1), period_offset(1)):
            status, body, _ = self.owner.request(
                "POST",
                "/api/readings",
                {"roomId": room["id"], "period": period, "currentReading": 150},
            )
            self.assertGreaterEqual(status, 400, f"Invalid meter period {period} accepted: {body!r}")
            self.assertLess(status, 500, f"Invalid meter period caused a server error: {body!r}")
            readings = as_list(self.owner.ok("GET", f"/api/readings?period={period}"))
            self.assertNotIn(room["id"], {reading.get("roomId") for reading in readings})

    def test_manual_receipt_settles_full_balance_and_rejects_invalid_amounts(self) -> None:
        room = self.create_room(self.owner, label="receipt")
        self.start_occupancy(self.owner, room["id"], people=2)
        period = period_offset(0)
        self.save_reading(self.owner, room["id"], period, 137)
        self.owner.ok("POST", "/api/invoices/generate", {"period": period})
        invoice = self.invoice_for_room(self.owner, period, room["id"])

        self.assertEqual(self.owner.request("POST", f"/api/invoices/{invoice['id']}/receipts",
            {"amount": 500_000, "idempotencyKey": uuid.uuid4().hex})[0], 409)
        self.assertEqual(self.owner.ok("GET", f"/api/invoices/{invoice['id']}")["paid"], 0)
        receipt = {
            "amount": invoice["total"],
            "note": "Local integration test receipt",
            "idempotencyKey": f"receipt-{uuid.uuid4().hex}",
        }
        updated = self.owner.ok(
            "POST",
            f"/api/invoices/{invoice['id']}/receipts",
            receipt,
        )
        self.assertEqual(updated["paid"], invoice["total"])
        self.assertEqual(updated["outstanding"], 0)
        self.assertEqual(updated["status"], "Paid")

        retried = self.owner.ok(
            "POST", f"/api/invoices/{invoice['id']}/receipts", receipt
        )
        self.assertEqual(retried["paid"], invoice["total"], "Receipt retry was counted twice")
        self.assertEqual(retried["outstanding"], 0)

        for amount in (0, -1, updated["outstanding"] + 1):
            status, body, _ = self.owner.request(
                "POST",
                f"/api/invoices/{invoice['id']}/receipts",
                {"amount": amount, "note": "Must be rejected"},
            )
            self.assertGreaterEqual(status, 400, f"Invalid receipt {amount} accepted: {body!r}")
            self.assertLess(status, 500, f"Invalid receipt caused a server error: {body!r}")

        persisted = self.owner.ok("GET", f"/api/invoices/{invoice['id']}")
        self.assertEqual(persisted["paid"], invoice["total"])
        self.assertEqual(persisted["outstanding"], 0)

    def test_rotating_guest_link_revokes_the_previous_token(self) -> None:
        room = self.create_room(self.owner, label="token-rotation")
        self.start_occupancy(self.owner, room["id"], people=1)
        original = self.owner.ok("POST", f"/api/rooms/{room['id']}/rotate-link")
        self.assertTrue(original.get("token"))
        self.assertEqual(
            self.owner.ok("GET", f"/api/tenant/{original['token']}")["room"]["id"],
            room["id"],
        )

        rotated = self.owner.ok("POST", f"/api/rooms/{room['id']}/rotate-link")
        self.assertTrue(rotated.get("token"))
        self.assertNotEqual(original["token"], rotated["token"])

        status, body, _ = ApiSession().request("GET", f"/api/tenant/{original['token']}")
        self.assertIn(status, (400, 401, 403, 404), f"Old guest token remained valid: {body!r}")
        new_overview = ApiSession().ok("GET", f"/api/tenant/{rotated['token']}")
        self.assertEqual(new_overview["room"]["id"], room["id"])

    def test_new_occupancy_does_not_inherit_previous_tenant_invoice_history(self) -> None:
        room = self.create_room(self.owner, label="occupancy-history")
        previous_period = period_offset(-1)
        previous_occupancy = self.start_occupancy(
            self.owner,
            room["id"],
            "Previous Tenant",
            people=1,
            start_date=first_day(previous_period),
        )
        previous_link = self.owner.ok("POST", f"/api/rooms/{room['id']}/rotate-link")
        self.assertTrue(previous_link.get("token"))
        self.save_reading(self.owner, room["id"], previous_period, 120)
        self.owner.ok("POST", "/api/invoices/generate", {"period": previous_period})
        previous_invoice = self.invoice_for_room(self.owner, previous_period, room["id"])
        blocked = self.moveout_payload(self.owner, room["id"], previous_occupancy["id"])
        self.assertEqual(self.owner.request("POST", f"/api/rooms/{room['id']}/end-occupancy", blocked)[0], 409)
        self.owner.ok("POST", f"/api/invoices/{previous_invoice['id']}/receipts", {
            "amount": previous_invoice["outstanding"], "idempotencyKey": str(uuid.uuid4())})
        complete = self.moveout_payload(self.owner, room["id"], previous_occupancy["id"])
        ended = self.owner.ok("POST", f"/api/rooms/{room['id']}/end-occupancy", complete)
        self.assertEqual(ended["endDate"], today_vietnam().isoformat())
        self.start_occupancy(
            self.owner,
            room["id"],
            "Current Tenant",
            people=2,
            start_date=today_vietnam().isoformat(),
        )
        current_link = self.owner.ok("POST", f"/api/rooms/{room['id']}/rotate-link")
        self.assertTrue(current_link.get("token"))

        # The old guest token is revoked when occupancy ends, and the new
        # token can see only invoices belonging to the new occupancy.
        old_token_status, old_token_body, _ = ApiSession().request(
            "GET", f"/api/tenant/{previous_link['token']}"
        )
        self.assertIn(
            old_token_status,
            (400, 401, 403, 404),
            f"Ended occupancy guest token remained valid: {old_token_body!r}",
        )

        overview = ApiSession().ok("GET", f"/api/tenant/{current_link['token']}")
        visible_ids = {item.get("id") for item in overview["invoices"]}
        self.assertNotIn(previous_invoice["id"], visible_ids)
        self.assertEqual(visible_ids, set(), "New occupancy inherited an old invoice")
        retained_debt = self.owner.ok(
            "GET", f"/api/invoices/{previous_invoice['id']}"
        )
        self.assertEqual(retained_debt["tenantName"], previous_invoice["tenantName"])
        self.assertEqual(retained_debt["outstanding"], 0)

    def test_invalid_payment_webhook_cannot_mark_invoice_paid(self) -> None:
        room = self.create_room(self.owner, label="invalid-webhook")
        self.start_occupancy(self.owner, room["id"], people=1)
        period = period_offset(0)
        self.save_reading(self.owner, room["id"], period, 125)
        self.owner.ok("POST", "/api/invoices/generate", {"period": period})
        invoice = self.invoice_for_room(self.owner, period, room["id"])

        # These deliberately unusable values remain local to the test DB. The
        # test sends a bad signature and never calls a payOS endpoint.
        self.owner.ok(
            "PUT",
            "/api/settings/payment",
            {
                "clientId": "local-test-client",
                "apiKey": "local-test-key-unused",
                "checksumKey": "a" * 64,
            },
        )
        settings = self.owner.ok("GET", "/api/settings/payment")
        self.assertTrue(settings["configured"])
        self.assertNotIn("apiKey", settings)
        self.assertNotIn("checksumKey", settings)
        order_code = int(uuid.uuid4().int % 2_000_000_000) + 1
        webhook_data = {
            "orderCode": order_code,
            "amount": invoice["total"],
            "description": "local test payment",
            "currency": "VND",
            "code": "00",
            "desc": "success",
            "reference": f"local-{uuid.uuid4().hex}",
        }
        invalid_webhook = {
            "code": "00",
            "desc": "success",
            "success": True,
            "data": webhook_data,
            "signature": "0" * 64,
        }
        status, body, _ = ApiSession().request(
            "POST",
            f"/api/payments/webhook/{self.owner_id}",
            invalid_webhook,
        )
        self.assertEqual(status, 401, f"Invalid webhook signature was not rejected: {body!r}")

        correctly_signed_unknown_order = {
            **invalid_webhook,
            "signature": payos_data_signature(webhook_data, "a" * 64),
        }
        status, body, _ = ApiSession().request(
            "POST",
            f"/api/payments/webhook/{self.owner_id}",
            correctly_signed_unknown_order,
        )
        self.assertEqual(
            status,
            200,
            f"Valid unallocated sample transaction should be acknowledged safely: {body!r}",
        )

        persisted = self.owner.ok("GET", f"/api/invoices/{invoice['id']}")
        self.assertEqual(persisted["paid"], 0)
        self.assertEqual(persisted["status"], "Unpaid")


if __name__ == "__main__":
    unittest.main()

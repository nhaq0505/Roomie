"""Billing lifecycle and concurrency integration checks against a disposable API."""

from __future__ import annotations

import calendar
import concurrent.futures
import math
import os
import sqlite3
import time
import unittest
import uuid
from contextlib import closing
from datetime import date, timedelta
from pathlib import Path
from urllib.parse import urlsplit

from test_api_integration import (
    ADMIN_EMAIL,
    ADMIN_PASSWORD,
    BASE_URL,
    OWNER_PASSWORD,
    ApiSession,
    as_list,
    first_day,
    period_offset,
    today_vietnam,
)


class BillingUpgradeIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        probe = ApiSession()
        try:
            status, body, _ = probe.request("GET", "/api/health")
        except OSError as error:
            raise unittest.SkipTest(f"Local API is not running at {BASE_URL}: {error}")
        if status != 200:
            raise unittest.SkipTest(f"Local API health check returned {status}: {body!r}")
        cls.admin = ApiSession()
        status, body, _ = cls.admin.request("POST", "/api/auth/login", {
            "email": ADMIN_EMAIL,
            "password": ADMIN_PASSWORD,
        })
        if status not in (200, 201):
            raise AssertionError(f"Could not log in with the Development admin: {status}: {body!r}")

    def setUp(self) -> None:
        suffix = uuid.uuid4().hex
        self.owner = ApiSession()
        self.user = self.owner.ok("POST", "/api/auth/register", {
            "name": f"Billing Owner {suffix[:8]}",
            "email": f"billing-{suffix}@example.invalid",
            "password": OWNER_PASSWORD,
            "phone": f"09{int(suffix[:8], 16) % 100_000_000:08d}",
        })
        self.admin.ok("POST", f"/api/admin/owners/{self.user['id']}/status", {
            "status": "Approved", "reason": "Disposable billing lifecycle test",
        })
        self.owner.ok("POST", "/api/auth/login", {"email": self.user["email"], "password": OWNER_PASSWORD})

    def create_room(self, *, rent: int = 3100, electricity: int = 100, water: int = 0,
                    service: int = 0, initial: int = 100) -> dict:
        suffix = uuid.uuid4().hex[:8]
        building = self.owner.ok("POST", "/api/buildings", {
            "name": f"Billing-{suffix}", "address": "Disposable billing fixture",
        })
        return self.owner.ok("POST", "/api/rooms", {
            "buildingId": building["id"], "name": f"Room-{suffix}", "rent": rent,
            "electricityRate": electricity, "waterFee": water,
            "waterPerPerson": False, "serviceFee": service, "initialReading": initial,
        })

    def start_occupancy(self, room_id: str, start_date: date | None = None, deposit: int = 0) -> dict:
        result = self.owner.ok("POST", f"/api/rooms/{room_id}/occupancy", {
            "tenantName": f"Billing Tenant {uuid.uuid4().hex[:6]}", "phone": "0900000000", "people": 1,
            "startDate": (start_date or date.fromisoformat(first_day(period_offset(-1)))).isoformat(),
            "depositAmount": deposit,
        })
        return result["occupancy"]

    def save_reading(self, room_id: str, period: str, value: int) -> dict:
        return self.owner.ok("POST", "/api/readings", {
            "roomId": room_id, "period": period, "currentReading": value,
        })

    def completion_payload(self, room_id: str, occupancy_id: str, refund: int = 0, request_id: str | None = None) -> dict:
        summary = self.owner.ok("GET", f"/api/rooms/{room_id}/moveout")
        deposit = summary["deposit"]
        return {
            "occupancyId": occupancy_id,
            "requestId": request_id or str(uuid.uuid4()),
            "refundAmount": refund,
            "expectedHeld": summary["heldDeposit"],
            "expectedDepositVersion": deposit["version"] if deposit is not None else None,
        }

    def test_midmonth_movein_gets_full_month_invoice_immediately(self) -> None:
        today = today_vietnam()
        room = self.create_room(rent=3100, electricity=100)
        occupancy = self.start_occupancy(room["id"], today)
        period = period_offset(0)
        preview = self.owner.ok("POST", "/api/readings/preview", {
            "roomId": room["id"], "period": period, "currentReading": 103,
        })
        saved = self.save_reading(room["id"], period, 103)
        invoice = saved["invoice"]
        self.assertEqual(invoice["lifecycleState"], "Issued")
        self.assertEqual(saved["lifecycleState"], "Issued")
        self.assertEqual(invoice["rent"], 3100)
        self.assertEqual(preview["suggestedRent"], 3100)
        self.assertEqual(preview["billableDays"], preview["monthDays"])
        self.assertEqual(invoice["version"], 1)
        tenant = self.owner.ok("GET", f"/api/tenant/{occupancy['token']}")
        self.assertIn(invoice["id"], {row["id"] for row in tenant["invoices"]})
        self.assertEqual(self.owner.request("POST", f"/api/invoices/{invoice['id']}/publish", {
            "requestId": str(uuid.uuid4()), "expectedVersion": invoice["version"],
            "rent": invoice["rent"], "electricityAmount": invoice["electricityAmount"],
            "waterAmount": invoice["waterAmount"], "serviceAmount": invoice["serviceAmount"],
        })[0], 409)

    def test_correction_is_idempotent_versioned_and_blocked_after_payment_or_later_meter(self) -> None:
        room = self.create_room(rent=1000, electricity=100)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period_offset(-2))))
        first = self.save_reading(room["id"], period_offset(-1), 105)["invoice"]
        second = self.save_reading(room["id"], period_offset(0), 110)["invoice"]
        self.assertEqual(first["lifecycleState"], "Issued")
        blocked = self.owner.request("POST", f"/api/invoices/{first['id']}/corrections", {
            "requestId": str(uuid.uuid4()), "expectedVersion": first["version"], "reason": "Late meter change",
            "currentReading": 106,
        })
        self.assertEqual(blocked[0], 409)

        receipt_key = str(uuid.uuid4())
        paid = self.owner.ok("POST", f"/api/invoices/{second['id']}/receipts", {
            "amount": second["outstanding"], "idempotencyKey": receipt_key, "expectedVersion": second["version"],
        })
        self.assertEqual(paid["version"], second["version"] + 1)
        blocked_paid = self.owner.request("POST", f"/api/invoices/{second['id']}/corrections", {
            "requestId": str(uuid.uuid4()), "expectedVersion": paid["version"], "reason": "Already paid",
            "description": "Changed after payment",
        })
        self.assertEqual(blocked_paid[0], 409)

        current = self.owner.ok("GET", f"/api/invoices/{first['id']}")
        correction = {
            "requestId": str(uuid.uuid4()), "expectedVersion": current["version"], "reason": "Due date adjustment",
            "dueDate": today_vietnam().isoformat(),
        }
        monthly = self.owner.ok("POST", f"/api/invoices/{first['id']}/corrections", correction)
        self.assertEqual(monthly["invoice"]["version"], current["version"] + 1)
        self.assertEqual(monthly["correction"]["reason"], "Due date adjustment")
        replay = self.owner.ok("POST", f"/api/invoices/{first['id']}/corrections", correction)
        self.assertEqual(replay["invoice"]["version"], monthly["invoice"]["version"])
        mismatch = self.owner.request("POST", f"/api/invoices/{first['id']}/corrections", {
            **correction, "dueDate": first_day(period_offset(0)),
        })
        self.assertEqual(mismatch[0], 409)

    def test_successful_latest_meter_correction_updates_reading_and_replays_exactly(self) -> None:
        period = period_offset(0)
        room = self.create_room(rent=1000, electricity=100)
        self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        invoice = self.save_reading(room["id"], period, 105)["invoice"]
        request = {
            "requestId": str(uuid.uuid4()), "expectedVersion": invoice["version"],
            "reason": "Owner reread the latest meter", "currentReading": 108,
        }
        corrected = self.owner.ok("POST", f"/api/invoices/{invoice['id']}/corrections", request)
        self.assertEqual(corrected["invoice"]["currentReading"], 108)
        self.assertEqual(corrected["invoice"]["consumption"], 8)
        self.assertEqual(corrected["invoice"]["electricityAmount"], 800)
        self.assertEqual(corrected["invoice"]["version"], invoice["version"] + 1)
        reading = next(row for row in as_list(self.owner.ok("GET", f"/api/readings?period={period}")) if row["roomId"] == room["id"])
        self.assertEqual(reading["currentReading"], 108)
        replay = self.owner.ok("POST", f"/api/invoices/{invoice['id']}/corrections", request)
        self.assertEqual(replay["invoice"]["version"], corrected["invoice"]["version"])
        mismatch = self.owner.request("POST", f"/api/invoices/{invoice['id']}/corrections", {
            **request, "currentReading": 109,
        })
        self.assertEqual(mismatch[0], 409)

        zero_room = self.create_room(rent=0, electricity=0, water=0, service=0)
        zero_occupancy = self.start_occupancy(zero_room["id"], date.fromisoformat(first_day(period)))
        zero_reading = self.save_reading(zero_room["id"], period, 105)
        completion = self.completion_payload(zero_room["id"], zero_occupancy["id"])
        ended = self.owner.ok("POST", f"/api/rooms/{zero_room['id']}/complete-moveout", completion)
        self.assertEqual(ended["endDate"], today_vietnam().isoformat())
        self.start_occupancy(zero_room["id"], today_vietnam())
        next_reading = self.save_reading(zero_room["id"], period, 106)
        self.assertEqual(next_reading["previousReading"], zero_reading["invoice"]["currentReading"])

    def test_concurrent_receipt_and_correction_have_one_versioned_winner(self) -> None:
        period = period_offset(0)
        room = self.create_room(rent=10_000, electricity=100)
        self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        invoice = self.save_reading(room["id"], period, 101)["invoice"]
        version = invoice["version"]
        receipt = {
            "amount": invoice["outstanding"], "expectedVersion": version, "idempotencyKey": str(uuid.uuid4()),
        }
        correction = {
            "requestId": str(uuid.uuid4()), "expectedVersion": version,
            "reason": "Concurrent owner update", "rent": invoice["rent"] + 1,
        }
        receipt_session = ApiSession()
        correction_session = ApiSession()
        for session in (receipt_session, correction_session):
            session.ok("POST", "/api/auth/login", {"email": self.user["email"], "password": OWNER_PASSWORD})

        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            receipt_future = pool.submit(receipt_session.request, "POST", f"/api/invoices/{invoice['id']}/receipts", receipt)
            correction_future = pool.submit(correction_session.request, "POST", f"/api/invoices/{invoice['id']}/corrections", correction)
            receipt_result = receipt_future.result(timeout=10)
            correction_result = correction_future.result(timeout=10)

        self.assertEqual(sorted((receipt_result[0], correction_result[0])), [200, 409],
                         f"Expected exactly one mutation and one conflict; got {receipt_result!r}, {correction_result!r}")
        final = self.owner.ok("GET", f"/api/invoices/{invoice['id']}")
        self.assertEqual(final["version"], version + 1)
        self.assertEqual(final["paid"] + final["outstanding"], final["total"])
        self.assertIn(final["paid"], (0, invoice["outstanding"]))
        history = self.owner.ok("GET", f"/api/invoices/{invoice['id']}/corrections")["items"]
        self.assertEqual(len(history) + int(final["paid"] > 0), 1)

    def test_manual_receipt_rejects_fractional_vnd_without_rounding(self) -> None:
        room = self.create_room(rent=1000, electricity=100)
        self.start_occupancy(room["id"], date.fromisoformat(first_day(period_offset(-1))))
        invoice = self.save_reading(room["id"], period_offset(0), 103)["invoice"]
        status, body, _ = self.owner.request("POST", f"/api/invoices/{invoice['id']}/receipts", {
            "amount": 1.5, "idempotencyKey": str(uuid.uuid4()),
        })
        self.assertEqual(status, 400, f"Fractional VND receipt was rounded instead of rejected: {body!r}")
        current = self.owner.ok("GET", f"/api/invoices/{invoice['id']}")
        self.assertEqual(current["paid"], 0)

    def test_proration_preview_and_final_invoice_routes_are_removed(self) -> None:
        period = period_offset(0)
        room = self.create_room(rent=1000, electricity=100)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        monthly = self.save_reading(room["id"], period, 103)["invoice"]
        path = f"/api/rooms/{room['id']}"
        self.assertEqual(self.owner.request("POST", path + "/moveout/preview", {
            "endDate": today_vietnam().isoformat(), "currentReading": 103,
        })[0], 404)
        self.assertEqual(self.owner.request("POST", path + "/final-invoice", {
            "requestId": str(uuid.uuid4()), "occupancyId": occupancy["id"],
            "currentReading": 103, "additionalAmount": 0,
        })[0], 404)
        after = self.owner.ok("GET", f"/api/invoices/{monthly['id']}")
        self.assertEqual((after["version"], after["total"], after["paid"]),
                         (monthly["version"], monthly["total"], monthly["paid"]))
        self.assertFalse(any(invoice["isFinalMeterReading"] for invoice in as_list(self.owner.ok("GET", "/api/invoices"))))

    def test_full_month_rent_is_independent_of_calendar_month_length(self) -> None:
        cases = ("2025-02", "2024-02", "2026-04", "2026-01")
        for period in cases:
            with self.subTest(period=period):
                room = self.create_room(rent=3100, electricity=0, water=0, service=0)
                self.start_occupancy(room["id"], date.fromisoformat(f"{period}-15"))
                invoice = self.save_reading(room["id"], period, 100)["invoice"]
                self.assertEqual(invoice["rent"], 3100)
                self.assertEqual(invoice["lifecycleState"], "Issued")

    def test_deposit_and_supplemental_corrections_use_amount_without_recomputing_lines(self) -> None:
        room = self.create_room(rent=0, electricity=0, water=0, service=0)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period_offset(0))), deposit=1000)
        deposit_id = occupancy["depositInvoiceId"]
        deposit = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        updated_deposit = self.owner.ok("POST", f"/api/invoices/{deposit_id}/corrections", {
            "requestId": str(uuid.uuid4()), "expectedVersion": deposit["version"],
            "reason": "Deposit amount was entered incorrectly", "amount": 1200,
        })["invoice"]
        self.assertEqual(updated_deposit["total"], 1200)
        self.assertEqual((updated_deposit["rent"], updated_deposit["electricityAmount"], updated_deposit["waterAmount"], updated_deposit["serviceAmount"]), (0, 0, 0, 0))

        supplemental = self.owner.ok("POST", f"/api/rooms/{room['id']}/supplemental-invoices", {
            "requestId": str(uuid.uuid4()), "occupancyId": occupancy["id"], "description": "Old description",
            "amount": 500, "dueDate": today_vietnam().isoformat(),
        })
        updated_supplemental = self.owner.ok("POST", f"/api/invoices/{supplemental['id']}/corrections", {
            "requestId": str(uuid.uuid4()), "expectedVersion": supplemental["version"],
            "reason": "Updated expense", "amount": 700, "description": "Repair lock and latch",
        })["invoice"]
        self.assertEqual(updated_supplemental["kind"], "Supplemental")
        self.assertEqual(updated_supplemental["total"], 700)
        self.assertEqual(updated_supplemental["description"], "Repair lock and latch")

    def test_moveout_keeps_full_month_invoice_unchanged_and_blocks_unpaid_debt(self) -> None:
        period = period_offset(0)
        room = self.create_room(rent=3100, electricity=100, water=300, service=0)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        monthly = self.save_reading(room["id"], period, 105)["invoice"]
        self.assertEqual(monthly["rent"], 3100)
        payload = self.completion_payload(room["id"], occupancy["id"])
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", payload)
        self.assertEqual(status, 409, body)
        self.assertIn("công nợ", body["message"])
        self.owner.ok("POST", f"/api/invoices/{monthly['id']}/receipts", {
            "amount": monthly["total"], "expectedVersion": monthly["version"],
            "idempotencyKey": str(uuid.uuid4()),
        })
        refreshed = self.completion_payload(room["id"], occupancy["id"])
        ended = self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout", refreshed)
        self.assertEqual(ended["refundAmount"], 0)
        self.assertEqual(ended["retainedAmount"], 0)
        after = self.owner.ok("GET", f"/api/invoices/{monthly['id']}")
        self.assertEqual((after["rent"], after["waterAmount"], after["total"], after["paid"]),
                         (monthly["rent"], monthly["waterAmount"], monthly["total"], monthly["total"]))
        self.assertFalse(any(invoice["isFinalMeterReading"] for invoice in as_list(self.owner.ok("GET", "/api/invoices"))))

    def test_moveout_manual_refund_is_audited_idempotent_and_scoped_to_occupancy(self) -> None:
        period = period_offset(0)
        room = self.create_room(rent=0, electricity=0, water=0, service=0)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period)), deposit=2000)
        deposit_id = occupancy["depositInvoiceId"]
        self.owner.ok("POST", f"/api/invoices/{deposit_id}/receipts", {
            "amount": 2000, "idempotencyKey": str(uuid.uuid4()),
        })
        stale_review = self.completion_payload(room["id"], occupancy["id"], refund=900)
        deposit = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        self.owner.ok("POST", f"/api/invoices/{deposit_id}/deposit-settlements", {
            "amount": 200, "kind": "Deduction", "reason": "Reviewed before closing",
            "idempotencyKey": str(uuid.uuid4()), "expectedVersion": deposit["version"],
        })
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", stale_review)
        self.assertEqual(status, 409, body)
        self.assertIn("đã thay đổi", body["message"])

        review = self.completion_payload(room["id"], occupancy["id"], refund=1000)
        too_much = {**review, "refundAmount": review["expectedHeld"] + 1}
        self.assertEqual(self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", too_much)[0], 400)
        ended = self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout", review)
        self.assertEqual(ended["refundAmount"], 1000)
        self.assertEqual(ended["refundedAmount"], 1000)
        self.assertEqual(ended["retainedAmount"], 800)
        self.assertEqual(ended["heldBeforeSettlement"], 1800)
        self.assertEqual(ended["actorId"], self.user["id"])
        self.assertTrue(ended["completedAt"])
        deposit_after = self.owner.ok("GET", f"/api/invoices/{deposit_id}")
        self.assertEqual(deposit_after["paid"], 2000)
        self.assertEqual(deposit_after["depositHeld"], 0)
        settlement_rows = deposit_after["depositSettlements"]
        self.assertEqual(sum(row["amount"] for row in settlement_rows if row["kind"] == "Refund"), 1000)
        self.assertEqual(sum(row["amount"] for row in settlement_rows if row["kind"] == "Deduction"), 1000)
        history = self.owner.ok("GET", f"/api/rooms/{room['id']}/moveout-completions")["items"]
        self.assertEqual(len(history), 1)
        self.assertEqual(history[0]["retainedAmount"], 800)
        replay = self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout", review)
        self.assertEqual(replay["id"], ended["id"])
        changed = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", {
            **review, "refundAmount": 999,
        })
        self.assertEqual(changed[0], 409)

        second = self.start_occupancy(room["id"], today_vietnam())
        replay_after_new_tenant = self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout", review)
        self.assertEqual(replay_after_new_tenant["occupancyId"], occupancy["id"])
        self.assertEqual(self.owner.ok("GET", f"/api/rooms/{room['id']}")["occupancy"]["id"], second["id"])

    def test_same_room_can_have_two_occupancy_readings_for_same_month(self) -> None:
        today = today_vietnam()
        period = period_offset(0)
        room = self.create_room(rent=3100, electricity=0, water=0, service=0)
        first = self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        first_reading = self.save_reading(room["id"], period, 100)
        self.owner.ok("POST", f"/api/invoices/{first_reading['invoice']['id']}/receipts", {
            "amount": first_reading["invoice"]["total"], "idempotencyKey": str(uuid.uuid4()),
        })
        self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout",
                      self.completion_payload(room["id"], first["id"]))
        second = self.start_occupancy(room["id"], today)
        second_reading = self.save_reading(room["id"], period, 101)
        self.assertNotEqual(first_reading["occupancyId"], second_reading["occupancyId"])
        self.assertEqual(second_reading["invoice"]["lifecycleState"], "Issued")
        self.assertEqual(second_reading["invoice"]["rent"], 3100)
        self.assertEqual(second_reading["previousReading"], 100)
        room_after_second_reading = self.owner.ok("GET", f"/api/rooms/{room['id']}")
        self.assertEqual(room_after_second_reading["previousReading"], 100)
        self.assertEqual(room_after_second_reading["occupancy"]["id"], second["id"])
        rows = as_list(self.owner.ok("GET", f"/api/readings?period={period}"))
        room_rows = [row for row in rows if row["roomId"] == room["id"]]
        self.assertEqual({row["occupancyId"] for row in room_rows}, {first["id"], second["id"]})

    def test_moveout_blocks_legacy_draft_and_pending_payos(self) -> None:
        configured_path = os.environ.get("TEST_SQLITE_DB")
        if not configured_path:
            self.skipTest("Set TEST_SQLITE_DB to the disposable SQLite API database for synthetic attempt coverage")
        parsed_api = urlsplit(BASE_URL)
        self.assertEqual(parsed_api.scheme, "http")
        self.assertIn(parsed_api.hostname, {"localhost", "127.0.0.1", "::1"})
        database_path = Path(configured_path).expanduser()
        self.assertTrue(database_path.is_absolute() and database_path.is_file() and not database_path.is_symlink())
        period = period_offset(0)
        room = self.create_room(rent=0, electricity=0, water=0, service=0)
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period)))
        invoice = self.save_reading(room["id"], period, 100)["invoice"]
        with closing(sqlite3.connect(database_path, timeout=10)) as db, db:
            db.execute("UPDATE Invoices SET LifecycleState = 'Draft' WHERE Id = ?", (invoice["id"].upper(),))
        completion = self.completion_payload(room["id"], occupancy["id"])
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", completion)
        self.assertEqual(status, 409, body)
        self.assertIn("nháp", body["message"])

        order_code = time.time_ns() // 1_000_000 + 300_000_000 + (uuid.uuid4().int % 1_000_000)
        owner_id = self.user["id"].upper()
        with closing(sqlite3.connect(database_path, timeout=10)) as db, db:
            db.execute("UPDATE Invoices SET LifecycleState = 'Issued' WHERE Id = ?", (invoice["id"].upper(),))
            session = db.execute("SELECT CreatedAt FROM AuthSessions WHERE UserId = ? ORDER BY CreatedAt DESC LIMIT 1", (owner_id,)).fetchone()
            self.assertIsNotNone(session)
            db.execute("""INSERT INTO PayOsAttempts
                (Id, OwnerId, InvoiceId, OrderCode, Amount, PaymentLinkId, CheckoutUrl,
                 Status, ProviderReference, CreatedAt, PaidAt, Kind)
                VALUES (?, ?, ?, ?, ?, NULL, NULL, 'Pending', NULL, ?, NULL, 'Invoice')""",
                (str(uuid.uuid4()).upper(), owner_id, invoice["id"].upper(), order_code, int(invoice["total"]), session[0]))
        self.addCleanup(self._fail_synthetic_attempt, database_path, order_code)
        status, body, _ = self.owner.request("POST", f"/api/rooms/{room['id']}/complete-moveout", completion)
        self.assertEqual(status, 409, body)
        self.assertIn("payOS", body["message"])
        summary = self.owner.ok("GET", f"/api/rooms/{room['id']}/moveout")
        self.assertIn(invoice["id"], summary["pendingInvoiceIds"])

    def test_pending_payos_blocks_correction_without_provider_calls(self) -> None:
        configured_path = os.environ.get("TEST_SQLITE_DB")
        if not configured_path:
            self.skipTest("Set TEST_SQLITE_DB to the disposable SQLite API database for synthetic pending-attempt coverage")
        parsed_api = urlsplit(BASE_URL)
        self.assertEqual(parsed_api.scheme, "http")
        self.assertIn(parsed_api.hostname, {"localhost", "127.0.0.1", "::1"})
        database_path = Path(configured_path).expanduser()
        self.assertTrue(database_path.is_absolute())
        self.assertTrue(database_path.is_file())
        self.assertFalse(database_path.is_symlink())

        room = self.create_room()
        occupancy = self.start_occupancy(room["id"], date.fromisoformat(first_day(period_offset(0))))
        invoice = self.save_reading(room["id"], period_offset(0), 101)["invoice"]
        owner_id = self.user["id"].upper()
        order_code = time.time_ns() // 1_000_000 + 200_000_000 + (uuid.uuid4().int % 1_000_000)
        attempt_id = str(uuid.uuid4()).upper()
        with closing(sqlite3.connect(database_path, timeout=10)) as db, db:
            session = db.execute("SELECT CreatedAt FROM AuthSessions WHERE UserId = ? ORDER BY CreatedAt DESC LIMIT 1", (owner_id,)).fetchone()
            self.assertIsNotNone(session, "Could not find the registered owner's API session")
            db.execute("""INSERT INTO PayOsAttempts
                (Id, OwnerId, InvoiceId, OrderCode, Amount, PaymentLinkId, CheckoutUrl,
                 Status, ProviderReference, CreatedAt, PaidAt, Kind)
                VALUES (?, ?, ?, ?, ?, NULL, NULL, 'Pending', NULL, ?, NULL, 'Invoice')""",
                (attempt_id, owner_id, invoice["id"].upper(), order_code, int(invoice["total"]), session[0]))
        self.addCleanup(self._fail_synthetic_attempt, database_path, order_code)
        response = self.owner.request("POST", f"/api/invoices/{invoice['id']}/corrections", {
            "requestId": str(uuid.uuid4()), "expectedVersion": invoice["version"],
            "reason": "Pending payment must be reconciled", "description": "Should not apply",
        })
        self.assertEqual(response[0], 409)
        self.assertIn("Reconcile", response[1]["message"])

    @staticmethod
    def _fail_synthetic_attempt(database_path: Path, order_code: int) -> None:
        with closing(sqlite3.connect(database_path, timeout=10)) as db, db:
            db.execute("UPDATE PayOsAttempts SET Status = 'Failed' WHERE OrderCode = ? AND Status = 'Pending'", (order_code,))

    def test_free_tier_enforces_five_rooms_quota(self) -> None:
        sub = self.owner.ok("GET", "/api/subscription")
        self.assertEqual(sub["tier"], "Free")
        self.assertFalse(sub["isPro"])
        self.assertEqual(sub["maxRooms"], 5)
        self.assertEqual(sub["roomCount"], 0)

        building = self.owner.ok("POST", "/api/buildings", {
            "name": f"Quota-Building-{uuid.uuid4().hex[:6]}", "address": "Quota address"
        })
        for i in range(5):
            room = self.owner.ok("POST", "/api/rooms", {
                "buildingId": building["id"], "name": f"Room-{i+1}", "rent": 1000,
                "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
                "serviceFee": 0, "initialReading": 0
            })
            self.assertTrue(room["id"])

        status, body, _ = self.owner.request("POST", "/api/rooms", {
            "buildingId": building["id"], "name": "Room-6-Forbidden", "rent": 1000,
            "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
            "serviceFee": 0, "initialReading": 0
        })
        self.assertEqual(status, 403)
        self.assertIn("Gói Miễn phí chỉ được tạo tối đa 5 phòng", body["message"])

    def test_admin_can_upgrade_and_downgrade_owner_subscription(self) -> None:
        building = self.owner.ok("POST", "/api/buildings", {
            "name": f"Sub-Building-{uuid.uuid4().hex[:6]}", "address": "Sub address"
        })
        for i in range(5):
            self.owner.ok("POST", "/api/rooms", {
                "buildingId": building["id"], "name": f"Sub-Room-{i+1}", "rent": 1000,
                "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
                "serviceFee": 0, "initialReading": 0
            })

        # Verify 6th room creation is blocked on Free
        self.assertEqual(self.owner.request("POST", "/api/rooms", {
            "buildingId": building["id"], "name": "Sub-Room-6", "rent": 1000,
            "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
            "serviceFee": 0, "initialReading": 0
        })[0], 403)

        # Admin upgrades owner to Pro with 30 days
        upgraded = self.admin.ok("POST", f"/api/admin/owners/{self.user['id']}/subscription", {
            "tier": "Pro", "days": 30, "reason": "Integration test upgrade"
        })
        self.assertEqual(upgraded["subscriptionTier"], "Pro")
        self.assertTrue(upgraded["isPro"])
        self.assertIsNotNone(upgraded["subscriptionExpiresAt"])

        # Owner creates 6th room on Pro tier -> succeeds
        sub = self.owner.ok("GET", "/api/subscription")
        self.assertEqual(sub["tier"], "Pro")
        self.assertTrue(sub["isPro"])
        room6 = self.owner.ok("POST", "/api/rooms", {
            "buildingId": building["id"], "name": "Sub-Room-6", "rent": 1000,
            "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
            "serviceFee": 0, "initialReading": 0
        })
        self.assertTrue(room6["id"])

        # Admin downgrades back to Free
        downgraded = self.admin.ok("POST", f"/api/admin/owners/{self.user['id']}/subscription", {
            "tier": "Free", "reason": "Integration test downgrade"
        })
        self.assertEqual(downgraded["subscriptionTier"], "Free")
        self.assertFalse(downgraded["isPro"])
        self.assertIsNone(downgraded["subscriptionExpiresAt"])

        # Owner cannot create 7th room because roomCount (6) >= maxRooms (5)
        status, body, _ = self.owner.request("POST", "/api/rooms", {
            "buildingId": building["id"], "name": "Sub-Room-7", "rent": 1000,
            "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
            "serviceFee": 0, "initialReading": 0
        })
        self.assertEqual(status, 403)
        self.assertIn("Gói Miễn phí chỉ được tạo tối đa 5 phòng", body["message"])

    def test_room_archive_and_restore_respects_free_tier_quota(self) -> None:
        building = self.owner.ok("POST", "/api/buildings", {
            "name": f"Restore-Building-{uuid.uuid4().hex[:6]}", "address": "Restore address"
        })
        rooms = [
            self.owner.ok("POST", "/api/rooms", {
                "buildingId": building["id"], "name": f"Room-Arch-{i+1}", "rent": 1000,
                "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
                "serviceFee": 0, "initialReading": 0
            })
            for i in range(5)
        ]

        # Archive one room -> active count drops to 4
        self.owner.ok("DELETE", f"/api/rooms/{rooms[4]['id']}")

        # Owner can create a replacement room to reach 5 again
        room_replacement = self.owner.ok("POST", "/api/rooms", {
            "buildingId": building["id"], "name": "Room-Replacement", "rent": 1000,
            "electricityRate": 3000, "waterFee": 0, "waterPerPerson": False,
            "serviceFee": 0, "initialReading": 0
        })
        self.assertTrue(room_replacement["id"])

        # Restoring the archived room while at 5 rooms fails with 403
        status, body, _ = self.owner.request("POST", f"/api/rooms/{rooms[4]['id']}/restore")
        self.assertEqual(status, 403)
        self.assertIn("khôi phục phòng", body["message"])

    def test_owner_subscription_endpoints_and_package_list(self) -> None:
        sub = self.owner.ok("GET", "/api/subscription")
        self.assertIn("availablePackages", sub)
        package_ids = [p["id"] for p in sub["availablePackages"]]
        self.assertIn("pro_1m", package_ids)
        self.assertIn("pro_3m", package_ids)
        self.assertIn("pro_1y", package_ids)

        # Calling upgrade endpoint returns either checkout or 502 gateway error if payos is unconfigured
        status, body, _ = self.owner.request("POST", "/api/subscription/upgrade", {"packageId": "pro_1m"})
        self.assertIn(status, (200, 502))

    def test_older_tenant_invoice_lookup_is_not_limited_to_first_page(self):
        room=self.create_room()
        occupancy=self.start_occupancy(room['id'])
        ids=[]
        for index in range(12):
            invoice=self.owner.ok('POST',f"/api/rooms/{room['id']}/supplemental-invoices",{
                'requestId':str(uuid.uuid4()),'occupancyId':occupancy['id'],'description':f'Pagination regression{index}',
                'amount':10,'dueDate':today_vietnam().isoformat()})
            ids.append(invoice['id'])
        token=occupancy['token']
        first=ApiSession().ok('GET',f'/api/tenant/{token}?page=1&pageSize=10')
        old=next(item for item in ids if item not in {x['id'] for x in first['invoices']})
        selected=ApiSession().ok('GET',f'/api/tenant/{token}?invoiceId={old}')
        self.assertEqual([x['id'] for x in selected['invoices']],[old])
        foreign=ApiSession().ok('GET',f'/api/tenant/{token}?invoiceId={uuid.uuid4()}')
        self.assertEqual(foreign['invoices'],[])
        owner_page=self.owner.ok('GET',f"/api/invoices?page=1&pageSize=100&occupancyId={occupancy['id']}")
        self.assertTrue(all(x['occupancyId']==occupancy['id'] for x in owner_page['items']))


if __name__ == "__main__":
    unittest.main()

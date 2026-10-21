"""Opt-in webhook replay tests using synthetic rows in a disposable PostgreSQL DB.

This module runs against the disposable PostgreSQL database used by the local API.
It never calls payOS.
"""

from __future__ import annotations

import os
import time
import unittest
import urllib.error
import uuid
from typing import Any
from urllib.parse import urlsplit

from test_api_integration import (
    ADMIN_EMAIL,
    ADMIN_PASSWORD,
    BASE_URL as API_BASE_URL,
    OWNER_PASSWORD,
    ApiSession,
    as_list,
    first_day,
    payos_data_signature,
    period_offset,
    today_vietnam,
)


class PayOsWebhookFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        parsed_api = urlsplit(API_BASE_URL)
        if parsed_api.scheme != "http" or parsed_api.hostname not in {"localhost", "127.0.0.1", "::1"}:
            raise AssertionError("The payment fixture is allowed to call only a localhost HTTP API")
        if parsed_api.path not in ("", "/"):
            raise AssertionError("API_BASE_URL must be a localhost origin without a path")

        try:
            status, body, _ = ApiSession().request("GET", "/api/health")
        except (OSError, urllib.error.URLError) as error:
            raise unittest.SkipTest(f"Local API is not running at {API_BASE_URL}: {error}")
        if status != 200:
            raise unittest.SkipTest(f"Local API health check returned {status}: {body!r}")

        from pg_fixture import get_pg_fixture
        cls.db = get_pg_fixture(API_BASE_URL)
        seeded_admin = cls.db.execute(
            'SELECT 1 FROM "Users" WHERE "Email" = ? AND "Role" = \'Admin\' AND "Status" = \'Approved\'',
            (ADMIN_EMAIL.lower(),),
        ).fetchone()
        if seeded_admin is None:
            raise AssertionError("Disposable PostgreSQL database is missing the expected seeded admin")

        cls.admin = ApiSession()
        status, body, _ = cls.admin.request(
            "POST", "/api/auth/login", {"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD}
        )
        if status not in (200, 201):
            raise AssertionError(f"Could not log in with the local Development admin: {status} {body!r}")

    def create_owner(self, label: str) -> tuple[ApiSession, dict[str, Any]]:
        suffix = uuid.uuid4().hex
        session = ApiSession()
        user = session.ok(
            "POST",
            "/api/auth/register",
            {
                "name": f"{label} {suffix[:8]}",
                "email": f"payos-fixture-{suffix}@example.invalid",
                "password": OWNER_PASSWORD,
                "phone": f"09{int(suffix[:8], 16) % 100_000_000:08d}",
            },
        )
        self.admin.ok(
            "POST",
            f"/api/admin/owners/{user['id']}/status",
            {"status": "Approved", "reason": "Local webhook fixture"},
        )
        session.ok("POST", "/api/auth/login", {"email": user["email"], "password": OWNER_PASSWORD})
        user_id = user["id"].upper()
        row = self.db.execute('SELECT "Email" FROM "Users" WHERE "Id" = ?', (user_id,)).fetchone()
        if row is None or row[0] != user["email"]:
            raise AssertionError(
                "Disposable PostgreSQL database is not behind API_BASE_URL; refusing to seed an unrelated database"
            )
        return session, user

    def create_invoice(self, owner: ApiSession, label: str, deposit_amount: int = 0) -> dict[str, Any]:
        suffix = uuid.uuid4().hex[:8]
        building = owner.ok(
            "POST", "/api/buildings", {"name": f"{label}-{suffix}", "address": "Local fixture"}
        )
        room = owner.ok(
            "POST",
            "/api/rooms",
            {
                "buildingId": building["id"],
                "name": f"Room-{suffix}",
                "rent": 1_000_000,
                "electricityRate": 1_000,
                "waterFee": 20_000,
                "waterPerPerson": False,
                "serviceFee": 50_000,
                "initialReading": 100,
            },
        )
        owner.ok(
            "POST",
            f"/api/rooms/{room['id']}/occupancy",
            {
                "tenantName": f"{label} Tenant-{suffix}",
                "depositAmount": deposit_amount,
                "phone": "0900000000",
                "people": 1,
                "startDate": first_day(period_offset(0)),
            },
        )
        if deposit_amount:
            deposits = as_list(owner.ok("GET", "/api/invoices"))
            return next(invoice for invoice in deposits if invoice["roomId"] == room["id"] and invoice["kind"] == "Deposit")
        period = period_offset(0)
        owner.ok(
            "POST",
            "/api/readings",
            {"roomId": room["id"], "period": period, "currentReading": 110},
        )
        invoices = as_list(owner.ok("GET", f"/api/invoices?period={period}"))
        matches = [invoice for invoice in invoices if invoice.get("roomId") == room["id"]]
        self.assertEqual(len(matches), 1, f"Expected one fixture invoice, got {matches!r}")
        return matches[0]

    def set_payment_key(self, owner: ApiSession, suffix: str, checksum_key: str) -> None:
        owner.ok(
            "PUT",
            "/api/settings/payment",
            {
                "clientId": f"local-fixture-{suffix}",
                "apiKey": f"unused-local-fixture-key-{suffix}",
                "checksumKey": checksum_key,
            },
        )

    def insert_attempt(
        self,
        owner_id: str,
        invoice_id: str,
        amount: int,
        *,
        status: str = "Pending",
    ) -> tuple[int, str]:
        order_code = time.time_ns() // 1_000_000 + 100_000_000 + (uuid.uuid4().int % 1_000_000)
        attempt_id = str(uuid.uuid4()).upper()
        owner_id = owner_id.upper()
        invoice_id = invoice_id.upper()
        session_created = self.db.execute(
            'SELECT "CreatedAt" FROM "AuthSessions" WHERE "UserId" = ? ORDER BY "CreatedAt" DESC LIMIT 1',
            (owner_id,),
        ).fetchone()
        if session_created is None:
            raise AssertionError("Could not find the API-created local owner session for the fixture")
        self.db.execute(
            """INSERT INTO "PayOsAttempts"
               ("Id", "OwnerId", "InvoiceId", "OrderCode", "Amount", "PaymentLinkId", "CheckoutUrl",
                "Status", "ProviderReference", "CreatedAt", "PaidAt", "Kind")
               VALUES (?, ?, ?, ?, ?, NULL, NULL, ?, NULL, ?, NULL, 'Invoice')""",
            (attempt_id, owner_id, invoice_id, order_code, amount, status, session_created[0]),
        )
        return order_code, attempt_id

    def mark_pending_failed(self, order_codes: list[int]) -> None:
        if not order_codes:
            return
        placeholders = ",".join("?" for _ in order_codes)
        self.db.execute(
            f'UPDATE "PayOsAttempts" SET "Status" = \'Failed\' WHERE "Status" = \'Pending\' AND "OrderCode" IN ({placeholders})',
            order_codes,
        )

    @staticmethod
    def webhook_data(order_code: int, amount: int, reference: str) -> dict[str, Any]:
        return {
            "orderCode": order_code,
            "amount": amount,
            "description": "local fixture payment",
            "currency": "VND",
            "code": "00",
            "desc": "success",
            "reference": reference,
        }

    def post_webhook(
        self,
        owner_id: str,
        checksum_key: str,
        data: dict[str, Any],
    ) -> tuple[int, Any]:
        payload = {
            "code": "00",
            "desc": "success",
            "success": True,
            "data": data,
            "signature": payos_data_signature(data, checksum_key),
        }
        status, body, _ = ApiSession().request(
            "POST", f"/api/payments/webhook/{owner_id}", payload
        )
        return status, body

    def test_webhook_replay_wrong_amount_and_owner_scope(self) -> None:
        owner_a, user_a = self.create_owner("Webhook Owner A")
        owner_b, user_b = self.create_owner("Webhook Owner B")
        invoice_a = self.create_invoice(owner_a, "Webhook-A")
        amount = int(invoice_a["total"])
        key_a = "a" * 64
        key_b = "b" * 64
        self.set_payment_key(owner_a, "a", key_a)
        self.set_payment_key(owner_b, "b", key_b)

        synthetic_orders: list[int] = []
        self.addCleanup(self.mark_pending_failed, synthetic_orders)

        wrong_order, _ = self.insert_attempt(user_a["id"], invoice_a["id"], amount)
        synthetic_orders.append(wrong_order)
        wrong_data = self.webhook_data(wrong_order, amount - 1, f"wrong-{uuid.uuid4().hex}")
        wrong_status, wrong_body = self.post_webhook(user_a["id"], key_a, wrong_data)
        self.assertEqual(wrong_status, 409, f"Mismatched payment amount was not rejected: {wrong_body!r}")
        self.assertEqual(self.owner_invoice(owner_a, invoice_a["id"])["paid"], 0)
        self.db.execute('UPDATE "PayOsAttempts" SET "Status" = \'Failed\' WHERE "OrderCode" = ?', (wrong_order,))

        valid_order, _ = self.insert_attempt(user_a["id"], invoice_a["id"], amount)
        synthetic_orders.append(valid_order)

        cross_owner_data = self.webhook_data(valid_order, amount, f"cross-owner-{uuid.uuid4().hex}")
        cross_status, cross_body = self.post_webhook(user_b["id"], key_b, cross_owner_data)
        self.assertEqual(cross_status, 200, f"Unknown owner-scoped order should be acknowledged safely: {cross_body!r}")
        self.assertEqual(self.owner_invoice(owner_a, invoice_a["id"])["paid"], 0)

        reference = f"paid-{uuid.uuid4().hex}"
        paid_data = self.webhook_data(valid_order, amount, reference)
        paid_status, paid_body = self.post_webhook(user_a["id"], key_a, paid_data)
        self.assertEqual(paid_status, 200, f"Correct signed payment webhook failed: {paid_body!r}")
        replay_status, replay_body = self.post_webhook(user_a["id"], key_a, paid_data)
        self.assertEqual(replay_status, 200, f"Duplicate signed webhook was not acknowledged: {replay_body!r}")

        paid_invoice = self.owner_invoice(owner_a, invoice_a["id"])
        self.assertEqual(paid_invoice["paid"], amount)
        self.assertEqual(paid_invoice["outstanding"], 0)
        self.assertEqual(paid_invoice["status"], "Paid")

        owner_db_id = user_a["id"].upper()
        invoice_db_id = invoice_a["id"].upper()
        receipt_count = int(self.db.execute(
            """SELECT COUNT(*) FROM "Receipts"
               WHERE "OwnerId" = ? AND "InvoiceId" = ? AND "Source" = 'payOS' AND "ExternalReference" = ?""",
            (owner_db_id, invoice_db_id, reference),
        ).fetchone()[0])
        webhook_count = int(self.db.execute(
            'SELECT COUNT(*) FROM "ProcessedWebhooks" WHERE "OwnerId" = ? AND "Reference" = ? AND "OrderCode" = ?',
            (owner_db_id, reference, valid_order),
        ).fetchone()[0])
        attempt = tuple(self.db.execute(
            'SELECT "Status", "ProviderReference" FROM "PayOsAttempts" WHERE "OrderCode" = ?',
            (valid_order,),
        ).fetchone())
        self.assertEqual(receipt_count, 1, "Replay created an extra payment receipt")
        self.assertEqual(webhook_count, 1, "Replay created duplicate processed-webhook records")
        self.assertEqual(attempt, ("Paid", reference))

    def test_deposit_webhook_receipt_and_replay(self) -> None:
        owner, user = self.create_owner("Deposit Webhook QA")
        invoice = self.create_invoice(owner, "Deposit webhook", deposit_amount=750_000)
        key = "d" * 64
        self.set_payment_key(owner, "deposit", key)
        order, _ = self.insert_attempt(user["id"], invoice["id"], 750_000)
        self.addCleanup(self.mark_pending_failed, [order])
        data = self.webhook_data(order, 750_000, f"deposit-{uuid.uuid4().hex}")
        for _ in range(2):
            status, body = self.post_webhook(user["id"], key, data)
            self.assertEqual(status, 200, body)
        result = self.owner_invoice(owner, invoice["id"])
        self.assertEqual(result["kind"], "Deposit")
        self.assertEqual(result["paid"], 750_000)
        self.assertEqual(result["depositHeld"], 750_000)
        self.assertEqual(result["status"], "Paid")
        count = int(self.db.execute('SELECT COUNT(*) FROM "Receipts" WHERE "InvoiceId" = ? AND "Source" = \'payOS\'', (invoice["id"].upper(),)).fetchone()[0])
        self.assertEqual(count, 1)

    def test_supplemental_signed_webhook_replay(self) -> None:
        owner, user = self.create_owner("Supplemental Webhook QA")
        monthly = self.create_invoice(owner, "Supplemental webhook")
        invoice = owner.ok("POST", f"/api/rooms/{monthly['roomId']}/supplemental-invoices", {"requestId": str(uuid.uuid4()), "occupancyId": monthly["occupancyId"], "description": "Additional repair fee", "amount": 25000, "dueDate": today_vietnam().isoformat()})
        key = "s" * 64
        self.set_payment_key(owner, "supplemental", key)
        order, _ = self.insert_attempt(user["id"], invoice["id"], 25000)
        self.addCleanup(self.mark_pending_failed, [order])
        data = self.webhook_data(order, 25000, f"supplemental-{uuid.uuid4().hex}")
        for _ in range(2):
            self.assertEqual(self.post_webhook(user["id"], key, data)[0], 200)
        result = self.owner_invoice(owner, invoice["id"])
        self.assertEqual(result["paid"], 25000)
        self.assertEqual(result["kind"], "Supplemental")
        self.assertEqual(self.owner_invoice(owner, monthly["id"])["paid"], 0)
        self.assertEqual(int(self.db.execute('SELECT COUNT(*) FROM "Receipts" WHERE "InvoiceId" = ?', (invoice["id"].upper(),)).fetchone()[0]), 1)

    def test_moveout_pending_payment_blocks_completion(self) -> None:
        owner, user = self.create_owner("Pending moveout")
        monthly = self.create_invoice(owner, "Pending moveout")
        path = f"/api/rooms/{monthly['roomId']}"
        order, _ = self.insert_attempt(user["id"], monthly["id"], int(monthly["total"]))
        self.addCleanup(self.mark_pending_failed, [order])
        summary = owner.ok("GET", path+"/moveout")
        self.assertIn(monthly["id"], summary["pendingInvoiceIds"])
        status, body, _ = owner.request("POST", path+"/complete-moveout", {
            "occupancyId": monthly["occupancyId"], "requestId": str(uuid.uuid4()),
            "refundAmount": 0, "expectedHeld": summary["heldDeposit"],
            "expectedDepositVersion": summary["deposit"]["version"] if summary["deposit"] else None,
        })
        self.assertEqual(status, 409)
        self.assertIn("payOS", body["message"])
        summary = owner.ok("GET", path+"/moveout")
        self.assertIn(monthly["id"], summary["pendingInvoiceIds"])

    @staticmethod
    def owner_invoice(owner: ApiSession, invoice_id: str) -> dict[str, Any]:
        return owner.ok("GET", f"/api/invoices/{invoice_id}")


if __name__ == "__main__":
    unittest.main()

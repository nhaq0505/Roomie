"""Disposable-API integration coverage for reports and reversible archives."""

from __future__ import annotations

import unittest
import uuid
from io import BytesIO
from datetime import timedelta
from decimal import Decimal
from typing import Any
from xml.etree import ElementTree as ET
from zipfile import ZipFile

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


class ReportsArchiveIntegrationTests(unittest.TestCase):
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
            raise AssertionError(f"Could not log in with the Development demo admin: {status}: {body!r}")

    def setUp(self) -> None:
        self.owner, self.owner_user = self.create_owner()
        self.approve(self.owner_user)
        self.owner.ok("POST", "/api/auth/login", {
            "email": self.owner_user["email"],
            "password": OWNER_PASSWORD,
        })

    def create_owner(self) -> tuple[ApiSession, dict[str, Any]]:
        suffix = uuid.uuid4().hex
        session = ApiSession()
        user = session.ok("POST", "/api/auth/register", {
            "name": f"Reports Owner {suffix[:8]}",
            "email": f"reports-{suffix}@example.invalid",
            "password": OWNER_PASSWORD,
            "phone": f"09{int(suffix[:8], 16) % 100_000_000:08d}",
        })
        return session, user

    def approve(self, user: dict[str, Any]) -> None:
        result = self.admin.ok("POST", f"/api/admin/owners/{user['id']}/status", {
            "status": "Approved",
            "reason": "Reports/archive disposable integration test",
        })
        self.assertEqual(result.get("status"), "Approved")

    def create_building(self, label: str) -> dict[str, Any]:
        return self.owner.ok("POST", "/api/buildings", {
            "name": f"{label}-{uuid.uuid4().hex[:8]}",
            "address": "Reports archive fixture",
        })

    def create_room(self, building_id: str, *, label: str = "Room", rent: int = 1000) -> dict[str, Any]:
        return self.owner.ok("POST", "/api/rooms", {
            "buildingId": building_id,
            "name": f"{label}-{uuid.uuid4().hex[:8]}",
            "rent": rent,
            "electricityRate": 0,
            "waterFee": 0,
            "waterPerPerson": False,
            "serviceFee": 0,
            "initialReading": 100,
        })

    def start_occupancy(self, room_id: str, *, deposit: int = 0, start_date: str | None = None) -> dict[str, Any]:
        return self.owner.ok("POST", f"/api/rooms/{room_id}/occupancy", {
            "tenantName": f"Tenant-{uuid.uuid4().hex[:8]}",
            "people": 1,
            "startDate": start_date or first_day(period_offset(0)),
            "depositAmount": deposit,
        })["occupancy"]

    def issue_monthly_invoice(self, room_id: str, *, period: str | None = None) -> dict[str, Any]:
        reading = self.owner.ok("POST", "/api/readings", {
            "roomId": room_id,
            "period": period or period_offset(0),
            "currentReading": 100,
        })
        invoice = reading.get("invoice")
        self.assertIsInstance(invoice, dict, f"Reading did not return its invoice: {reading!r}")
        self.assertEqual(invoice.get("lifecycleState"), "Issued")
        return invoice

    def test_report_totals_exclude_deposit_credit_and_preserve_tenant_scope(self) -> None:
        building = self.create_building("report-cash")
        room = self.create_room(building["id"], rent=1000)
        occupancy = self.start_occupancy(room["id"], deposit=1000)
        monthly = self.issue_monthly_invoice(room["id"])

        self.owner.ok("POST", f"/api/invoices/{occupancy['depositInvoiceId']}/receipts", {
            "amount": 1000,
            "idempotencyKey": str(uuid.uuid4()),
        })
        # Each cash confirmation settles its entire invoice; separate charges preserve report pagination.
        for amount, note in [(400, "=1+1"), (50, "Full manual receipt")]:
            charge = self.owner.ok("POST", f"/api/rooms/{room['id']}/supplemental-invoices", {
                "requestId": str(uuid.uuid4()), "occupancyId": occupancy['id'],
                "description": "Report fixture charge", "amount": amount, "dueDate": today_vietnam().isoformat(),
            })
            self.owner.ok("POST", f"/api/invoices/{charge['id']}/receipts", {
                "amount": amount, "note": note, "idempotencyKey": str(uuid.uuid4()),
            })
        self.owner.ok("POST", f"/api/invoices/{occupancy['depositInvoiceId']}/deposit-settlements", {
            "amount": 200,
            "kind": "Deduction",
            "reason": "Apply part of deposit to current rent",
            "idempotencyKey": str(uuid.uuid4()),
            "targetInvoiceId": monthly["id"],
        })

        today = today_vietnam().isoformat()
        filters = f"dateFrom={today}&dateTo={today}&buildingId={building['id']}&roomId={room['id']}"
        receipts = self.owner.ok("GET", f"/api/reports/receipts?{filters}")
        self.assertEqual(receipts["totalCount"], 3, "Internal Source=Deposit credit appeared as cash receipt")
        self.assertEqual(receipts["totals"], {"invoiceCash": 450, "depositCash": 1000, "totalCash": 1450})
        self.assertEqual({row["source"] for row in receipts["items"]}, {"Manual"})
        paged_ids = []
        for page in range(1, 4):
            paged = self.owner.ok("GET", f"/api/reports/receipts?{filters}&page={page}&pageSize=1")
            self.assertEqual(paged["totalCount"], 3)
            self.assertEqual(len(paged["items"]), 1)
            self.assertEqual(paged["totals"], receipts["totals"], "Page totals were calculated from only the current page")
            paged_ids.append(paged["items"][0]["receiptId"])
        self.assertEqual(len(set(paged_ids)), 3, "Stable report paging duplicated a receipt")

        debts = self.owner.ok("GET", f"/api/reports/debt?dateFrom={today}&dateTo={today}&roomId={room['id']}")
        self.assertEqual(debts["totals"]["currentOutstanding"], 800)
        self.assertEqual([row["invoiceKind"] for row in debts["items"]], ["Monthly"])

        deposits = self.owner.ok("GET", f"/api/reports/deposits?{filters}")
        self.assertEqual(deposits["totals"], {
            "periodReceived": 1000,
            "periodRefunded": 0,
            "periodDeducted": 200,
            "currentHeld": 800,
        })
        self.assertEqual(deposits["items"][0]["currentHeld"], 800)

        tomorrow = (today_vietnam() + timedelta(days=1)).isoformat()
        outside = self.owner.ok("GET", f"/api/reports/receipts?dateFrom={tomorrow}&dateTo={tomorrow}&roomId={room['id']}")
        self.assertEqual(outside["totalCount"], 0, "Vietnam-local date range included tomorrow's receipts")

        export_status, export_body, export_headers = self.owner.request("GET", f"/api/reports/receipts/export.xlsx?{filters}")
        self.assertEqual(export_status, 200, export_body)
        self.assertTrue(export_headers.get("Content-Type", "").startswith(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"))
        self.assertIsInstance(export_body, bytes)
        self.assertTrue(export_body.startswith(b"PK"), "Excel response did not contain an XLSX ZIP payload")
        with ZipFile(BytesIO(export_body)) as workbook:
            namespace = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
            summary = ET.fromstring(workbook.read("xl/worksheets/sheet1.xml"))
            details = ET.fromstring(workbook.read("xl/worksheets/sheet2.xml"))
            summary_amount = summary.find(".//s:c[@r='B7']/s:v", namespace)
            receipt_date_cell = details.find(".//s:c[@r='A2']", namespace)
            receipt_date = receipt_date_cell.find("s:v", namespace) if receipt_date_cell is not None else None
            receipt_amount = details.find(".//s:c[@r='B2']/s:v", namespace)
            self.assertEqual(Decimal(summary_amount.text) if summary_amount is not None else None, Decimal("450"))
            self.assertTrue(receipt_date is not None and float(receipt_date.text) > 0,
                            "Receipt date was not exported as a typed spreadsheet date")
            self.assertTrue(receipt_date_cell is not None and receipt_date_cell.get("s") is not None,
                            "Receipt date did not have a spreadsheet date format")
            self.assertEqual(Decimal(receipt_amount.text) if receipt_amount is not None else None,
                             Decimal(str(receipts["items"][0]["amount"])))
            shared_strings: list[str] = []
            if "xl/sharedStrings.xml" in workbook.namelist():
                shared_strings_doc = ET.fromstring(workbook.read("xl/sharedStrings.xml"))
                shared_strings = ["".join(node.text or "" for node in item.findall(".//s:t", namespace))
                                  for item in shared_strings_doc.findall("s:si", namespace)]
            injection_cell = None
            injected_text = None
            for cell in details.findall(".//s:c", namespace):
                if cell.get("t") == "s":
                    string_index = cell.find("s:v", namespace)
                    cell_text = shared_strings[int(string_index.text)] if string_index is not None else None
                elif cell.get("t") == "inlineStr":
                    cell_text = "".join(node.text or "" for node in cell.findall(".//s:t", namespace))
                else:
                    continue
                if cell_text in ("'=1+1", "=1+1"):
                    injection_cell, injected_text = cell, cell_text
                    break
            self.assertIsNotNone(injection_cell, "Formula-like note was missing from the details sheet")
            self.assertIsNone(injection_cell.find("s:f", namespace), "Formula-like user text became an Excel formula")
            self.assertIn(injection_cell.get("t"), ("s", "inlineStr"), "Formula-like user text was not stored as text")
            self.assertIn(injected_text, ("'=1+1", "=1+1"), "Formula-like user text was unexpectedly changed")
            if injected_text == "=1+1":
                styles = ET.fromstring(workbook.read("xl/styles.xml"))
                style_index = int(injection_cell.get("s", "0"))
                cell_formats = styles.find("s:cellXfs", namespace)
                self.assertIsNotNone(cell_formats)
                self.assertEqual(list(cell_formats)[style_index].get("quotePrefix"), "1",
                                 "Unprefixed formula-like text must use Excel's quotePrefix style")

        for report_type, summary_total, detail_total in (
            ("debt", Decimal("800"), Decimal("800")),
            ("deposits", Decimal("1000"), Decimal("800")),
        ):
            status, body, _ = self.owner.request("GET", f"/api/reports/{report_type}/export.xlsx?{filters}")
            self.assertEqual(status, 200, body)
            with ZipFile(BytesIO(body)) as workbook:
                summary = ET.fromstring(workbook.read("xl/worksheets/sheet1.xml"))
                details = ET.fromstring(workbook.read("xl/worksheets/sheet2.xml"))
                total = summary.find(".//s:c[@r='B7']/s:v", namespace)
                detail_total_cell = details.find(".//s:c[@r='G2']/s:v" if report_type == "debt"
                                                else ".//s:c[@r='F2']/s:v", namespace)
                period_cell = details.find(".//s:c[@r='A2']", namespace)
                self.assertEqual(Decimal(total.text) if total is not None else None, summary_total)
                self.assertEqual(Decimal(detail_total_cell.text) if detail_total_cell is not None else None,
                                 detail_total)
                period_value = period_cell.find("s:v", namespace) if period_cell is not None else None
                self.assertTrue(period_value is not None and float(period_value.text) > 0,
                                f"{report_type} period was not exported as a typed spreadsheet date")
                self.assertIsNotNone(period_cell.get("s"), f"{report_type} period lacks a date format")

        other, other_user = self.create_owner()
        self.approve(other_user)
        other.ok("POST", "/api/auth/login", {"email": other_user["email"], "password": OWNER_PASSWORD})
        isolated = other.ok("GET", f"/api/reports/receipts?{filters}")
        self.assertEqual(isolated["totalCount"], 0)
        self.assertEqual(isolated["totals"]["totalCash"], 0)

    def test_debt_report_includes_unpaid_deposit_and_filters_by_invoice_period(self) -> None:
        building = self.create_building("report-debt")
        room = self.create_room(building["id"], rent=1000)
        old_period = period_offset(-2)
        occupancy = self.start_occupancy(room["id"], deposit=1000, start_date=first_day(old_period))
        self.issue_monthly_invoice(room["id"], period=old_period)

        debts = self.owner.ok("GET", f"/api/reports/debt?buildingId={building['id']}&roomId={room['id']}")
        self.assertEqual(debts["totals"]["currentOutstanding"], 2000)
        self.assertEqual(debts["totals"]["overdueOutstanding"], 1000)
        self.assertEqual(debts["totals"]["invoiceCount"], 2)
        self.assertEqual({row["invoiceKind"] for row in debts["items"]}, {"Monthly", "Deposit"})
        self.assertIn(occupancy["depositInvoiceId"], {row["invoiceId"] for row in debts["items"]})

        period_only = self.owner.ok("GET", f"/api/reports/debt?dateFrom={old_period}-15&dateTo={old_period}-16&roomId={room['id']}")
        self.assertEqual(period_only["totals"]["currentOutstanding"], 1000)
        self.assertEqual(period_only["totals"]["overdueOutstanding"], 1000)
        self.assertEqual([row["period"] for row in period_only["items"]], [old_period])

    def test_building_restore_only_reactivates_its_archive_batch(self) -> None:
        building = self.create_building("archive-batch")
        individually_archived = self.create_room(building["id"], label="Individual")
        building_batch_room = self.create_room(building["id"], label="BuildingBatch")

        room_archive = self.owner.ok("DELETE", f"/api/rooms/{individually_archived['id']}")
        building_archive = self.owner.ok("DELETE", f"/api/buildings/{building['id']}")
        self.assertNotEqual(room_archive["archiveBatchId"], building_archive["archiveBatchId"])
        self.assertEqual(self.owner.request("POST", f"/api/rooms/{building_batch_room['id']}/restore")[0], 409)
        self.assertEqual(self.owner.request("POST", "/api/rooms", {
            "buildingId": building["id"],
            "name": "ShouldNotBeCreated",
            "rent": 1000,
            "electricityRate": 0,
            "waterFee": 0,
            "waterPerPerson": False,
            "serviceFee": 0,
            "initialReading": 100,
        })[0], 409)

        self.assertEqual(as_list(self.owner.ok("GET", "/api/buildings")), [])
        self.assertEqual(as_list(self.owner.ok("GET", "/api/rooms")), [])
        restored = self.owner.ok("POST", f"/api/buildings/{building['id']}/restore")
        self.assertEqual(restored["restoredRoomIds"], [building_batch_room["id"]])

        archived_rows = as_list(self.owner.ok("GET", "/api/rooms?includeArchived=true"))
        archived_by_id = {row["id"]: row for row in archived_rows}
        self.assertTrue(archived_by_id[individually_archived["id"]]["isArchived"])
        self.assertFalse(archived_by_id[building_batch_room["id"]]["isArchived"])
        self.assertEqual(as_list(self.owner.ok("GET", "/api/rooms"))[0]["id"], building_batch_room["id"])

        self.owner.ok("POST", f"/api/rooms/{individually_archived['id']}/restore")
        self.assertEqual(len(as_list(self.owner.ok("GET", "/api/rooms"))), 2)

    def test_building_archive_is_atomic_when_a_child_has_an_active_tenant(self) -> None:
        building = self.create_building("archive-block")
        available = self.create_room(building["id"], label="Available")
        occupied = self.create_room(building["id"], label="Occupied")
        self.start_occupancy(occupied["id"])

        status, body, _ = self.owner.request("DELETE", f"/api/buildings/{building['id']}")
        self.assertEqual(status, 409, body)
        self.assertIn(occupied["name"], body["message"])
        rooms = {row["id"]: row for row in as_list(self.owner.ok("GET", "/api/rooms"))}
        self.assertIn(available["id"], rooms)
        self.assertIn(occupied["id"], rooms)
        self.assertEqual(self.owner.request("DELETE", f"/api/rooms/{occupied['id']}")[0], 409)

    def test_archive_operations_are_owner_scoped(self) -> None:
        building = self.create_building("archive-owner")
        room = self.create_room(building["id"])
        other, other_user = self.create_owner()
        self.approve(other_user)
        other.ok("POST", "/api/auth/login", {"email": other_user["email"], "password": OWNER_PASSWORD})

        self.assertEqual(other.request("DELETE", f"/api/rooms/{room['id']}")[0], 404)
        self.assertEqual(other.request("DELETE", f"/api/buildings/{building['id']}")[0], 404)
        active_rooms = as_list(self.owner.ok("GET", "/api/rooms"))
        self.assertIn(room["id"], {row["id"] for row in active_rooms})

    def test_archived_room_keeps_receipt_history_by_snapshot_ids(self) -> None:
        building = self.create_building("archive-history")
        room = self.create_room(building["id"], label="History", rent=1000)
        self.start_occupancy(room["id"])
        monthly = self.issue_monthly_invoice(room["id"])
        self.owner.ok("POST", f"/api/invoices/{monthly['id']}/receipts", {
            "amount": monthly["total"],
            "idempotencyKey": str(uuid.uuid4()),
        })
        moveout = self.owner.ok("GET", f"/api/rooms/{room['id']}/moveout")
        completion = self.owner.ok("POST", f"/api/rooms/{room['id']}/complete-moveout", {
            "occupancyId": monthly["occupancyId"],
            "requestId": str(uuid.uuid4()),
            "refundAmount": 0,
            "expectedHeld": moveout["heldDeposit"],
            "expectedDepositVersion": moveout["deposit"]["version"] if moveout["deposit"] else None,
        })
        self.assertEqual(completion["retainedAmount"], 0)
        renamed_room = f"Renamed-{uuid.uuid4().hex[:8]}"
        self.owner.ok("PATCH", f"/api/rooms/{room['id']}", {
            "name": renamed_room,
            "rent": room["rent"],
            "electricityRate": room["electricityRate"],
            "waterFee": room["waterFee"],
            "waterPerPerson": room["waterPerPerson"],
            "serviceFee": room["serviceFee"],
        })
        self.owner.ok("DELETE", f"/api/rooms/{room['id']}")
        history = self.owner.ok("GET", f"/api/rooms/{room['id']}/moveout-completions")
        self.assertEqual(history["items"][0]["requestId"], completion["requestId"])

        today = today_vietnam().isoformat()
        report = self.owner.ok("GET", f"/api/reports/receipts?dateFrom={today}&dateTo={today}&roomId={room['id']}&buildingId={building['id']}")
        self.assertEqual(report["totalCount"], 1)
        self.assertEqual(report["items"][0]["buildingId"], building["id"])
        self.assertEqual(report["items"][0]["roomId"], room["id"])
        self.assertEqual(report["items"][0]["roomName"], room["name"])
        self.assertNotEqual(report["items"][0]["roomName"], renamed_room)
        self.assertEqual(report["items"][0]["amount"], monthly["total"])


if __name__ == "__main__":
    unittest.main()

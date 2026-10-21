"""Occupancy, credential recovery and monthly KPIs on a disposable local API."""
import os
import concurrent.futures
import unittest
import uuid
from pathlib import Path
from datetime import date
from urllib.parse import urlsplit
import test_billing_upgrade as billing
from test_api_integration import BASE_URL, ApiSession, OWNER_PASSWORD, ADMIN_PASSWORD, ADMIN_EMAIL, first_day, period_offset


class AccountOccupancyDashboardTests(unittest.TestCase):
    setUpClass = classmethod(billing.BillingUpgradeIntegrationTests.setUpClass.__func__)
    setUp = billing.BillingUpgradeIntegrationTests.setUp
    def create_room(self, **kwargs):
        room = billing.BillingUpgradeIntegrationTests.create_room(self, **kwargs)
        return self.owner.ok('PATCH', f"/api/rooms/{room['id']}", {
            'name': room['name'], 'rent': room['rent'], 'electricityRate': room['electricityRate'],
            'waterFee': room['waterFee'], 'waterPerPerson': True, 'serviceFee': room['serviceFee']})
    start_occupancy = billing.BillingUpgradeIntegrationTests.start_occupancy
    save_reading = billing.BillingUpgradeIntegrationTests.save_reading
    new_password = '123456789'

    def edit(self, room, occupancy, **changes):
        return self.owner.request('PATCH', f"/api/rooms/{room['id']}/occupancy", {
            'occupancyId': occupancy['id'], 'tenantName': 'Updated tenant', 'phone': '0912345678',
            'people': 3, **changes})

    def fixture_db(self):
        from pg_fixture import get_pg_fixture
        db = get_pg_fixture(BASE_URL)
        self.assertIsNotNone(db.execute('SELECT "Id" FROM "Users" WHERE "Email"=?', (self.user['email'],)).fetchone(),
            'Fixture database must belong to the same disposable API')
        return db

    def test_edit_preserves_issued_unpaid_paid_and_uses_new_count(self):
        room = self.create_room(water=80)
        occupancy = self.start_occupancy(room['id'], date.fromisoformat(first_day(period_offset(-3))))
        invoices = [self.save_reading(room['id'], period_offset(offset), 110 + n * 10)['invoice']
                    for n, offset in enumerate([-3, -2, -1])]
        for invoice, amount in zip(invoices[1:], [invoices[1]['total'], invoices[2]['total']]):
            self.owner.ok('POST', f"/api/invoices/{invoice['id']}/receipts", {
                'amount': amount, 'note': 'Synthetic history guard', 'idempotencyKey': uuid.uuid4().hex})
        before = [self.owner.ok('GET', f"/api/invoices/{x['id']}") for x in invoices]
        status, updated, _ = self.edit(room, occupancy)
        self.assertEqual(status, 200, updated)
        self.assertEqual(updated['occupancy']['people'], 3)
        self.assertEqual(updated['occupancy']['phone'], '0912345678')
        for old in before:
            self.assertEqual(old, self.owner.ok('GET', f"/api/invoices/{old['id']}"))
        preview = self.owner.ok('POST', '/api/readings/preview', {'roomId': room['id'], 'period': period_offset(0), 'currentReading': 150})
        self.assertEqual(preview['suggestedWater'], 240)
        current = self.save_reading(room['id'], period_offset(0), 150)['invoice']
        self.assertEqual(current['waterAmount'], 240)
        self.assertEqual(current['tenantName'], 'Updated tenant')
        self.assertEqual(self.owner.ok('GET', f"/api/tenant/{occupancy['token']}")['occupancy']['people'], 3)

    def test_edit_validation_scope_stale_identity_and_fixed_water(self):
        room = self.create_room(water=80)
        occupancy = self.start_occupancy(room['id'])
        for changes in [{'people': 0}, {'people': 31}, {'tenantName': ' '}, {'tenantName': 'x' * 161}, {'phone': 'x' * 61}]:
            self.assertEqual(self.edit(room, occupancy, **changes)[0], 400)
        self.assertEqual(self.edit(room, occupancy, occupancyId=str(uuid.uuid4()))[0], 409)
        self.assertEqual(ApiSession().request('PATCH', f"/api/rooms/{room['id']}/occupancy", {})[0], 401)
        other = ApiSession()
        other.ok('POST', '/api/auth/login', {'email': 'owner@phongtro.local', 'password': 'Owner123!'})
        self.assertEqual(other.request('PATCH', f"/api/rooms/{room['id']}/occupancy", {'occupancyId': occupancy['id'], 'tenantName': 'Other', 'people': 2})[0], 404)
        self.owner.ok('PATCH', f"/api/rooms/{room['id']}", {'name': room['name'], 'rent': room['rent'], 'electricityRate': room['electricityRate'], 'waterFee': 80, 'waterPerPerson': False, 'serviceFee': 0})
        self.assertEqual(self.edit(room, occupancy, phone='')[0], 200)
        self.assertEqual(self.save_reading(room['id'], period_offset(0), 110)['invoice']['waterAmount'], 80)

    def test_draft_recalculation_audit_and_stale_publish(self):
        room = self.create_room(water=80)
        occupancy = self.start_occupancy(room['id'])
        invoice = self.save_reading(room['id'], period_offset(0), 110)['invoice']
        db = self.fixture_db()
        db.execute('UPDATE "Invoices" SET "LifecycleState"=? WHERE "Id"=?', ('Draft', invoice['id'].upper()))
        db.commit()
        self.assertEqual(self.edit(room, occupancy)[0], 200)
        draft = self.owner.ok('GET', f"/api/invoices/{invoice['id']}")
        self.assertEqual(draft['waterAmount'], 240)
        self.assertEqual(draft['version'], invoice['version'] + 1)
        history = self.owner.ok('GET', f"/api/invoices/{invoice['id']}/corrections")
        self.assertEqual(len(history['items']), 1)
        self.assertEqual(self.owner.request('POST', f"/api/invoices/{invoice['id']}/publish", {
            'requestId': str(uuid.uuid4()), 'expectedVersion': invoice['version'], 'rent': draft['rent'],
            'electricityAmount': draft['electricityAmount'], 'waterAmount': draft['waterAmount'], 'serviceAmount': draft['serviceAmount']})[0], 409)

    def test_pending_draft_blocks_edit_and_saved_reading_count_survives_sweep(self):
        room = self.create_room(water=80)
        occupancy = self.start_occupancy(room['id'])
        invoice = self.save_reading(room['id'], period_offset(0), 110)['invoice']
        db = self.fixture_db()
        db.execute('UPDATE "Invoices" SET "LifecycleState"=\'Draft\' WHERE "Id"=?', (invoice['id'].upper(),))
        recorded = db.execute('SELECT "CreatedAt" FROM "AuthSessions" WHERE "UserId"=? LIMIT 1', (self.user['id'].upper(),)).fetchone()[0]
        order = uuid.uuid4().int % 10**12
        db.execute('INSERT INTO "PayOsAttempts" ("Id","OwnerId","InvoiceId","OrderCode","Amount","Status","CreatedAt","Kind") VALUES (?,?,?,?,?,\'Pending\',?,\'Invoice\')',
            (str(uuid.uuid4()).upper(), self.user['id'].upper(), invoice['id'].upper(), order, invoice['total'], recorded)); db.commit()
        try:
            self.assertEqual(self.edit(room, occupancy)[0], 409)
            self.assertEqual(self.owner.ok('GET', f"/api/rooms/{room['id']}")['occupancy']['people'], 1)
        finally:
            db.execute('UPDATE "PayOsAttempts" SET "Status"=\'Failed\' WHERE "OrderCode"=?', (order,)); db.commit()
        # Simulate a missing invoice after a reading was finalized; recovery uses the saved count.
        db.execute('DELETE FROM "PayOsAttempts" WHERE "OrderCode"=?', (order,))
        db.execute('DELETE FROM "Invoices" WHERE "Id"=?', (invoice['id'].upper(),)); db.commit()
        self.assertEqual(self.edit(room, occupancy)[0], 200)
        self.owner.ok('POST', '/api/invoices/generate', {'period': period_offset(0)})
        invoices = self.owner.ok('GET', f"/api/invoices?period={period_offset(0)}")
        self.assertEqual(invoices[0]['waterAmount'], 80)

    def test_paid_draft_blocks_edit_atomically(self):
        room = self.create_room(water=80)
        occupancy = self.start_occupancy(room['id'])
        invoice = self.save_reading(room['id'], period_offset(0), 110)['invoice']
        self.owner.ok('POST', f"/api/invoices/{invoice['id']}/receipts", {'amount': invoice['outstanding'], 'note': '', 'idempotencyKey': uuid.uuid4().hex})
        db = self.fixture_db()
        db.execute('UPDATE "Invoices" SET "LifecycleState"=? WHERE "Id"=?', ('Draft', invoice['id'].upper()))
        db.commit()
        self.assertEqual(self.edit(room, occupancy)[0], 409)
        self.assertEqual(self.owner.ok('GET', f"/api/rooms/{room['id']}")['occupancy']['people'], 1)
        self.assertEqual(self.owner.ok('GET', f"/api/invoices/{invoice['id']}")['waterAmount'], 80)

    def test_password_change_validation_and_revocation(self):
        body = {'currentPassword': OWNER_PASSWORD, 'newPassword': self.new_password, 'confirmPassword': self.new_password}
        for changes in [{'currentPassword': 'wrong'}, {'newPassword': 'weak', 'confirmPassword': 'weak'}, {'confirmPassword': 'Mismatch123!'}]:
            self.assertEqual(self.owner.request('POST', '/api/auth/change-password', {**body, **changes})[0], 400)
        second = ApiSession()
        second.ok('POST', '/api/auth/login', {'email': self.user['email'], 'password': OWNER_PASSWORD})
        self.owner.ok('POST', '/api/auth/change-password', body)
        self.assertEqual(second.request('GET', '/api/auth/me')[0], 401)
        self.assertEqual(self.owner.request('GET', '/api/auth/me')[0], 200)
        self.assertEqual(ApiSession().request('POST', '/api/auth/login', {'email': self.user['email'], 'password': OWNER_PASSWORD})[0], 401)
        self.owner.ok('POST', '/api/auth/login', {'email': self.user['email'], 'password': self.new_password})

    def issue_reset(self):
        return self.admin.ok('POST', f"/api/admin/owners/{self.user['id']}/reset-password", {'currentPassword': ADMIN_PASSWORD})

    def reset(self, token):
        return ApiSession().request('POST', '/api/auth/reset-password', {'email': self.user['email'], 'token': token,
            'newPassword': self.new_password, 'confirmPassword': self.new_password})

    def test_admin_reset_reauth_single_use_replacement_and_no_plaintext_storage(self):
        route = f"/api/admin/owners/{self.user['id']}/reset-password"
        self.assertEqual(self.owner.request('POST', route, {'currentPassword': OWNER_PASSWORD})[0], 403)
        self.assertEqual(self.admin.request('POST', route, {'currentPassword': 'wrong'})[0], 400)
        self.assertEqual(self.admin.request('POST', f'/api/admin/owners/{self.admin.ok("GET", "/api/auth/me")["id"]}/reset-password', {'currentPassword': ADMIN_PASSWORD})[0], 404)
        first = self.issue_reset()
        self.assertEqual(self.owner.request('GET', '/api/auth/me')[0], 401)
        self.assertEqual(ApiSession().request('POST', '/api/auth/login', {'email': self.user['email'], 'password': OWNER_PASSWORD})[0], 403)
        second = self.issue_reset()
        self.assertEqual(self.reset(first['token'])[0], 400)
        self.assertEqual(self.reset('0' * 64)[0], 400)
        db = self.fixture_db()
        stored = db.execute('SELECT "PasswordResetHash","PasswordHash" FROM "Users" WHERE "Email"=?', (self.user['email'],)).fetchone()
        self.assertNotEqual(stored[0], second['token'])
        self.assertNotIn(OWNER_PASSWORD, stored[1])
        self.assertEqual(self.reset(second['token'])[0], 200)
        self.assertEqual(self.reset(second['token'])[0], 400)
        self.owner.ok('POST', '/api/auth/login', {'email': self.user['email'], 'password': self.new_password})

    def test_same_reset_token_concurrently_is_consumed_once(self):
        token = self.issue_reset()['token']
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: self.reset(token)[0], range(2)))
        self.assertEqual(results.count(200), 1)
        self.assertTrue(all(code in [200, 400, 409] for code in results), results)

    def test_admin_can_change_own_password(self):
        db = self.fixture_db()
        db.execute('UPDATE "Users" SET "Role"=\'Admin\' WHERE "Email"=?', (self.user['email'],)); db.commit()
        admin = ApiSession()
        admin.ok('POST', '/api/auth/login', {'email': self.user['email'], 'password': OWNER_PASSWORD})
        admin.ok('POST', '/api/auth/change-password', {'currentPassword': OWNER_PASSWORD,
            'newPassword': self.new_password, 'confirmPassword': self.new_password})
        self.assertEqual(admin.request('GET', '/api/auth/me')[0], 200)
        admin.ok('POST', '/api/auth/login', {'email': self.user['email'], 'password': self.new_password})
        self.assertEqual(admin.ok('GET', '/api/auth/me')['role'], 'Admin')

    def test_expired_token_cannot_reset(self):
        token = self.issue_reset()['token']
        db = self.fixture_db()
        db.execute('UPDATE "Users" SET "PasswordResetExpiresAt"=\'1970-01-01T00:00:00Z\' WHERE "Email"=?', (self.user['email'],)); db.commit()
        self.assertEqual(self.reset(token)[0], 400)

    def test_monthly_kpis_preserve_all_time_exclude_deposit_and_draft(self):
        room = self.create_room(rent=1000, water=80)
        occupancy = self.start_occupancy(room['id'], date.fromisoformat(first_day(period_offset(-2))), deposit=500)
        prior = self.save_reading(room['id'], period_offset(-1), 110)['invoice']
        current = self.save_reading(room['id'], period_offset(0), 120)['invoice']
        for invoice, amount in [(prior, prior['total']), (current, current['total'])]:
            self.owner.ok('POST', f"/api/invoices/{invoice['id']}/receipts", {'amount': amount, 'note': '', 'idempotencyKey': uuid.uuid4().hex})
        extra = self.owner.ok('POST', f"/api/rooms/{room['id']}/supplemental-invoices", {'requestId': str(uuid.uuid4()), 'occupancyId': occupancy['id'], 'description': 'Synthetic extra', 'amount': 50, 'dueDate': first_day(period_offset(1))})
        summary = self.owner.ok('GET', '/api/dashboard')
        self.assertEqual(summary['period'], period_offset(0))
        self.assertEqual(summary['monthlyBilled'], current['total'] + extra['total'])
        self.assertEqual(summary['monthlyPaid'], current['total'])
        self.assertEqual(summary['monthlyOutstanding'], summary['monthlyBilled'] - current['total'])
        self.assertAlmostEqual(summary['collectionRate'], round(current['total'] / summary['monthlyBilled'] * 100, 2))
        self.assertEqual(summary['totalBilled'], summary['monthlyBilled'] + prior['total'])
        empty = self.create_room(rent=1000)
        self.start_occupancy(empty['id'])
        draft = self.save_reading(empty['id'], period_offset(0), 110)['invoice']
        db = self.fixture_db(); db.execute('UPDATE "Invoices" SET "LifecycleState"=? WHERE "Id"=?', ('Draft', draft['id'].upper())); db.commit()
        self.assertEqual(self.owner.ok('GET', '/api/dashboard')['monthlyBilled'], summary['monthlyBilled'])

    def test_empty_dashboard_has_zero_collection_rate(self):
        summary = self.owner.ok('GET', '/api/dashboard')
        self.assertEqual([summary[k] for k in ['monthlyBilled', 'monthlyPaid', 'monthlyOutstanding', 'collectionRate']], [0, 0, 0, 0])

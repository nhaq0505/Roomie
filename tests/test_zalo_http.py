"""Zalo API authorization/settings contract on a disposable API with the gateway disabled."""
import unittest
import uuid
from test_api_integration import ApiSession, ADMIN_EMAIL, ADMIN_PASSWORD, OWNER_PASSWORD

BANK = {'scheduleEnabled': False, 'dayOfMonth': 31, 'hour': 8, 'minute': 30,
        'bankBin': '970436', 'bankAccount': '123456789', 'bankAccountName': 'FIXTURE OWNER'}

class ZaloHttpTests(unittest.TestCase):
    def setUp(self):
        self.admin = ApiSession()
        self.admin.ok('POST', '/api/auth/login', {'email': ADMIN_EMAIL, 'password': ADMIN_PASSWORD})
        self.owner, self.user = self.create_owner()

    def create_owner(self, approve=True):
        session = ApiSession()
        user = session.ok('POST', '/api/auth/register', {'name': 'Zalo fixture', 'email': f'zalo-{uuid.uuid4().hex}@example.invalid', 'password': OWNER_PASSWORD, 'phone': ''})
        self.admin.ok('POST', f"/api/admin/owners/{user['id']}/status", {'status': 'Approved' if approve else 'Pending', 'reason': 'Disposable Zalo test'})
        session.ok('POST', '/api/auth/login', {'email': user['email'], 'password': OWNER_PASSWORD})
        return session, user

    def test_owner_only_and_same_origin(self):
        for path in ('settings', 'connection', 'history'):
            self.assertEqual(ApiSession().request('GET', '/api/zalo/' + path)[0], 401)
            self.assertEqual(self.admin.request('GET', '/api/zalo/' + path)[0], 403)
        self.assertEqual(self.owner.request('PUT', '/api/zalo/settings', BANK, origin='https://evil.invalid')[0], 403)
        pending, _ = self.create_owner(approve=False)
        self.assertEqual(pending.request('GET', '/api/zalo/settings')[0], 403)
        self.admin.ok('POST', f"/api/admin/owners/{self.user['id']}/status", {'status': 'Suspended', 'reason': 'Disposable test'})
        self.assertEqual(self.owner.request('GET', '/api/zalo/connection')[0], 401)

    def test_settings_persist_without_cross_owner_leak(self):
        self.owner.ok('PUT', '/api/zalo/settings', BANK)
        settings = self.owner.ok('GET', '/api/zalo/settings')
        for key in ('scheduleEnabled', 'dayOfMonth', 'hour', 'minute'): self.assertEqual(settings[key], BANK[key])
        self.assertFalse(settings['gatewayEnabled'])
        self.assertNotIn('gatewayToken', settings)
        other, _ = self.create_owner()
        self.assertEqual(other.ok('GET', '/api/zalo/settings')['bankAccount'], '')
        self.assertEqual(other.ok('GET', '/api/zalo/history')['items'], [])
        self.assertEqual(self.owner.ok('GET', '/api/zalo/history?page=2')['page'], 2)

    def test_schedule_validation_without_bank_requirement(self):
        self.owner.ok('PUT', '/api/zalo/settings', {'scheduleEnabled': False, 'dayOfMonth': 1, 'hour': 8, 'minute': 0})
        for patch in ({'dayOfMonth': 0}, {'dayOfMonth': 32}, {'hour': 6}, {'hour': 21}, {'minute': 60}):
            self.assertEqual(self.owner.request('PUT', '/api/zalo/settings', {**BANK, **patch})[0], 400)
        self.assertEqual(self.owner.request('PUT', '/api/zalo/settings', {**BANK, 'hour': 20, 'minute': 59})[0], 204)

    def test_unavailable_gateway_never_returns_fake_qr_or_queues_work(self):
        self.assertEqual(self.owner.ok('GET', '/api/zalo/connection'), {'status': 'Unavailable', 'qrImage': None})
        self.assertEqual(self.owner.request('POST', '/api/zalo/connect')[0], 503)
        self.assertEqual(self.owner.request('POST', '/api/zalo/queue')[0], 409)
        self.owner.ok('PUT', '/api/zalo/settings', BANK)
        self.assertEqual(self.owner.request('POST', '/api/zalo/queue')[0], 409)
        self.assertEqual(self.owner.ok('GET', '/api/zalo/history')['total'], 0)

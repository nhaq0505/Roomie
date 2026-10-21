"""Disposable-only regression for pagination, password-only login and credential policy."""
import unittest
import uuid
from test_api_integration import ApiSession, ADMIN_EMAIL, ADMIN_PASSWORD, OWNER_PASSWORD

class OperationalHardeningTests(unittest.TestCase):
    def setUp(self):
        self.admin=ApiSession(); self.admin.ok('POST','/api/auth/login',{'email':ADMIN_EMAIL,'password':ADMIN_PASSWORD})
    def test_admin_pagination_and_validation(self):
        for i in range(3):
            ApiSession().ok('POST','/api/auth/register',{'email':f'page-{uuid.uuid4().hex}@example.invalid','name':'Pagination fixture','password':OWNER_PASSWORD,'phone':''})
        first=self.admin.ok('GET','/api/admin/owners?page=1&pageSize=1')
        second=self.admin.ok('GET','/api/admin/owners?page=2&pageSize=1')
        self.assertEqual(len(first['items']),1); self.assertNotEqual(first['items'][0]['id'],second['items'][0]['id'])
        self.assertGreaterEqual(first['total'],3)
        self.assertEqual(self.admin.request('GET','/api/admin/owners?page=0')[0],400)
        self.assertEqual(self.admin.request('GET','/api/admin/owners?pageSize=101')[0],400)
    def test_register_password_length_policy(self):
        for password in ['abcdefgh', ' ' * 9, 'x' * 257]:
            status,_,_=ApiSession().request('POST','/api/auth/register',{'email':f'weak-{uuid.uuid4().hex}@example.invalid','name':'Weak fixture','password':password,'phone':''})
            self.assertEqual(status,400)
        for password in ('abcdefghi', '123456789'):
            email = f'simple-{uuid.uuid4().hex}@example.invalid'
            client = ApiSession()
            client.ok('POST', '/api/auth/register', {'email': email, 'name': 'Password fixture', 'password': password, 'phone': ''})
            ApiSession().ok('POST', '/api/auth/login', {'email': email, 'password': password})
    def test_password_only_login_with_legacy_mfa_state(self):
        # Only the disposable PostgreSQL admin row is changed, then restored exactly.
        from pg_fixture import get_pg_fixture
        from test_api_integration import BASE_URL
        db = get_pg_fixture(BASE_URL)
        previous = db.query_row('SELECT "MfaEnabled", "ProtectedMfaSecret" FROM "Users" WHERE "Email" = ?', (ADMIN_EMAIL,))
        self.assertIsNotNone(previous, "Disposable PostgreSQL missing admin user")
        prev_enabled = (previous[0] == 't')
        prev_secret = previous[1] if len(previous) > 1 and previous[1] != 'NULL' else None
        try:
            for enabled in (False, True):
                db.execute('UPDATE "Users" SET "MfaEnabled" = ?, "ProtectedMfaSecret" = ? WHERE "Email" = ?', (enabled, 'unused-legacy-secret', ADMIN_EMAIL))
                for extra in ({}, {'code': 'invalid-legacy-code'}):
                    client = ApiSession()
                    user = client.ok('POST', '/api/auth/login', {'email': ADMIN_EMAIL, 'password': ADMIN_PASSWORD, **extra})
                    self.assertFalse(user['mfaEnabled']); self.assertFalse(user['mfaRequired'])
                    me = client.ok('GET', '/api/auth/me')
                    self.assertFalse(me['mfaEnabled']); self.assertFalse(me['mfaRequired'])
                    client.ok('GET', '/api/admin/owners?page=1&pageSize=1')
                    client.ok('POST', '/api/auth/logout', {})
                    self.assertEqual(client.request('GET', '/api/auth/me')[0], 401)
                self.assertEqual(ApiSession().request('POST', '/api/auth/login', {'email': ADMIN_EMAIL, 'password': 'Incorrect123!', 'code': '123456'})[0], 401)
            for endpoint in ('setup', 'confirm'):
                self.assertEqual(self.admin.request('POST', '/api/auth/mfa/' + endpoint, {'currentPassword': ADMIN_PASSWORD, 'code': '123456'})[0], 404)
        finally:
            db.execute('UPDATE "Users" SET "MfaEnabled" = ?, "ProtectedMfaSecret" = ? WHERE "Email" = ?', (prev_enabled, prev_secret, ADMIN_EMAIL))

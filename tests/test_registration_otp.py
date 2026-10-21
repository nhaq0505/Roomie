"""Opt-in OTP HTTP tests; requires the loopback sink and a disposable PostgreSQL API."""
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
import json
import os
from pathlib import Path
import unittest
from urllib.parse import urlsplit
import uuid
from test_api_integration import ApiSession, BASE_URL


class RegistrationOtpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.environ.get('TEST_OTP_OUTBOX'):
            raise unittest.SkipTest('Requires opt-in disposable OTP SMTP sink')
        cls.outbox = Path(os.environ['TEST_OTP_OUTBOX']).resolve()
        tmp = str(Path('/tmp').resolve()) + os.sep
        if not str(cls.outbox).startswith(tmp):
            raise AssertionError('OTP fixtures must use disposable /tmp directories')
        if urlsplit(BASE_URL).hostname not in {'localhost', '127.0.0.1', '::1'} or urlsplit(BASE_URL).scheme != 'http':
            raise AssertionError('OTP fixtures require HTTP localhost API')
        if not cls.outbox.is_dir():
            raise AssertionError('Start disposable SMTP sink and API first')
        from pg_fixture import get_pg_fixture
        cls.db = get_pg_fixture(BASE_URL)

    def setUp(self):
        self.client = ApiSession()
        self.email = 'otp-' + uuid.uuid4().hex + '@example.invalid'
        self.body = {'name': 'Synthetic OTP owner', 'email': self.email, 'phone': '0900000000', 'password': 'abcdefghi'}

    def register(self, body=None):
        status, result, _ = self.client.request('POST', '/api/auth/register', body or self.body)
        self.assertEqual(status, 202, result)
        self.assertTrue(result['verificationRequired'])
        self.assertNotIn('code', result)
        return result

    def code(self):
        messages = [json.loads(p.read_text()) for p in sorted(self.outbox.glob('*.json'), key=lambda p: p.stat().st_mtime_ns)]
        return [m['code'] for m in messages if m['email'] == self.email][-1]

    def verify(self, challenge, code=None):
        return self.client.request('POST', '/api/auth/verify-registration', {'challengeToken': challenge['challengeToken'], 'code': self.code() if code is None else code})

    def change(self, **values):
        allowed = {'LastSentAt', 'CodeExpiresAt', 'ExpiresAt'}
        self.assertTrue(set(values).issubset(allowed))
        pg_vals = [('1970-01-01T00:00:00Z' if v == 0 else v) for v in values.values()]
        set_clause = ', '.join(f'"{k}" = ?' for k in values)
        self.db.execute(f'UPDATE "RegistrationVerifications" SET {set_clause} WHERE "Email" = ?', [*pg_vals, self.email])

    def test_only_correct_otp_creates_login_capable_owner(self):
        challenge = self.register()
        self.assertEqual(self.client.request('POST', '/api/auth/login', {'email': self.email, 'password': self.body['password']})[0], 401)
        pending = self.db.execute('SELECT "CodeHash","ChallengeHash","PasswordHash" FROM "RegistrationVerifications" WHERE "Email"=?', (self.email,)).fetchone()
        self.assertNotEqual(pending[0], self.code())
        self.assertNotEqual(pending[1], challenge['challengeToken'])
        self.assertNotEqual(pending[2], self.body['password'])
        self.assertEqual(self.verify(challenge)[0], 201)
        self.assertEqual(self.client.request('GET', '/api/auth/me')[0], 401)
        user = self.client.ok('POST', '/api/auth/login', {'email': self.email, 'password': self.body['password']})
        self.assertEqual(user['status'], 'Approved')
        self.assertEqual(user['subscriptionTier'], 'Free')
        self.assertEqual(self.verify(challenge)[0], 400)
        self.assertEqual(self.client.request('POST', '/api/auth/register', self.body)[0], 409)

    def test_wrong_codes_lock_then_resend_invalidates_old_code(self):
        challenge = self.register(); original = self.code()
        for _ in range(5):
            self.assertEqual(self.verify(challenge, 'not-otp')[0], 400)
        self.assertEqual(self.verify(challenge, original)[0], 400)
        self.assertEqual(self.client.request('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})[0], 429)
        self.change(LastSentAt=0)
        self.client.ok('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})
        self.assertNotEqual(self.code(), original)
        self.assertEqual(self.verify(challenge, original)[0], 400)
        self.assertEqual(self.verify(challenge)[0], 201)

    def test_expiry_and_challenge_binding(self):
        challenge = self.register()
        self.assertEqual(self.verify({'challengeToken': '0' * 64})[0], 400)
        self.change(CodeExpiresAt=0)
        self.assertEqual(self.verify(challenge)[0], 400)
        self.change(LastSentAt=0)
        self.client.ok('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})
        self.change(ExpiresAt=0)
        self.assertEqual(self.verify(challenge)[0], 400)
        self.assertEqual(self.client.request('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})[0], 400)

    def test_cooldown_and_per_email_hourly_send_cap(self):
        challenge = self.register()
        self.assertEqual(self.client.request('POST', '/api/auth/register', self.body)[0], 429)
        for _ in range(4):
            self.change(LastSentAt=0)
            self.client.ok('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})
        # Exercise the real tracker on its separate DB connection: successful
        # resends must return within the HTTP timeout and persist each fact.
        count = int(self.db.execute('''SELECT count(*) FROM "RegistrationFacts"
            WHERE "Stage"='CodeResent' AND "RegistrationAttemptId" IN
            (SELECT "UserId" FROM "RegistrationVerifications" WHERE "Email"=?)''', (self.email,)).fetchone()[0])
        self.assertEqual(count, 4)
        self.change(LastSentAt=0)
        self.assertEqual(self.client.request('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})[0], 429)
        self.assertEqual(self.client.request('POST', '/api/auth/register', self.body)[0], 429)

    def test_restarting_registration_replaces_old_credentials_and_challenge(self):
        old = self.register()
        self.change(LastSentAt=0)
        updated = {**self.body, 'password': 'UpdatedOtpPassword456!'}
        new = self.register(updated)
        self.assertEqual(self.verify(old)[0], 400)
        self.assertEqual(self.verify(new)[0], 201)
        self.assertEqual(self.client.request('POST', '/api/auth/login', {'email': self.email, 'password': self.body['password']})[0], 401)
        self.client.ok('POST', '/api/auth/login', {'email': self.email, 'password': updated['password']})

    def test_smtp_failures_rollback_initial_send_and_preserve_previous_code(self):
        marker = self.outbox / '.fail'
        try:
            marker.touch()
            self.assertEqual(self.client.request('POST', '/api/auth/register', self.body)[0], 503)
        finally:
            marker.unlink(missing_ok=True)
        self.assertEqual(int(self.db.execute('SELECT count(*) FROM "RegistrationVerifications" WHERE "Email"=?', (self.email,)).fetchone()[0]), 0)
        challenge = self.register(); code = self.code()
        self.change(LastSentAt=0)
        try:
            marker.touch()
            self.assertEqual(self.client.request('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})[0], 503)
        finally:
            marker.unlink(missing_ok=True)
        count = int(self.db.execute('''SELECT count(*) FROM "RegistrationFacts"
            WHERE "Stage"='CodeResent' AND "RegistrationAttemptId" IN
            (SELECT "UserId" FROM "RegistrationVerifications" WHERE "Email"=?)''', (self.email,)).fetchone()[0])
        self.assertEqual(count, 0, 'Failed SMTP delivery must not count as a successful resend')
        self.assertEqual(self.verify(challenge, code)[0], 201)

    def test_concurrent_verification_consumes_code_once(self):
        challenge = self.register(); code = self.code()
        def verify_once():
            return ApiSession().request('POST', '/api/auth/verify-registration', {'challengeToken': challenge['challengeToken'], 'code': code})[0]
        with ThreadPoolExecutor(max_workers=2) as pool:
            statuses = list(pool.map(lambda _: verify_once(), range(2)))
        self.assertEqual(statuses.count(201), 1, statuses)
        self.assertTrue(all(status in (201, 400, 409) for status in statuses), statuses)
        self.assertEqual(int(self.db.execute('SELECT count(*) FROM "Users" WHERE "Email"=?', (self.email,)).fetchone()[0]), 1)


class RegistrationOtpHttpTests(unittest.TestCase):
    """Provider-independent HTTP coverage, also runnable against disposable PostgreSQL."""
    @classmethod
    def setUpClass(cls):
        path = os.environ.get('TEST_OTP_OUTBOX')
        if not path:
            raise unittest.SkipTest('Requires opt-in disposable OTP SMTP sink')
        cls.outbox = Path(path).resolve()
        if not str(cls.outbox).startswith(str(Path('/tmp').resolve()) + os.sep) or not cls.outbox.is_dir():
            raise AssertionError('OTP SMTP sink must use an existing disposable /tmp directory')
        if urlsplit(BASE_URL).hostname not in {'localhost', '127.0.0.1', '::1'} or urlsplit(BASE_URL).scheme != 'http':
            raise AssertionError('OTP HTTP fixtures require HTTP localhost API')

    setUp = RegistrationOtpTests.setUp
    register = RegistrationOtpTests.register
    code = RegistrationOtpTests.code
    verify = RegistrationOtpTests.verify

    def test_verification_gates_login_and_replay(self):
        challenge = self.register()
        self.assertEqual(self.client.request('POST', '/api/auth/login', {'email': self.email, 'password': self.body['password']})[0], 401)
        self.assertEqual(self.verify(challenge, 'invalid')[0], 400)
        self.assertEqual(self.verify(challenge)[0], 201)
        self.assertEqual(self.verify(challenge)[0], 400)
        self.client.ok('POST', '/api/auth/login', {'email': self.email, 'password': self.body['password']})
        self.assertEqual(self.client.ok('GET', '/api/auth/me')['email'], self.email)

    def test_email_cooldown_also_blocks_new_registration(self):
        challenge = self.register()
        self.assertEqual(self.client.request('POST', '/api/auth/resend-registration', {'challengeToken': challenge['challengeToken']})[0], 429)
        self.assertEqual(self.client.request('POST', '/api/auth/register', self.body)[0], 429)

    def test_concurrent_confirmation_has_one_winner(self):
        challenge = self.register(); code = self.code()
        payload = {'challengeToken': challenge['challengeToken'], 'code': code}
        with ThreadPoolExecutor(max_workers=2) as pool:
            statuses = list(pool.map(lambda _: ApiSession().request('POST', '/api/auth/verify-registration', payload)[0], range(2)))
        self.assertEqual(statuses.count(201), 1, statuses)
        self.assertTrue(all(status in (201, 400, 409) for status in statuses), statuses)

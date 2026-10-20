"""Recovery against the runner-owned API and loopback-only SMTP sink; never real email."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import time
import unittest
from urllib.parse import urlsplit
import uuid
from test_api_integration import ApiSession, BASE_URL


class PasswordRecoveryHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not os.environ.get('TEST_OTP_OUTBOX'):
            raise unittest.SkipTest('Requires disposable loopback SMTP fixture')
        cls.outbox = Path(os.environ['TEST_OTP_OUTBOX']).resolve()
        if not str(cls.outbox).startswith(str(Path('/tmp').resolve()) + os.sep) or urlsplit(BASE_URL).hostname not in {'localhost', '127.0.0.1', '::1'}:
            raise AssertionError('Recovery tests require disposable /tmp and localhost')

    def setUp(self):
        self.client = ApiSession()
        self.email = 'recovery-' + uuid.uuid4().hex + '@example.invalid'
        self.password = 'originalpassword'
        registration = self.client.request('POST', '/api/auth/register', {
            'name': 'Recovery fixture', 'email': self.email, 'phone': '0900000000', 'password': self.password})
        self.assertEqual(registration[0], 202)
        self.registration = registration[1]
        code = self.wait_email('Mã xác nhận đăng ký Roomie')['code']
        self.client.ok('POST', '/api/auth/verify-registration', {'challengeToken': self.registration['challengeToken'], 'code': code})
        self.client.ok('POST', '/api/auth/login', {'email': self.email, 'password': self.password})

    def wait_email(self, subject):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            messages = [json.loads(p.read_text()) for p in self.outbox.glob('*.json')]
            matching = [m for m in messages if m['email'] == self.email and m['subject'] == subject]
            if matching:
                return matching[-1]
            time.sleep(.05)
        self.fail('Expected loopback fixture email was not delivered')

    def start(self):
        status, result, headers = self.client.request('POST', '/api/auth/forgot-password', {'email': self.email})
        self.assertEqual(status, 202, result)
        self.assertEqual(headers.get('Cache-Control'), 'no-store')
        self.assertNotIn('code', result)
        return result

    def verify(self, challenge):
        code = self.wait_email('Mã đặt lại mật khẩu Roomie')['code']
        return self.client.ok('POST', '/api/auth/verify-password-recovery', {'challengeToken': challenge['challengeToken'], 'code': code})

    def test_complete_recovers_account_revokes_sessions_and_notifies(self):
        second = ApiSession()
        second.ok('POST', '/api/auth/login', {'email': self.email, 'password': self.password})
        challenge = self.start()
        self.assertEqual(self.client.request('GET', '/api/auth/me')[0], 200)
        proof = self.verify(challenge)
        result = self.client.request('POST', '/api/auth/complete-password-recovery', {
            'resetToken': proof['resetToken'], 'newPassword': 'newpassword', 'confirmPassword': 'newpassword'})
        self.assertEqual(result[0], 200, result[1])
        self.assertEqual(second.request('GET', '/api/auth/me')[0], 401)
        self.client.ok('POST', '/api/auth/login', {'email': self.email, 'password': 'newpassword'})
        self.assertEqual(ApiSession().request('POST', '/api/auth/login', {'email': self.email, 'password': self.password})[0], 401)
        self.wait_email('Mật khẩu Roomie đã được thay đổi')

    def test_unknown_email_has_same_response_and_cooldown(self):
        known = self.start()
        email = 'missing-' + uuid.uuid4().hex + '@example.invalid'
        response = self.client.request('POST', '/api/auth/forgot-password', {'email': email})
        self.assertEqual(response[0], 202)
        self.assertEqual(set(response[1]), set(known))
        self.assertEqual(self.client.request('POST', '/api/auth/forgot-password', {'email': email})[0], 429)
        self.assertEqual(self.client.request('POST', '/api/auth/forgot-password', {'email': self.email})[0], 429)
        time.sleep(2.5)
        self.assertFalse(any(json.loads(p.read_text())['email'] == email for p in self.outbox.glob('*.json')))

    def test_wrong_code_lockout_does_not_lock_account(self):
        challenge = self.start()
        code = self.wait_email('Mã đặt lại mật khẩu Roomie')['code']
        for _ in range(5):
            self.assertEqual(self.client.request('POST', '/api/auth/verify-password-recovery', {'challengeToken': challenge['challengeToken'], 'code': 'bad'})[0], 400)
        self.assertEqual(self.client.request('POST', '/api/auth/verify-password-recovery', {'challengeToken': challenge['challengeToken'], 'code': code})[0], 400)
        ApiSession().ok('POST', '/api/auth/login', {'email': self.email, 'password': self.password})

    def test_purpose_separation_and_single_use_grant(self):
        challenge = self.start()
        code = self.wait_email('Mã đặt lại mật khẩu Roomie')['code']
        self.assertEqual(self.client.request('POST', '/api/auth/verify-password-recovery', {'challengeToken': self.registration['challengeToken'], 'code': code})[0], 400)
        self.assertEqual(self.client.request('POST', '/api/auth/complete-password-recovery', {'resetToken': challenge['challengeToken'], 'newPassword': 'newpassword', 'confirmPassword': 'newpassword'})[0], 400)
        grant = self.verify(challenge)
        body = {'resetToken': grant['resetToken'], 'newPassword': 'newpassword', 'confirmPassword': 'newpassword'}
        self.client.ok('POST', '/api/auth/complete-password-recovery', body)
        self.assertEqual(ApiSession().request('POST', '/api/auth/complete-password-recovery', body)[0], 400)

    def test_concurrent_completion_has_one_winner(self):
        grant = self.verify(self.start())
        body = {'resetToken': grant['resetToken'], 'newPassword': 'newpassword', 'confirmPassword': 'newpassword'}
        with ThreadPoolExecutor(max_workers=2) as pool:
            statuses = list(pool.map(lambda _: ApiSession().request('POST', '/api/auth/complete-password-recovery', body)[0], range(2)))
        self.assertEqual(statuses.count(200), 1, statuses)
        self.assertTrue(all(status in (200, 400, 409) for status in statuses), statuses)

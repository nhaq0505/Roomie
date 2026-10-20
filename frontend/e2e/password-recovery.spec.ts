import { expect, test, type Page } from '@playwright/test';

async function guest(page: Page) {
  await page.route('**/api/auth/me', route => route.fulfill({ status: 401, json: { message: 'Guest fixture' } }));
}

test('email recovery requires OTP proof before accepting a new password', async ({ page }) => {
  await guest(page);
  let verifies = 0, completes = 0;
  await page.route('**/api/auth/forgot-password', route => {
    expect(route.request().postDataJSON()).toEqual({ email: 'owner@example.invalid' });
    return route.fulfill({ status: 202, json: { email: 'owner@example.invalid', challengeToken: 'a'.repeat(64), expiresAt: new Date(Date.now() + 600000).toISOString(), resendAvailableAt: new Date(Date.now() + 60000).toISOString() } });
  });
  await page.route('**/api/auth/verify-password-recovery', route => {
    verifies++;
    const body = route.request().postDataJSON();
    expect(body.challengeToken).toBe('a'.repeat(64));
    return body.code === '123456' ? route.fulfill({ json: { resetToken: 'b'.repeat(64), expiresAt: new Date(Date.now() + 600000).toISOString() } })
      : route.fulfill({ status: 400, json: { message: 'Mã khôi phục không hợp lệ hoặc đã hết hạn.' } });
  });
  await page.route('**/api/auth/complete-password-recovery', route => {
    completes++;
    expect(route.request().postDataJSON()).toEqual({ resetToken: 'b'.repeat(64), newPassword: 'newpassword', confirmPassword: 'newpassword' });
    return route.fulfill({ json: { success: true } });
  });
  await page.goto('/login');
  await page.getByRole('link', { name: 'Quên mật khẩu', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Quên mật khẩu', exact: true })).toBeVisible();
  await page.getByLabel('Email', { exact: true }).fill('owner@example.invalid');
  await page.getByRole('button', { name: 'Gửi mã khôi phục' }).click();
  await expect(page.getByText(/Nếu email có tài khoản/)).toBeVisible();
  await expect(page.getByLabel('Mật khẩu mới', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Gửi lại mã sau/ })).toBeDisabled();
  await page.getByLabel('Mã xác nhận', { exact: true }).fill('000000');
  await page.getByRole('button', { name: 'Xác minh mã OTP', exact: true }).click();
  await expect(page.getByText('Mã khôi phục không hợp lệ hoặc đã hết hạn.', { exact: true })).toBeVisible();
  expect(completes).toBe(0);
  // Ant Design's loading icon changes the button's accessible name while the
  // failed request settles. Wait for the form to unlock before editing/retrying.
  const verifySubmit = page.locator('#recovery-code button[type="submit"]');
  await expect(verifySubmit).toBeEnabled();
  await expect(page.getByLabel('Mã xác nhận', { exact: true })).toBeEnabled();
  await page.getByLabel('Mã xác nhận', { exact: true }).fill('123456');
  await verifySubmit.click();
  await page.getByLabel('Mật khẩu mới', { exact: true }).fill('12345678');
  await page.getByLabel('Xác nhận mật khẩu mới', { exact: true }).fill('12345678');
  await page.getByRole('button', { name: 'Lưu mật khẩu', exact: true }).click();
  await expect(page.getByText('Mật khẩu mới cần từ 9 đến 256 ký tự.', { exact: true })).toBeVisible();
  expect(completes).toBe(0);
  await page.getByLabel('Mật khẩu mới', { exact: true }).fill('newpassword');
  await page.getByLabel('Xác nhận mật khẩu mới', { exact: true }).fill('newpassword');
  await page.getByRole('button', { name: 'Lưu mật khẩu', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByText('Đã đổi mật khẩu. Vui lòng đăng nhập lại.', { exact: true })).toBeVisible();
  expect(verifies).toBe(2); expect(completes).toBe(1);
  expect(await page.evaluate(() => Object.values(sessionStorage).some(value => value.includes('b'.repeat(64))))).toBe(false);
});

test('resend rotates the challenge and mobile recovery keeps the manual fallback', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.clock.install();
  await guest(page);
  let sends = 0;
  await page.route('**/api/auth/forgot-password', route => route.fulfill({ status: 202, json: {
    email: 'owner@example.invalid', challengeToken: String(++sends).repeat(64),
    expiresAt: new Date(Date.now() + 600000).toISOString(), resendAvailableAt: new Date(Date.now() + 60000).toISOString(),
  } }));
  await page.goto('/reset-password');
  await page.getByLabel('Email', { exact: true }).fill('owner@example.invalid');
  await page.getByRole('button', { name: 'Gửi mã khôi phục' }).click();
  await expect(page.getByRole('button', { name: /Gửi lại mã sau/ })).toBeDisabled();
  await page.clock.fastForward(61000);
  await page.getByRole('button', { name: 'Gửi lại mã', exact: true }).click();
  expect(sends).toBe(2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await page.getByRole('link', { name: 'Dùng mã do quản trị viên cấp' }).click();
  await expect(page).toHaveURL(/method=admin/);
  await expect(page.getByLabel('Mã đặt lại mật khẩu', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Khôi phục qua email' }).click();
  await expect(page.getByRole('button', { name: 'Gửi mã khôi phục' })).toBeVisible();
});

test('recovery expiry and connection errors preserve a retry path', async ({ page }) => {
  await guest(page);
  let attempt = 0;
  await page.route('**/api/auth/forgot-password', route => ++attempt === 1
    ? route.fulfill({ status: 503, json: { message: 'Fixture server unavailable' } })
    : route.fulfill({ status: 202, json: { email: 'owner@example.invalid', challengeToken: 'a'.repeat(64), expiresAt: new Date(Date.now() - 1000).toISOString(), resendAvailableAt: new Date(Date.now() - 1000).toISOString() } }));
  await page.goto('/reset-password');
  await page.getByLabel('Email', { exact: true }).fill('owner@example.invalid');
  await page.getByRole('button', { name: 'Gửi mã khôi phục' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByLabel('Email', { exact: true })).toHaveValue('owner@example.invalid');
  await page.getByRole('button', { name: 'Gửi mã khôi phục' }).click();
  await expect(page.getByText('Mã đã hết hạn. Vui lòng yêu cầu mã mới.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Xác minh mã OTP', exact: true })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Gửi lại mã', exact: true })).toBeEnabled();
});

import { expect, test, type Page } from '@playwright/test';
const initialSettings = { scheduleEnabled: false, dayOfMonth: 31, hour: 8, minute: 30, bankBin: '', bankAccount: '', bankAccountName: '', gatewayEnabled: true };
async function setup(page: Page, status = 'Disconnected') {
  let settings = { ...initialSettings }, state = status;
  await page.route('**/api/auth/me', route => route.fulfill({ json: { id: 'fixture-owner', role: 'Owner', status: 'Approved', name: 'Fixture', email: 'owner@example.invalid' } }));
  await page.route('**/api/zalo/settings', route => {
    if (route.request().method() === 'PUT') { settings = { ...settings, ...route.request().postDataJSON() }; return route.fulfill({ status: 204 }); }
    return route.fulfill({ json: settings });
  });
  await page.route('**/api/zalo/connection', route => route.fulfill({ json: { status: state, qrImage: state === 'QrReady' ? 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6n1cAAAAASUVORK5CYII=' : null } }));
  await page.route('**/api/zalo/connect', route => { state = 'QrReady'; return route.fulfill({ json: { status: state } }); });
  await page.route('**/api/zalo/disconnect', route => { state = 'Disconnected'; settings.scheduleEnabled = false; return route.fulfill({ json: { status: state } }); });
  await page.route('**/api/zalo/history?*', route => route.fulfill({ json: { items: [{ invoiceId: 'invoice-fixture', roomName: 'P101', period: '2026-10', status: 'Unknown', detail: 'DeliveryUnknown', queuedAtMs: 1792458000000, lastAttemptAtMs: 1792458000000 }], total: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/zalo/queue', route => route.fulfill({ json: { queued: 1 } }));
  await page.route('**/api/whatsapp/settings', route => {
    if (route.request().method() === 'PUT') { settings = { ...settings, ...route.request().postDataJSON() }; return route.fulfill({ status: 204 }); }
    return route.fulfill({ json: settings });
  });
  await page.route('**/api/whatsapp/connection', route => route.fulfill({ json: { status: state, qrImage: state === 'QrReady' ? 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6n1cAAAAASUVORK5CYII=' : null } }));
  await page.route('**/api/whatsapp/connect', route => { state = 'QrReady'; return route.fulfill({ json: { status: state } }); });
  await page.route('**/api/whatsapp/disconnect', route => { state = 'Disconnected'; settings.scheduleEnabled = false; return route.fulfill({ json: { status: state } }); });
  await page.route('**/api/whatsapp/history?*', route => route.fulfill({ json: { items: [{ invoiceId: 'invoice-fixture', roomName: 'P101', period: '2026-10', status: 'Unknown', detail: 'DeliveryUnknown', queuedAtMs: 1792458000000, lastAttemptAtMs: 1792458000000 }], total: 1, page: 1, pageSize: 20 } }));
  await page.route('**/api/whatsapp/queue', route => route.fulfill({ json: { queued: 1 } }));
  return { getSettings: () => settings, setState: (value: string) => { state = value; } };
}
for (const width of [375, 1280]) test(`Zalo connect, queue and guide at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  const fixture = await setup(page);
  await page.goto('/settings/zalo');
  await expect(page.getByRole('heading', { name: 'Nhắc hóa đơn qua Zalo', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /Nhắc hóa đơn còn nợ$/ })).toBeDisabled();
  await page.getByRole('button', { name: /Tạo QR kết nối$/ }).click();
  await expect(page.getByRole('img', { name: 'QR đăng nhập Zalo', exact: true })).toBeVisible();
  fixture.setState('Connected');
  await expect(page.getByText('Đã kết nối Zalo', { exact: true })).toBeVisible({ timeout: 7000 });
  await expect(page.getByRole('img', { name: 'QR đăng nhập Zalo', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: /Nhắc hóa đơn còn nợ$/ }).click();
  await page.getByRole('button', { name: 'Thêm vào hàng đợi', exact: true }).click();
  await expect(page.getByText('Đã thêm 1 hóa đơn vào hàng đợi.', { exact: true })).toBeVisible();
  await expect(page.getByText('Có thể tin đã gửi. Kiểm tra Zalo trước khi thêm lại sau 20 giờ.', { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  await expect(page.locator('.ant-popconfirm')).not.toBeVisible();
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: `test-results/zalo-${width}.png`, fullPage: true });
  await page.getByRole('button', { name: 'Hướng dẫn sử dụng', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('thư viện không chính thức');
  await page.getByRole('button', { name: 'Đóng', exact: true }).click();
  await page.getByRole('button', { name: 'Ngắt kết nối', exact: true }).click();
  await page.locator('.ant-popconfirm').getByRole('button', { name: 'Ngắt kết nối', exact: true }).click();
  await expect(page.getByRole('button', { name: /Tạo QR kết nối$/ })).toBeVisible();
  expect(fixture.getSettings().scheduleEnabled).toBe(false);
});
test('unavailable gateway and errors expose retry without showing a fake QR', async ({ page }) => {
  await setup(page, 'Unavailable');
  await page.route('**/api/zalo/settings', route => route.fulfill({ json: { ...initialSettings, gatewayEnabled: false } }));
  await page.route('**/api/zalo/history?*', route => route.fulfill({ status: 503, json: { message: 'Fixture outage' } }));
  await page.goto('/settings/zalo');
  await expect(page.getByRole('button', { name: /Tạo QR kết nối$/ })).toBeDisabled();
  await expect(page.getByRole('img', { name: 'QR đăng nhập Zalo', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: /Thử lại$/ })).toBeVisible({ timeout: 10000 });
});

for (const width of [375, 1280]) test(`settings menu groups payment and Zalo at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  await setup(page, 'Connected');
  await page.route('**/api/settings/payment', route => route.fulfill({ json: { clientId: '', webhookStatus: 'NotConfigured', confirmationMode: 'Automatic' } }));
  await page.route('**/api/settings/payment/banks', route => route.fulfill({ json: [] }));
  await page.goto('/settings/zalo');
  if (width > 991) {
    const menu = page.locator('.app-sider');
    await expect(menu.getByRole('menuitem', { name: /Zalo$/ })).toBeVisible();
    const settings = menu.locator('.ant-menu-submenu-title').filter({ hasText: 'Cấu hình' });
    await settings.click();
    await expect(menu.getByRole('menuitem', { name: /Zalo$/ })).not.toBeVisible();
    await expect(page).toHaveURL(/settings\/zalo$/);
    await settings.click();
    await menu.getByRole('menuitem', { name: /Thanh toán$/ }).click();
    await expect(page).toHaveURL(/settings\/payment$/);
    await menu.getByRole('menuitem', { name: /Zalo$/ }).click();
  } else {
    const nav = page.getByRole('navigation', { name: 'Điều hướng chính' });
    await expect(nav.getByRole('link', { name: 'Zalo', exact: true })).toHaveCount(0);
    await nav.getByRole('button', { name: /Cấu hình/ }).click();
    await page.locator('.ant-dropdown:visible').getByRole('menuitem', { name: /Thanh toán$/ }).click();
    await expect(page).toHaveURL(/settings\/payment$/);
    await page.getByRole('button', { name: 'Mở menu', exact: true }).click();
    const drawer = page.locator('.mobile-menu-drawer');
    await drawer.getByRole('menuitem', { name: /Zalo$/ }).click();
    await expect(drawer).not.toBeVisible();
  }
  await expect(page).toHaveURL(/settings\/zalo$/);
  await expect(page.locator('.breadcrumb-label strong')).toHaveText('Zalo');
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  await page.screenshot({ path: `test-results/settings-menu-${width}.png`, fullPage: true });
});

test('WhatsApp connect, queue and guide work alongside Zalo', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const fixture = await setup(page);
  await page.goto('/settings/whatsapp');
  await expect(page.getByRole('heading', { name: 'Nhắc hóa đơn qua WhatsApp', exact: true })).toBeVisible();
  await expect(page.locator('.breadcrumb-label strong')).toHaveText('WhatsApp');
  await page.getByRole('button', { name: /Tạo QR kết nối$/ }).click();
  await expect(page.getByRole('img', { name: 'QR đăng nhập WhatsApp', exact: true })).toBeVisible();
  fixture.setState('Connected');
  await expect(page.getByText('Đã kết nối WhatsApp', { exact: true })).toBeVisible({ timeout: 7000 });
  await page.getByRole('button', { name: /Nhắc hóa đơn còn nợ$/ }).click();
  await page.getByRole('button', { name: 'Thêm vào hàng đợi', exact: true }).click();
  await expect(page.getByText('Đã thêm 1 hóa đơn vào hàng đợi.', { exact: true })).toBeVisible();
  await expect(page.getByText('Có thể tin đã gửi. Kiểm tra WhatsApp trước khi thêm lại sau 20 giờ.', { exact: true })).toBeVisible();
});

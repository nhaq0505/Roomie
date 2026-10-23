import { expect, test, type Page } from '@playwright/test';
const owner = { id: 'fixture-owner', name: 'Fixture Owner', email: 'owner@example.invalid', role: 'Owner', status: 'Approved' };
const invoice = (id: string, total: number) => ({ id, roomId: 'r1', roomName: 'Phòng 101', buildingName: 'Fixture', tenantName: 'Fixture Tenant', kind: 'Monthly', lifecycleState: 'Issued', period: '2026-10', status: 'Unpaid', total, paid: 0, outstanding: total, rent: total, electricityAmount: 0, waterAmount: 0, serviceAmount: 0 });
const invoices = [invoice('i1', 100000), invoice('i2', 200000)];
const overview = (manual = true) => ({ confirmationMode: manual ? 'Manual' : 'Automatic', room: { id: 'r1', name: 'Phòng 101', buildingName: 'Fixture' }, occupancy: { tenantName: 'Fixture Tenant' }, invoices, total: 12, totals: { outstanding: 300000, unpaidCount: 12 } });
const manual = { id: 'fixture-request', occupancyId: 'fixture-occupancy', amount: 300000, bankBin: '970436', accountNumber: '12345678', accountName: 'SYNTHETIC TEST ONLY', description: 'RMFIXTUREONLY', qrUrl: 'https://img.vietqr.io/image/fixture.png', invoices: [{ invoiceId: 'i1', amount: 100000 }, { invoiceId: 'i2', amount: 200000 }], createdAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 86400000).toISOString(), confirmedAt: null };
async function isolated(page: Page) {
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin === 'http://127.0.0.1:4178') return route.continue();
    if (url.hostname === 'img.vietqr.io') return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240"><text x="10" y="120">TEST IMAGE - NOT PAYABLE</text></svg>' });
    return route.abort();
  });
}

test('owner defaults to manual VietQR mode and saves manual bank details separately from payOS credentials', async ({ page }) => {
  await isolated(page);
  let settings: object = { configured: false, webhookStatus: 'NotConfigured', confirmationMode: 'Manual', webhookUrl: null };
  let saves = 0;
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/me') return route.fulfill({ json: owner });
    if (path === '/api/settings/payment') return route.fulfill({ json: settings });
    if (path.endsWith('/banks')) return route.fulfill({ json: [{ bin: '970436', name: 'Vietcombank', shortName: 'VCB' }] });
    if (path.endsWith('/preferences')) {
      const data = route.request().postDataJSON();
      expect(data).toMatchObject({ confirmationMode: 'Manual', bankBin: '970436', accountNumber: '12345678', accountName: 'SYNTHETIC TEST ONLY' });
      saves++; settings = { configured: false, webhookStatus: 'NotConfigured', confirmationMode: 'Manual', manualBank: data };
      return route.fulfill({ json: settings });
    }
    return route.fulfill({ json: [] });
  });
  await page.goto('/settings/payment');
  await expect(page.getByRole('radio', { name: 'Xác nhận thanh toán thủ công', exact: true })).toBeChecked();
  await expect(page.getByLabel('Client ID', { exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Hướng dẫn thiết lập', exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Cấu hình payOS', exact: true })).toHaveCount(0);
  expect(saves).toBe(0);
  await page.getByRole('radio', { name: 'Tự động xác nhận thanh toán', exact: true }).check();
  await expect(page.getByLabel('Client ID', { exact: true })).toBeVisible();
  await page.getByRole('radio', { name: 'Xác nhận thanh toán thủ công', exact: true }).check();
  await expect(page.getByLabel('Client ID', { exact: true })).toHaveCount(0);
  await page.getByLabel('Ngân hàng nhận tiền', { exact: true }).click();
  await page.getByTitle('VCB — Vietcombank', { exact: true }).click();
  await page.getByLabel('Số tài khoản', { exact: true }).fill('12345678');
  await page.getByLabel('Tên chủ tài khoản', { exact: true }).fill('SYNTHETIC TEST ONLY');
  await page.getByRole('button', { name: 'Lưu chế độ thanh toán', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Yêu cầu chuyển khoản', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('Client ID', { exact: true })).toHaveCount(0);
  expect(saves).toBe(1);
});

test('tenant can pay one invoice or all pages with manual VietQR without confirming their own payment', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await isolated(page); let payosCalls = 0; const selections: unknown[] = [];
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/me') return route.fulfill({ status: 401, json: { message: 'Guest' } });
    if (path.endsWith('/manual-payments')) { selections.push(route.request().postDataJSON()); return route.fulfill({ json: manual }); }
    if (path.endsWith('/manual-payments/fixture-request')) return route.fulfill({ json: { ...manual, canConfirm: true, status: 'Ready' } });
    if (path.endsWith('/payment')) { payosCalls++; return route.fulfill({ status: 409, json: { message: 'No payOS in manual mode' } }); }
    return route.fulfill({ json: overview() });
  });
  await page.goto('/t/fixture-token');
  await page.getByRole('button', { name: 'Thanh toán tất cả', exact: true }).click();
  await expect(page).toHaveURL(/invoices\/all\/checkout/);
  await expect(page.getByText('RMFIXTUREONLY', { exact: true })).toBeVisible();
  expect(selections).toEqual([{ invoiceId: null }]);
  expect(payosCalls).toBe(0);
  await expect(page.getByRole('button', { name: 'Xác nhận đã nhận tiền', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await page.goto('/t/fixture-token/invoices/i1/checkout');
  await expect(page.getByText('RMFIXTUREONLY', { exact: true })).toBeVisible();
  expect(selections).toContainEqual({ invoiceId: 'i1' });
  expect(payosCalls).toBe(0);
});

test('automatic pay-all creates one server-side payOS order and reconciles by scoped order code', async ({ page }) => {
  await isolated(page); let creates = 0, reconciles = 0;
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/me') return route.fulfill({ status: 401, json: { message: 'Guest' } });
    if (path === '/api/tenant/fixture-token/payment') {
      creates++; return route.fulfill({ json: { checkoutUrl: 'https://pay.payos.vn/web/fixture-only', qrCode: 'SYNTHETIC-NOT-PAYABLE', amount: 300000, orderCode: 12345, accountName: 'SYNTHETIC TEST', accountNumber: '12345678', bankBin: '970436', description: 'FIXTURE', status: 'pending', expiresAt: Math.floor(Date.now() / 1000) + 600 } });
    }
    if (path.endsWith('/orders/12345/reconcile')) { reconciles++; return route.fulfill({ json: { status: 'Pending' } }); }
    return route.fulfill({ json: overview(false) });
  });
  await page.goto('/t/fixture-token/invoices/all/checkout');
  await expect(page.getByText('SYNTHETIC TEST', { exact: true })).toBeVisible();
  expect(creates).toBe(1);
  await page.getByRole('button', { name: /Kiểm tra ngay$/ }).click();
  expect(reconciles).toBeGreaterThan(0);
});

for (const width of [375, 1440]) {
  test(`owner confirms a grouped transfer from invoices at ${width}px`, async ({ page }) => {
    await isolated(page);
    await page.setViewportSize({ width, height: 900 });
    let confirmed = false, confirmations = 0, invoiceReads = 0;
    await page.route('**/api/**', route => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/auth/me') return route.fulfill({ json: owner });
      if (path === '/api/manual-payments') return route.fulfill({ json: { items: confirmed ? [] : [{ ...manual, roomName: 'Phòng 101', tenantName: 'Fixture Tenant' }], total: confirmed ? 0 : 1 } });
      if (path === '/api/manual-payments/fixture-request/confirm') {
        expect(route.request().postDataJSON()).toEqual({ amount: 300000 });
        confirmed = true; confirmations++;
        return route.fulfill({ json: { ...manual, confirmedAt: new Date().toISOString() } });
      }
      if (path === '/api/invoices') {
        invoiceReads++;
        return route.fulfill({ json: { items: invoices.map(item => confirmed ? { ...item, paid: item.total, outstanding: 0, status: 'Paid' } : item), total: 2, totals: { billed: 300000, paid: confirmed ? 300000 : 0, outstanding: confirmed ? 0 : 300000 } } });
      }
      return route.fulfill({ json: [] });
    });
    await page.goto('/invoices');
    await expect(page.getByRole('heading', { name: 'Yêu cầu chuyển khoản', exact: true })).toBeVisible();
    await page.screenshot({ path: `/tmp/roomie-ui-review/payments/pending-${width}.png`, fullPage: true });
    await page.getByRole('button', { name: 'Xác nhận đã nhận tiền', exact: true }).click();
    const dialog = page.getByRole('dialog');
    const submit = dialog.getByRole('button', { name: 'Ghi nhận khoản thu', exact: true });
    await expect(dialog.getByRole('spinbutton')).toHaveCount(0);
    await expect(dialog).toContainText('Xác nhận này thanh toán toàn bộ số tiền còn lại');
    await dialog.screenshot({ path: `/tmp/roomie-ui-review/payments/confirm-${width}.png`, animations: 'disabled' });
    await expect(submit).toBeEnabled();
    await submit.click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('heading', { name: 'Yêu cầu chuyển khoản', exact: true })).toHaveCount(0);
    await expect(page.locator('.manual-payment-request')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Xác nhận đã nhận tiền', exact: true })).toHaveCount(0);
    expect(confirmations).toBe(1);
    expect(invoiceReads).toBeGreaterThan(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: `/tmp/roomie-ui-review/payments/invoices-${width}.png`, fullPage: true });
  });
}


test('stale transfer conflict refreshes balances, explains the invoice error and can be dismissed without another receipt', async ({ page }) => {
  await isolated(page);
  let stale = false, dismissed = false, confirmations = 0, dismissals = 0;
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/me') return route.fulfill({ json: owner });
    if (path === '/api/manual-payments') return route.fulfill({ json: { items: dismissed ? [] : [{ ...manual,
      canConfirm: !stale, canDismiss: stale, status: stale ? 'AlreadyPaid' : 'Ready',
      statusMessage: stale ? 'Các hóa đơn của yêu cầu này đã được thu đủ. Không ghi thêm khoản thu.' : null
    }], total: dismissed ? 0 : 1 } });
    if (path.endsWith('/confirm')) {
      confirmations++; stale = true;
      return route.fulfill({ status: 409, json: { message: 'Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.' } });
    }
    if (path.endsWith('/dismiss')) { dismissals++; dismissed = true; return route.fulfill({ json: { ...manual, status: 'Dismissed' } }); }
    if (path === '/api/invoices') return route.fulfill({ json: { items: [], total: 0, totals: { billed: 300000, paid: stale ? 300000 : 0, outstanding: stale ? 0 : 300000 } } });
    return route.fulfill({ json: [] });
  });
  await page.goto('/invoices');
  await page.getByRole('button', { name: 'Xác nhận đã nhận tiền', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Ghi nhận khoản thu', exact: true }).click();
  await expect(dialog).toContainText('Các hóa đơn của yêu cầu này đã được thu đủ');
  await expect(dialog.locator('.ant-btn-loading')).toHaveCount(0);
  // Ant Design keeps the loading icon in the accessible name during its exit animation.
  await expect(dialog.getByRole('button', { name: /Ghi nhận khoản thu$/ })).toBeDisabled();
  await expect(page.getByText('Lượt thuê đã thay đổi', { exact: false })).toHaveCount(0);
  await dialog.getByRole('button', { name: 'Close', exact: true }).click();
  await page.getByRole('button', { name: 'Bỏ khỏi danh sách', exact: true }).click();
  await page.getByRole('tooltip').getByRole('button', { name: 'Bỏ khỏi danh sách', exact: true }).click();
  await expect(page.locator('.manual-payment-request')).toHaveCount(0);
  expect(confirmations).toBe(1); expect(dismissals).toBe(1);
});


test('tenant checks QR status with GET and hides an obsolete QR before offering a new one', async ({ page }) => {
  await isolated(page);
  let stale = false, creates = 0, statusReads = 0;
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/auth/me') return route.fulfill({ status: 401, json: { message: 'Guest' } });
    if (path.endsWith('/manual-payments')) { creates++; stale = false; return route.fulfill({ json: { ...manual, id: `request-${creates}` } }); }
    if (path.includes('/manual-payments/request-')) {
      statusReads++;
      return route.fulfill({ json: { ...manual, status: stale ? 'InvoiceChanged' : 'Ready', canConfirm: !stale,
        statusMessage: stale ? 'Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.' : null } });
    }
    return route.fulfill({ json: overview() });
  });
  await page.goto('/t/fixture-token/invoices/i1/checkout');
  await expect(page.getByRole('img', { name: 'Mã VietQR chuyển khoản', exact: true })).toBeVisible();
  stale = true;
  await expect(page.getByRole('img', { name: 'Mã VietQR chuyển khoản', exact: true })).toHaveCount(0, { timeout: 10000 });
  await expect(page.getByText('Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.', { exact: true })).toBeVisible();
  expect(creates).toBe(1); expect(statusReads).toBeGreaterThan(1);
  await page.getByRole('button', { name: /Cập nhật mã QR/ }).click();
  await expect(page.getByRole('img', { name: 'Mã VietQR chuyển khoản', exact: true })).toBeVisible();
  expect(creates).toBe(2);
});

import { test, expect, type Page, type Route } from '@playwright/test';

const owner = { id: 'fixture-expense-owner', name: 'Chủ Trọ Chi Phí', email: 'expense@example.com', role: 'Owner' as const, status: 'Approved' as const };
const building = { id: 'bld-exp', name: 'Nhà trọ Hướng Dương', address: '12 Đường Số 3', isArchived: false };
const room = { id: 'room-exp', buildingId: 'bld-exp', buildingName: 'Nhà trọ Hướng Dương', name: 'Phòng 201', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 0, occupancy: null, isArchived: false };
const subscription = (isPro: boolean) => ({ tier: isPro ? 'Pro' : 'Free', isPro, expiresAt: null, roomCount: 1, maxRooms: isPro ? 2147483647 : 5, availablePackages: [] });

interface ExpenseRow {
  id: string; expenseDate: string; category: string; amount: number; description: string | null; buildingId: string | null; buildingName: string | null;
  roomId: string | null; roomName: string | null; utilityPeriod: string | null; utilityKwh: number | null; recurringExpenseId: string | null;
  recurrencePeriod: string | null; attachmentCount: number; version: number; createdAt: string; updatedAt: string;
}

function today() {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
  const part = (type: string) => parts.find((item) => item.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}

function listResponse(rows: ExpenseRow[]) {
  const byCategory = Object.values(rows.reduce<Record<string, { category: string; amount: number; count: number }>>((acc, row) => {
    acc[row.category] ??= { category: row.category, amount: 0, count: 0 };
    acc[row.category].amount += row.amount; acc[row.category].count += 1; return acc;
  }, {})).sort((a, b) => b.amount - a.amount);
  return { items: rows, page: 1, pageSize: 50, totalCount: rows.length, totals: { total: rows.reduce((sum, row) => sum + row.amount, 0), count: rows.length, byCategory } };
}

async function baseRoutes(page: Page, isPro: boolean, handler: (route: Route, path: string, method: string, url: URL) => Promise<boolean>) {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    const method = route.request().method();
    if (await handler(route, path, method, url)) return;
    if (path === '/api/auth/me') return route.fulfill({ json: owner });
    if (path === '/api/subscription') return route.fulfill({ json: subscription(isPro) });
    if (path === '/api/buildings') return route.fulfill({ json: [building] });
    if (path === '/api/rooms') return route.fulfill({ json: [room] });
    return route.fulfill({ json: [] });
  });
}

test.describe('Expense ledger (Sổ chi phí)', () => {
  test('E1: add, edit, filter and delete an expense with idempotent create', async ({ page }) => {
    let rows: ExpenseRow[] = [];
    const createKeys: string[] = [];
    const listUrls: string[] = [];
    let putBody: Record<string, unknown> | null = null;
    let deleteUrl = '';
    await baseRoutes(page, true, async (route, path, method, url) => {
      if (path === '/api/expenses' && method === 'GET') { listUrls.push(url.search); await route.fulfill({ json: listResponse(rows) }); return true; }
      if (path === '/api/expenses' && method === 'POST') {
        createKeys.push(route.request().headers()['idempotency-key'] ?? '');
        const body = route.request().postDataJSON() as Partial<ExpenseRow>;
        const created: ExpenseRow = {
          id: 'exp-1', expenseDate: String(body.expenseDate), category: String(body.category), amount: Number(body.amount), description: body.description ?? null,
          buildingId: body.buildingId ?? null, buildingName: body.buildingId ? building.name : null, roomId: null, roomName: null, utilityPeriod: null, utilityKwh: null,
          recurringExpenseId: null, recurrencePeriod: null, attachmentCount: 0, version: 1, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
        };
        rows = [created];
        await route.fulfill({ status: 201, json: created }); return true;
      }
      if (path === '/api/expenses/exp-1' && method === 'PUT') {
        putBody = route.request().postDataJSON() as Record<string, unknown>;
        rows = [{ ...rows[0], amount: Number(putBody.amount), version: 2 }];
        await route.fulfill({ json: rows[0] }); return true;
      }
      if (path === '/api/expenses/exp-1' && method === 'DELETE') { deleteUrl = url.search; rows = []; await route.fulfill({ json: { deleted: true } }); return true; }
      return false;
    });

    await page.goto('/expenses');
    await expect(page.getByRole('heading', { level: 2, name: 'Chi phí' })).toBeVisible();
    await expect(page.getByText('Chưa có khoản chi trong khoảng này')).toBeVisible();

    await page.getByRole('button', { name: 'Thêm khoản chi' }).click();
    const modal = page.locator('.ant-modal').filter({ hasText: 'Thêm khoản chi' });
    await expect(modal).toBeVisible();
    await modal.getByLabel('Số tiền (VND)').fill('350000');
    await modal.getByLabel('Ghi chú').fill('Thay vòi nước phòng 201');
    await modal.getByRole('button', { name: 'Ghi khoản chi' }).click();
    await expect(modal).toBeHidden();
    expect(createKeys).toHaveLength(1);
    expect(createKeys[0]).toMatch(/^[0-9a-f-]{36}$/);
    await expect(page.locator('.reports-table-wrap').getByText('Thay vòi nước phòng 201').first()).toBeVisible();
    await expect(page.locator('.report-total-accent')).toContainText('350.000');

    await page.getByRole('button', { name: 'Sửa khoản chi' }).first().click();
    const editModal = page.locator('.ant-modal').filter({ hasText: 'Sửa khoản chi' });
    await editModal.getByLabel('Số tiền (VND)').fill('400000');
    await editModal.getByRole('button', { name: 'Lưu thay đổi' }).click();
    await expect(editModal).toBeHidden();
    expect(putBody).toMatchObject({ amount: 400000, expectedVersion: 1 });
    await expect(page.locator('.report-total-accent')).toContainText('400.000');

    await page.getByRole('combobox', { name: 'Lọc theo danh mục' }).click();
    await page.locator('.ant-select-dropdown:visible').getByText('Sửa chữa, bảo trì', { exact: true }).click();
    await expect.poll(() => listUrls.some((search) => search.includes('category=Repair'))).toBe(true);

    await page.getByRole('button', { name: 'Xóa khoản chi' }).first().click();
    await page.locator('.ant-popconfirm').getByRole('button', { name: 'Xóa' }).click();
    await expect.poll(() => deleteUrl).toContain('expectedVersion=2');
    await expect(page.getByText('Chưa có khoản chi trong khoảng này')).toBeVisible();
  });

  test('E2: attachments upload as multipart for Pro owners', async ({ page }) => {
    const expense: ExpenseRow = {
      id: 'exp-att', expenseDate: today(), category: 'Electricity', amount: 1200000, description: 'Hóa đơn EVN tháng này', buildingId: building.id, buildingName: building.name,
      roomId: null, roomName: null, utilityPeriod: null, utilityKwh: null, recurringExpenseId: null, recurrencePeriod: null, attachmentCount: 0, version: 1,
      createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    };
    let attachments: { id: string; expenseId: string; fileName: string; contentType: string; sizeBytes: number; createdAt: string }[] = [];
    let uploadContentType = '';
    await baseRoutes(page, true, async (route, path, method) => {
      if (path === '/api/expenses' && method === 'GET') { await route.fulfill({ json: listResponse([{ ...expense, attachmentCount: attachments.length }]) }); return true; }
      if (path === '/api/expenses/attachments/usage') { await route.fulfill({ json: { usedBytes: 0, limitBytes: 209715200, maxFileBytes: 2097152, maxFilesPerExpense: 5 } }); return true; }
      if (path === '/api/expenses/exp-att/attachments' && method === 'GET') { await route.fulfill({ json: attachments }); return true; }
      if (path === '/api/expenses/exp-att/attachments' && method === 'POST') {
        uploadContentType = route.request().headers()['content-type'] ?? '';
        attachments = [{ id: 'att-1', expenseId: 'exp-att', fileName: 'hoa-don-evn.pdf', contentType: 'application/pdf', sizeBytes: 2048, createdAt: new Date().toISOString() }];
        await route.fulfill({ status: 201, json: attachments[0] }); return true;
      }
      return false;
    });
    await page.goto('/expenses');
    await page.getByRole('button', { name: 'Chứng từ của khoản chi' }).first().click();
    const drawer = page.locator('.expense-attachments-drawer');
    await expect(drawer.getByText('Chưa có chứng từ')).toBeVisible();
    await drawer.locator('input[type=file]').setInputFiles({ name: 'hoa-don-evn.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\n%%EOF') });
    await expect(drawer.getByText('hoa-don-evn.pdf')).toBeVisible();
    expect(uploadContentType).toContain('multipart/form-data');
  });

  test('E3: Free owners see upsells for Pro features but can still use the ledger', async ({ page }) => {
    await baseRoutes(page, false, async (route, path, method) => {
      if (path === '/api/expenses' && method === 'GET') { await route.fulfill({ json: listResponse([]) }); return true; }
      if (path === '/api/recurring-expenses') { await route.fulfill({ json: [] }); return true; }
      if (path.startsWith('/api/reports/profit-loss') || path.startsWith('/api/reports/electricity')) { await route.fulfill({ status: 403, json: { message: 'Báo cáo lãi/lỗ dành cho gói Pro. Vui lòng nâng cấp để sử dụng.' } }); return true; }
      if (path.startsWith('/api/reports/')) { await route.fulfill({ json: { items: [], page: 1, pageSize: 50, totalCount: 0, totals: { invoiceCash: 0, depositCash: 0, totalCash: 0 } } }); return true; }
      return false;
    });
    await page.goto('/expenses');
    await expect(page.getByRole('button', { name: 'Thêm khoản chi' })).toBeEnabled();
    await page.getByRole('tab', { name: /Chi định kỳ/ }).click();
    await expect(page.getByText('Chi định kỳ tự động với gói Pro')).toBeVisible();

    await page.goto('/reports');
    await page.getByRole('tab', { name: /Lãi\/lỗ/ }).click();
    await expect(page.getByText('Xem lãi ròng theo tháng với gói Pro')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Xuất Excel' })).toBeDisabled();
    await page.getByRole('tab', { name: /Đối chiếu điện/ }).click();
    await expect(page.getByText('Đối chiếu điện EVN với gói Pro')).toBeVisible();
  });

  test('E4: Pro profit/loss and electricity reconciliation render totals and missing data honestly', async ({ page }) => {
    await baseRoutes(page, true, async (route, path) => {
      if (path === '/api/reports/profit-loss') {
        await route.fulfill({ json: {
          dateFrom: '2026-08-01', dateTo: '2026-09-30', buildingId: null,
          totals: { income: 1500000, expenses: 1900000, net: -400000 },
          months: [{ month: '2026-08', income: 1000000, expenses: 300000, net: 700000 }, { month: '2026-09', income: 500000, expenses: 1600000, net: -1100000 }],
          expensesByCategory: [{ category: 'Repair', amount: 1900000, count: 2 }], sharedExpensesExcluded: 0,
        } }); return true;
      }
      if (path === '/api/reports/electricity-reconciliation') {
        await route.fulfill({ json: { periodFrom: '2026-08', periodTo: '2026-09', buildingId: null, rows: [
          { buildingId: building.id, buildingName: building.name, period: '2026-09', evnKwh: null, evnAmount: 0, evnBillCount: 0, billedKwh: 120, billedAmount: 420000, lossKwh: null, lossPercent: null, lossNullReason: 'NoEvnBill', margin: 420000, draftInvoiceCount: 0 },
          { buildingId: building.id, buildingName: building.name, period: '2026-08', evnKwh: 500, evnAmount: 1500000, evnBillCount: 2, billedKwh: 400, billedAmount: 1400000, lossKwh: 100, lossPercent: 20, lossNullReason: null, margin: -100000, draftInvoiceCount: 1 },
        ] } }); return true;
      }
      if (path.startsWith('/api/reports/')) { await route.fulfill({ json: { items: [], page: 1, pageSize: 50, totalCount: 0, totals: { invoiceCash: 0, depositCash: 0, totalCash: 0 } } }); return true; }
      return false;
    });
    await page.goto('/reports');
    await page.getByRole('tab', { name: /Lãi\/lỗ/ }).click();
    const panel = page.locator('.profit-loss-panel');
    await expect(panel.locator('.report-total-accent .profit-negative')).toContainText('400.000');
    await expect(panel.getByText('Sửa chữa, bảo trì')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Xuất Excel' })).toBeEnabled();

    await page.getByRole('tab', { name: /Đối chiếu điện/ }).click();
    const electricity = page.locator('.electricity-panel');
    await expect(electricity.getByText('Chưa có dữ liệu').first()).toBeVisible();
    await expect(electricity.getByText(/20\.00%/).first()).toBeVisible();
    await expect(electricity.getByText('1 nháp').first()).toBeVisible();
  });
});

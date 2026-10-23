import { test, expect } from '@playwright/test';
import type { OwnerRequest, User } from '../src/api';

const adminUser: User = {
  id: 'fixture-admin-01',
  name: 'Quản Trị Viên',
  email: 'admin@roomie.vn',
  role: 'Admin',
  status: 'Approved',
};

test.describe('Admin Workflows (Quản trị viên)', () => {
  test.describe.configure({ mode: 'serial' });

  // Scenario A1: Admin Login & Gate
  test('Scenario A1: Admin Login & Gate (auto redirect to /admin/owners)', async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/login' && method === 'POST') {
        return route.fulfill({ json: adminUser });
      }
      if (path === '/api/auth/me') {
        return route.fulfill({ json: adminUser });
      }
      if (path === '/api/admin/owners') {
        return route.fulfill({
          json: {
            items: [],
            total: 0,
            pendingCount: 0,
          },
        });
      }
      return route.fulfill({ json: [] });
    });

    // 1. Visit /login
    await page.goto('/login');
    await page.locator('#email').fill('admin@roomie.vn');
    await page.locator('#password').fill('AdminSecret123!');
    await page.getByRole('button', { name: 'Đăng nhập' }).click();

    // 2. Verify auto redirect to /admin/owners
    await expect(page).toHaveURL(/admin\/owners/);

    // 3. Verify Admin Dashboard Header & Titles
    await expect(page.getByText('QUẢN TRỊ HỆ THỐNG')).toBeVisible();
    await expect(page.getByRole('heading', { level: 2, name: 'Tài khoản chủ trọ' })).toBeVisible();
    await expect(page.getByText('Yêu cầu cần xem xét')).toBeVisible();
  });

  // Scenario A2: Review & Approve Pending Owner
  test('Scenario A2: Review & Approve Pending Owner', async ({ page }) => {
    let owners: OwnerRequest[] = [
      {
        id: 'owner-01',
        name: 'Chủ Trọ Tân Phú',
        email: 'tanphu@example.com',
        phone: '0912345678',
        status: 'Pending',
        isPro: false,
        roomCount: 2,
        maxRooms: 5,
        createdAt: '2026-10-01T08:00:00Z',
      },
    ];

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/owners') {
        const pendingCount = owners.filter((o) => o.status === 'Pending').length;
        return route.fulfill({
          json: {
            items: owners,
            total: owners.length,
            pendingCount,
          },
        });
      }
      if (path.startsWith('/api/admin/owners/') && path.endsWith('/status') && method === 'POST') {
        const id = path.split('/')[4];
        const body = route.request().postDataJSON() as { status: 'Approved' | 'Rejected' | 'Suspended'; reason?: string };
        const owner = owners.find((o) => o.id === id);
        if (owner) {
          owner.status = body.status;
          owner.reason = body.reason ?? null;
        }
        return route.fulfill({ json: owner });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/admin/owners');
    await expect(page.getByRole('heading', { level: 2, name: 'Tài khoản chủ trọ' })).toBeVisible();

    // 1. Verify pending owner is listed with "Chờ duyệt"
    const ownerCard = page.locator('article.mobile-record, tr').filter({ hasText: 'Chủ Trọ Tân Phú' });
    await expect(ownerCard).toBeVisible();
    await expect(ownerCard.locator('.status-pending')).toHaveText('Chờ duyệt');

    // 2. Click "Duyệt" button
    await ownerCard.getByRole('button', { name: 'Duyệt' }).click();

    // 3. Confirm modal
    const confirmModal = page.locator('.ant-modal-confirm');
    await expect(confirmModal).toBeVisible();
    await expect(confirmModal).toContainText('Duyệt tài khoản Chủ Trọ Tân Phú?');
    await confirmModal.getByRole('button', { name: 'Duyệt tài khoản' }).click();
    await expect(confirmModal).toBeHidden();

    // 4. Verify status updates to "Đã duyệt" and button becomes "Tạm khóa"
    await expect(ownerCard.locator('.status-paid')).toHaveText('Đã duyệt');
    await expect(ownerCard.getByRole('button', { name: 'Tạm khóa' })).toBeVisible();
    await expect(ownerCard.getByRole('button', { name: 'Duyệt' })).toBeHidden();
  });

  // Scenario A3: Reject Owner with Mandatory Reason
  test('Scenario A3: Reject Owner with Mandatory Reason', async ({ page }) => {
    let owners: OwnerRequest[] = [
      {
        id: 'owner-02',
        name: 'Chủ Trọ Bình Chánh',
        email: 'binhchanh@example.com',
        phone: '0987654321',
        status: 'Pending',
        isPro: false,
        roomCount: 0,
        maxRooms: 5,
        createdAt: '2026-10-02T08:00:00Z',
      },
    ];

    let capturedReason = '';

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/owners') {
        const pendingCount = owners.filter((o) => o.status === 'Pending').length;
        return route.fulfill({
          json: {
            items: owners,
            total: owners.length,
            pendingCount,
          },
        });
      }
      if (path.startsWith('/api/admin/owners/') && path.endsWith('/status') && method === 'POST') {
        const id = path.split('/')[4];
        const body = route.request().postDataJSON() as { status: 'Approved' | 'Rejected' | 'Suspended'; reason?: string };
        capturedReason = body.reason || '';
        const owner = owners.find((o) => o.id === id);
        if (owner) {
          owner.status = body.status;
          owner.reason = body.reason ?? null;
        }
        return route.fulfill({ json: owner });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/admin/owners');
    const ownerCard = page.locator('article.mobile-record, tr').filter({ hasText: 'Chủ Trọ Bình Chánh' });
    await expect(ownerCard).toBeVisible();

    // 1. Click "Từ chối" button
    await ownerCard.getByRole('button', { name: 'Từ chối' }).click();

    // 2. Reject Modal opens
    const rejectModal = page.locator('.ant-modal').filter({ hasText: 'Từ chối yêu cầu đăng ký' });
    await expect(rejectModal).toBeVisible();

    // 3. Fill mandatory rejection reason
    const reasonInput = rejectModal.getByLabel('Lý do');
    await reasonInput.fill('Thông tin liên hệ không chính xác');
    await rejectModal.getByRole('button', { name: 'Xác nhận' }).click();
    await expect(rejectModal).toBeHidden();

    // 4. Verify request captured reason and status updated to "Từ chối"
    expect(capturedReason).toBe('Thông tin liên hệ không chính xác');
    await expect(ownerCard.locator('.status-unpaid')).toHaveText('Từ chối');
    await expect(ownerCard.getByRole('button', { name: 'Duyệt lại' })).toBeVisible();
  });

  // Scenario A4: Manage Owner Subscription
  test('Scenario A4: Manage Owner Subscription (upgrade to Pro 30 days)', async ({ page }) => {
    let owners: OwnerRequest[] = [
      {
        id: 'owner-03',
        name: 'Chủ Trọ Bình Thạnh',
        email: 'binhthanh@example.com',
        phone: '0903334444',
        status: 'Approved',
        isPro: false,
        roomCount: 5,
        maxRooms: 5,
        createdAt: '2026-10-01T08:00:00Z',
      },
    ];

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/owners') {
        return route.fulfill({
          json: {
            items: owners,
            total: owners.length,
            pendingCount: 0,
          },
        });
      }
      if (path.startsWith('/api/admin/owners/') && path.endsWith('/subscription') && method === 'POST') {
        const id = path.split('/')[4];
        const body = route.request().postDataJSON() as { tier: 'Pro' | 'Free'; days?: number; reason?: string };
        const owner = owners.find((o) => o.id === id);
        if (owner) {
          owner.isPro = body.tier === 'Pro';
          owner.subscriptionTier = body.tier;
          owner.subscriptionExpiresAt = '2026-11-16T10:00:00Z';
          owner.maxRooms = body.tier === 'Pro' ? null : 5;
        }
        return route.fulfill({ json: owner });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/admin/owners');
    const ownerCard = page.locator('article.mobile-record, tr').filter({ hasText: 'Chủ Trọ Bình Thạnh' });
    await expect(ownerCard).toBeVisible();

    // 1. Verify initial Free tier
    await expect(ownerCard.getByText('Miễn phí')).toBeVisible();
    await expect(ownerCard.getByText('5 / 5')).toBeVisible();

    // 2. Open action dropdown and click "Gói cước"
    await ownerCard.getByRole('button', { name: 'Tác vụ Chủ Trọ Bình Thạnh' }).click();
    await page.getByRole('menuitem', { name: 'Gói cước' }).click();

    // 3. Subscription modal opens
    const subModal = page.locator('.ant-modal').filter({ hasText: 'Quản lý gói cước · Chủ Trọ Bình Thạnh' });
    await expect(subModal).toBeVisible();

    // Radio Pro and 30 days are default; submit
    await subModal.getByRole('button', { name: 'Cập nhật gói cước' }).click();
    await expect(subModal).toBeHidden();

    // 4. Verify tag updates to gold "PRO" and room limit is "∞"
    await expect(ownerCard.locator('.ant-tag-gold')).toHaveText('PRO');
    await expect(ownerCard.getByText('5 / ∞')).toBeVisible();
  });

  // Scenario A5: Admin Promotions & Voucher Management (/admin/promotions)
  test('Scenario A5: Admin Promotions & Voucher Management (welcome campaign, create, edit, toggle, delete)', async ({ page }) => {
    let welcomePromo = {
      id: 'welcome',
      name: 'Tặng Pro người đăng ký mới',
      isEnabled: true,
      rewardMonths: 3,
      startsAt: null as string | null,
      endsAt: null as string | null,
    };

    let codes = [
      {
        id: 'code-01',
        code: 'CHAO2026',
        description: 'Ưu đãi chào năm mới',
        rewardMonths: 2,
        maxRedemptions: 20,
        redeemedCount: 5,
        isActive: true,
        expiresAt: null as string | null,
        createdAt: '2026-10-01T08:00:00Z',
      },
    ];

    let capturedWelcomeUpdate: Record<string, unknown> | null = null;
    let capturedCreatedCode: Record<string, unknown> | null = null;

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/promotions' && method === 'GET') {
        return route.fulfill({ json: { welcome: welcomePromo, codes } });
      }
      if (path === '/api/admin/promotions/welcome' && method === 'PUT') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        capturedWelcomeUpdate = body;
        welcomePromo = {
          ...welcomePromo,
          isEnabled: Boolean(body.isEnabled),
          rewardMonths: Number(body.rewardMonths),
        };
        return route.fulfill({ json: welcomePromo });
      }
      if (path === '/api/admin/promotions/codes' && method === 'POST') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        capturedCreatedCode = body;
        const created = {
          id: 'code-02',
          code: String(body.code),
          description: (body.description as string) || '',
          rewardMonths: Number(body.rewardMonths),
          maxRedemptions: (body.maxRedemptions as number | null) ?? null,
          redeemedCount: 0,
          isActive: Boolean(body.isActive ?? true),
          expiresAt: (body.expiresAt as string | null) ?? null,
          createdAt: '2026-10-23T08:00:00Z',
        };
        codes = [created, ...codes];
        return route.fulfill({ status: 201, json: created });
      }
      if (path.startsWith('/api/admin/promotions/codes/') && method === 'PATCH') {
        const id = path.split('/').pop()!;
        const body = route.request().postDataJSON() as Record<string, unknown>;
        codes = codes.map((c) =>
          c.id === id
            ? {
                ...c,
                description: (body.description as string) ?? c.description,
                rewardMonths: Number(body.rewardMonths ?? c.rewardMonths),
                maxRedemptions: (body.maxRedemptions as number | null) ?? null,
                isActive: Boolean(body.isActive),
              }
            : c,
        );
        const updated = codes.find((c) => c.id === id);
        return route.fulfill({ json: updated });
      }
      if (path.startsWith('/api/admin/promotions/codes/') && method === 'DELETE') {
        const id = path.split('/').pop()!;
        codes = codes.filter((c) => c.id !== id);
        return route.fulfill({ status: 204, body: '' });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/admin/promotions');
    await expect(page.getByRole('heading', { name: 'Chương trình Khuyến mãi' })).toBeVisible();
    await expect(page.getByText('CHAO2026')).toBeVisible();
    await expect(page.getByText('+2 tháng Pro')).toBeVisible();

    // 1. Update welcome campaign rewardMonths from 3 -> 6
    const welcomeMonthInput = page.getByLabel('Số tháng Pro tặng kèm').first();
    await welcomeMonthInput.fill('6');
    await page.getByRole('button', { name: 'Lưu cấu hình chiến dịch' }).click();
    await expect(page.getByText('Đã cập nhật chiến dịch chào mừng thành công.')).toBeVisible();
    expect(capturedWelcomeUpdate?.rewardMonths).toBe(6);

    // 2. Open "Tạo mã mới" modal and test form validation (< 3 chars rejected)
    await page.getByRole('button', { name: 'Tạo mã mới' }).click();
    const modal = page.locator('.ant-modal').filter({ hasText: 'Tạo mã khuyến mãi mới' });
    await expect(modal).toBeVisible();

    const codeInput = modal.getByLabel('Mã khuyến mãi');
    await codeInput.fill('ab');
    await modal.getByRole('button', { name: 'Tạo mã' }).click();
    await expect(modal.getByText('Mã từ 3 đến 32 ký tự.')).toBeVisible();

    // 3. Fill valid lowercase code (auto-normalized to uppercase) and create
    await codeInput.fill('tet2026');
    await expect(codeInput).toHaveValue('TET2026');
    await modal.getByLabel('Ghi chú').fill('Chiến dịch Tết 2026');
    await modal.getByLabel('Số tháng Pro tặng kèm').fill('3');
    await modal.getByLabel('Giới hạn lượt dùng').fill('50');
    await modal.getByRole('button', { name: 'Tạo mã' }).click();

    await expect(modal).toBeHidden();
    expect(capturedCreatedCode?.code).toBe('TET2026');
    expect(capturedCreatedCode?.rewardMonths).toBe(3);
    expect(capturedCreatedCode?.maxRedemptions).toBe(50);
    await expect(page.getByText('TET2026')).toBeVisible();
    await expect(page.getByText('+3 tháng Pro')).toBeVisible();

    // 4. Edit existing voucher code
    await page.getByRole('button', { name: 'Sửa mã TET2026' }).click();
    const editModal = page.locator('.ant-modal').filter({ hasText: 'Sửa mã khuyến mãi' });
    await expect(editModal).toBeVisible();
    await expect(editModal.getByLabel('Mã khuyến mãi')).toBeDisabled();
    await editModal.getByLabel('Ghi chú').fill('Chiến dịch Tết Bính Ngọ 2026');
    await editModal.getByRole('button', { name: 'Lưu thay đổi' }).click();
    await expect(editModal).toBeHidden();
    await expect(page.getByText('Chiến dịch Tết Bính Ngọ 2026')).toBeVisible();

    // 5. Delete voucher code via Popconfirm
    await page.getByRole('button', { name: 'Xóa mã TET2026' }).click();
    const popconfirm = page.locator('.ant-popover').filter({ hasText: 'Xóa mã khuyến mãi này?' });
    await expect(popconfirm).toBeVisible();
    await popconfirm.getByRole('button', { name: 'Xóa' }).click();
    await expect(page.getByText('TET2026')).toBeHidden();
  });
});

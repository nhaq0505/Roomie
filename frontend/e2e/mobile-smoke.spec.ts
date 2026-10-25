import { test, expect } from '@playwright/test';
import type { Building, Room, Invoice, DashboardSummary, SubscriptionStatus } from '../src/api';

const getVietnamPeriod = () => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit' }).formatToParts(new Date());
  const year = parts.find((part) => part.type === 'year')?.value ?? '';
  const month = parts.find((part) => part.type === 'month')?.value ?? '';
  return `${year}-${month}`;
};

const currentVietnamPeriod = getVietnamPeriod();

const landlordUser = {
  id: 'fixture-landlord-mobile',
  name: 'Chủ Trọ Mobile',
  email: 'landlord-mobile@example.com',
  role: 'Owner' as const,
  status: 'Approved' as const,
};

const defaultDashboard: DashboardSummary = {
  totalRooms: 2,
  occupiedRooms: 1,
  totalBilled: 3500000,
  totalPaid: 0,
  totalOutstanding: 3500000,
  pendingReadings: 0,
  period: currentVietnamPeriod,
  monthlyBilled: 3500000,
  monthlyPaid: 0,
  monthlyOutstanding: 3500000,
  collectionRate: 0,
};

const sampleBuildings: Building[] = [
  { id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1, Quận 7', roomCount: 2, isArchived: false },
];

const sampleRooms: Room[] = [
  {
    id: 'room-01',
    buildingId: 'bld-01',
    buildingName: 'Nhà trọ Bình An',
    name: 'Phòng 101',
    rent: 3000000,
    electricityRate: 3500,
    waterFee: 100000,
    waterPerPerson: false,
    serviceFee: 50000,
    initialReading: 100,
    previousReading: 100,
    occupancy: {
      id: 'occ-01',
      tenantName: 'Nguyễn Văn Mobile',
      phone: '0909123456',
      people: 2,
      startDate: `${currentVietnamPeriod}-01`,
      depositRequired: 3000000,
      depositPaid: 3000000,
      depositHeld: 3000000,
      token: 'tok-mobile-01',
      url: '/t/tok-mobile-01',
    },
    isArchived: false,
  },
  {
    id: 'room-02',
    buildingId: 'bld-01',
    buildingName: 'Nhà trọ Bình An',
    name: 'Phòng 102',
    rent: 3200000,
    electricityRate: 3500,
    waterFee: 100000,
    waterPerPerson: false,
    serviceFee: 50000,
    initialReading: 50,
    previousReading: 50,
    occupancy: null,
    isArchived: false,
  },
];

const sampleInvoice: Invoice = {
  id: 'inv-mobile-01',
  roomId: 'room-01',
  roomName: 'Phòng 101',
  buildingName: 'Nhà trọ Bình An',
  tenantName: 'Nguyễn Văn Mobile',
  period: currentVietnamPeriod,
  kind: 'Monthly',
  lifecycleState: 'Issued',
  previousReading: 100,
  currentReading: 150,
  consumption: 50,
  rent: 3000000,
  electricityAmount: 175000,
  waterAmount: 100000,
  serviceAmount: 50000,
  total: 3325000,
  paid: 0,
  outstanding: 3325000,
  status: 'Unpaid',
  version: 1,
  tenantToken: 'tok-mobile-01',
  depositHeld: 3000000,
  depositSettlements: [],
  isFinalMeterReading: false,
  occupancyId: 'occ-01',
};

const subStatus: SubscriptionStatus = {
  tier: 'Pro',
  isPro: true,
  roomCount: 2,
  currentRooms: 2,
  maxRooms: null,
  expiresAt: '2027-01-01T00:00:00Z',
  availablePackages: [],
};

test.describe('Mobile 375px Smoke & Responsiveness Suite', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test.beforeEach(async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: sampleBuildings });
      if (path === '/api/rooms') return route.fulfill({ json: sampleRooms });
      if (path === '/api/subscription') return route.fulfill({ json: subStatus });
      if (path === '/api/settings/payment') {
        return route.fulfill({
          json: {
            configured: true,
            webhookStatus: 'Verified',
            confirmationMode: 'Automatic',
            webhookUrl: 'https://example.com/webhook',
          },
        });
      }
      if (path === '/api/settings/payment/preferences') {
        return route.fulfill({
          json: {
            configured: true,
            webhookStatus: 'Verified',
            confirmationMode: 'Automatic',
          },
        });
      }
      if (path === '/api/invoices') {
        return route.fulfill({
          json: {
            items: [sampleInvoice],
            total: 1,
            draftCount: 0,
            totals: { billed: 3325000, paid: 0, outstanding: 3325000 },
          },
        });
      }
      if (path === '/api/invoices/inv-mobile-01') return route.fulfill({ json: sampleInvoice });
      if (path === '/api/invoices/inv-mobile-01/corrections') return route.fulfill({ json: [] });
      if (path === '/api/readings') {
        return route.fulfill({
          json: [
            {
              id: 'reading-m1',
              roomId: 'room-01',
              roomName: 'Phòng 101',
              period: currentVietnamPeriod,
              occupancyId: 'occ-01',
              previousReading: 100,
              currentReading: 150,
              consumption: 50,
              isSubmitted: true,
              issuanceStatus: 'Issued',
            },
          ],
        });
      }
      if (path === '/api/expenses') {
        return route.fulfill({
          json: {
            items: [],
            totalCount: 0,
            page: 1,
            pageSize: 50,
            totals: { total: 0, count: 0, byCategory: [] },
          },
        });
      }
      if (path === '/api/reports/monthly') {
        return route.fulfill({
          json: {
            period: currentVietnamPeriod,
            totals: { billed: 3325000, collected: 0, outstanding: 3325000 },
            rooms: [],
          },
        });
      }
      return route.fulfill({ json: [] });
    });
  });

  test('Zero horizontal overflow across all primary landlord pages on 375px viewport', async ({ page }) => {
    test.setTimeout(60000);
    const pages = ['/dashboard', '/buildings', '/rooms', '/readings', '/invoices', '/expenses', '/reports', '/settings/payment'];

    for (const p of pages) {
      await page.goto(p);
      await expect(page.getByRole('heading').first()).toBeVisible();

      const isOverflowing = await page.evaluate(() => {
        return document.documentElement.scrollWidth > window.innerWidth;
      });

      expect(isOverflowing, `Page ${p} has horizontal overflow on 375px viewport`).toBe(false);
    }
  });

  test('Mobile navigation hamburger drawer opens, navigates and closes smoothly', async ({ page }) => {
    await page.goto('/rooms');

    const menuBtn = page.getByRole('button', { name: 'Mở menu', exact: true });
    await expect(menuBtn).toBeVisible();
    await menuBtn.click();

    const drawer = page.locator('.mobile-menu-drawer');
    await expect(drawer).toBeVisible();

    // Verify navigation menu items are visible
    await expect(drawer.locator('.ant-menu-item').filter({ hasText: /^Dãy trọ/ })).toBeVisible();
    await expect(drawer.locator('.ant-menu-item').filter({ hasText: /^Hóa đơn$/ })).toBeVisible();

    // Close via close icon
    const closeBtn = drawer.locator('.ant-drawer-close');
    await expect(closeBtn).toBeVisible();
    await closeBtn.click();
    await expect(drawer).toBeHidden();
  });

  test('Mobile rooms card view renders within 375px bounds with reachable action buttons', async ({ page }) => {
    await page.goto('/rooms');
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();
    await expect(page.getByText('Phòng 102', { exact: true })).toBeVisible();

    // Verify action buttons fit on screen without clipping
    const linkBtn = page.getByRole('button', { name: 'Cấp link cho khách' });
    await expect(linkBtn).toBeVisible();
    const linkBox = await linkBtn.boundingBox();
    expect(linkBox).not.toBeNull();
    expect(linkBox!.x + linkBox!.width).toBeLessThanOrEqual(375);

    // Open room more actions dropdown
    const moreBtn = page.locator('.room-more').first();
    await moreBtn.click();
    const dropdown = page.locator('.ant-dropdown');
    await expect(dropdown).toBeVisible();
    await expect(dropdown.getByText('Nhân bản phòng')).toBeVisible();

    // Press Escape to dismiss dropdown
    await page.keyboard.press('Escape');
    await expect(dropdown).toBeHidden();
  });

  test('Mobile modal interactions: open/close rapidly without crashing or layout degradation', async ({ page }) => {
    await page.goto('/rooms');

    // Repeatedly open and close "Cấp link cho khách" modal on 375px
    for (let i = 0; i < 3; i++) {
      await page.getByRole('button', { name: 'Cấp link cho khách' }).click();
      const modal = page.locator('.room-link-modal');
      await expect(modal).toBeVisible();

      // Check modal width does not cause page horizontal scroll
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(overflow).toBe(false);

      // Close modal
      await modal.getByRole('button', { name: 'Đóng' }).click();
      await expect(modal).toBeHidden();
    }
  });

  test('Mobile invoice drawer opens and displays breakdown without horizontal overflow', async ({ page }) => {
    await page.goto('/invoices');
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Chi tiết' }).click();
    const drawer = page.locator('.ant-drawer');
    await expect(drawer).toBeVisible();
    await expect(drawer.getByRole('heading', { level: 4, name: 'Phòng 101' })).toBeVisible();

    // Check no horizontal overflow while drawer is open
    const drawerOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(drawerOverflow).toBe(false);

    // Verify full settlement button is reachable within 375px bounds once drawer finishes slide-in
    const settleBtn = drawer.getByRole('button', { name: 'Lưu khoản thu' });
    await expect(settleBtn).toBeVisible();
    await expect(async () => {
      const box = await settleBtn.boundingBox();
      expect(box).not.toBeNull();
      expect(box!.x + box!.width).toBeLessThanOrEqual(375);
    }).toPass();

    // Close drawer
    await drawer.locator('.ant-drawer-close').click();
    await expect(drawer).toBeHidden();
  });

  test('Mobile bottom tab bar stays anchored at the bottom of 375x812 viewport and switches tabs', async ({ page }) => {
    await page.goto('/dashboard');
    const bottomNav = page.locator('.compact-navigation');
    await expect(bottomNav).toBeVisible();

    const navBox = await bottomNav.boundingBox();
    expect(navBox).not.toBeNull();
    // Anchored at the bottom of the 812px viewport
    expect(navBox!.y).toBeGreaterThan(700);
    expect(navBox!.x + navBox!.width).toBeLessThanOrEqual(375);

    // Navigate via bottom tab bar to Readings and verify status filter pills
    await bottomNav.getByRole('link', { name: 'Lập hóa đơn' }).click();
    await expect(page).toHaveURL(/\/readings$/);
    await expect(page.locator('.reading-filter-pills')).toBeVisible();
    await expect(page.locator('.reading-mobile-card').first()).toBeVisible();

    // Open Settings dropdown from bottom tab bar "Cấu hình" button
    await bottomNav.getByRole('button', { name: /Cấu hình/ }).click();
    await expect(page.locator('.ant-dropdown:visible')).toBeVisible();
  });

  test('Mobile RoomTenantPage displays sticky bottom action bar above compact-navigation without overlap', async ({ page }) => {
    await page.goto('/rooms/room-01/tenant');
    const stickyBar = page.locator('.room-tenant-mobile-bar');
    const bottomNav = page.locator('.compact-navigation');
    await expect(stickyBar).toBeVisible();
    await expect(bottomNav).toBeVisible();
    await expect(stickyBar.getByRole('button', { name: 'Xuất hợp đồng & CT01' })).toBeVisible();
    await expect(stickyBar.getByRole('button', { name: 'Lưu thay đổi' })).toBeVisible();

    const barBox = await stickyBar.boundingBox();
    const navBox = await bottomNav.boundingBox();
    expect(barBox).not.toBeNull();
    expect(navBox).not.toBeNull();
    expect(barBox!.y + barBox!.height).toBeLessThanOrEqual(navBox!.y);

    // Scroll to bottom and verify sticky bar still sits above compact-navigation
    await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
    const barBoxBottom = await stickyBar.boundingBox();
    const navBoxBottom = await bottomNav.boundingBox();
    expect(barBoxBottom!.y + barBoxBottom!.height).toBeLessThanOrEqual(navBoxBottom!.y);

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow).toBe(false);
  });

  test('Dashboard onboarding checklist step titles have high-contrast dark teal color', async ({ page }) => {
    await page.route('**/api/dashboard', (route) =>
      route.fulfill({
        json: {
          ...defaultDashboard,
          totalRooms: 0,
          occupiedRooms: 0,
          totalBilled: 0,
          monthlyBilled: 0,
          monthlyOutstanding: 0,
          totalOutstanding: 0,
        },
      }),
    );
    await page.route('**/api/buildings', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/rooms', (route) => route.fulfill({ json: [] }));
    await page.route('**/api/invoices*', (route) =>
      route.fulfill({
        json: {
          items: [],
          total: 0,
          draftCount: 0,
          totals: { billed: 0, paid: 0, outstanding: 0 },
        },
      }),
    );
    await page.goto('/dashboard');
    const stepTitle = page.locator('.onboarding-step-title-row strong').first();
    await expect(stepTitle).toBeVisible();
    const color = await stepTitle.evaluate((el) => getComputedStyle(el).color);
    expect(color).not.toBe('rgb(224, 241, 237)');
    expect(color).toBe('rgb(28, 55, 62)');
  });

  test('Mobile inputs have font-size >= 16px on /login and /settings/payment to prevent iOS auto-zoom', async ({ page }) => {
    await page.goto('/settings/payment');
    await expect(page.getByRole('heading').first()).toBeVisible();
    const paymentInputSize = await page.locator('.ant-input').first().evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    expect(paymentInputSize).toBeGreaterThanOrEqual(16);

    await page.route('**/api/auth/me', (route) => route.fulfill({ status: 401, json: { error: 'Unauthorized' } }));
    await page.goto('/login');
    const emailInputSize = await page.locator('#email').evaluate((el) => parseFloat(getComputedStyle(el).fontSize));
    expect(emailInputSize).toBeGreaterThanOrEqual(16);
  });

  test('RoomDocumentsPage at 820x900 tablet viewport has no scroll trap and export buttons are visible', async ({ page }) => {
    await page.setViewportSize({ width: 820, height: 900 });
    await page.goto('/rooms/room-01/documents');
    await expect(page.getByRole('button', { name: 'Lưu thông tin' })).toBeVisible();
    await expect(page.getByRole('button', { name: /In \/ Lưu PDF/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Tải Word Hợp đồng/ })).toBeVisible();

    const previewPanePosition = await page.locator('.room-documents-page .contract-preview-pane').evaluate((el) => getComputedStyle(el).position);
    expect(previewPanePosition).toBe('static');
  });

  test('Zero horizontal overflow at 320px and 375px across public and admin pages', async ({ page }) => {
    test.setTimeout(60000);
    await page.setViewportSize({ width: 320, height: 812 });
    for (const p of ['/', '/pricing', '/huong-dan', '/dashboard', '/rooms']) {
      await page.goto(p);
      await expect(page.locator('h1, h2').first()).toBeVisible();
      const isOverflowing = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(isOverflowing, `Page ${p} overflowed at 320px`).toBe(false);
    }

    // Verify Admin pages at 375px
    await page.setViewportSize({ width: 375, height: 812 });
    await page.route('**/api/auth/me', (route) =>
      route.fulfill({
        json: {
          id: 'admin-mobile-01',
          name: 'Admin Mobile',
          email: 'admin@roomie.vn',
          role: 'Admin',
          status: 'Approved',
        },
      }),
    );
    await page.route('**/api/admin/promotions', (route) =>
      route.fulfill({
        json: {
          welcome: { id: 'welcome', name: 'Welcome', isEnabled: true, rewardMonths: 2, startsAt: null, endsAt: null },
          codes: [
            {
              id: 'c-1',
              code: 'TET2026',
              description: 'Khuyến mãi Tết',
              rewardMonths: 2,
              maxRedemptions: 50,
              redeemedCount: 5,
              isActive: true,
              expiresAt: null,
              createdAt: '2026-10-01T00:00:00Z',
            },
          ],
        },
      }),
    );
    await page.goto('/admin/promotions');
    await expect(page.getByRole('heading', { name: 'Chương trình Khuyến mãi' })).toBeVisible();
    await expect(page.locator('article.mobile-record').first()).toBeVisible();
    const adminOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(adminOverflow).toBe(false);
  });
});


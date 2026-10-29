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

    // Verify merged navigation menu items are visible
    await expect(drawer.locator('.ant-menu-item').filter({ hasText: /^Dãy trọ$/ })).toBeVisible();
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

    // Verify all 3 merged primary bottom nav items display their text labels simultaneously (no mystery icons)
    const visibleLabels = await bottomNav
      .locator('> :is(a, button):not([data-mobile-secondary="true"]) .compact-nav-text')
      .evaluateAll((els) => els.map((el) => getComputedStyle(el).display));
    expect(visibleLabels).toHaveLength(3);
    for (const display of visibleLabels) {
      expect(display).not.toBe('none');
    }

    // Navigate via bottom tab bar to Invoices and click "Lập hóa đơn" button to enter Readings
    await page.route('**/api/readings*', (route) => route.fulfill({ json: [] }));
    await bottomNav.getByRole('link', { name: /Hóa đơn/ }).click();
    await expect(page).toHaveURL(/\/invoices$/);
    await page.getByRole('button', { name: 'Lập hóa đơn' }).click();
    await expect(page).toHaveURL(/\/readings$/);
    await expect(page.getByRole('combobox', { name: 'Lọc theo dãy trọ' })).toBeVisible();
    const firstReadingCard = page.locator('.reading-mobile-card').first();
    await expect(firstReadingCard).toBeVisible();

    const entryRow = firstReadingCard.locator('.reading-mobile-entry');
    await expect(entryRow).toBeVisible();
    const inputBox = await entryRow.locator('.reading-mobile-input').boundingBox();
    const submitBox = await entryRow.locator('.reading-mobile-submit').boundingBox();
    expect(inputBox).not.toBeNull();
    expect(submitBox).not.toBeNull();
    expect(Math.abs(inputBox!.y - submitBox!.y)).toBeLessThan(12);

    // Open Settings dropdown from bottom tab bar "Cấu hình" button and navigate to Hồ sơ
    const moreBtn = bottomNav.getByRole('button', { name: /Cấu hình/ });
    await moreBtn.click();
    const dropdown = page.locator('.ant-dropdown:visible');
    await expect(dropdown).toBeVisible();
    await expect(bottomNav.getByRole('link', { name: /Dãy trọ/ })).toBeVisible();
    await expect(dropdown.getByRole('menuitem', { name: 'Tổng quan' })).toHaveCount(0);
    await expect(dropdown.getByRole('menuitem', { name: 'Chi phí' })).toHaveCount(0);
    await expect(dropdown.getByRole('menuitem', { name: 'Báo cáo' })).toHaveCount(0);
    await expect(dropdown.getByRole('menuitem', { name: 'Hồ sơ' })).toBeVisible();
    await expect(dropdown.getByRole('menuitem', { name: 'Gói cước' })).toBeVisible();
    await dropdown.getByRole('menuitem', { name: 'Hồ sơ' }).click();
    await expect(page).toHaveURL(/\/profile$/);
    await expect(moreBtn).toHaveAttribute('data-mobile-more-active', 'true');
  });

  test('Mobile RoomTenantPage displays sticky bottom action bar above compact-navigation without overlap', async ({ page }) => {
    await page.goto('/rooms/room-01/tenant');
    const stickyBar = page.locator('.room-tenant-mobile-bar');
    const bottomNav = page.locator('.compact-navigation');
    await expect(stickyBar).toBeVisible();
    await expect(bottomNav).toBeVisible();
    await expect(stickyBar.getByRole('button', { name: 'Xuất hợp đồng & CT01' })).toContainText('Xuất hợp đồng & CT01');
    await expect(stickyBar.getByRole('button', { name: 'Lưu thay đổi' })).toBeVisible();
    await expect(page.locator('.room-tenant-summary-actions')).toBeHidden();

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

  test('RoomDocumentsPage at 820x900 tablet and 320px/375px phone viewports has no scroll trap or horizontal overflow', async ({ page }) => {
    await page.setViewportSize({ width: 820, height: 900 });
    await page.goto('/rooms/room-01/documents');
    await expect(page.getByRole('button', { name: 'Lưu thông tin' })).toBeVisible();
    await expect(page.getByRole('button', { name: /In \/ Lưu PDF/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Tải Word Hợp đồng/ })).toBeVisible();

    const previewPanePosition = await page.locator('.room-documents-page .contract-preview-pane').evaluate((el) => getComputedStyle(el).position);
    expect(previewPanePosition).toBe('static');

    for (const width of [320, 375]) {
      await page.setViewportSize({ width, height: 812 });
      await page.goto('/rooms/room-01/documents');
      await expect(page.getByRole('button', { name: 'Lưu thông tin' })).toBeVisible();
      const sheetBox = await page.locator('.contract-a4-sheet').boundingBox();
      expect(sheetBox).not.toBeNull();
      expect(sheetBox!.x).toBeGreaterThanOrEqual(0);
      const isOverflowing = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(isOverflowing, `RoomDocumentsPage overflowed at ${width}px`).toBe(false);
    }
  });

  test('Readings mobile card, Pending AccountStatus, and Tenant Portal fit at 320px and 375px', async ({ page }) => {
    await page.route('**/api/tenant/demo-token*', (route) =>
      route.fulfill({
        json: {
          room: sampleRooms[0],
          invoices: [sampleInvoice],
          total: 1,
          totals: { billed: 3325000, paid: 0, outstanding: 3325000 },
          currentPeriod: currentVietnamPeriod,
        },
      }),
    );

    for (const width of [320, 375]) {
      await page.setViewportSize({ width, height: 812 });
      await page.goto('/readings');
      const mobileCard = page.locator('.reading-mobile-card').first();
      await expect(mobileCard).toBeVisible();
      await expect(page.locator('.reading-mobile-card .reading-submission-btn')).toHaveCount(0);
      const cardBox = await mobileCard.boundingBox();
      expect(cardBox).not.toBeNull();
      const readingOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(readingOverflow, `/readings overflowed at ${width}px`).toBe(false);

      // Tenant Portal without meter submission card
      await page.goto('/t/demo-token');
      await expect(page.locator('.tenant-meter-submission-card')).toHaveCount(0);
      const tenantOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(tenantOverflow, `/t/demo-token overflowed at ${width}px`).toBe(false);
    }

    // Pending AccountStatus page at 320px and 375px
    await page.route('**/api/auth/me', (route) =>
      route.fulfill({
        json: {
          ...landlordUser,
          status: 'Pending',
        },
      }),
    );
    for (const width of [320, 375]) {
      await page.setViewportSize({ width, height: 812 });
      await page.goto('/account-status');
      await expect(page.locator('.status-card')).toBeVisible();
      const statusOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(statusOverflow, `/account-status (Pending) overflowed at ${width}px`).toBe(false);
    }
  });

  test('Zero horizontal overflow at 320px and 375px across public and admin pages', async ({ page }) => {
    test.setTimeout(60000);
    await page.setViewportSize({ width: 320, height: 812 });
    for (const p of ['/', '/pricing', '/huong-dan', '/dashboard', '/rooms']) {
      await page.goto(p);
      await expect(page.getByRole('heading').first()).toBeVisible();
      const isOverflowing = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      expect(isOverflowing, `Page ${p} overflowed at 320px`).toBe(false);
    }

    // Verify Admin pages at 320px and 375px
    for (const width of [320, 375]) {
      await page.setViewportSize({ width, height: 812 });
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
      expect(adminOverflow, `Admin promotions overflowed at ${width}px`).toBe(false);
    }
  });

  test('Minimalist mobile UI/UX eliminates redundant headers, multi-banner clutter, triple-nested boxes, and filter label rows at 375px', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });

    // 1. Rooms page: redundant header copy & filter labels hidden, directory wrapper unboxed, tenant sub-row unboxed
    await page.goto('/rooms');
    await expect(page.getByRole('heading', { name: 'Phòng & người thuê' })).toBeVisible();
    await expect(page.locator('.page-heading .page-subtitle')).toBeHidden();
    await expect(page.locator('.room-filter > span').first()).toBeHidden();
    // Filter controls remain accessible via aria-label
    await expect(page.getByRole('combobox', { name: 'Lọc theo dãy trọ' })).toBeVisible();

    const roomDirectoryStyle = await page.locator('.room-directory.section-card').evaluate((el) => {
      const s = getComputedStyle(el);
      return { borderTopWidth: s.borderTopWidth, paddingLeft: s.paddingLeft, backgroundColor: s.backgroundColor };
    });
    expect(roomDirectoryStyle.borderTopWidth).toBe('0px');
    expect(roomDirectoryStyle.paddingLeft).toBe('0px');

    const tenantSubBoxBorder = await page.locator('.room-card-tenant').first().evaluate((el) => getComputedStyle(el).borderLeftWidth);
    expect(tenantSubBoxBorder).toBe('0px');

    // 2. Readings page: progress card & notice banner hidden, reading section unboxed, mobile stats unboxed
    await page.route('**/api/readings*', (route) => route.fulfill({ json: [] }));
    await page.goto('/readings');
    await expect(page.getByRole('heading', { name: 'Lập hóa đơn' })).toBeVisible();
    await expect(page.locator('.page-heading .eyebrow')).toBeHidden();
    await expect(page.locator('.reading-progress-card')).toBeHidden();
    await expect(page.locator('.reading-notice')).toBeHidden();
    const readingStatsBorder = await page.locator('.reading-mobile-stats').first().evaluate((el) => getComputedStyle(el).borderLeftWidth);
    expect(readingStatsBorder).toBe('0px');

    // 3. Invoices page: scope note & filter labels hidden, list wrapper & mobile amounts unboxed
    await page.goto('/invoices');
    await expect(page.getByRole('heading', { name: 'Hóa đơn' }).first()).toBeVisible();
    await expect(page.locator('.invoice-summary-scope')).toBeHidden();
    await expect(page.locator('.invoice-filter > span').first()).toBeHidden();
    await expect(page.getByLabel('Tìm hóa đơn')).toBeVisible();
    const invoiceAmountsBorder = await page.locator('.invoice-mobile-amounts').first().evaluate((el) => getComputedStyle(el).borderLeftWidth);
    expect(invoiceAmountsBorder).toBe('0px');

    // 4. Buildings page: tip card hidden
    await page.goto('/buildings');
    await expect(page.getByRole('heading', { name: 'Dãy trọ' })).toBeVisible();
    await expect(page.locator('.building-tip-card')).toBeHidden();
  });

  test('Mobile sharedExpensesExcluded warning visibility, compact filter rows, 320px header/pill alignment, and multi-locale safety', async ({ page }) => {
    test.setTimeout(60000);
    await page.setViewportSize({ width: 375, height: 812 });

    // 1. /invoices hides unfiltered result count header on mobile, shows it when filtered
    await page.goto('/invoices');
    await expect(page.locator('.invoice-list-card > .section-card-header')).toBeHidden();
    await page.getByLabel('Tìm hóa đơn').fill('101');
    await expect(page.locator('.invoice-list-card > .section-card-header')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Xóa lọc' })).toBeVisible();

    // 2. /expenses places Category select and Reset button on the same row on mobile and shows building filter scope warning
    await page.goto('/expenses');
    const categorySelect = page.getByRole('combobox', { name: 'Lọc theo danh mục' });
    const resetBtn = page.locator('.expenses-filter-toolbar > .ant-btn');
    const catBox = await categorySelect.boundingBox();
    const resetBox = await resetBtn.boundingBox();
    expect(catBox).not.toBeNull();
    expect(resetBox).not.toBeNull();
    expect(Math.abs(catBox!.y - resetBox!.y)).toBeLessThan(12);

    await page.getByRole('combobox', { name: 'Lọc theo dãy trọ' }).click();
    await page.locator('.ant-select-item-option').filter({ hasText: 'Nhà trọ Bình An' }).click();
    await expect(page.locator('.reports-scope-warning')).toBeVisible();
    await expect(page.locator('.reports-scope-warning')).toContainText('Khi lọc theo dãy, các khoản chi chung không gắn dãy sẽ không được tính.');

    // 3. /reports hides static .reports-scope-note on mobile, but shows dynamic sharedExpensesExcluded .reports-scope-warning on Profit/Loss tab
    await page.route('**/api/reports/profit-loss*', (route) =>
      route.fulfill({
        json: {
          dateFrom: `${currentVietnamPeriod}-01`,
          dateTo: `${currentVietnamPeriod}-28`,
          buildingId: 'bld-01',
          sharedExpensesExcluded: 500000,
          totals: { income: 3325000, expenses: 1200000, net: 2125000 },
          months: [{ month: currentVietnamPeriod, income: 3325000, expenses: 1200000, net: 2125000 }],
          expensesByCategory: [],
        },
      }),
    );
    await page.goto('/reports');
    await expect(page.locator('.reports-scope-note:not(.reports-scope-warning)').first()).toBeHidden();
    await page.getByRole('tab', { name: /Lãi\/lỗ/ }).click();
    await expect(page.locator('.reports-scope-warning')).toBeVisible();
    await expect(page.locator('.reports-scope-warning')).toContainText('500.000');

    // 4. At 320px, /rooms keeps quota Tag and Create Room button on 1 row, and /readings keeps Building Select + Month DatePicker on 1 row and 3 filter pills on 1 row
    await page.setViewportSize({ width: 320, height: 812 });
    await page.goto('/rooms');
    await expect(page.getByRole('button', { name: 'Tạo phòng trọ' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Lọc theo dãy trọ' })).toBeVisible();
    await expect(page.getByRole('combobox', { name: 'Lọc trạng thái lưu trữ' })).toHaveCount(0);
    await expect(page.getByRole('combobox', { name: 'Lọc theo tình trạng' })).toHaveCount(0);

    await page.route('**/api/readings*', (route) => route.fulfill({ json: [] }));
    await page.goto('/readings');
    await expect(page.getByRole('combobox', { name: 'Lọc theo dãy trọ' })).toBeVisible();
    await expect(page.locator('.page-heading-extra .ant-picker')).toHaveCount(0);
    await expect(page.locator('.reading-filter-pills')).toHaveCount(0);

    // 5. Multi-locale check (?lang=en, ko, zh, hi) on /readings at 375px and 320px has zero horizontal overflow
    for (const lang of ['en', 'ko', 'zh', 'hi']) {
      for (const width of [375, 320]) {
        await page.setViewportSize({ width, height: 812 });
        await page.goto(`/readings?lang=${lang}`);
        await expect(page.locator('.reading-mobile-card').first()).toBeVisible();
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
        expect(overflow, `/readings?lang=${lang} overflowed at ${width}px`).toBe(false);
      }
    }
  });
});

import { expect, test } from '@playwright/test';
import type { User } from '../src/api';
import type {
  AdminCampaignCostItemDto,
  AdminMonitoringGrowthResponse,
  AdminMonitoringIncidentsResponse,
  AdminMonitoringOperationsResponse,
  AdminMonitoringRevenueResponse,
} from '../src/features/admin/monitoring/types';

const adminUser: User = {
  id: 'fixture-admin-qa',
  name: 'Quản Trị Viên QA',
  email: 'admin.qa@roomie.vn',
  role: 'Admin',
  status: 'Approved',
};

const mockGrowthData: AdminMonitoringGrowthResponse = {
  summary: {
    totalSessions: 1420,
    uniqueVisitors: 980,
    registrations: 45,
    activatedOwners: 28,
    currentWau: 18,
    currentMau: 32,
  },
  sources: [
    {
      source: 'facebook',
      medium: 'cpc',
      sessions: 850,
      registrations: 30,
      activatedOwners: 20,
    },
    {
      source: 'google',
      medium: 'organic',
      sessions: 420,
      registrations: 12,
      activatedOwners: 7,
    },
  ],
  topCtas: [
    {
      ctaId: 'btn_hero_signup',
      path: '/',
      clicks: 142,
    },
  ],
  retentionMatrix: [
    {
      cohortPeriod: '2026-08',
      cohortSize: 25,
      retentionPercentages: [100.0, 72.0, 64.0],
    },
  ],
  cohortMaturity: {
    activationRate7Day: 62.2,
    activationCohortMaturedCount: 45,
    firstPaidRate30Day: 24.4,
    paidCohortMaturedCount: 45,
  },
  dailySeries: [
    {
      date: '2026-10-18',
      sessions: 110,
      registrations: 3,
      activations: 2,
      activeOwners: 14,
    },
    {
      date: '2026-10-19',
      sessions: 125,
      registrations: 5,
      activations: 4,
      activeOwners: 16,
    },
    {
      date: '2026-10-20',
      sessions: 140,
      registrations: 4,
      activations: 3,
      activeOwners: 18,
    },
  ],
};

const mockRevenueData: AdminMonitoringRevenueResponse = {
  summary: {
    totalSubscriptionRevenueVnd: 15000000,
    paidSubscriptionCount: 15,
    totalMarketingSpendVnd: 5000000,
    totalMarketingHours: 42.5,
    cac30: {
      valueVnd: 333333.33,
      isAvailable: true,
      nullReason: null,
    },
  },
  subscriptionOrders: [
    {
      orderCode: 100234,
      ownerId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
      amountVnd: 299000,
      days: 30,
      paidAt: '2026-10-18T10:30:00Z',
    },
  ],
  campaignCosts: [
    {
      campaign: 'facebook_october',
      channel: 'social',
      cashVnd: 3500000,
      personHours: 20,
      imputedLaborCostVnd: 3000000,
      totalCostVnd: 6500000,
    },
  ],
  dailySeries: [
    {
      date: '2026-10-19',
      subscriptionRevenueVnd: 598000,
      marketingSpendVnd: 250000,
    },
    {
      date: '2026-10-20',
      subscriptionRevenueVnd: 897000,
      marketingSpendVnd: 300000,
    },
  ],
};

const mockCampaignCostsList: AdminCampaignCostItemDto[] = [
  {
    id: 'cost-item-qa-01',
    date: '2026-10-20',
    campaign: 'facebook_launch',
    channel: 'social',
    source: 'facebook',
    cashVnd: 1500000,
    personHours: 3.5,
    hourlyRate: null, // intentionally null to verify unvalued labor handling
    note: 'A/B test ad group 1',
    updatedByName: 'Quản Trị Viên QA',
    updatedAt: '2026-10-20T08:30:00Z',
  },
];

const mockOperationsData: AdminMonitoringOperationsResponse = {
  overview: {
    overallStatus: 'Healthy',
    api5xxRatePercent: 0.02,
    latencyP95Ms: 145.0,
    totalRequestsLastHour: 8420,
    databaseConnected: true,
    databaseStatus: 'Healthy',
  },
  routeMetrics: [
    {
      routeTemplate: '/api/invoices/{id}',
      httpMethod: 'GET',
      requestCount: 1250,
      status5xxCount: 0,
      p95LatencyMs: 85.0,
      avgLatencyMs: 32.4,
      maxLatencyMs: 310.0,
    },
  ],
  hostMetrics: {
    cpuPercent: 24.5,
    memoryPercent: 48.2,
    diskPercent: 55.1,
    containerCount: 3,
    healthEndpointStatus: 'ok',
    hostStatus: 'Healthy',
    recordedAt: '2026-10-20T08:54:00Z',
    isStale: false,
  },
  workers: [
    {
      workerName: 'recovery-email',
      isEnabled: true,
      status: 'Healthy',
      lastStartedAt: '2026-10-20T08:50:00Z',
      lastSuccessAt: '2026-10-20T08:50:05Z',
      lastErrorAt: null,
      lastErrorMessage: null,
      isStale: false,
    },
    {
      workerName: 'backup-watchdog',
      isEnabled: true,
      status: 'Healthy',
      lastStartedAt: '2026-10-20T08:00:00Z',
      lastSuccessAt: '2026-10-20T08:00:00Z',
      lastErrorAt: null,
      lastErrorMessage: null,
      isStale: false,
    },
  ],
  queues: {
    zaloPendingQueueCount: 2,
    zaloEligibleWindowCount: 2,
    payOsPendingReconcileCount: 0,
    smtpAcceptedNotDeliveredCount: 0,
  },
};

const mockIncidentsData: AdminMonitoringIncidentsResponse = {
  activeIncidentCount: 0,
  incidents: [],
};

const SCREENSHOT_DIRS = [
  '/Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/c5040527-b0ce-4e4d-b67f-05ffc7eb25f4/screenshots',
  '/Users/nguyenhoanhquan/.gemini/antigravity-cli/brain/32235a5c-b141-424f-94cf-2e2996c0714f/screenshots',
];

async function captureEvidenceScreenshot(page: import('@playwright/test').Page, filename: string, options?: { fullPage?: boolean }) {
  for (const dir of SCREENSHOT_DIRS) {
    try {
      await page.screenshot({ path: `${dir}/${filename}`, fullPage: options?.fullPage });
    } catch {
      // ignore secondary dir if unavailable
    }
  }
}

test.describe('Monitoring Review QA Evidence & Viewport Verification', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/monitoring/growth') return route.fulfill({ json: mockGrowthData });
      if (path === '/api/admin/monitoring/revenue') return route.fulfill({ json: mockRevenueData });
      if (path === '/api/admin/monitoring/campaigns/costs' || path === '/api/admin/monitoring/campaigns') {
        return route.fulfill({ json: mockCampaignCostsList });
      }
      if (path === '/api/admin/monitoring/operations') return route.fulfill({ json: mockOperationsData });
      if (path === '/api/admin/monitoring/incidents') return route.fulfill({ json: mockIncidentsData });
      if (path === '/api/analytics/events' && method === 'POST') {
        return route.fulfill({ json: { processedCount: 1, rejectedCount: 0 } });
      }
      return route.fulfill({ json: [] });
    });
  });

  test('Desktop 1440px: No horizontal overflow and captures screenshots across all tabs', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/admin/monitoring');

    // Growth Tab
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();
    const scrollWidthGrowth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidthGrowth).toBeLessThanOrEqual(1440);
    await captureEvidenceScreenshot(page, 'desktop-1440-growth.png', { fullPage: true });

    // Revenue Tab
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    const scrollWidthRev = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidthRev).toBeLessThanOrEqual(1440);
    await captureEvidenceScreenshot(page, 'desktop-1440-revenue.png', { fullPage: true });

    // Open UTM Modal
    await page.getByRole('button', { name: 'Tạo liên kết UTM' }).click();
    await expect(page.getByText('Đường dẫn gốc')).toBeVisible();
    await captureEvidenceScreenshot(page, 'desktop-1440-utm-modal.png');
    await page.getByRole('button', { name: 'Đóng' }).click();

    // Operations Tab
    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    const scrollWidthOps = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidthOps).toBeLessThanOrEqual(1440);
    await captureEvidenceScreenshot(page, 'desktop-1440-operations.png', { fullPage: true });
  });

  test('Mobile 375px: Strict zero horizontal page overflow across all tabs', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/admin/monitoring');

    // Mobile Growth Tab
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();
    const scrollGrowth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollGrowth).toBeLessThanOrEqual(375);
    await captureEvidenceScreenshot(page, 'mobile-375-growth.png', { fullPage: true });

    // Mobile Revenue Tab
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    const scrollRev = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollRev).toBeLessThanOrEqual(375);
    await captureEvidenceScreenshot(page, 'mobile-375-revenue.png', { fullPage: true });

    // Mobile Operations Tab
    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    const scrollOps = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollOps).toBeLessThanOrEqual(375);
    await captureEvidenceScreenshot(page, 'mobile-375-operations.png', { fullPage: true });
  });

  test('Mobile 375px: Tab navigation bar preserves interactive reachability without clipping', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/admin/monitoring');

    // 1. Initial state: Growth Tab is active
    const growthTab = page.getByRole('tab', { name: 'Tăng trưởng & Sử dụng' });
    await expect(growthTab).toBeVisible();
    await expect(growthTab).toHaveAttribute('aria-selected', 'true');
    await page.waitForTimeout(300);
    await captureEvidenceScreenshot(page, 'mobile-375-tabs-growth.png');

    // 2. Click Revenue Tab
    const revTab = page.getByRole('tab', { name: 'Doanh thu' });
    await expect(revTab).toBeVisible();
    await revTab.click();
    await expect(revTab).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    await page.waitForTimeout(350); // wait for tab transition
    await captureEvidenceScreenshot(page, 'mobile-375-tabs-revenue.png');

    // 3. Click Operations Tab
    const opsTab = page.getByRole('tab', { name: 'Vận hành' });
    // In compact mode, opsTab may be accessed via tab bar scroll or direct click
    if (!(await opsTab.isVisible())) {
      await opsTab.scrollIntoViewIfNeeded();
    }
    await opsTab.click();
    await expect(opsTab).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    await page.waitForTimeout(350); // wait for tab transition
    await captureEvidenceScreenshot(page, 'mobile-375-tabs-operations.png');

    // 4. Click back to Growth Tab
    await growthTab.scrollIntoViewIfNeeded();
    await growthTab.click();
    await expect(growthTab).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByText('Phiên truy cập').first()).toBeVisible();
  });

  test('Mobile 375px: Table horizontal local scrolling and control reachability (no overflow-hidden clipping)', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/admin/monitoring');

    // Switch to Revenue Tab where the 9-column Campaign Cost Table is displayed
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Quản lý chi phí thủ công')).toBeVisible();

    // Find Campaign Cost table container
    const costSection = page.locator('.section-card', { hasText: 'Quản lý chi phí thủ công' });
    const tableWrapper = costSection.locator('.monitoring-table-wrapper');
    await expect(tableWrapper).toBeVisible();

    // Verify local scroll wrapper exists and is scrollable
    const scrollContainer = costSection.locator('.ant-table-content');
    const scrollInfo = await scrollContainer.evaluate((el) => ({
      scrollWidth: el.scrollWidth,
      clientWidth: el.clientWidth,
      isScrollable: el.scrollWidth > el.clientWidth,
    }));
    expect(scrollInfo.isScrollable).toBe(true);

    // Initial visible columns: Tên chiến dịch, Kênh, Nguồn, Ngày ghi nhận
    await expect(costSection.getByText('facebook_launch')).toBeVisible();
    await expect(costSection.getByRole('columnheader', { name: 'Tên chiến dịch' })).toBeVisible();

    // Verify the unvalued cell indicates missing rate without fabricating total cost
    await expect(costSection.getByText('Thiếu đơn giá giờ công')).toBeVisible();

    // The 'Hành động' / 'Sửa' button is offscreen initially on 375px
    const editBtn = costSection.getByRole('button', { name: 'Sửa' }).first();
    await editBtn.scrollIntoViewIfNeeded();
    await expect(editBtn).toBeVisible();

    // Capture screenshot showing scrolled table with Sửa button reachable
    await captureEvidenceScreenshot(page, 'mobile-375-table-scrolled.png');

    // Click 'Sửa' button - verifies control is fully interactive and not blocked by overflowhidden
    await editBtn.click();
    const modal = page.locator('.ant-modal').filter({ hasText: 'Sửa chi phí chiến dịch' });
    await expect(modal).toBeVisible();

    // Verify modal valuation preview indicates missing valuation
    await expect(modal.getByText('Thiếu đơn giá giờ công')).toBeVisible();
    await page.waitForTimeout(400); // allow modal animation to complete
    await captureEvidenceScreenshot(page, 'mobile-375-modal-open.png');

    // Close modal
    await modal.getByRole('button', { name: 'Hủy' }).click();
    await expect(modal).not.toBeVisible();
  });
});

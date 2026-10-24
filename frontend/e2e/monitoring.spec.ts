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
  id: 'fixture-admin-01',
  name: 'Quản Trị Viên',
  email: 'admin@roomie.vn',
  role: 'Admin',
  status: 'Approved',
};

const ownerUser: User = {
  id: 'fixture-owner-01',
  name: 'Chủ Trọ Test',
  email: 'owner@roomie.vn',
  role: 'Owner',
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
    id: 'cost-item-01',
    date: '2026-10-20',
    campaign: 'facebook_launch',
    channel: 'social',
    source: 'facebook',
    cashVnd: 1500000,
    personHours: 3.5,
    hourlyRate: 200000,
    note: 'A/B test ad group 1',
    updatedByName: 'Quản Trị Viên',
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

const SCREENSHOT_DIR = 'test-results/screenshots';

test.describe('Admin Monitoring Dashboard (/admin/monitoring)', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/monitoring/growth') return route.fulfill({ json: mockGrowthData });
      if (path === '/api/admin/monitoring/revenue') return route.fulfill({ json: mockRevenueData });
      if (path === '/api/admin/monitoring/campaigns/costs' || path === '/api/admin/monitoring/campaigns') {
        if (method === 'POST') {
          const body = route.request().postDataJSON();
          const newItem: AdminCampaignCostItemDto = {
            id: `cost-${Date.now()}`,
            date: body.date,
            campaign: body.campaign,
            channel: body.channel,
            source: body.source,
            cashVnd: body.cashVnd,
            personHours: body.personHours,
            hourlyRate: body.hourlyRate,
            note: body.note,
            updatedByName: 'Quản Trị Viên',
            updatedAt: new Date().toISOString(),
          };
          return route.fulfill({ json: newItem });
        }
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

  test('Desktop 1440px: renders Admin Monitoring shell, tabs, and Growth metrics', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/admin/monitoring');

    // Page Title
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();

    // Sider Navigation item
    const navItem = page.locator('.ant-menu-item', { hasText: 'Giám sát' });
    await expect(navItem).toBeVisible();

    // Tab 1: Growth & Usage KPIs
    await expect(page.getByText('Phiên truy cập').first()).toBeVisible();
    await expect(page.getByText('1,420')).toBeVisible();
    await expect(page.getByText('Khách duy nhất').first()).toBeVisible();
    await expect(page.getByText('980')).toBeVisible();

    // Cohort maturity
    await expect(page.getByText('Tỷ lệ kích hoạt 7 ngày')).toBeVisible();
    await expect(page.getByText('62.2%')).toBeVisible();

    // Traffic sources table
    await expect(page.getByText('facebook').first()).toBeVisible();

    // Desktop 1440px screenshot for Growth
    const scrollGrowth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollGrowth).toBeLessThanOrEqual(1440);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/desktop-1440-growth.png`, fullPage: true });

    // Revenue Tab Screenshot
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    const scrollRev = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollRev).toBeLessThanOrEqual(1440);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/desktop-1440-revenue.png`, fullPage: true });

    // UTM modal screenshot
    await page.getByRole('button', { name: 'Tạo liên kết UTM' }).click();
    await expect(page.getByText('Đường dẫn gốc')).toBeVisible();
    await page.screenshot({ path: `${SCREENSHOT_DIR}/desktop-1440-utm-modal.png` });
    await page.getByRole('button', { name: 'Đóng' }).click();

    // Operations Tab Screenshot
    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    const scrollOps = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollOps).toBeLessThanOrEqual(1440);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/desktop-1440-operations.png`, fullPage: true });
  });

  test('Revenue Tab: shows Pro Revenue, CAC30, UTM link builder, and campaign costs', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/admin/monitoring');

    // Click Doanh thu tab
    await page.getByRole('tab', { name: 'Doanh thu' }).click();

    // Check KPI cards
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    await expect(page.getByText('Chi phí chuyển đổi chủ trọ (CAC30)')).toBeVisible();

    // Check Campaign cost item in table
    await expect(page.getByText('facebook_launch')).toBeVisible();

    // Open UTM Link Builder Modal
    await page.getByRole('button', { name: 'Tạo liên kết UTM' }).click();
    await expect(page.getByText('Đường dẫn gốc')).toBeVisible();
    await expect(page.getByText('Liên kết đã tạo:')).toBeVisible();

    // Close UTM modal
    await page.getByRole('button', { name: 'Đóng' }).click();
  });

  test('Operations Tab: displays system overview, host telemetry, queues, and background workers', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/admin/monitoring');

    // Click Vận hành tab
    await page.getByRole('tab', { name: 'Vận hành' }).click();

    // System overview
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    await expect(page.getByText('Bình thường').first()).toBeVisible();
    await expect(page.getByText('Độ trễ p95').first()).toBeVisible();

    // Host telemetry
    await expect(page.getByText('Sử dụng CPU')).toBeVisible();
    await expect(page.getByText('Sử dụng RAM')).toBeVisible();
    await expect(page.getByText('Dung lượng ổ đĩa')).toBeVisible();

    // Queues
    await expect(page.getByText('Hàng đợi Zalo (Khung giờ 07:00-21:00)')).toBeVisible();
    await expect(page.getByText('recovery-email')).toBeVisible();
  });

  test('Mobile 375px: responsive layout renders correctly on mobile viewport', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/admin/monitoring');

    // 1. Mobile Growth Tab
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();
    const scrollGrowth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollGrowth).toBeLessThanOrEqual(375);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/mobile-375-growth.png`, fullPage: true });

    // 2. Mobile Revenue Tab
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();
    const scrollRev = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollRev).toBeLessThanOrEqual(375);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/mobile-375-revenue.png`, fullPage: true });

    // 3. Mobile Operations Tab
    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
    const scrollOps = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollOps).toBeLessThanOrEqual(375);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/mobile-375-operations.png`, fullPage: true });
  });

  test('Campaign Cost Modal: preserves null hourlyRate on edit and distinguishes explicit 0', async ({ page }) => {
    const unvaluedCostItem: AdminCampaignCostItemDto = {
      id: 'cost-unvalued-qa',
      date: '2026-10-20',
      campaign: 'volunteer_recruitment',
      channel: 'community',
      source: 'offline',
      cashVnd: 500000,
      personHours: 10,
      hourlyRate: null,
      note: 'Volunteer labor unvalued',
      updatedByName: 'Quản Trị Viên',
      updatedAt: '2026-10-20T08:00:00Z',
    };

    let submittedPayload: Record<string, unknown> | null = null;

    await page.route('**/api/admin/monitoring/campaigns/costs*', async (route) => {
      const method = route.request().method();
      if (method === 'POST') {
        submittedPayload = route.request().postDataJSON();
        return route.fulfill({ json: { ...unvaluedCostItem, ...submittedPayload } });
      }
      return route.fulfill({ json: [unvaluedCostItem] });
    });

    await page.goto('/admin/monitoring');
    await page.getByRole('tab', { name: 'Doanh thu' }).click();

    // Verify warning tag in table for missing valuation
    const costSection = page.locator('.section-card', { hasText: 'Quản lý chi phí thủ công' });
    await expect(costSection.getByText('Thiếu đơn giá giờ công')).toBeVisible();

    // Click Edit button
    await costSection.getByRole('button', { name: 'Sửa' }).click();

    // Verify modal is open
    const modal = page.locator('.ant-modal', { hasText: 'Sửa chi phí chiến dịch' });
    await expect(modal).toBeVisible();

    // Verify preview indicates missing valuation
    await expect(modal.getByText('Thiếu đơn giá giờ công')).toBeVisible();

    // Save without editing hourlyRate
    await modal.getByRole('button', { name: 'Cập nhật chi phí' }).click();
    await expect(modal).not.toBeVisible();

    // Crucial check: hourlyRate MUST remain null, not coerced to 0!
    expect(submittedPayload).not.toBeNull();
    expect(submittedPayload!['hourlyRate']).toBeNull();

    // Now test explicit 0 hourly rate
    await costSection.getByRole('button', { name: 'Thêm chi phí chiến dịch' }).click();
    const createModal = page.locator('.ant-modal', { hasText: 'Thêm chi phí chiến dịch' });
    await expect(createModal).toBeVisible();

    await createModal.getByLabel('Tên chiến dịch').fill('founder_sweat_equity');
    await createModal.getByLabel('Số giờ công').fill('8');
    await createModal.getByLabel('Đơn giá giờ công (VND/giờ)').fill('0');

    // Total preview shows 0 VND (no missing valuation warning)
    await createModal.getByRole('button', { name: 'Lưu chi phí' }).click();
    await expect(createModal).not.toBeVisible();

    expect(submittedPayload).not.toBeNull();
    expect(submittedPayload!['hourlyRate']).toBe(0);
  });

  test('Clamped Date Window: renders effective date range tag while preserving requested filter intent', async ({ page }) => {
    const clampedGrowthData: AdminMonitoringGrowthResponse = {
      ...mockGrowthData,
      effectiveFromDate: '2026-08-01',
      effectiveToDate: '2026-10-20',
    };

    await page.route('**/api/admin/monitoring/growth*', (route) => route.fulfill({ json: clampedGrowthData }));

    await page.goto('/admin/monitoring');

    // Switch to 90 days filter
    await page.locator('.monitoring-filters-bar label', { hasText: '90 ngày' }).click();

    // Verify effective range banner surfaces server clamped window
    const effectiveTag = page.locator('.monitoring-effective-range');
    await expect(effectiveTag).toBeVisible();
    await expect(effectiveTag.getByText('Dữ liệu thực tế: 2026-08-01 đến 2026-10-20')).toBeVisible();

    // Crucial check: user filter intent '90d' is preserved in the UI
    const activeRadio = page.locator('.monitoring-filters-bar .ant-radio-button-wrapper-checked');
    await expect(activeRadio).toContainText('90 ngày');
  });

  test('Fallback Funnel: renders non-null registration/activation stages when totalSessions is null', async ({ page }) => {
    const nullSessionsGrowthData: AdminMonitoringGrowthResponse = {
      ...mockGrowthData,
      summary: {
        ...mockGrowthData.summary,
        totalSessions: null, // Unknown session count (e.g. multi-day window where raw web events pruned)
        uniqueVisitors: null,
        registrations: 45,
        activatedOwners: 28,
      },
      funnel: null, // Server funnel missing, forces fallback to summary
    };

    await page.route('**/api/admin/monitoring/growth*', (route) => route.fulfill({ json: nullSessionsGrowthData }));

    await page.goto('/admin/monitoring');

    const funnelCard = page.locator('.section-card', { hasText: 'Phễu chuyển đổi' });
    await expect(funnelCard).toBeVisible();

    // Fallback funnel must NOT be suppressed to MonitoringEmptyState ("Chưa có dữ liệu")
    await expect(funnelCard.getByText('Chưa có dữ liệu')).toHaveCount(0);

    // Phiên truy cập stage renders Unknown ("—")
    await expect(funnelCard.getByText('Phiên truy cập')).toBeVisible();

    // Registrations and Activations stages render their authentic known values
    await expect(funnelCard.getByText('Đăng ký')).toBeVisible();
    await expect(funnelCard.getByText('45')).toBeVisible();

    await expect(funnelCard.getByText('Chủ trọ kích hoạt')).toBeVisible();
    await expect(funnelCard.getByText('28')).toBeVisible();

    // Step rate from registrations (45) to activations (28) = 62%
    await expect(funnelCard.getByText('(62% từ bước trước)')).toBeVisible();
  });

  test('Gate Security: non-Admin owner is redirected away from /admin/monitoring to /dashboard', async ({ page }) => {
    // Override /api/auth/me to return Owner role
    await page.route('**/api/auth/me', (route) => route.fulfill({ json: ownerUser }));

    await page.goto('/admin/monitoring');
    await expect(page).toHaveURL(/dashboard/);
  });

  test('Ops Window & Campaign Filter: verifies actual network URLs and window/campaign parameters', async ({ page }) => {
    const requestedOpsWindows: string[] = [];
    const requestedSourcesWindows: string[] = [];
    let requestedCampaignFilter: string | null = null;

    page.on('request', (req) => {
      try {
        const url = new URL(req.url());
        if (url.pathname.includes('/api/admin/monitoring/growth')) {
          const sw = url.searchParams.get('sourcesWindow');
          if (sw) requestedSourcesWindows.push(sw);
        }
        if (url.pathname.includes('/api/admin/monitoring/operations')) {
          requestedOpsWindows.push(url.searchParams.get('window') || '1h');
        }
        if (url.pathname.includes('/api/admin/monitoring/campaigns/costs')) {
          requestedCampaignFilter = url.searchParams.get('campaign');
        }
      } catch {
        // ignore invalid urls
      }
    });

    await page.goto('/admin/monitoring');

    // Verify Traffic & UTM sources window filter (1 ngày, 7 ngày, Tất cả)
    await expect.poll(() => requestedSourcesWindows).toContain('all');
    const sourcesCard = page.locator('.section-card', { hasText: 'Thống kê nguồn & UTM' });
    await sourcesCard.locator('label', { hasText: '1 ngày' }).click();
    await expect.poll(() => requestedSourcesWindows).toContain('1d');
    await sourcesCard.locator('label', { hasText: '7 ngày' }).click();
    await expect.poll(() => requestedSourcesWindows).toContain('7d');

    // Switch to Operations tab
    await page.getByRole('tab', { name: 'Vận hành' }).click();

    // Verify initial ops window is 1h
    await expect.poll(() => requestedOpsWindows).toContain('1h');

    // Click 24h ops window
    await page.locator('.monitoring-filters-bar label', { hasText: '24 giờ' }).click();
    await expect.poll(() => requestedOpsWindows).toContain('24h');

    // Click 7d ops window
    await page.locator('.monitoring-filters-bar label', { hasText: '7 ngày' }).click();
    await expect.poll(() => requestedOpsWindows).toContain('7d');

    // Switch to Revenue tab and test campaign filter
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    const campaignInput = page.getByPlaceholder('Chiến dịch');
    await campaignInput.fill('fb_test_q4');

    // Refetch/verify network URL receives campaign query parameter
    await page.getByRole('button', { name: 'Làm mới' }).click();
    await expect.poll(() => requestedCampaignFilter).toBe('fb_test_q4');
  });

  test('Null Telemetry: renders explicit unknown states, no synthetic counts, and no invented green watchdog', async ({ page }) => {
    const nullOpsData: AdminMonitoringOperationsResponse = {
      overview: {
        overallStatus: 'Unknown',
        api5xxRatePercent: null,
        latencyP95Ms: null,
        totalRequestsLastHour: null,
        databaseConnected: null,
        databaseStatus: null,
        window: '1h',
      },
      routeMetrics: [],
      hostMetrics: {
        cpuPercent: null,
        memoryPercent: null,
        diskPercent: null,
        containerCount: null,
        healthEndpointStatus: 'Unknown',
        hostStatus: 'Unknown',
        recordedAt: null,
        isStale: false,
      },
      workers: [], // No backup worker supplied
      queues: {
        zaloPendingQueueCount: null,
        zaloEligibleWindowCount: null,
        payOsPendingReconcileCount: null,
        smtpAcceptedNotDeliveredCount: null,
      },
    };

    await page.route('**/api/admin/monitoring/operations*', (route) => route.fulfill({ json: nullOpsData }));

    await page.goto('/admin/monitoring');
    await page.getByRole('tab', { name: 'Vận hành' }).click();

    // 1. Synthetic counts (<50ms, 420, 750) must NOT exist in the operations tab
    const opsContent = page.locator('.operations-tab-content');
    await expect(opsContent.getByText('<50ms')).toHaveCount(0);
    await expect(opsContent.getByText('420')).toHaveCount(0);
    await expect(opsContent.getByText('750')).toHaveCount(0);

    // 2. Histogram shows explicit unavailable state
    await expect(page.getByText('Chưa có dữ liệu phân phối độ trễ')).toBeVisible();

    // 3. Watchdog 27h shows Unknown, never hardcoded green "Đã sao lưu hợp lệ..."
    await expect(page.getByText('Đã sao lưu hợp lệ trong vòng 27 giờ qua')).toHaveCount(0);
    const watchdogCard = page.locator('.monitoring-stat-card', { hasText: 'Chó canh gác sao lưu' });
    await expect(watchdogCard.getByText('Chưa xác định')).toBeVisible();

    // 4. Host metrics render explicit Unknown (not healthy 0% progress)
    const hostSection = page.locator('.section-card', { hasText: 'Hạ tầng máy chủ (Host Systemd)' });
    await expect(hostSection.getByText('Chưa xác định').first()).toBeVisible();

    // 5. Container count renders Unknown (not "0 active" or "0 đang chạy")
    await expect(page.getByText('0 active')).toHaveCount(0);
    const containerRow = hostSection.locator('span', { hasText: 'Trạng thái container:' });
    await expect(containerRow.getByText('Chưa xác định')).toBeVisible();

    // 6. Queues render Unknown (not 0)
    const queueSection = page.locator('.section-card', { hasText: 'Dịch vụ tích hợp & Hàng đợi' });
    await expect(queueSection.getByText('Chưa xác định').first()).toBeVisible();
  });

  test('Authentic Zero Telemetry: preserves authentic 0 values and authoritative watchdog status', async ({ page }) => {
    const zeroOpsData: AdminMonitoringOperationsResponse = {
      overview: {
        overallStatus: 'Healthy',
        api5xxRatePercent: 0,
        latencyP95Ms: 15.0,
        totalRequestsLastHour: 0,
        databaseConnected: true,
        databaseStatus: 'Healthy',
        window: '1h',
      },
      routeMetrics: [],
      hostMetrics: {
        cpuPercent: 0,
        memoryPercent: 0,
        diskPercent: 0,
        containerCount: 0,
        healthEndpointStatus: 'Healthy',
        hostStatus: 'Healthy',
        recordedAt: '2026-10-20T09:00:00Z',
        isStale: false,
      },
      workers: [
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
        zaloPendingQueueCount: 0,
        zaloEligibleWindowCount: 0,
        payOsPendingReconcileCount: 0,
        smtpAcceptedNotDeliveredCount: 0,
      },
    };

    await page.route('**/api/admin/monitoring/operations*', (route) => route.fulfill({ json: zeroOpsData }));

    await page.goto('/admin/monitoring');
    await page.getByRole('tab', { name: 'Vận hành' }).click();

    // Authentic zero values must be preserved
    await expect(page.getByText('0.0%').first()).toBeVisible();
    await expect(page.getByText('0 đang chạy')).toBeVisible();
    await expect(page.getByText('0 đang chờ')).toBeVisible();
    await expect(page.getByText('0 trong khung giờ hợp lệ')).toBeVisible();

    // Authoritative backup watchdog surfaces Healthy status from worker
    const watchdogCard = page.locator('.monitoring-stat-card', { hasText: 'Chó canh gác sao lưu' });
    await expect(watchdogCard.getByText('Bình thường')).toBeVisible();
  });

  test('Growth Tab: null Month 0 retention renders explicit unknown without defaulting to 100%', async ({ page }) => {
    const nullGrowthData: AdminMonitoringGrowthResponse = {
      ...mockGrowthData,
      retentionMatrix: [
        {
          cohortPeriod: '2026-08',
          cohortSize: 20,
          retentionPercentages: [null, null],
        },
      ],
      cohortMaturity: {
        activationRate7Day: null,
        activationCohortMaturedCount: 0,
        firstPaidRate30Day: null,
        paidCohortMaturedCount: 0,
      },
    };

    await page.route('**/api/admin/monitoring/growth*', (route) => route.fulfill({ json: nullGrowthData }));

    await page.goto('/admin/monitoring');

    // Verify Cohort retention table
    const retentionSection = page.locator('.section-card', { hasText: 'Mức độ kích hoạt (Activation)' });
    await expect(retentionSection).toBeVisible();

    // Month 0 must render Unknown ("—"), NOT "100%"
    const row = retentionSection.locator('.ant-table-row', { hasText: '2026-08' });
    await expect(row.getByText('100%')).toHaveCount(0);
    await expect(row.getByText('Chưa xác định').first()).toBeVisible();

    // Immature 7-day cohort tooltip explanation on hover
    const maturitySection = page.locator('.section-card', { hasText: 'Độ trưởng thành Cohort' });
    await maturitySection.locator('.anticon-question-circle').first().hover();
    await expect(page.getByText('Chưa đủ 7 ngày trưởng thành')).toBeVisible();
  });
});

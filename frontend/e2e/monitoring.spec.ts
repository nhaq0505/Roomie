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

test.describe('Admin Monitoring Dashboard (/admin/monitoring)', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: adminUser });
      if (path === '/api/admin/monitoring/growth') return route.fulfill({ json: mockGrowthData });
      if (path === '/api/admin/monitoring/revenue') return route.fulfill({ json: mockRevenueData });
      if (path === '/api/admin/monitoring/campaigns') {
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

    // Verify page header on mobile
    await expect(page.getByRole('heading', { level: 2, name: 'Giám sát hệ thống' })).toBeVisible();

    // Verify tabs remain interactive
    await page.getByRole('tab', { name: 'Doanh thu' }).click();
    await expect(page.getByText('Doanh thu Pro').first()).toBeVisible();

    await page.getByRole('tab', { name: 'Vận hành' }).click();
    await expect(page.getByText('Trạng thái chung').first()).toBeVisible();
  });

  test('Gate Security: non-Admin owner is redirected away from /admin/monitoring to /dashboard', async ({ page }) => {
    // Override /api/auth/me to return Owner role
    await page.route('**/api/auth/me', (route) => route.fulfill({ json: ownerUser }));

    await page.goto('/admin/monitoring');
    await expect(page).toHaveURL(/dashboard/);
  });
});

import { test, expect } from '@playwright/test';
import type { Building, Room, Invoice, Reading, DashboardSummary, SubscriptionStatus, PaymentSettings } from '../src/api';

const landlordUser = {
  id: 'fixture-landlord-01',
  name: 'Chủ Trọ Bình An',
  email: 'landlord@example.com',
  role: 'Owner' as const,
  status: 'Approved' as const,
};

const defaultDashboard: DashboardSummary = {
  totalRooms: 1,
  occupiedRooms: 0,
  totalBilled: 0,
  totalPaid: 0,
  totalOutstanding: 0,
  pendingReadings: 0,
  period: '2026-10',
  monthlyBilled: 0,
  monthlyPaid: 0,
  monthlyOutstanding: 0,
  collectionRate: 0,
};

test.describe('Landlord Workflows (Chủ trọ)', () => {
  test.describe.configure({ mode: 'serial' });

  // Scenario L1: Hostel/Building CRUD
  test('Scenario L1: Hostel/Building CRUD (create, verify, archive, filter, restore)', async ({ page }) => {
    let buildings: Building[] = [
      { id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', roomCount: 2, isArchived: false },
    ];
    let rooms: Room[] = [
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
        occupancy: null,
        isArchived: false,
      },
    ];

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') {
        return route.fulfill({ json: landlordUser });
      }
      if (path === '/api/dashboard') {
        return route.fulfill({ json: defaultDashboard });
      }
      if (path === '/api/buildings') {
        if (method === 'GET') {
          return route.fulfill({ json: buildings });
        }
        if (method === 'POST') {
          const body = route.request().postDataJSON() as { name: string; address: string };
          const created: Building = {
            id: `bld-${Date.now()}`,
            name: body.name,
            address: body.address,
            roomCount: 0,
            isArchived: false,
          };
          buildings = [...buildings, created];
          return route.fulfill({ json: created });
        }
      }
      if (path.startsWith('/api/buildings/') && (method === 'PATCH' || method === 'PUT')) {
        const id = path.split('/')[3];
        const body = route.request().postDataJSON() as { name: string; address?: string };
        const b = buildings.find((item) => item.id === id);
        if (b) {
          b.name = body.name;
          if (body.address !== undefined) b.address = body.address;
        }
        return route.fulfill({ json: b });
      }
      if (path.startsWith('/api/buildings/') && path.endsWith('/restore') && method === 'POST') {
        const id = path.split('/')[3];
        const b = buildings.find((item) => item.id === id);
        if (b) {
          b.isArchived = false;
          b.archivedAt = null;
        }
        return route.fulfill({ json: { restored: true, restoredRoomIds: [] } });
      }
      if (path.startsWith('/api/buildings/') && method === 'DELETE') {
        const id = path.split('/')[3];
        const b = buildings.find((item) => item.id === id);
        if (b) {
          b.isArchived = true;
          b.archivedAt = new Date().toISOString();
        }
        return route.fulfill({ json: { archived: true, archiveBatchId: 'batch-01' } });
      }
      if (path === '/api/rooms') {
        return route.fulfill({ json: rooms });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/buildings');
    await expect(page.getByRole('heading', { level: 2, name: 'Dãy trọ' })).toBeVisible();
    await expect(page.getByText('Nhà trọ Bình An', { exact: true })).toBeVisible();

    // 1. Create building via modal
    await page.getByRole('button', { name: 'Tạo dãy trọ' }).click();
    const createModal = page.locator('.ant-modal').filter({ hasText: 'Thêm dãy trọ mới' });
    await expect(createModal).toBeVisible();

    await createModal.getByLabel('Tên dãy trọ').fill('Nhà trọ Hoa Sen');
    await createModal.getByLabel('Địa chỉ').fill('456 Đường Hoa Sen, Quận 7');
    await createModal.getByRole('button', { name: 'Tạo dãy trọ' }).click();

    // 2. Verify new building card is rendered
    await expect(page.getByRole('heading', { level: 4, name: 'Nhà trọ Hoa Sen' })).toBeVisible();
    await expect(page.getByText('456 Đường Hoa Sen, Quận 7')).toBeVisible();

    // 2b. Edit building ("Sửa dãy trọ")
    const hoaSenCard = page.locator('.building-card').filter({ hasText: 'Nhà trọ Hoa Sen' });
    await hoaSenCard.getByRole('button', { name: 'Sửa' }).click();
    const editModal = page.locator('.ant-modal').filter({ hasText: 'Sửa dãy trọ' });
    await expect(editModal).toBeVisible();
    await editModal.getByLabel('Tên dãy trọ').fill('Nhà trọ Hoa Sen Mới');
    await editModal.getByLabel('Địa chỉ').fill('456 Đường Hoa Sen Mới, Quận 7');
    await editModal.getByRole('button', { name: 'Lưu thay đổi' }).click();
    await expect(editModal).toBeHidden();
    await expect(page.getByRole('heading', { level: 4, name: 'Nhà trọ Hoa Sen Mới' })).toBeVisible();
    await expect(page.getByText('456 Đường Hoa Sen Mới, Quận 7')).toBeVisible();

    // 3. Archive building via confirm modal
    const updatedCard = page.locator('.building-card').filter({ hasText: 'Nhà trọ Hoa Sen Mới' });
    await updatedCard.getByRole('button', { name: 'Lưu trữ' }).click();

    const archiveConfirmModal = page.locator('.ant-modal-confirm');
    await expect(archiveConfirmModal).toBeVisible();
    await expect(archiveConfirmModal).toContainText('Lưu trữ dãy trọ Nhà trọ Hoa Sen Mới?');
    await archiveConfirmModal.getByRole('button', { name: 'Lưu trữ dãy trọ' }).click();
    await expect(archiveConfirmModal).toBeHidden();

    // Verify building is no longer in active list
    await expect(page.locator('.building-card').filter({ hasText: 'Nhà trọ Hoa Sen Mới' })).toHaveCount(0);

    // 4. Switch filter to "Đã lưu trữ"
    const filterSelect = page.locator('.archive-filter-toolbar .ant-select');
    await filterSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: 'Đã lưu trữ' }).click();

    // Verify archived building appears with "Đã lưu trữ" tag
    const archivedCard = page.locator('.building-card').filter({ hasText: 'Nhà trọ Hoa Sen Mới' });
    await expect(archivedCard).toBeVisible();
    await expect(archivedCard.locator('.building-archived-tag')).toHaveText('Đã lưu trữ');

    // 5. Restore building
    await Promise.all([
      page.waitForResponse((res) => res.url().includes('/restore') && res.status() === 200),
      archivedCard.getByRole('button', { name: 'Khôi phục' }).click(),
    ]);
    await expect(page.getByText('Đã khôi phục dãy trọ')).toBeVisible();

    // Switch back to "Đang hoạt động" and verify building is restored
    await filterSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: 'Đang hoạt động' }).click();
    await expect(page.locator('.building-card').filter({ hasText: 'Nhà trọ Hoa Sen Mới' })).toBeVisible();

    // 6. Navigate to rooms page and verify "Lưu trữ phòng" terminology (not "Xóa phòng")
    await page.goto('/rooms');
    await expect(page.getByRole('heading', { level: 2, name: 'Phòng & người thuê' })).toBeVisible();
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();

    // Open room more actions dropdown
    await page.locator('.room-more').first().click();

    // Assert "Lưu trữ phòng" label is used instead of "Xóa phòng"
    await expect(page.getByRole('menuitem', { name: 'Lưu trữ phòng' })).toBeVisible();
    await expect(page.getByText('Xóa phòng')).toHaveCount(0);

    // Open confirm dialog and verify safe terminology
    await page.getByRole('menuitem', { name: 'Lưu trữ phòng' }).click();
    const roomArchiveModal = page.locator('.ant-modal-confirm');
    await expect(roomArchiveModal).toBeVisible();
    await expect(roomArchiveModal).toContainText('Lưu trữ phòng Phòng 101?');
    await expect(roomArchiveModal.getByRole('button', { name: 'Lưu trữ phòng' })).toBeVisible();
    await expect(roomArchiveModal.getByRole('button', { name: 'Xóa phòng' })).toHaveCount(0);
    await roomArchiveModal.getByRole('button', { name: 'Hủy' }).click();
  });

  // Scenario L2: Room Creation & Free Quota Enforcement
  test('Scenario L2: Room Creation & Free Quota Enforcement', async ({ page }) => {
    let rooms: Room[] = [];
    const building: Building = { id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', isArchived: false };
    let subStatus: SubscriptionStatus = {
      tier: 'Free',
      expiresAt: null,
      isPro: false,
      roomCount: 0,
      currentRooms: 0,
      maxRooms: 5,
      availablePackages: [
        { id: 'pro_1m', name: '1 tháng', days: 30, price: 99000, priceVnd: 99000, description: 'Gói 1 tháng' },
      ],
    };
    let delayPost = false;
    let finishPost: () => void = () => {};

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [building] });
      if (path === '/api/subscription') return route.fulfill({ json: subStatus });
      if (path === '/api/settings/payment') return route.fulfill({ json: { webhookStatus: 'Verified', configured: true } });
      if (path === '/api/rooms') {
        if (method === 'GET') return route.fulfill({ json: rooms });
        if (method === 'POST') {
          if (delayPost) {
            await new Promise<void>((resolve) => { finishPost = resolve; });
          }
          const body = route.request().postDataJSON() as {
            buildingId: string;
            name: string;
            rent: number;
            electricityRate: number;
            waterFee: number;
            waterPerPerson: boolean;
            serviceFee: number;
            initialReading?: number;
          };
          const newRoom: Room = {
            id: `room-${Date.now()}`,
            buildingId: body.buildingId,
            buildingName: building.name,
            name: body.name,
            rent: body.rent,
            electricityRate: body.electricityRate,
            waterFee: body.waterFee,
            waterPerPerson: body.waterPerPerson,
            serviceFee: body.serviceFee,
            initialReading: body.initialReading ?? 0,
            previousReading: body.initialReading ?? 0,
            occupancy: null,
            isArchived: false,
          };
          rooms = [...rooms, newRoom];
          subStatus.currentRooms = rooms.length;
          return route.fulfill({ json: newRoom });
        }
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByRole('heading', { level: 2, name: 'Phòng & người thuê' })).toBeVisible();

    // 1. Create Room via modal (verifying absence of initial electricity meter reading field)
    await page.getByRole('button', { name: 'Tạo phòng trọ' }).first().click();
    const roomModal = page.locator('.ant-modal').filter({ hasText: 'Thêm phòng mới' });
    await expect(roomModal).toBeVisible();
    await expect(roomModal.getByLabel('Chỉ số điện ban đầu')).toHaveCount(0);

    await roomModal.getByLabel('Tên phòng').fill('Phòng 101');
    await roomModal.getByLabel('Giá thuê / tháng').fill('3000000');
    await roomModal.getByLabel('Giá điện / kWh').fill('3500');
    await roomModal.locator('#waterFee').fill('100000');
    await roomModal.getByLabel('Phí dịch vụ / tháng').fill('50000');

    await roomModal.getByRole('button', { name: 'Tạo phòng' }).click();

    // 2. Verify room displayed with status "Còn trống"
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();
    await expect(page.locator('.room-state-vacant')).toHaveText('Còn trống');

    // 2b. Duplicate Room ("Nhân bản phòng") & verify isBusy / confirmLoading disabled state
    await page.locator('.room-more').first().click();
    await expect(page.getByRole('menuitem', { name: 'Nhân bản phòng' })).toBeVisible();
    await page.getByRole('menuitem', { name: 'Nhân bản phòng' }).click();

    const dupModal = page.locator('.ant-modal').filter({ hasText: 'Nhân bản Phòng 101' });
    await expect(dupModal).toBeVisible();
    await expect(dupModal.getByLabel('Tên phòng')).toHaveValue('Phòng 101 (bản sao)');
    await expect(dupModal.getByLabel('Chỉ số điện ban đầu')).toHaveCount(0);

    // Simulate in-flight request to verify confirmLoading state on submit button
    delayPost = true;
    const submitBtn = dupModal.getByRole('button', { name: 'Tạo phòng' });
    await submitBtn.click();
    await expect(submitBtn).toHaveClass(/ant-btn-loading/);

    // Complete request and verify modal closes and duplicated room is visible
    finishPost();
    delayPost = false;
    await expect(dupModal).toBeHidden();
    await expect(page.getByText('Phòng 101 (bản sao)', { exact: true })).toBeVisible();

    // 3. Quota enforcement: Free plan with 5/5 active rooms
    rooms = [
      { id: 'r1', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 101', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r2', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 102', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r3', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 103', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r4', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 104', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r5', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 105', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
    ];
    subStatus = { ...subStatus, roomCount: 5, currentRooms: 5, maxRooms: 5, isPro: false };

    await page.reload();
    await expect(page.getByRole('heading', { level: 2, name: 'Phòng & người thuê' })).toBeVisible();

    // Verify Free quota alert is displayed
    await expect(page.getByText('Đã đạt giới hạn phòng của gói Miễn phí')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Nâng cấp tài khoản' })).toBeVisible();

    // Verify upgrade button navigates to account status
    await page.getByRole('button', { name: 'Nâng cấp tài khoản' }).click();
    await expect(page).toHaveURL(/account-status/);
  });

  // Scenario L3: Tenant Check-in & Private Link Issuance
  test('Scenario L3: Tenant Check-in & Private Link Issuance', async ({ page }) => {
    let room: Room = {
      id: 'room-101',
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
      occupancy: null,
      isArchived: false,
    };

    const paymentSettings: PaymentSettings = {
      webhookUrl: 'https://api.example.com/api/webhooks/payos',
      webhookStatus: 'Verified',
      webhookVerifiedAt: '2026-10-01T00:00:00Z',
      webhookError: null,
      configured: true,
      clientId: 'mock-client-id',
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, roomCount: 1, currentRooms: 1, maxRooms: null, availablePackages: [{ id: 'pro_1m', name: '1 tháng', days: 30, price: 99000, priceVnd: 99000, description: 'Gói 1 tháng' }] } });
      if (path === '/api/settings/payment') return route.fulfill({ json: paymentSettings });

      if (path === '/api/rooms') {
        return route.fulfill({ json: [room] });
      }
      if (path === '/api/rooms/room-101/occupancy' && method === 'POST') {
        const body = route.request().postDataJSON() as {
          tenantName: string;
          phone: string;
          people: number;
          startDate: string;
          depositAmount: number;
          initialReading?: number;
        };
        room = {
          ...room,
          initialReading: body.initialReading ?? room.initialReading,
          previousReading: body.initialReading ?? room.previousReading,
          occupancy: {
            id: 'occ-01',
            tenantName: body.tenantName,
            phone: body.phone,
            people: body.people,
            startDate: body.startDate,
            depositRequired: body.depositAmount,
            depositPaid: 0,
            depositHeld: 0,
            token: 'fixture-token-abc',
            url: '/t/fixture-token-abc',
          },
        };
        return route.fulfill({ json: room });
      }
      if (path === '/api/rooms/room-101/rotate-link' && method === 'POST') {
        if (room.occupancy) {
          room.occupancy.token = 'rotated-token-xyz';
          room.occupancy.url = '/t/rotated-token-xyz';
        }
        return route.fulfill({ json: { token: 'rotated-token-xyz', url: '/t/rotated-token-xyz' } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();
    await expect(page.locator('.room-state-vacant')).toHaveText('Còn trống');

    // 1. Check in tenant on dedicated page (/rooms/room-101/tenant)
    await page.getByRole('button', { name: 'Thêm người thuê' }).click();
    await expect(page).toHaveURL(/\/rooms\/room-101\/tenant/);
    const tenantWorkspace = page.locator('.room-tenant-page');
    await expect(tenantWorkspace).toBeVisible();

    await tenantWorkspace.getByLabel('Tên người thuê').fill('Nguyễn Văn A');
    await tenantWorkspace.getByLabel('Số điện thoại').fill('0901234567');
    await tenantWorkspace.getByLabel('Chỉ số điện ban đầu').fill('100');
    await tenantWorkspace.getByLabel('Tiền cọc yêu cầu').fill('3000000');

    await tenantWorkspace.getByRole('button', { name: 'Lưu người thuê' }).first().click();

    // 2. Room transitions to "Đang thuê" with tenant info
    await expect(page).toHaveURL(/\/rooms$/);
    await expect(page.locator('.room-state-occupied')).toHaveText('Đang thuê');
    await expect(page.getByText('Nguyễn Văn A', { exact: true })).toBeVisible();
    await expect(page.getByText('0901234567')).toBeVisible();

    // 3. Issue private tenant link
    await page.getByRole('button', { name: 'Cấp link cho khách' }).click();
    const linkModal = page.locator('.ant-modal').filter({ hasText: 'Link hóa đơn' });
    await expect(linkModal).toBeVisible();

    const linkInput = linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]');
    await expect(linkInput).toBeVisible();
    const initialUrl = await linkInput.inputValue();
    expect(initialUrl).toContain('/t/fixture-token-abc');

    // Verify QR code and copy button
    await expect(linkModal.locator('.ant-qrcode')).toBeVisible();
    await expect(linkModal.getByRole('button', { name: 'Sao chép liên kết' })).toBeVisible();

    // 4. Test link rotation
    await linkModal.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();
    const confirmRotate = page.locator('.ant-modal-confirm');
    await expect(confirmRotate).toBeVisible();
    await confirmRotate.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();

    // Verify rotated link value
    await expect(linkInput).toHaveValue(/rotated-token-xyz/);
    await linkModal.getByRole('button', { name: 'Đóng' }).click();
  });

  // Scenario L3b: Initial Tenant Link Issuance (when token not pre-generated)
  test('Scenario L3b: Initial Link Issuance on Room Without Prior Token (No Revoke Dialog)', async ({ page }) => {
    let room: Room = {
      id: 'room-102',
      buildingId: 'bld-01',
      buildingName: 'Nhà trọ Bình An',
      name: 'Phòng 102',
      rent: 3000000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: {
        id: 'occ-02',
        tenantName: 'Trần Quốc Bảo',
        phone: '0912345678',
        people: 1,
        startDate: '2026-10-01',
        depositRequired: 3000000,
        depositPaid: 0,
        depositHeld: 0,
        token: undefined,
        url: undefined,
      },
      isArchived: false,
    };

    let rotateCalled = false;
    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, roomCount: 1, currentRooms: 1, maxRooms: null, availablePackages: [{ id: 'pro_1m', name: '1 tháng', days: 30, price: 99000, priceVnd: 99000, description: 'Gói 1 tháng' }] } });
      if (path === '/api/rooms') {
        return route.fulfill({ json: [room] });
      }
      if (path === '/api/rooms/room-102/rotate-link' && method === 'POST') {
        rotateCalled = true;
        if (room.occupancy) {
          room.occupancy.token = 'newly-created-token-999';
          room.occupancy.url = '/t/newly-created-token-999';
        }
        return route.fulfill({ json: { token: 'newly-created-token-999', url: '/t/newly-created-token-999' } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Phòng 102', { exact: true })).toBeVisible();

    // Clicking "Cấp link cho khách" on a room without a token must NOT show the revoke confirmation modal
    await page.getByRole('button', { name: 'Cấp link cho khách' }).click();

    // Confirm dialog must NOT appear
    const confirmModal = page.locator('.ant-modal-confirm');
    await expect(confirmModal).toHaveCount(0);

    // Link modal must open directly with newly generated token
    const linkModal = page.locator('.ant-modal').filter({ hasText: 'Link hóa đơn' });
    await expect(linkModal).toBeVisible();
    const linkInput = linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]');
    await expect(linkInput).toHaveValue(/newly-created-token-999/);
    await expect(linkModal.locator('.ant-qrcode')).toBeVisible();
    expect(rotateCalled).toBe(true);
    await linkModal.getByRole('button', { name: 'Đóng' }).click();
  });

  // Scenario L4: Meter Reading & Invoice Generation
  test('Scenario L4: Meter Reading & Invoice Generation', async ({ page }) => {
    const period = '2026-10';
    const room: Room = {
      id: 'room-101',
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
        tenantName: 'Nguyễn Văn A',
        phone: '0901234567',
        people: 2,
        startDate: '2026-10-01',
        depositRequired: 3000000,
        depositPaid: 3000000,
        depositHeld: 3000000,
      },
      isArchived: false,
    };

    let readingSaved: Reading | undefined = undefined;
    let generatedInvoice: Invoice | undefined = undefined;

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/rooms') return route.fulfill({ json: [room] });
      if (path === '/api/readings') {
        if (method === 'GET') {
          return route.fulfill({ json: readingSaved ? [readingSaved] : [] });
        }
        if (method === 'POST') {
          const body = route.request().postDataJSON() as { roomId: string; period: string; currentReading: number };
          const consumption = body.currentReading - (room.previousReading ?? 100);
          readingSaved = {
            id: 'reading-01',
            roomId: body.roomId,
            occupancyId: room.occupancy!.id,
            roomName: room.name,
            period: body.period,
            previousReading: room.previousReading ?? 100,
            currentReading: body.currentReading,
            consumption,
            isSubmitted: true,
            issuanceStatus: 'Issued',
          };
          generatedInvoice = {
            id: 'inv-101',
            roomId: room.id,
            roomName: room.name,
            period: body.period,
            kind: 'Monthly',
            lifecycleState: 'Issued',
            previousReading: 100,
            currentReading: body.currentReading,
            consumption,
            rent: 3000000,
            electricityAmount: consumption * 3500,
            waterAmount: 100000,
            serviceAmount: 50000,
            total: 3000000 + consumption * 3500 + 100000 + 50000,
            paid: 0,
            outstanding: 3000000 + consumption * 3500 + 100000 + 50000,
            status: 'Unpaid',
            depositHeld: 3000000,
            depositSettlements: [],
            isFinalMeterReading: false,
            occupancyId: room.occupancy!.id,
          };
          return route.fulfill({ json: { reading: readingSaved, invoice: generatedInvoice } });
        }
      }
      if (path === '/api/invoices') {
        return route.fulfill({ json: { items: generatedInvoice ? [generatedInvoice] : [], total: generatedInvoice ? 1 : 0, draftCount: 0 } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/readings');
    await expect(page.getByRole('heading', { level: 2, name: 'Lập hóa đơn' })).toBeVisible();
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();

    // 1. Enter new reading with consumption calculation
    const readingInput = page.getByLabel('Chỉ số mới Phòng 101');
    await readingInput.fill('180');

    // Verify consumption displays 80 kWh (180 - 100) and estimated cost
    await expect(page.locator('.consumption-value')).toHaveText('80 kWh');
    await expect(page.getByText('≈ 280.000')).toBeVisible();

    // 2. Click "Chốt & lập hóa đơn" (opens single modal.confirm directly, no Popconfirm)
    await page.getByRole('button', { name: 'Chốt & lập hóa đơn' }).click();

    // 3. Confirm Modal dialog directly
    const confirmModal = page.locator('.ant-modal-confirm');
    await expect(confirmModal).toBeVisible();
    await expect(confirmModal).toContainText('Tiền thuê được tính đủ theo tháng, không phụ thuộc ngày bắt đầu thuê.');
    await confirmModal.getByRole('button', { name: 'Chốt & lập hóa đơn' }).click();

    // 5. Verify tag updates to "Đã phát hành" and progress bar completes
    await expect(page.locator('.submitted-tag')).toHaveText('Đã phát hành');
    await expect(page.getByText('1/1 phòng đã chốt')).toBeVisible();
    await expect(page.getByText('Tất cả phòng đang thuê đã có hóa đơn cho kỳ này.')).toBeVisible();
  });

  // Scenario L5: Invoice Drawer & Idempotent Cash Collection
  test('Scenario L5: Invoice Drawer & Idempotent Cash Collection', async ({ page }) => {
    let capturedIdempotencyKey: string | null = null;
    let invoice: Invoice = {
      id: 'inv-101',
      roomId: 'room-101',
      roomName: 'Phòng 101',
      buildingName: 'Nhà trọ Bình An',
      tenantName: 'Nguyễn Văn A',
      period: '2026-10',
      kind: 'Monthly',
      lifecycleState: 'Issued',
      previousReading: 100,
      currentReading: 180,
      consumption: 80,
      rent: 3000000,
      electricityAmount: 280000,
      waterAmount: 100000,
      serviceAmount: 0,
      total: 3380000,
      paid: 0,
      outstanding: 3380000,
      status: 'Unpaid',
      version: 1,
      tenantToken: 'token-abc',
      depositHeld: 3000000,
      depositSettlements: [],
      isFinalMeterReading: false,
      occupancyId: 'occ-01',
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/invoices') {
        return route.fulfill({
          json: {
            items: [invoice],
            total: 1,
            draftCount: 0,
            totals: { billed: 3380000, paid: invoice.paid, outstanding: invoice.outstanding },
          },
        });
      }
      if (path === '/api/invoices/inv-101') {
        return route.fulfill({ json: invoice });
      }
      if (path === '/api/invoices/inv-101/corrections') {
        return route.fulfill({ json: [] });
      }
      if (path === '/api/invoices/inv-101/receipts' && method === 'POST') {
        const body = route.request().postDataJSON() as {
          amount: number;
          note: string;
          idempotencyKey: string;
          expectedVersion?: number;
        };
        capturedIdempotencyKey = body.idempotencyKey;
        invoice = {
          ...invoice,
          paid: invoice.paid + body.amount,
          outstanding: Math.max(0, invoice.outstanding - body.amount),
          status: invoice.outstanding - body.amount <= 0 ? 'Paid' : 'Partial',
          version: (invoice.version ?? 1) + 1,
        };
        return route.fulfill({
          json: {
            id: `receipt-${Date.now()}`,
            invoiceId: invoice.id,
            amount: body.amount,
            paid: invoice.paid,
            outstanding: invoice.outstanding,
            status: invoice.status,
          },
        });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/invoices');
    await expect(page.getByRole('heading', { level: 2, name: 'Hóa đơn' })).toBeVisible();
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();
    await expect(page.locator('.status-unpaid')).toHaveText('Chưa thanh toán');

    // 1. Open Invoice Drawer
    await page.getByRole('button', { name: 'Chi tiết' }).click();
    const drawer = page.locator('.ant-drawer');
    await expect(drawer).toBeVisible();
    await expect(drawer.getByRole('heading', { level: 4, name: 'Phòng 101' })).toBeVisible();

    // Verify invoice breakdown cards
    await expect(drawer.getByText('3.000.000')).toBeVisible(); // rent
    await expect(drawer.getByText('280.000')).toBeVisible(); // electricity
    await expect(drawer.getByText('100.000')).toBeVisible(); // water
    await expect(drawer.locator('.breakdown-total').getByText('3.380.000')).toBeVisible(); // total

    // A single confirmation settles the full outstanding balance, with no editable amount.
    await expect(drawer.getByLabel('Số tiền nhận')).toHaveCount(0);
    await drawer.getByLabel('Ghi chú').fill('Tất toán');
    await drawer.getByRole('button', { name: 'Lưu khoản thu' }).click();
    const confirmReceiptModal = page.locator('.ant-modal-confirm');
    await expect(confirmReceiptModal).toBeVisible();
    await expect(confirmReceiptModal).toContainText('3.380.000');
    await confirmReceiptModal.getByRole('button', { name: 'Ghi nhận khoản thu' }).click();
    expect(capturedIdempotencyKey).toBeTruthy();
    expect(capturedIdempotencyKey!.length).toBeGreaterThan(10);

    // Verify status transitions to Paid
    await expect(drawer.locator('.status-paid')).toHaveText('Đã thanh toán');
    await expect(drawer.getByText('Hóa đơn đã được thanh toán đầy đủ.')).toBeVisible();
  });

  test('Scenario L8: Preview and export Rental Contract & CT01 Residence Form', async ({ page }) => {
    const rooms: Room[] = [
      {
        id: 'room-101',
        buildingId: 'bld-01',
        buildingName: 'Nhà trọ Bình An',
        name: 'Phòng 101',
        rent: 3500000,
        electricityRate: 3500,
        waterFee: 100000,
        waterPerPerson: true,
        serviceFee: 50000,
        initialReading: 120,
        previousReading: 120,
        occupancy: {
          id: 'occ-101',
          tenantName: 'Nguyễn Văn Khách',
          phone: '0909123456',
          people: 2,
          startDate: '2026-10-01',
          depositRequired: 3500000,
          depositPaid: 3500000,
          depositHeld: 3500000,
        },
        isArchived: false,
      },
    ];

    let documentData = {
      roomId: 'room-101',
      buildingId: 'bld-01',
      occupancyId: 'occ-101',
      ownerName: 'Chủ Trọ Bình An',
      ownerPhone: '0911222333',
      ownerEmail: 'landlord@example.com',
      ownerCitizenId: '079088001122',
      ownerDateOfBirth: '1988-05-10',
      ownerCitizenIdIssuedDate: '2022-01-15',
      ownerCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
      ownerPermanentAddress: '123 Đường Số 1, Quận 7, TP.HCM',
      bankBin: '970436',
      bankName: 'Ngân hàng TMCP Ngoại thương Việt Nam',
      bankShortName: 'Vietcombank',
      bankAccountNumber: '0123456789',
      bankAccountName: 'CHU TRO BINH AN',
      buildingName: 'Nhà trọ Bình An',
      buildingAddress: '123 Đường Số 1, Phường Tân Phong, Quận 7, TP.HCM',
      policeStationName: 'Công an Phường Tân Phong, Quận 7, TP.HCM',
      roomName: 'Phòng 101',
      roomAreaSqm: 25,
      rent: 3500000,
      rentInWords: 'Ba triệu năm trăm nghìn đồng chẵn',
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: true,
      serviceFee: 50000,
      initialReading: 120,
      tenantName: 'Nguyễn Văn Khách',
      tenantPhone: '0909123456',
      tenantEmail: 'khach@example.com',
      people: 2,
      tenantCitizenId: '079099001234',
      tenantDateOfBirth: '1999-08-20',
      tenantGender: 'Nam',
      tenantCitizenIdIssuedDate: '2023-06-01',
      tenantCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
      tenantPermanentAddress: '45 Lê Duẩn, Đà Nẵng',
      startDate: '2026-10-01',
      contractEndDate: '2027-10-01',
      paymentDueDay: 5,
      depositRequired: 3500000,
      depositInWords: 'Ba triệu năm trăm nghìn đồng chẵn',
      depositPaid: 3500000,
      handoverAssets: '01 máy lạnh, 01 tủ lạnh',
      additionalTerms: 'Không nuôi thú cưng',
      householdHeadRelation: 'Người thuê trọ',
      coResidents: [],
    };

    let savedPayload: Record<string, unknown> | null = null;

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') {
        return route.fulfill({
          json: [{ id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', isArchived: false }],
        });
      }
      if (path === '/api/rooms') return route.fulfill({ json: rooms });
      if (path === '/api/subscription/status') {
        return route.fulfill({
          json: { tier: 'Pro', isPro: true, expiresAt: null, roomCount: 1, maxRooms: 999, availablePackages: [] },
        });
      }
      if (path === '/api/rooms/room-101/documents' && method === 'GET') {
        return route.fulfill({ json: documentData });
      }
      if (path === '/api/rooms/room-101/documents' && method === 'PUT') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        savedPayload = body;
        documentData = { ...documentData, ...body } as typeof documentData;
        return route.fulfill({ json: documentData });
      }
      if (path === '/api/rooms/room-101/documents/contract.docx' || path === '/api/rooms/room-101/documents/ct01.docx') {
        return route.fulfill({
          status: 200,
          contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
          body: Buffer.from('PK-mock-docx-content'),
        });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Nguyễn Văn Khách')).toBeVisible();

    // Open room action menu -> Xuất hợp đồng & CT01
    await page.getByRole('button', { name: 'Tác vụ Phòng 101' }).click();
    await page.getByRole('menuitem', { name: 'Xuất hợp đồng & CT01' }).click();

    const drawer = page.locator('.contract-documents-drawer');
    await expect(drawer).toBeVisible();

    // Verify Rental Contract A4 preview shows pre-filled values
    const preview = drawer.getByTestId('document-a4-preview');
    await expect(preview).toContainText('HỢP ĐỒNG THUÊ PHÒNG TRỌ');
    await expect(preview).toContainText('Nguyễn Văn Khách');
    await expect(preview).toContainText('Ba triệu năm trăm nghìn đồng chẵn');
    await expect(preview).toContainText('0123456789');

    // Switch to CT01 preview tab
    await drawer.getByText('Bản xem trước: Mẫu CT01 (TT 66/2023/TT-BCA)').click();
    await expect(preview).toContainText('TỜ KHAI THAY ĐỔI THÔNG TIN CƯ TRÚ');
    await expect(preview).toContainText('NGUYỄN VĂN KHÁCH');
    await expect(preview).toContainText('Công an Phường Tân Phong, Quận 7, TP.HCM');

    // Update owner Name and tenant Citizen ID in the form and verify live A4 preview updates
    const ownerNameInput = drawer.getByLabel('Họ và tên chủ trọ');
    await expect(ownerNameInput).toBeEnabled();
    await ownerNameInput.fill('Nguyễn Hoàng Chủ Trọ');
    await expect(preview).toContainText('NGUYỄN HOÀNG CHỦ TRỌ');

    const tenantIdInput = drawer.getByLabel('Số CCCD / Định danh cá nhân (12 số)');
    await tenantIdInput.fill('079099887766');

    // Verify multi-column form controls stay horizontally aligned on the same baseline
    const controlBoxFor = (label: string) =>
      drawer.getByLabel(label).first().locator('xpath=ancestor::div[contains(@class,"ant-form-item-control-input")][1]').boundingBox();

    const tenantDobBox = await controlBoxFor('Ngày sinh');
    const tenantIssuedBox = await controlBoxFor('Ngày cấp CCCD');
    expect(tenantDobBox).not.toBeNull();
    expect(tenantIssuedBox).not.toBeNull();
    expect(Math.abs(tenantDobBox!.y - tenantIssuedBox!.y)).toBeLessThanOrEqual(1);

    const endDateBox = await controlBoxFor('Ngày kết thúc hợp đồng');
    const dueDayBox = await controlBoxFor('Hạn đóng tiền (ngày)');
    const areaBox = await controlBoxFor('Diện tích phòng');
    expect(endDateBox).not.toBeNull();
    expect(dueDayBox).not.toBeNull();
    expect(areaBox).not.toBeNull();
    expect(Math.abs(endDateBox!.y - dueDayBox!.y)).toBeLessThanOrEqual(1);
    expect(Math.abs(dueDayBox!.y - areaBox!.y)).toBeLessThanOrEqual(1);

    await drawer.getByRole('button', { name: 'Lưu thông tin' }).click();

    expect(savedPayload).toBeTruthy();
    expect(savedPayload?.ownerName).toBe('Nguyễn Hoàng Chủ Trọ');
    expect(savedPayload?.tenantCitizenId).toBe('079099887766');
  });

  test('Scenario L9: CCCD QR scan auto-fill (tenant, co-resident, owner) and live cross-device sync without reload', async ({ page }) => {
    let room: Room = {
      id: 'room-202',
      buildingId: 'bld-01',
      buildingName: 'Nhà trọ Bình An',
      name: 'Phòng 202',
      rent: 4000000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: true,
      serviceFee: 50000,
      initialReading: 50,
      previousReading: 50,
      occupancy: null,
      isArchived: false,
    };

    let documentData = {
      roomId: 'room-202',
      buildingId: 'bld-01',
      occupancyId: 'occ-202',
      ownerName: 'Chủ Trọ Bình An',
      ownerPhone: '0911222333',
      ownerEmail: 'landlord@example.com',
      ownerCitizenId: '',
      ownerDateOfBirth: null as string | null,
      ownerCitizenIdIssuedDate: null as string | null,
      ownerCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
      ownerPermanentAddress: '',
      bankBin: '970436',
      bankName: 'Ngân hàng TMCP Ngoại thương Việt Nam',
      bankShortName: 'Vietcombank',
      bankAccountNumber: '0123456789',
      bankAccountName: 'CHU TRO BINH AN',
      buildingName: 'Nhà trọ Bình An',
      buildingAddress: '123 Đường Số 1, Quận 7, TP.HCM',
      policeStationName: 'Công an Phường Tân Phong, Quận 7, TP.HCM',
      roomName: 'Phòng 202',
      roomAreaSqm: 25,
      rent: 4000000,
      rentInWords: 'Bốn triệu đồng chẵn',
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: true,
      serviceFee: 50000,
      initialReading: 50,
      tenantName: 'Lê Thị Mai Anh',
      tenantPhone: '0909888777',
      tenantEmail: '',
      people: 2,
      tenantCitizenId: '079098001234',
      tenantDateOfBirth: '1998-12-24',
      tenantGender: 'Nữ',
      tenantCitizenIdIssuedDate: '2022-09-10',
      tenantCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
      tenantPermanentAddress: '88 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh',
      startDate: '2026-10-01',
      contractEndDate: '2027-10-01',
      paymentDueDay: 5,
      depositRequired: 4000000,
      depositInWords: 'Bốn triệu đồng chẵn',
      depositPaid: 4000000,
      handoverAssets: '01 máy lạnh',
      additionalTerms: '',
      householdHeadRelation: 'Người thuê trọ',
      coResidents: [
        {
          fullName: 'Trần Minh Quân',
          citizenId: '079200005678',
          gender: 'Nam',
          dateOfBirth: '2000-05-05',
          relation: 'Người thuê trọ',
        },
      ],
    };

    let submittedTenantBody: Record<string, unknown> | null = null;

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') {
        return route.fulfill({
          json: [{ id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', isArchived: false }],
        });
      }
      if (path === '/api/rooms') return route.fulfill({ json: [room] });
      if (path === '/api/subscription/status' || path === '/api/subscription') {
        return route.fulfill({
          json: { tier: 'Pro', isPro: true, expiresAt: null, roomCount: 1, maxRooms: 999, availablePackages: [] },
        });
      }
      if (path === '/api/rooms/room-202/occupancy' && method === 'POST') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        submittedTenantBody = body;
        room = {
          ...room,
          occupancy: {
            id: 'occ-202',
            tenantName: String(body.tenantName ?? ''),
            phone: String(body.phone ?? ''),
            people: Number(body.people ?? 1),
            startDate: String(body.startDate ?? '2026-10-01'),
            tenantCitizenId: String(body.tenantCitizenId ?? ''),
            tenantDateOfBirth: String(body.tenantDateOfBirth ?? ''),
            tenantGender: String(body.tenantGender ?? ''),
            tenantPermanentAddress: String(body.tenantPermanentAddress ?? ''),
            depositRequired: 0,
            depositPaid: 0,
            depositHeld: 0,
          },
        };
        return route.fulfill({ json: room });
      }
      if (path === '/api/rooms/room-202/documents' && method === 'GET') {
        return route.fulfill({ json: documentData });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms/room-202/tenant');
    const tenantWorkspace = page.locator('.room-tenant-page');
    await expect(tenantWorkspace).toBeVisible();

    // 1. Open QR modal and test invalid QR string validation first
    await tenantWorkspace.getByRole('button', { name: 'Quét / Tải ảnh CCCD' }).first().click();
    const qrModal = page.locator('.cccd-qr-modal');
    await expect(qrModal).toBeVisible();

    const qrInput = qrModal.locator('#cccd-qr-raw-input');
    await qrInput.fill('chuoi-khong-hop-le');
    await qrModal.getByRole('button', { name: 'Điền thông tin từ chuỗi QR' }).click();
    await expect(qrModal.getByText('Chuỗi QR không đúng định dạng CCCD Việt Nam.')).toBeVisible();

    // 2. Paste valid 7-field primary tenant CCCD QR
    await qrInput.fill('079098001234|025123456|Lê Thị Mai Anh|24121998|Nữ|88 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh|10092022');
    await expect(qrModal).toBeHidden();

    // Verify primary tenant fields are auto-filled
    await expect(tenantWorkspace.getByLabel('Tên người thuê')).toHaveValue('Lê Thị Mai Anh');
    await expect(tenantWorkspace.getByLabel('Số CCCD / Định danh cá nhân (12 số)')).toHaveValue('079098001234');
    await expect(tenantWorkspace.getByLabel('Địa chỉ thường trú')).toHaveValue('88 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh');

    // 3. Scan / Paste co-resident CCCD QR (and verify duplicate CCCD updates existing row instead of duplicating)
    await tenantWorkspace.getByRole('button', { name: 'Quét CCCD thêm thành viên' }).first().click();
    await expect(qrModal).toBeVisible();
    await qrModal.locator('#cccd-qr-raw-input').fill('079200005678||Trần Minh Quân|05052000|Nam|88 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh|12102022');
    await expect(qrModal).toBeHidden();

    await expect(tenantWorkspace.locator('.room-tenant-coresident-row')).toHaveCount(1);
    await expect(tenantWorkspace.getByText('2 người')).toBeVisible();

    // Re-scan the same co-resident Citizen ID with updated name -> should still be 1 row (no duplicate)
    await tenantWorkspace.getByRole('button', { name: 'Quét CCCD thêm thành viên' }).first().click();
    await qrModal.locator('#cccd-qr-raw-input').fill('079200005678|Trần Minh Quân|05052000|Nam|88 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh|12102022');
    await expect(qrModal).toBeHidden();
    await expect(tenantWorkspace.locator('.room-tenant-coresident-row')).toHaveCount(1);

    // Save tenant and open Contract & CT01 page directly
    await tenantWorkspace.getByRole('button', { name: 'Xuất hợp đồng & CT01' }).first().click();
    await expect(page).toHaveURL(/\/rooms\/room-202\/documents$/);
    expect(submittedTenantBody).toBeTruthy();
    expect(submittedTenantBody?.tenantName).toBe('Lê Thị Mai Anh');
    expect(submittedTenantBody?.tenantCitizenId).toBe('079098001234');
    expect(submittedTenantBody?.people).toBe(2);

    // 4. Verify live cross-device sync on Contract page without F5 reload:
    // Simulate landlord scanning a new tenant CCCD on their phone and saving to the server while PC has Contract page open
    const preview = page.getByTestId('document-a4-preview');
    await expect(preview).toContainText('Lê Thị Mai Anh');

    documentData = {
      ...documentData,
      tenantName: 'Phạm Hoàng Gia Bảo',
      tenantCitizenId: '079095009999',
      tenantPermanentAddress: '100 Điện Biên Phủ, Quận Bình Thạnh, TP. Hồ Chí Minh',
    };

    // Wait for background sync (or window focus) without calling page.reload()
    await expect(preview).toContainText('Phạm Hoàng Gia Bảo', { timeout: 8000 });
    await expect(preview).toContainText('079095009999');
  });

  test('Scenario L10: Owner Subscription Voucher Redemption & Error Handling (/account-status)', async ({ page }) => {
    let currentUser = {
      ...landlordUser,
      isPro: false,
      subscriptionTier: 'Free' as 'Free' | 'Pro',
      subscriptionExpiresAt: null as string | null,
      maxRooms: 5 as number | null,
    };

    let subStatus = {
      tier: 'Free' as 'Free' | 'Pro',
      isPro: false,
      expiresAt: null as string | null,
      roomCount: 5,
      maxRooms: 5,
      availablePackages: [
        { id: 'pro_1m', name: 'Gói 1 tháng', days: 30, price: 99000, description: 'Gia hạn theo tháng' },
      ],
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: currentUser });
      if (path === '/api/auth/profile' && method === 'PUT') {
        const body = route.request().postDataJSON() as Record<string, unknown>;
        currentUser = {
          ...currentUser,
          name: String(body.name ?? currentUser.name),
          phone: (body.phone as string | null) ?? null,
          citizenId: (body.citizenId as string | null) ?? null,
          permanentAddress: (body.permanentAddress as string | null) ?? null,
        };
        return route.fulfill({ json: currentUser });
      }
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/subscription') return route.fulfill({ json: subStatus });
      if (path === '/api/subscription/redeem-code' && method === 'POST') {
        const body = route.request().postDataJSON() as { code?: string };
        const code = (body.code ?? '').trim().toUpperCase();
        if (code === 'HET_HAN') {
          return route.fulfill({
            status: 400,
            json: { message: 'Mã khuyến mãi này đã hết hạn sử dụng.' },
          });
        }
        if (code === 'TANGPRO3') {
          currentUser = {
            ...currentUser,
            isPro: true,
            subscriptionTier: 'Pro',
            subscriptionExpiresAt: '2027-01-23T08:00:00Z',
            maxRooms: null,
          };
          subStatus = {
            ...subStatus,
            tier: 'Pro',
            isPro: true,
            expiresAt: '2027-01-23T08:00:00Z',
            maxRooms: 999,
          };
          return route.fulfill({
            json: {
              success: true,
              message: 'Kích hoạt thành công!',
              subscriptionTier: 'Pro',
              subscriptionExpiresAt: '2027-01-23T08:00:00Z',
              addedMonths: 3,
            },
          });
        }
        return route.fulfill({ status: 404, json: { message: 'Mã khuyến mãi không tồn tại.' } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/account-status');
    await expect(page.getByRole('heading', { name: 'Gói cước dịch vụ & Giới hạn phòng' })).toBeVisible();
    await expect(page.getByText('Đã đạt giới hạn phòng của gói Miễn phí')).toBeVisible();

    const voucherInput = page.getByPlaceholder('Ví dụ: TET2026, BANMOI');
    const applyButton = page.getByRole('button', { name: 'Áp dụng' });
    await expect(applyButton).toBeDisabled();

    // 1. Try expired voucher code -> error toast shown, input retained for correction
    await voucherInput.fill('het_han');
    await expect(voucherInput).toHaveValue('HET_HAN');
    await expect(applyButton).toBeEnabled();
    await applyButton.click();
    await expect(page.getByText('Mã khuyến mãi này đã hết hạn sử dụng.')).toBeVisible();
    await expect(voucherInput).toHaveValue('HET_HAN');

    // 2. Redeem valid voucher code -> upgraded to Pro, input cleared, limit warning disappears
    await voucherInput.fill('tangpro3');
    await expect(voucherInput).toHaveValue('TANGPRO3');
    await expect(applyButton).not.toHaveClass(/ant-btn-loading/);
    await applyButton.click();

    await expect(page.getByText('Kích hoạt thành công! Bạn nhận được 3 tháng sử dụng gói Pro.')).toBeVisible();
    await expect(voucherInput).toHaveValue('');
    await expect(page.locator('.ant-tag-gold').filter({ hasText: 'PRO' })).toBeVisible();
    await expect(page.getByText('5 phòng (Không giới hạn)')).toBeVisible();

    // 3. Navigate to dedicated "Hồ sơ" (/profile) menu item and update Landlord Profile (Bên A)
    await page.locator('.app-sider').getByText('Hồ sơ', { exact: true }).click();
    await expect(page).toHaveURL(/\/profile$/);
    await expect(page.getByRole('heading', { name: 'Hồ sơ' })).toBeVisible();
    await page.getByLabel('Họ và tên chủ trọ').fill('Trần Văn Chủ Hộ');
    await page.getByLabel('Số điện thoại chủ trọ').fill('0909555666');
    await page.getByLabel('Số CCCD / Định danh chủ hộ (12 số)').fill('079088005566');
    await page.getByRole('button', { name: 'Lưu thông tin' }).click();
    await expect(page.getByText('Đã cập nhật thông tin chủ trọ.')).toBeVisible();
    await expect(page.locator('.profile-button')).toContainText('Trần Văn Chủ Hộ');
  });

  // Scenario L11: New Landlord Onboarding Checklist (5 steps, progress, dismiss & reopen)
  // Scenario L12: Tenant self check-in alert in Contract/CT01 workspace & meter submission approval/rejection in ReadingsPage
  test('Scenario L12: Tenant self check-in alert in Contract/CT01 and meter submission review/approval/rejection', async ({ page }) => {
    const rooms: Room[] = [
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
        previousReading: 100,
        initialReading: 100,
        occupancy: {
          id: 'occ-01',
          depositRequired: 3000000,
          depositPaid: 3000000,
          depositHeld: 3000000,
          tenantName: 'Nguyễn Thị Thuê',
          phone: '0908889999',
          people: 2,
          startDate: '2026-01-01',
          tenantCitizenId: '079199001234',
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
        previousReading: 200,
        initialReading: 200,
        occupancy: {
          id: 'occ-02',
          depositRequired: 3200000,
          depositPaid: 3200000,
          depositHeld: 3200000,
          tenantName: 'Lê Văn Minh',
          phone: '0907778888',
          people: 1,
          startDate: '2026-01-01',
        },
        isArchived: false,
      },
    ];

    let readings: Reading[] = [];
    let invoices: Invoice[] = [];
    let submissions = [
      {
        id: 'sub-01',
        roomId: 'room-01',
        occupancyId: 'occ-01',
        period: '2026-10',
        previousReading: 100,
        submittedReading: 145,
        approvedReading: null as number | null,
        tenantNote: 'Đồng hồ chụp lúc 8h sáng',
        rejectReason: null as string | null,
        status: 'Pending' as 'Pending' | 'Approved' | 'Rejected',
        photoFileName: 'meter-101.jpg',
        photoContentType: 'image/png',
        photoSizeBytes: 1024,
        submittedAt: '2026-10-24T08:00:00Z',
        reviewedAt: null as string | null,
        readingId: null as string | null,
        invoiceId: null as string | null,
      },
      {
        id: 'sub-02',
        roomId: 'room-02',
        occupancyId: 'occ-02',
        period: '2026-10',
        previousReading: 200,
        submittedReading: 230,
        approvedReading: null as number | null,
        tenantNote: 'Ảnh hơi tối',
        rejectReason: null as string | null,
        status: 'Pending' as 'Pending' | 'Approved' | 'Rejected',
        photoFileName: 'meter-102.jpg',
        photoContentType: 'image/png',
        photoSizeBytes: 1024,
        submittedAt: '2026-10-24T08:15:00Z',
        reviewedAt: null as string | null,
        readingId: null as string | null,
        invoiceId: null as string | null,
      },
    ];

    const png1x1 = Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
      'base64',
    );

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/rooms') return route.fulfill({ json: rooms });
      if (path === '/api/rooms/room-01/documents' && method === 'GET') {
        return route.fulfill({
          json: {
            roomId: 'room-01',
            buildingId: 'bld-01',
            occupancyId: 'occ-01',
            ownerName: 'Chủ Trọ Bình An',
            ownerPhone: '0901112222',
            ownerEmail: 'landlord@example.com',
            ownerCitizenId: '079088001122',
            ownerDateOfBirth: '1988-05-10',
            ownerCitizenIdIssuedDate: '2022-01-15',
            ownerCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
            ownerPermanentAddress: '123 Đường Số 1, Quận 7',
            buildingName: 'Nhà trọ Bình An',
            buildingAddress: '123 Đường Số 1, Quận 7',
            policeStationName: 'Công an Phường Tân Phú, Quận 7',
            roomName: 'Phòng 101',
            roomAreaSqm: 25,
            rent: 3000000,
            rentInWords: 'Ba triệu đồng chẵn',
            electricityRate: 3500,
            waterFee: 100000,
            waterPerPerson: false,
            serviceFee: 50000,
            initialReading: 100,
            tenantName: 'Nguyễn Thị Thuê',
            tenantPhone: '0908889999',
            tenantEmail: 'thue@example.com',
            people: 2,
            tenantCitizenId: '079199001234',
            tenantDateOfBirth: '1999-04-15',
            tenantGender: 'Nữ',
            tenantCitizenIdIssuedDate: '2022-08-10',
            tenantCitizenIdIssuedPlace: 'Cục Cảnh sát QLHC về TTXH',
            tenantPermanentAddress: '100 Trần Hưng Đạo, Quận 1',
            startDate: '2026-01-01',
            contractEndDate: '2027-01-01',
            paymentDueDay: 5,
            depositRequired: 3000000,
            depositInWords: 'Ba triệu đồng chẵn',
            depositPaid: 3000000,
            handoverAssets: '01 máy lạnh, 01 giường',
            additionalTerms: null,
            householdHeadRelation: 'Người thuê trọ',
            coResidents: [
              {
                fullName: 'Trần Văn Cùng Ở',
                dateOfBirth: '2000-01-20',
                gender: 'Nam',
                citizenId: '079200005678',
                relation: 'Em ruột',
              },
            ],
            tenantSelfCheckinAt: '2026-10-24T09:30:00Z',
          },
        });
      }
      if (path === '/api/readings' && method === 'GET') {
        return route.fulfill({ json: readings });
      }
      if (path === '/api/readings/submissions' && method === 'GET') {
        return route.fulfill({ json: submissions });
      }
      if (path.startsWith('/api/readings/submissions/') && path.endsWith('/photo')) {
        return route.fulfill({
          status: 200,
          contentType: 'image/png',
          body: png1x1,
        });
      }
      if (path === '/api/readings/submissions/sub-02/reject' && method === 'POST') {
        const body = route.request().postDataJSON() as { reason?: string | null };
        submissions = submissions.map((s) =>
          s.id === 'sub-02'
            ? { ...s, status: 'Rejected', rejectReason: body.reason ?? 'Ảnh chưa rõ', reviewedAt: '2026-10-24T10:00:00Z' }
            : s,
        );
        return route.fulfill({ json: submissions.find((s) => s.id === 'sub-02') });
      }
      if (path === '/api/readings/submissions/sub-01/approve' && method === 'POST') {
        const body = route.request().postDataJSON() as { currentReading?: number | null };
        const approvedVal = body.currentReading ?? 145;
        readings = [
          ...readings,
          {
            id: 'reading-01',
            roomId: 'room-01',
            occupancyId: 'occ-01',
            roomName: 'Phòng 101',
            buildingName: 'Nhà trọ Bình An',
            period: '2026-10',
            previousReading: 100,
            currentReading: approvedVal,
            consumption: approvedVal - 100,
            invoiceId: 'inv-01',
            issuanceStatus: 'Issued',
          },
        ];
        invoices = [
          ...invoices,
          {
            id: 'inv-01',
            roomId: 'room-01',
            occupancyId: 'occ-01',
            roomName: 'Phòng 101',
            buildingName: 'Nhà trọ Bình An',
            tenantName: 'Nguyễn Thị Thuê',
            period: '2026-10',
            kind: 'Monthly',
            lifecycleState: 'Issued',
            rent: 3000000,
            electricityAmount: (approvedVal - 100) * 3500,
            waterAmount: 100000,
            serviceAmount: 50000,
            total: 3150000 + (approvedVal - 100) * 3500,
            paid: 0,
            status: 'Unpaid',
          },
        ];
        submissions = submissions.map((s) =>
          s.id === 'sub-01'
            ? {
                ...s,
                status: 'Approved',
                approvedReading: approvedVal,
                reviewedAt: '2026-10-24T10:05:00Z',
                readingId: 'reading-01',
                invoiceId: 'inv-01',
              }
            : s,
        );
        return route.fulfill({ json: submissions.find((s) => s.id === 'sub-01') });
      }
      if (path === '/api/invoices' && method === 'GET') {
        return route.fulfill({ json: { items: invoices, total: invoices.length, draftCount: 0 } });
      }
      return route.fulfill({ json: [] });
    });

    // 1. Verify tenantSelfCheckinAt alert on Contract & CT01 workspace
    await page.goto('/rooms/room-01/documents');
    await expect(page.getByText(/Khách thuê đã gửi ảnh CCCD qua link phòng/)).toBeVisible();
    await expect(
      page.getByText(/Hệ thống đã tự động nhận diện và lưu thông tin Hợp đồng \/ Tờ khai CT01 của khách thuê/),
    ).toBeVisible();
    await expect(page.getByTestId('document-a4-preview')).toContainText('079199001234');
    await expect(page.getByTestId('document-a4-preview')).toContainText('Trần Văn Cùng Ở');

    // 2. Navigate to ReadingsPage (/readings) and verify prefilled draft + submission badges
    await page.goto('/readings');
    await expect(page.getByLabel('Chỉ số mới Phòng 101')).toHaveValue(/^145(\.000)?$/);
    await expect(page.getByRole('button', { name: 'Khách gửi: 145 kWh · Xem ảnh & Duyệt' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Khách gửi: 230 kWh · Xem ảnh & Duyệt' })).toBeVisible();

    // 3. Reject submission for Phòng 102
    await page.getByRole('button', { name: 'Khách gửi: 230 kWh · Xem ảnh & Duyệt' }).click();
    const reviewModal = page.locator('.ant-modal').filter({ hasText: 'Kiểm tra & duyệt chỉ số điện khách gửi' });
    await expect(reviewModal).toBeVisible();
    await expect(reviewModal.getByText('Ghi chú từ khách thuê: Ảnh hơi tối')).toBeVisible();
    await reviewModal.getByPlaceholder(/Lý do từ chối/).fill('Ảnh bị mờ, chụp lại giúp anh');
    await reviewModal.getByRole('button', { name: 'Yêu cầu khách chụp lại' }).click();
    await expect(reviewModal).toBeHidden();
    await expect(page.getByText('Đã yêu cầu chụp lại')).toBeVisible();

    // 4. Inspect, adjust reading to 146, and approve submission for Phòng 101
    await page.getByRole('button', { name: 'Khách gửi: 145 kWh · Xem ảnh & Duyệt' }).click();
    await expect(reviewModal).toBeVisible();
    await expect(reviewModal.getByText('Ghi chú từ khách thuê: Đồng hồ chụp lúc 8h sáng')).toBeVisible();
    const approveInput = reviewModal.getByLabel('Chỉ số duyệt chốt');
    await expect(approveInput).toHaveValue(/^145(\.000)?$/);
    await approveInput.fill('146');
    await expect(reviewModal).toContainText('46 kWh');
    await reviewModal.getByRole('button', { name: 'Duyệt chỉ số & phát hành hóa đơn' }).click();
    await expect(reviewModal).toBeHidden();
    await expect(page.getByText('Đã duyệt ảnh khách gửi')).toBeVisible();
  });

  test('Scenario L11: New Landlord Onboarding Checklist on Dashboard tracks progress and supports dismiss/reopen', async ({ page }) => {
    await page.route('**/api/**', async (route) => {
      const path = new URL(route.request().url()).pathname;
      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') {
        return route.fulfill({
          json: {
            ...defaultDashboard,
            totalRooms: 1,
            occupiedRooms: 0,
            totalBilled: 0,
            monthlyBilled: 0,
          },
        });
      }
      if (path === '/api/buildings') {
        return route.fulfill({ json: [{ id: 'bld-01', name: 'Nhà trọ Bình An', address: '123 Đường Số 1', roomCount: 1, isArchived: false }] });
      }
      if (path === '/api/rooms') {
        return route.fulfill({
          json: [
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
              occupancy: null,
              isArchived: false,
            },
          ],
        });
      }
      if (path === '/api/settings/payment') {
        return route.fulfill({
          json: {
            configured: false,
            webhookStatus: 'NotConfigured',
            confirmationMode: 'Manual',
            manualBank: null,
          },
        });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/dashboard');
    const checklist = page.getByTestId('onboarding-checklist');
    await expect(checklist).toBeVisible();
    await expect(checklist.getByText('Hoàn thành 2/5 bước thiết lập')).toBeVisible();
    await expect(page.getByTestId('onboarding-step-building')).toHaveAttribute('data-done', 'true');
    await expect(page.getByTestId('onboarding-step-room')).toHaveAttribute('data-done', 'true');
    await expect(page.getByTestId('onboarding-step-payment')).toHaveAttribute('data-done', 'false');
    await expect(page.getByTestId('onboarding-step-payment').getByText('Bước tiếp theo')).toBeVisible();

    // Dismiss checklist and reopen from Quick Actions header
    await page.getByTestId('onboarding-dismiss').click();
    await expect(checklist).toHaveCount(0);
    const reopenBtn = page.getByTestId('onboarding-reopen');
    await expect(reopenBtn).toBeVisible();
    await reopenBtn.click();
    await expect(checklist).toBeVisible();
  });
});

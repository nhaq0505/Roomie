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
      currentRooms: 0,
      maxRooms: 5,
      availablePackages: [],
    };

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
          const body = route.request().postDataJSON() as {
            buildingId: string;
            name: string;
            rent: number;
            electricityRate: number;
            waterFee: number;
            waterPerPerson: boolean;
            serviceFee: number;
            initialReading: number;
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
            initialReading: body.initialReading,
            previousReading: body.initialReading,
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

    // 1. Create Room via modal
    await page.getByRole('button', { name: 'Tạo phòng trọ' }).first().click();
    const roomModal = page.locator('.ant-modal').filter({ hasText: 'Thêm phòng mới' });
    await expect(roomModal).toBeVisible();

    await roomModal.getByLabel('Tên phòng').fill('Phòng 101');
    await roomModal.getByLabel('Giá thuê / tháng').fill('3000000');
    await roomModal.getByLabel('Giá điện / kWh').fill('3500');
    await roomModal.locator('#waterFee').fill('100000');
    await roomModal.getByLabel('Phí dịch vụ / tháng').fill('50000');
    await roomModal.getByLabel('Chỉ số điện ban đầu').fill('100');

    await roomModal.getByRole('button', { name: 'Tạo phòng' }).click();

    // 2. Verify room displayed with status "Còn trống"
    await expect(page.getByText('Phòng 101', { exact: true })).toBeVisible();
    await expect(page.locator('.room-state-vacant')).toHaveText('Còn trống');

    // 3. Quota enforcement: Free plan with 5/5 active rooms
    rooms = [
      { id: 'r1', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 101', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r2', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 102', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r3', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 103', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r4', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 104', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
      { id: 'r5', buildingId: 'bld-01', buildingName: building.name, name: 'Phòng 105', rent: 3000000, electricityRate: 3500, waterFee: 100000, serviceFee: 50000, occupancy: null, isArchived: false },
    ];
    subStatus = { ...subStatus, currentRooms: 5, maxRooms: 5, isPro: false };

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
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 1, maxRooms: null, availablePackages: [] } });
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
        };
        room = {
          ...room,
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

    // 1. Check in tenant
    await page.getByRole('button', { name: 'Thêm người thuê' }).click();
    const occupancyModal = page.locator('.ant-modal').filter({ hasText: 'Thêm người thuê' });
    await expect(occupancyModal).toBeVisible();

    await occupancyModal.getByLabel('Tên người thuê').fill('Nguyễn Văn A');
    await occupancyModal.getByLabel('Số điện thoại').fill('0901234567');
    await occupancyModal.getByLabel('Số người ở').fill('2');
    await occupancyModal.getByLabel('Tiền cọc yêu cầu').fill('3000000');

    await occupancyModal.getByRole('button', { name: 'Lưu người thuê' }).click();

    // 2. Room transitions to "Đang thuê" with tenant info
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
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 1, maxRooms: null, availablePackages: [] } });
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
});

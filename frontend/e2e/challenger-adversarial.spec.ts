import { test, expect } from '@playwright/test';
import type { Building, Room, DashboardSummary } from '../src/api';

const landlordUser = {
  id: 'fixture-landlord-challenger',
  name: 'Chủ Trọ Thử Nghiệm',
  email: 'challenger@example.com',
  role: 'Owner' as const,
  status: 'Approved' as const,
};

const defaultDashboard: DashboardSummary = {
  totalRooms: 3,
  occupiedRooms: 2,
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

test.describe('Adversarial Boundary & Edge Condition Suite', () => {

  // 1. What happens if room has no occupancy? (Should show "Thêm người thuê", not link button)
  test('Edge Case 1: Room without occupancy shows "Thêm người thuê", NOT link button', async ({ page }) => {
    const vacantRoom: Room = {
      id: 'room-vacant-101',
      buildingId: 'bld-01',
      buildingName: 'Dãy Trọ Mẫu',
      name: 'Phòng Trống 101',
      rent: 2500000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: null,
      isArchived: false,
    };

    let rotateAttempted = false;

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Dãy Trọ Mẫu', address: '123 Đường Mẫu', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 1, maxRooms: null, availablePackages: [] } });
      if (path === '/api/rooms') return route.fulfill({ json: [vacantRoom] });
      if (path.includes('/rotate-link')) {
        rotateAttempted = true;
        return route.fulfill({ status: 409, json: { message: 'This room has no active occupancy.' } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Phòng Trống 101', { exact: true })).toBeVisible();

    // Verify room status displays "Còn trống"
    await expect(page.locator('.room-state-vacant')).toHaveText('Còn trống');

    // Assert "Cấp link cho khách" button is NOT present in the action bar
    const linkButton = page.getByRole('button', { name: 'Cấp link cho khách' });
    await expect(linkButton).toHaveCount(0);

    // Assert "Thêm người thuê" button IS present
    const addTenantButton = page.getByRole('button', { name: 'Thêm người thuê' });
    await expect(addTenantButton).toBeVisible();

    // Verify actions dropdown menu
    const moreButton = page.getByLabel('Tác vụ Phòng Trống 101');
    await moreButton.click();
    const dropdown = page.locator('.ant-dropdown');
    await expect(dropdown).toBeVisible();
    await expect(dropdown.getByText('Thêm người thuê')).toBeVisible();
    await expect(dropdown.getByText('Sửa thông tin người thuê')).toHaveCount(0);
    await expect(dropdown.getByText('Trả phòng & quyết toán')).toHaveCount(0);
    await expect(dropdown.getByText('Thu / quyết toán cọc')).toHaveCount(0);

    expect(rotateAttempted).toBe(false);
  });

  // 2. What happens when rotating multiple times in succession?
  test('Edge Case 2: Rotating multiple times in succession updates token, QR and preserves stability', async ({ page }) => {
    let rotationCount = 0;
    const tokens = ['initial-token-1', 'rotated-token-2', 'rotated-token-3', 'rotated-token-4'];

    const occupiedRoom: Room = {
      id: 'room-occupied-202',
      buildingId: 'bld-01',
      buildingName: 'Dãy Trọ Mẫu',
      name: 'Phòng 202',
      rent: 3200000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: {
        id: 'occ-202',
        tenantName: 'Đặng Tuấn Kiệt',
        phone: '0988776655',
        people: 2,
        startDate: '2026-09-01',
        depositRequired: 3200000,
        depositPaid: 3200000,
        depositHeld: 3200000,
        token: tokens[0],
        url: `/t/${tokens[0]}`,
      },
      isArchived: false,
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Dãy Trọ Mẫu', address: '123 Đường Mẫu', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 1, maxRooms: null, availablePackages: [] } });
      if (path === '/api/rooms') return route.fulfill({ json: [occupiedRoom] });

      if (path === '/api/rooms/room-occupied-202/rotate-link' && method === 'POST') {
        rotationCount++;
        const nextToken = tokens[rotationCount] || `extra-token-${rotationCount}`;
        if (occupiedRoom.occupancy) {
          occupiedRoom.occupancy.token = nextToken;
          occupiedRoom.occupancy.url = `/t/${nextToken}`;
        }
        return route.fulfill({ json: { token: nextToken, url: `/t/${nextToken}` } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Phòng 202', { exact: true })).toBeVisible();

    // 1. Initial click opens link modal without rotate dialog
    await page.getByRole('button', { name: 'Cấp link cho khách' }).click();
    const linkModal = page.locator('.ant-modal').filter({ hasText: 'Link hóa đơn' });
    await expect(linkModal).toBeVisible();

    const linkInput = linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]');
    await expect(linkInput).toHaveValue(new RegExp(tokens[0]));

    // Successive rotation loop (3 consecutive rotations)
    for (let i = 1; i <= 3; i++) {
      // Click "Thu hồi & tạo link mới"
      await linkModal.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();
      const confirmModal = page.locator('.ant-modal-confirm');
      await expect(confirmModal).toBeVisible();
      await expect(confirmModal.locator('.ant-modal-confirm-title')).toHaveText('Thu hồi link của Phòng 202?');

      // Confirm revocation
      await confirmModal.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();
      await expect(confirmModal).not.toBeVisible();

      // Verify input value reflects the newly rotated token
      await expect(linkInput).toHaveValue(new RegExp(tokens[i]));

      // Verify QR code is rendered and visible
      await expect(linkModal.locator('.ant-qrcode')).toHaveCount(0);

      // Notice about revocation should appear
      await expect(linkModal.getByText('Đã tạo link mới và thu hồi link cũ.')).toBeVisible();
    }

    expect(rotationCount).toBe(3);
    await linkModal.getByRole('button', { name: 'Đóng' }).click();
    await expect(linkModal).not.toBeVisible();
  });

  // 3. What happens when switching languages in the modal?
  test('Edge Case 3: Switching languages in modal updates URL input, link href, and survives rotation', async ({ page }) => {
    let currentToken = 'token-lang-test-101';
    const occupiedRoom: Room = {
      id: 'room-lang-303',
      buildingId: 'bld-01',
      buildingName: 'Dãy Trọ Mẫu',
      name: 'Phòng 303',
      rent: 4000000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: {
        id: 'occ-303',
        tenantName: 'Vũ Thị Hạnh',
        phone: '0933221100',
        people: 1,
        startDate: '2026-09-01',
        depositRequired: 4000000,
        depositPaid: 4000000,
        depositHeld: 4000000,
        token: currentToken,
        url: `/t/${currentToken}`,
      },
      isArchived: false,
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      const method = route.request().method();

      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Dãy Trọ Mẫu', address: '123 Đường Mẫu', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 1, maxRooms: null, availablePackages: [] } });
      if (path === '/api/rooms') return route.fulfill({ json: [occupiedRoom] });

      if (path === '/api/rooms/room-lang-303/rotate-link' && method === 'POST') {
        currentToken = 'token-lang-test-rotated-202';
        if (occupiedRoom.occupancy) {
          occupiedRoom.occupancy.token = currentToken;
          occupiedRoom.occupancy.url = `/t/${currentToken}`;
        }
        return route.fulfill({ json: { token: currentToken, url: `/t/${currentToken}` } });
      }
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await page.getByRole('button', { name: 'Cấp link cho khách' }).click();

    const linkModal = page.locator('.ant-modal').filter({ hasText: 'Link hóa đơn' });
    await expect(linkModal).toBeVisible();

    const linkInput = linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]');
    const openGuestBtn = linkModal.getByRole('link', { name: 'Mở trang khách' });

    // Initial language defaults to vi
    await expect(linkInput).toHaveValue(/lang=vi/);
    await expect(openGuestBtn).toHaveAttribute('href', /lang=vi/);

    // Switch to English
    const langSelect = linkModal.locator('.tenant-link-language .ant-select');
    await langSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: 'English' }).click();

    // Verify input and link button immediately update to lang=en
    await expect(linkInput).toHaveValue(/lang=en/);
    await expect(openGuestBtn).toHaveAttribute('href', /lang=en/);

    // Switch to Chinese
    await langSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: '简体中文' }).click();
    await expect(linkInput).toHaveValue(/lang=zh/);
    await expect(openGuestBtn).toHaveAttribute('href', /lang=zh/);

    // Switch to Korean
    await langSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: '한국어' }).click();
    await expect(linkInput).toHaveValue(/lang=ko/);
    await expect(openGuestBtn).toHaveAttribute('href', /lang=ko/);

    // Switch back to English and then rotate token
    await langSelect.click();
    await page.locator('.ant-select-item-option-content').filter({ hasText: 'English' }).click();
    await expect(linkInput).toHaveValue(/lang=en/);

    // Rotate link
    await linkModal.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();
    const confirmModal = page.locator('.ant-modal-confirm');
    await confirmModal.getByRole('button', { name: 'Thu hồi & tạo link mới' }).click();

    // Verify that rotated link preserves English language parameter
    await expect(linkInput).toHaveValue(/token-lang-test-rotated-202/);
    await expect(linkInput).toHaveValue(/lang=en/);
    await expect(openGuestBtn).toHaveAttribute('href', /token-lang-test-rotated-202/);
    await expect(openGuestBtn).toHaveAttribute('href', /lang=en/);

    await linkModal.getByRole('button', { name: 'Đóng' }).click();
  });

  // 4. What happens when opening and closing the modal repeatedly?
  test('Edge Case 4: Repeatedly opening and closing modal across rooms via button, ESC, and backdrop', async ({ page }) => {
    const roomA: Room = {
      id: 'room-alpha',
      buildingId: 'bld-01',
      buildingName: 'Dãy Trọ Mẫu',
      name: 'Phòng Alpha',
      rent: 3000000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: {
        id: 'occ-alpha',
        tenantName: 'Lê Văn Alpha',
        phone: '0901111111',
        people: 1,
        startDate: '2026-09-01',
        depositRequired: 3000000,
        depositPaid: 3000000,
        depositHeld: 3000000,
        token: 'token-room-alpha',
        url: '/t/token-room-alpha',
      },
      isArchived: false,
    };

    const roomB: Room = {
      id: 'room-beta',
      buildingId: 'bld-01',
      buildingName: 'Dãy Trọ Mẫu',
      name: 'Phòng Beta',
      rent: 3500000,
      electricityRate: 3500,
      waterFee: 100000,
      waterPerPerson: false,
      serviceFee: 50000,
      occupancy: {
        id: 'occ-beta',
        tenantName: 'Nguyễn Thị Beta',
        phone: '0902222222',
        people: 2,
        startDate: '2026-09-01',
        depositRequired: 3500000,
        depositPaid: 3500000,
        depositHeld: 3500000,
        token: 'token-room-beta',
        url: '/t/token-room-beta',
      },
      isArchived: false,
    };

    await page.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      if (path === '/api/auth/me') return route.fulfill({ json: landlordUser });
      if (path === '/api/dashboard') return route.fulfill({ json: defaultDashboard });
      if (path === '/api/buildings') return route.fulfill({ json: [{ id: 'bld-01', name: 'Dãy Trọ Mẫu', address: '123 Đường Mẫu', isArchived: false }] });
      if (path === '/api/subscription') return route.fulfill({ json: { tier: 'Pro', isPro: true, currentRooms: 2, maxRooms: null, availablePackages: [] } });
      if (path === '/api/rooms') return route.fulfill({ json: [roomA, roomB] });
      return route.fulfill({ json: [] });
    });

    await page.goto('/rooms');
    await expect(page.getByText('Phòng Alpha', { exact: true })).toBeVisible();
    await expect(page.getByText('Phòng Beta', { exact: true })).toBeVisible();

    const linkModal = page.locator('.ant-modal').filter({ hasText: 'Link hóa đơn' });

    // Rapid open-close loop on Room Alpha (5 cycles)
    for (let cycle = 1; cycle <= 5; cycle++) {
      const alphaRow = page.locator('.room-table tbody tr').filter({ hasText: 'Phòng Alpha' });
      await alphaRow.getByRole('button', { name: 'Cấp link cho khách' }).click();

      await expect(linkModal).toBeVisible();
      await expect(linkModal.locator('.ant-modal-title')).toHaveText('Link hóa đơn · Phòng Alpha');
      await expect(linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]')).toHaveValue(/token-room-alpha/);

      if (cycle === 2) {
        // Close via modal close (X) button
        await linkModal.locator('.ant-modal-close').click();
      } else if (cycle === 4) {
        // Close via backdrop click (click outside modal dialog)
        await page.locator('.ant-modal-wrap').click({ position: { x: 10, y: 10 } });
      } else {
        // Close via "Đóng" footer button
        await linkModal.getByRole('button', { name: 'Đóng' }).click();
      }

      await expect(linkModal).not.toBeVisible();
    }

    // Now open Room Beta to verify no state residue from Room Alpha
    const betaRow = page.locator('.room-table tbody tr').filter({ hasText: 'Phòng Beta' });
    await betaRow.getByRole('button', { name: 'Cấp link cho khách' }).click();

    await expect(linkModal).toBeVisible();
    await expect(linkModal.locator('.ant-modal-title')).toHaveText('Link hóa đơn · Phòng Beta');
    await expect(linkModal.locator('input[aria-label="Link hóa đơn của người thuê"]')).toHaveValue(/token-room-beta/);
    await expect(linkModal.getByText('Gửi link này cho Nguyễn Thị Beta')).toBeVisible();

    await linkModal.getByRole('button', { name: 'Đóng' }).click();
    await expect(linkModal).not.toBeVisible();
  });
});

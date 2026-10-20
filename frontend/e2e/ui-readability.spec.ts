import { expect, test, type Locator, type Page } from '@playwright/test';

async function fixture(page: Page, owner = false) {
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:4178') return route.abort();
    if (!url.pathname.startsWith('/api/')) return route.continue();
    if (url.pathname === '/api/auth/me') return owner
      ? route.fulfill({ json: { id: 'ui-owner', name: 'Chủ trọ', email: 'owner@example.invalid', role: 'Owner', status: 'Approved' } })
      : route.fulfill({ status: 401, json: { message: 'Guest fixture' } });
    return route.fulfill({ json: url.pathname === '/api/subscription'
      ? { tier: 'Free', isPro: false, currentRooms: 0, maxRooms: 5, expiresAt: null, availablePackages: [] } : [] });
  });
}

async function contrast(target: Locator, placeholder = false, backgroundSelector?: string) {
  return target.evaluate((element, { placeholder, backgroundSelector }) => {
    const parse = (value: string) => {
      const numbers = value.match(/[\d.]+/g)!.map(Number);
      return [numbers[0], numbers[1], numbers[2], numbers[3] ?? 1];
    };
    let node: Element | null = backgroundSelector ? document.querySelector(backgroundSelector) : element;
    let background = [255, 255, 255, 1];
    while (node) {
      const color = parse(getComputedStyle(node).backgroundColor);
      if (color[3] === 1) { background = color; break; }
      node = node.parentElement;
    }
    const foreground = parse(getComputedStyle(element, placeholder ? '::placeholder' : null).color);
    const blend = foreground.slice(0, 3).map((channel, i) => channel * foreground[3] + background[i] * (1 - foreground[3]));
    const luminance = (rgb: number[]) => rgb.slice(0, 3).map(channel => {
      const value = channel / 255;
      return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
    }).reduce((sum, value, i) => sum + value * [.2126, .7152, .0722][i], 0);
    const a = luminance(blend), b = luminance(background);
    return (Math.max(a, b) + .05) / (Math.min(a, b) + .05);
  }, { placeholder, backgroundSelector });
}

test('auth actions, helper text and placeholders retain readable contrast', async ({ page }) => {
  await fixture(page);
  for (const path of ['/login', '/reset-password']) {
    await page.goto(path);
    const submit = page.locator('.auth-submit');
    await expect(submit).toBeVisible();
    expect(await contrast(submit)).toBeGreaterThanOrEqual(4.5);
    await submit.hover();
    expect(await contrast(submit)).toBeGreaterThanOrEqual(4.5);
    expect(await contrast(page.locator('.form-intro .ant-typography').last())).toBeGreaterThanOrEqual(4.5);
    expect(await contrast(page.getByLabel('Email', { exact: true }), true)).toBeGreaterThanOrEqual(4.5);
  }
});

test('landing keeps sign-in reachable in the header across mobile and desktop widths', async ({ page }) => {
  await fixture(page);
  await page.goto('/');
  for (const width of [320, 375, 540, 600, 768, 800, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    const login = page.locator('.landing-header').getByRole('link', { name: 'Đăng nhập', exact: true });
    await expect(login).toBeVisible();
    const box = (await login.boundingBox())!;
    expect(box.y + box.height).toBeLessThan(180);
    expect(box.x + box.width).toBeLessThanOrEqual(width);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  }
  await page.locator('.landing-header').getByRole('link', { name: 'Đăng nhập', exact: true }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test('mobile drawer close button is visible on its dark header and supports keyboard dismissal', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await fixture(page, true);
  await page.goto('/rooms');
  await page.getByRole('button', { name: 'Mở menu', exact: true }).focus();
  await page.keyboard.press('Enter');
  const close = page.locator('.mobile-menu-drawer .ant-drawer-close');
  await expect(close).toBeVisible();
  expect(await contrast(close, false, '.mobile-menu-drawer .ant-drawer-header')).toBeGreaterThanOrEqual(3);
  await close.focus();
  await expect(close).toBeFocused();
  await expect(close).toHaveCSS('outline-style', 'solid');
  await page.keyboard.press('Enter');
  await expect(close).not.toBeVisible();
});

import { expect, test, type Page } from '@playwright/test';

declare global {
  interface Window { __landingMotionStarts: string[]; }
}

async function prepare(page: Page, pauseStories = false, pauseArt = false) {
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:4178') return route.abort();
    if (url.pathname.startsWith('/api/')) {
      return route.fulfill({ status: 401, json: { message: 'Fixture guest' } });
    }
    return route.continue();
  });
  await page.addInitScript(({ pauseStories, pauseArt }) => {
    window.__landingMotionStarts = [];
    const original = Element.prototype.animate;
    Element.prototype.animate = function (...args: Parameters<Element['animate']>) {
      window.__landingMotionStarts.push(this.getAttribute('class') ?? '');
      const animation = original.apply(this, args);
      if (pauseStories && this.classList.contains('story-copy') || pauseArt && this.classList.contains('rooms-art')) animation.pause();
      return animation;
    };
  }, { pauseStories, pauseArt });
}

test('offscreen artwork fades through intermediate opacity instead of flashing into view', async ({ page }) => {
  await prepare(page, false, true);
  await page.goto('/');
  const target = page.locator('.rooms-art');
  await expect(target).toHaveCSS('opacity', '0');
  await target.evaluate(element => element.scrollIntoView({ behavior: 'instant', block: 'center' }));
  await expect.poll(() => target.evaluate(element => element.getAnimations().length)).toBe(1);
  const opacity = await target.evaluate(async element => {
    const animation = element.getAnimations()[0];
    await animation.ready;
    const timing = animation.effect!.getTiming();
    const values = [0, 0.2, 0.5, 0.8].map(progress => {
      animation.currentTime = timing.delay + Number(timing.duration) * progress;
      return Number(getComputedStyle(element).opacity);
    });
    animation.play();
    return values;
  });
  expect(opacity[0]).toBe(0);
  expect(opacity[1]).toBeGreaterThan(0);
  expect(opacity[2]).toBeGreaterThan(opacity[1]);
  expect(opacity[3]).toBeGreaterThan(opacity[2]);
  expect(opacity[3]).toBeLessThan(1);
});

test('landing reveals each section once and leaves its original layout intact', async ({ page }) => {
  await prepare(page);
  await page.goto('/');
  await expect(page.locator('.hero-content h1')).toHaveCSS('opacity', '1');
  await expect(page.locator('.hero-content .landing-btn')).toBeVisible();
  await expect(page.locator('html')).toHaveCSS('scroll-behavior', 'smooth');
  const target = page.locator('.rooms-art');
  await expect(target).toHaveCSS('opacity', '0');
  // Already-painted hero content must never be hidden and replayed after hydration.
  expect(await page.locator('.landing-dashboard').getAttribute('data-landing-pending')).toBeNull();
  const originalBox = await target.evaluate(element => ({ width: element.clientWidth, height: element.clientHeight }));
  await target.evaluate(element => element.scrollIntoView({ behavior: 'instant', block: 'center' }));
  await expect.poll(() => page.evaluate(() => window.__landingMotionStarts.filter(name => name.split(' ').includes('rooms-art')).length)).toBe(1);
  await expect.poll(() => target.evaluate(element => element.getAnimations().length)).toBe(0);
  await expect(target).toHaveCSS('opacity', '1');
  await expect(target).toHaveCSS('transform', 'none');
  expect(await target.evaluate(element => ({ width: element.clientWidth, height: element.clientHeight }))).toEqual(originalBox);
  await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
  await target.evaluate(element => element.scrollIntoView({ behavior: 'instant', block: 'center' }));
  await expect.poll(() => page.evaluate(() => window.__landingMotionStarts.filter(name => name.split(' ').includes('rooms-art')).length)).toBe(1);
  expect(await page.locator('.hero-content').evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0);
});

test('Reduce Motion disables reveals and smooth scrolling, including a live preference change', async ({ page }) => {
  await prepare(page);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await expect(page.locator('html')).toHaveCSS('scroll-behavior', 'auto');
  await page.locator('.rooms-art').evaluate(element => element.scrollIntoView({ behavior: 'instant' }));
  await expect(page.locator('.rooms-art')).toHaveCSS('opacity', '1');
  expect(await page.evaluate(() => window.__landingMotionStarts)).toEqual([]);

  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await page.reload();
  await page.locator('.billing-art').evaluate(element => element.scrollIntoView({ behavior: 'instant', block: 'center' }));
  await expect.poll(() => page.evaluate(() => window.__landingMotionStarts.length)).toBeGreaterThan(0);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await expect.poll(() => page.locator('.landing-page').evaluate(element => element.getAnimations({ subtree: true }).length)).toBe(0);
  await expect(page.locator('html')).toHaveCSS('scroll-behavior', 'auto');
  await expect(page.locator('.tenant-art')).toHaveCSS('opacity', '1');
});

test('prerendered landing remains readable without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await prepare(page);
  await page.goto('http://127.0.0.1:4178/');
  await expect(page.locator('.hero-content h1')).toBeVisible();
  await expect(page.locator('.rooms-art')).toHaveCSS('opacity', '1');
  await expect(page.locator('.landing-cta .landing-btn')).toHaveAttribute('href', '/register');
  await context.close();
});

test('keyboard focus immediately reveals a link inside an unfinished animation', async ({ page }) => {
  await prepare(page, true);
  await page.goto('/');
  const story = page.locator('.story-copy').first();
  await story.evaluate(element => element.scrollIntoView({ behavior: 'instant', block: 'center' }));
  await expect.poll(() => story.evaluate(element => element.getAnimations().length)).toBe(1);
  const link = story.locator('.landing-text-link');
  await link.focus();
  await expect(link).toBeFocused();
  await expect.poll(() => story.evaluate(element => element.getAnimations().length)).toBe(0);
  await expect(story).toHaveCSS('opacity', '1');
  await expect(story).toHaveCSS('transform', 'none');
});

test('unsupported motion APIs preserve readable content and keyboard navigation', async ({ page }) => {
  await prepare(page);
  await page.addInitScript(() => {
    Object.defineProperty(window, 'IntersectionObserver', { value: undefined, configurable: true });
  });
  await page.goto('/');
  const link = page.locator('.product-story .landing-text-link').first();
  await link.focus();
  await expect(link).toBeFocused();
  await expect(page.locator('.story-copy').first()).toHaveCSS('opacity', '1');
  expect(await page.evaluate(() => window.__landingMotionStarts)).toEqual([]);
  await link.press('Enter');
  await expect(page).toHaveURL(/\/register$/);
});

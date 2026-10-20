import assert from 'node:assert/strict';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(__dirname, '..');

const bundle = await build({
  stdin: {
    contents: 'export * from "./src/shared/analytics";',
    resolveDir: frontendRoot,
    loader: 'ts',
  },
  bundle: true,
  write: false,
  format: 'cjs',
  platform: 'node',
});

function createMockEnvironment() {
  const storage = new Map();
  const beaconCalls = [];
  const fetchCalls = [];

  const localStorage = {
    getItem: (key) => storage.has(key) ? storage.get(key) : null,
    setItem: (key, val) => { storage.set(key, String(val)); },
    removeItem: (key) => { storage.delete(key); },
    clear: () => { storage.clear(); },
  };

  const document = {
    referrer: '',
  };

  const window = {
    location: {
      hostname: 'roomie.io.vn',
      pathname: '/',
      search: '',
    },
    document,
    localStorage,
  };

  const navigator = {
    sendBeacon: (url, blob) => {
      beaconCalls.push({ url, blob });
      return true;
    },
  };

  const fetch = async (url, init) => {
    fetchCalls.push({ url, init });
    return { ok: true, status: 200 };
  };

  const sandbox = {
    module: { exports: {} },
    exports: {},
    window,
    document,
    navigator,
    localStorage,
    fetch,
    URL,
    URLSearchParams,
    Blob,
    Date,
    Math,
    Number,
    String,
    Boolean,
    JSON,
    Array,
    Set,
    Map,
    Uint8Array,
    crypto: {
      randomUUID: () => 'uuid-test-1234-5678-90ab-cdef12345678',
      getRandomValues: (arr) => arr.fill(42),
    },
  };

  vm.runInNewContext(bundle.outputFiles[0].text, sandbox);
  return {
    tracker: sandbox.module.exports,
    storage,
    beaconCalls,
    fetchCalls,
    window,
    document,
  };
}

// 1. Browser ID tests
{
  const env = createMockEnvironment();
  const bid1 = env.tracker.getOrCreateBrowserId();
  assert.equal(bid1, 'uuid-test-1234-5678-90ab-cdef12345678');
  assert.equal(env.storage.get('roomie_bid'), bid1);

  // Subsequent call preserves same ID
  const bid2 = env.tracker.getOrCreateBrowserId();
  assert.equal(bid2, bid1);
}

// 2. Session ID & 30-min idle window tests
{
  const env = createMockEnvironment();
  const t0 = 10000000;
  const s0 = env.tracker.getOrCreateSessionId(t0);
  assert.equal(s0.isNew, true);
  assert.ok(s0.sessionId);

  // Call within 10 minutes (less than 30 min idle) -> reuses session
  const t1 = t0 + 10 * 60 * 1000;
  const s1 = env.tracker.getOrCreateSessionId(t1);
  assert.equal(s1.isNew, false);
  assert.equal(s1.sessionId, s0.sessionId);

  // Call after 31 minutes idle -> new session
  const t2 = t1 + 31 * 60 * 1000;
  const s2 = env.tracker.getOrCreateSessionId(t2);
  assert.equal(s2.isNew, true);
  assert.ok(s2.sessionId);
}

// 3. Attribution parsing tests
{
  const env = createMockEnvironment();
  
  // Direct visit
  const direct = env.tracker.parseAttributionFromUrl('', '', '/', 1000);
  assert.equal(direct.source, '(direct)');
  assert.equal(direct.landingPath, '/');

  // UTM visit
  const utm = env.tracker.parseAttributionFromUrl(
    '?utm_source=facebook&utm_medium=cpc&utm_campaign=summer_sale&utm_content=banner1',
    'https://l.facebook.com/l.php?u=...',
    '/pricing',
    2000
  );
  assert.equal(utm.source, 'facebook');
  assert.equal(utm.medium, 'cpc');
  assert.equal(utm.campaign, 'summer_sale');
  assert.equal(utm.content, 'banner1');
  assert.equal(utm.landingPath, '/pricing?utm_source=facebook&utm_medium=cpc&utm_campaign=summer_sale&utm_content=banner1');

  // External referrer without UTM
  const organic = env.tracker.parseAttributionFromUrl(
    '',
    'https://www.google.com/search?q=roomie',
    '/',
    3000
  );
  assert.equal(organic.source, 'www.google.com');
  assert.equal(organic.medium, 'referral');
}

// 4. First touch & Last non-direct attribution persistence
{
  const env = createMockEnvironment();
  const t0 = 1000000;

  // Visit 1: from Google search
  env.window.location.search = '?utm_source=google&utm_medium=organic&utm_campaign=brand';
  env.document.referrer = 'https://www.google.com';
  env.tracker.recordAttribution(t0);

  const firstTouch = env.tracker.getFirstTouchSource(t0);
  assert.ok(firstTouch);
  assert.equal(firstTouch.source, 'google');

  const lastNonDirect1 = env.tracker.getLastNonDirectSource(t0);
  assert.ok(lastNonDirect1);
  assert.equal(lastNonDirect1.source, 'google');

  // Visit 2: direct visit (user opens bookmarked tab 2 days later)
  const t1 = t0 + 2 * 24 * 3600 * 1000;
  env.window.location.search = '';
  env.document.referrer = '';
  env.tracker.recordAttribution(t1);

  // First touch still Google (not overwritten by direct)
  assert.equal(env.tracker.getFirstTouchSource(t1).source, 'google');
  // Last non-direct still Google (direct visits don't overwrite last non-direct)
  assert.equal(env.tracker.getLastNonDirectSource(t1).source, 'google');

  // Visit 3: from Zalo campaign 5 days later
  const t2 = t1 + 3 * 24 * 3600 * 1000;
  env.window.location.search = '?utm_source=zalo&utm_medium=chat&utm_campaign=owner_invite';
  env.tracker.recordAttribution(t2);

  // First touch remains original Google touch
  assert.equal(env.tracker.getFirstTouchSource(t2).source, 'google');
  // Last non-direct is now Zalo!
  assert.equal(env.tracker.getLastNonDirectSource(t2).source, 'zalo');

  // 35 days later: last non-direct expired (> 30 days)
  const t3 = t2 + 35 * 24 * 3600 * 1000;
  assert.equal(env.tracker.getLastNonDirectSource(t3), null);
}

// 5. PII scrubbing & sanitization
{
  const env = createMockEnvironment();

  // Test email, phone, token redaction
  const dirtyProps = {
    userEmail: 'sensitive.user@example.com',
    phoneNumber: '0912345678',
    auth_token: 'secret-bearer-token-12345',
    password: 'MyPassword123!',
    otpCode: '123456',
    normalField: 'pricing_table_pro',
  };

  const scrubbed = env.tracker.scrubPii(dirtyProps);
  assert.equal(scrubbed.userEmail, '[REDACTED]');
  assert.equal(scrubbed.phoneNumber, '[REDACTED]');
  assert.equal(scrubbed.auth_token, '[REDACTED]');
  assert.equal(scrubbed.password, '[REDACTED]');
  assert.equal(scrubbed.otpCode, '[REDACTED]');
  assert.equal(scrubbed.normalField, 'pricing_table_pro');

  // URL query stripping
  const dirtyUrl = '/checkout?secret=xyz123&otp=999888&utm_source=fb&token=bad';
  const cleanUrl = env.tracker.sanitizePath(dirtyUrl);
  assert.equal(cleanUrl, '/checkout?utm_source=fb');
  assert.ok(!cleanUrl.includes('secret'));
  assert.ok(!cleanUrl.includes('otp'));
}

// 6. Registration attribution context
{
  const env = createMockEnvironment();
  env.window.location.search = '?utm_source=tiktok&utm_medium=video';
  env.tracker.recordAttribution(100000);

  const regContext = env.tracker.getRegistrationAttributionContext(100000);
  assert.ok(regContext.browserId);
  assert.ok(regContext.sessionId);
  assert.equal(regContext.firstTouch.source, 'tiktok');
  assert.equal(regContext.lastNonDirect.source, 'tiktok');
}

// 7. Event tracking primitive
{
  const env = createMockEnvironment();
  
  // Non-allowlisted event fails
  const bad = env.tracker.trackEvent('unknown_hack_event', {});
  assert.equal(bad, false);
  assert.equal(env.beaconCalls.length, 0);

  // Allowlisted event succeeds
  const good = env.tracker.trackEvent('cta_click', { cta: 'register_header' });
  assert.equal(good, true);
  assert.equal(env.beaconCalls.length, 1);
  assert.equal(env.beaconCalls[0].url, '/api/analytics/events');
}

console.log('PASS: first-party browser tracking primitive (browser ID, 30m idle session, attribution, PII scrubbing, registration context, allowlisted events)');

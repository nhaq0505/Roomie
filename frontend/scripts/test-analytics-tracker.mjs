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

function createMockEnvironment(options = {}) {
  const storage = new Map();
  const beaconCalls = [];
  const fetchCalls = [];

  const localStorage = options.disableLocalStorage
    ? {
        getItem: () => { throw new Error('SecurityError: localStorage blocked in private browsing'); },
        setItem: () => { throw new Error('QuotaExceededError'); },
        removeItem: () => { throw new Error('Blocked'); },
        clear: () => { storage.clear(); },
      }
    : {
        getItem: (key) => storage.has(key) ? storage.get(key) : null,
        setItem: (key, val) => { storage.set(key, String(val)); },
        removeItem: (key) => { storage.delete(key); },
        clear: () => { storage.clear(); },
      };

  const document = {
    referrer: options.referrer || '',
  };

  const window = {
    location: {
      hostname: 'roomie.io.vn',
      pathname: options.pathname || '/',
      search: options.search || '',
    },
    document,
    localStorage,
  };

  const navigator = {
    webdriver: options.isBot || false,
    userAgent: options.userAgent || 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)',
    sendBeacon: (url, blob) => {
      beaconCalls.push({ url, blob });
      return true;
    },
  };

  const fetch = async (url, init) => {
    fetchCalls.push({ url, init });
    return { ok: true, status: 200 };
  };

  let uuidCounter = 1000;
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
    WeakSet,
    Uint8Array,
    crypto: options.brokenCrypto
      ? {
          randomUUID: () => { throw new Error('Crypto disabled'); },
          getRandomValues: () => { throw new Error('Crypto disabled'); },
        }
      : {
          randomUUID: () => `c4b18f8e-d4c3-4d82-841f-${uuidCounter++}`,
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

// 1. Browser ID & persistence
{
  const env = createMockEnvironment();
  const bid1 = env.tracker.getOrCreateBrowserId();
  assert.equal(bid1, 'c4b18f8e-d4c3-4d82-841f-1000');
  assert.equal(env.storage.get('roomie_bid'), bid1);

  // Subsequent call preserves same ID
  const bid2 = env.tracker.getOrCreateBrowserId();
  assert.equal(bid2, bid1);
}

// 2. Safe ID generation when crypto throws
{
  const env = createMockEnvironment({ brokenCrypto: true });
  const bid = env.tracker.getOrCreateBrowserId();
  assert.ok(bid);
  assert.equal(typeof bid, 'string');
  assert.ok(bid.length >= 16);
}

// 3. In-memory storage stability when localStorage throws (private browsing)
{
  const env = createMockEnvironment({ disableLocalStorage: true });
  const bid1 = env.tracker.getOrCreateBrowserId();
  assert.ok(bid1);
  const bid2 = env.tracker.getOrCreateBrowserId();
  assert.equal(bid1, bid2, 'Browser ID must remain stable in memory when localStorage throws');

  const s1 = env.tracker.getOrCreateSessionId(1000);
  const s2 = env.tracker.getOrCreateSessionId(1000 + 10 * 60 * 1000);
  assert.equal(s1.sessionId, s2.sessionId, 'Session ID must remain stable across calls within idle window');
}

// 4. Session ID & exact 30-min idle boundary
{
  const env = createMockEnvironment();
  const t0 = 10000000;
  const s0 = env.tracker.getOrCreateSessionId(t0);
  assert.equal(s0.isNew, true);
  assert.ok(s0.sessionId);

  // Exactly 29 minutes and 59 seconds: NOT expired
  const t1 = t0 + (30 * 60 * 1000 - 1000);
  const s1 = env.tracker.getOrCreateSessionId(t1);
  assert.equal(s1.isNew, false);
  assert.equal(s1.sessionId, s0.sessionId);

  // Exactly 30 minutes: EXPIRED
  const t2 = t1 + (30 * 60 * 1000);
  const s2 = env.tracker.getOrCreateSessionId(t2);
  assert.equal(s2.isNew, true);
  assert.notEqual(s2.sessionId, s0.sessionId);

  // Corrupt or future timestamp in storage triggers graceful session renewal
  env.storage.set('roomie_last_active', String(t2 + 999999999));
  const sFuture = env.tracker.getOrCreateSessionId(t2 + 1000);
  assert.equal(sFuture.isNew, true, 'Future timestamp must reset session');
}

// 5. Route normalization and token masking
{
  const env = createMockEnvironment();
  assert.equal(env.tracker.normalizeRoute('/'), '/');
  assert.equal(env.tracker.normalizeRoute('/login'), '/login');
  assert.equal(env.tracker.normalizeRoute('/dashboard/'), '/dashboard');
  assert.equal(env.tracker.normalizeRoute('/admin/monitoring?foo=bar#hash'), '/admin/monitoring');

  // Tenant routes: must mask raw secret tokens
  const tenantView = env.tracker.normalizeRoute('/t/d1e2f3a4b5c6d7e8f9a0b1c2');
  assert.equal(tenantView, '/tenant/[token]');

  const tenantCheckout = env.tracker.normalizeRoute('/tenant/secret-token-abc-123/invoices/992211/checkout');
  assert.equal(tenantCheckout, '/tenant/[token]/invoices/[invoice_id]/checkout');

  // GUID masking in paths
  const guidPath = env.tracker.normalizeRoute('/buildings/3fa85f64-5717-4562-b3fc-2c963f66afa6');
  assert.equal(guidPath, '/buildings/[id]');
}

// 6. PII scrubbing & cyclic object protection
{
  const env = createMockEnvironment();

  // Test email, phone, token redaction in strings
  const scrubbedStr = env.tracker.scrubString('Call 0912345678 or email admin@roomie.vn with token eyJhbGciOi');
  assert.ok(!scrubbedStr.includes('0912345678'));
  assert.ok(!scrubbedStr.includes('admin@roomie.vn'));
  assert.ok(!scrubbedStr.includes('eyJhbGciOi'));
  assert.ok(scrubbedStr.includes('[REDACTED_PHONE]'));
  assert.ok(scrubbedStr.includes('[REDACTED_EMAIL]'));
  assert.ok(scrubbedStr.includes('[REDACTED_TOKEN]'));

  // Cyclic object must NOT cause stack overflow or throw
  const cyclicObj = { a: 1, label: 'test' };
  cyclicObj.self = cyclicObj;
  const scrubbedObj = env.tracker.scrubPii(cyclicObj);
  assert.equal(scrubbedObj.a, 1);
  assert.equal(scrubbedObj.self, '[CIRCULAR]');

  // Deeply nested object beyond depth 5 must be bounded
  let deep = { val: 'end' };
  for (let i = 0; i < 10; i++) deep = { nested: deep };
  const scrubbedDeep = env.tracker.scrubPii(deep);
  assert.ok(scrubbedDeep !== undefined);

  // Label sanitization matching server regex ^[a-zA-Z0-9_\-\.]{1,64}$
  assert.equal(env.tracker.sanitizeLabel('facebook_cpc'), 'facebook_cpc');
  assert.equal(env.tracker.sanitizeLabel('dirty@email.com'), '_REDACTED_EMAIL_');
  assert.ok(/^[a-zA-Z0-9_\-\.]{1,64}$/.test(env.tracker.sanitizeLabel('dirty@email.com')));
}

// 7. Attribution parsing & first-touch preservation
{
  const env = createMockEnvironment();
  const t0 = 1000000;

  // Visit 1: Google search
  env.window.location.search = '?utm_source=google&utm_medium=cpc&utm_campaign=summer_promo';
  env.document.referrer = 'https://www.google.com';
  env.tracker.recordAttribution(t0);

  const ft1 = env.tracker.getFirstTouchSource(t0);
  assert.equal(ft1.source, 'google');
  assert.equal(ft1.campaign, 'summer_promo');

  const lnd1 = env.tracker.getLastNonDirectSource(t0);
  assert.equal(lnd1.source, 'google');

  // Visit 2: Direct visit 5 days later
  const t1 = t0 + 5 * 24 * 3600 * 1000;
  env.window.location.search = '';
  env.document.referrer = '';
  env.tracker.recordAttribution(t1);

  // First touch must NOT be overwritten!
  const ft2 = env.tracker.getFirstTouchSource(t1);
  assert.equal(ft2.source, 'google');
  // Last non-direct must stay Google (not overwritten by direct)
  const lnd2 = env.tracker.getLastNonDirectSource(t1);
  assert.equal(lnd2.source, 'google');

  // Visit 3: Zalo campaign 10 days later
  const t2 = t1 + 5 * 24 * 3600 * 1000;
  env.window.location.search = '?utm_source=zalo&utm_medium=chat&utm_campaign=q4';
  env.tracker.recordAttribution(t2);

  // First touch remains original Google touch
  assert.equal(env.tracker.getFirstTouchSource(t2).source, 'google');
  // Last non-direct is updated to Zalo
  assert.equal(env.tracker.getLastNonDirectSource(t2).source, 'zalo');

  // Registration attribution DTO
  const regDto = env.tracker.getRegistrationAttributionForApi(t2);
  assert.ok(regDto.browserId);
  assert.equal(regDto.utmSource, 'zalo');
  assert.equal(regDto.utmCampaign, 'q4');
}

// 8. Event Batch Request payload verification
{
  const env = createMockEnvironment({ pathname: '/pricing' });
  const ok = env.tracker.trackEvent('page_view', {
    eventName: 'Pricing Page View',
    ctaId: 'btn_hero_signup',
  });
  assert.equal(ok, true);
  assert.equal(env.beaconCalls.length, 1);
  assert.equal(env.beaconCalls[0].url, '/api/analytics/events');

  // Inspect the serialized batch payload sent over wire
  const sentBlob = env.beaconCalls[0].blob;
  assert.ok(sentBlob);

  // Non-allowlisted event rejected
  const bad = env.tracker.trackEvent('paid_activation_hack', {});
  assert.equal(bad, false);
}

// 9. Bot exclusion
{
  const envBot = createMockEnvironment({ isBot: true });
  const ok = envBot.tracker.trackEvent('page_view', { eventName: 'Bot Visit' });
  assert.equal(ok, false, 'Bot tracking must be suppressed');
  assert.equal(envBot.beaconCalls.length, 0);
}

console.log('PASS: first-party browser tracking primitive (browser ID, 30m idle session, attribution, PII scrubbing, registration context, allowlisted events, security boundaries)');

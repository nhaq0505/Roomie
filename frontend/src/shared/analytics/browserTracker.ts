// First-party browser tracking primitive
// Strict privacy: No PII (no email, phone, OTP, token, IP, bank info, or raw query tokens).
// Server-authoritative milestones; client events are best-effort, bounded, rate-limited, and SSR-safe.

export interface AttributionData {
  source: string;
  medium?: string;
  campaign?: string;
  term?: string;
  content?: string;
  landingPath?: string;
  timestamp: number;
}

export interface RegistrationAttributionContext {
  browserId: string;
  sessionId: string;
  firstTouch: AttributionData | null;
  lastNonDirect: AttributionData | null;
}

export interface AttributionTouchDto {
  source?: string;
  medium?: string;
  campaign?: string;
  content?: string;
  term?: string;
  landingPage?: string;
  referrer?: string;
  occurredAt?: string;
}

export interface RegistrationAttributionDto {
  browserId?: string;
  firstTouch?: AttributionTouchDto;
  lastTouch?: AttributionTouchDto;
  utmSource?: string;
  utmMedium?: string;
  utmCampaign?: string;
  utmContent?: string;
  utmTerm?: string;
  landingPage?: string;
  referrer?: string;
}

export type AllowlistedEventType =
  | 'page_view'
  | 'cta_click'
  | 'session_start'
  | 'landing_view'
  | 'pricing_view'
  | 'auth_attempt'
  | 'registration_submit'
  | 'custom';

export const ALLOWED_EVENT_TYPES = new Set<string>([
  'page_view',
  'cta_click',
  'session_start',
  'landing_view',
  'pricing_view',
  'auth_attempt',
  'registration_submit',
  'custom',
]);

const STORAGE_KEYS = {
  BROWSER_ID: 'roomie_bid',
  SESSION_ID: 'roomie_sid',
  LAST_ACTIVE: 'roomie_last_active',
  FIRST_TOUCH: 'roomie_first_touch',
  LAST_NON_DIRECT: 'roomie_last_non_direct',
} as const;

export const SESSION_IDLE_TIMEOUT_MS = 30 * 60 * 1000; // 30 minutes idle timeout
export const ATTRIBUTION_WINDOW_MS = 30 * 24 * 60 * 60 * 1000; // 30 days window
const MAX_EVENTS_PER_MINUTE = 60;
const MAX_KEY_LENGTH = 32;
const MAX_VAL_LENGTH = 128;
const MAX_PAYLOAD_BYTES = 4096;

// In-memory fallback cache so IDs remain stable even when localStorage is blocked or throws
const inMemoryStorage = new Map<string, string>();

// In-memory rate limiting state
let eventCountInWindow = 0;
let windowStartTime = 0;

export function isBrowser(): boolean {
  return typeof window !== 'undefined' && typeof document !== 'undefined';
}

export function isBot(): boolean {
  if (!isBrowser()) return false;
  try {
    if (typeof navigator !== 'undefined') {
      if (navigator.webdriver) return true;
      const ua = navigator.userAgent || '';
      if (/bot|crawler|spider|crawling|googlebot|bingbot|slurp|duckduckbot|baiduspider|yandexbot/i.test(ua)) {
        return true;
      }
    }
  } catch {
    // Ignore
  }
  return false;
}

export function safeGetStorage(key: string): string | null {
  if (!isBrowser()) return inMemoryStorage.get(key) ?? null;
  try {
    const val = localStorage.getItem(key);
    return val !== null ? val : (inMemoryStorage.get(key) ?? null);
  } catch {
    return inMemoryStorage.get(key) ?? null;
  }
}

export function safeSetStorage(key: string, value: string): void {
  inMemoryStorage.set(key, value);
  if (!isBrowser()) return;
  try {
    localStorage.setItem(key, value);
  } catch {
    // LocalStorage blocked, quota exceeded, or in private mode; memoryStorage preserves stability
  }
}

export function clearInMemoryStorage(): void {
  inMemoryStorage.clear();
}

export function generateId(): string {
  try {
    if (typeof crypto !== 'undefined') {
      if (typeof crypto.randomUUID === 'function') {
        return crypto.randomUUID();
      }
      if (typeof crypto.getRandomValues === 'function') {
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
      }
    }
  } catch {
    // Fallback if crypto throws in restrictive contexts
  }
  const bytes = new Uint8Array(16);
  for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

// Ensure first-party browser ID (persists indefinitely per browser, no cross-device linking)
export function getOrCreateBrowserId(): string {
  let id = safeGetStorage(STORAGE_KEYS.BROWSER_ID);
  if (!id) {
    id = generateId();
    safeSetStorage(STORAGE_KEYS.BROWSER_ID, id);
  }
  return id;
}

// Session ID with 30-minute idle expiration
export function getOrCreateSessionId(now = Date.now()): { sessionId: string; isNew: boolean } {
  const currentSessionId = safeGetStorage(STORAGE_KEYS.SESSION_ID);
  const rawLastActive = safeGetStorage(STORAGE_KEYS.LAST_ACTIVE);
  const lastActive = rawLastActive ? Number(rawLastActive) : 0;

  const isExpired =
    !currentSessionId ||
    isNaN(lastActive) ||
    lastActive <= 0 ||
    lastActive > now ||
    now - lastActive >= SESSION_IDLE_TIMEOUT_MS;

  if (isExpired) {
    const newSessionId = generateId();
    safeSetStorage(STORAGE_KEYS.SESSION_ID, newSessionId);
    safeSetStorage(STORAGE_KEYS.LAST_ACTIVE, String(now));
    return { sessionId: newSessionId, isNew: true };
  }

  safeSetStorage(STORAGE_KEYS.LAST_ACTIVE, String(now));
  return { sessionId: currentSessionId, isNew: false };
}

// Known static routes in the application
const STATIC_ROUTES = new Set<string>([
  '/',
  '/login',
  '/register',
  '/reset-password',
  '/change-password',
  '/account-status',
  '/welcome',
  '/dashboard',
  '/buildings',
  '/rooms',
  '/readings',
  '/invoices',
  '/reports',
  '/settings/payment',
  '/settings/zalo',
  '/admin/owners',
  '/admin/monitoring',
]);

// Route allowlisting & normalization to prevent leaking arbitrary tokens or PII in URL paths
export function normalizeRoute(rawPathname: string): string {
  if (!rawPathname || typeof rawPathname !== 'string') return '/';

  let clean = rawPathname.trim();
  const qIdx = clean.indexOf('?');
  if (qIdx >= 0) clean = clean.slice(0, qIdx);
  const hashIdx = clean.indexOf('#');
  if (hashIdx >= 0) clean = clean.slice(0, hashIdx);

  clean = clean.replace(/\/+/g, '/');
  if (!clean.startsWith('/')) clean = '/' + clean;
  if (clean.length > 1 && clean.endsWith('/')) clean = clean.slice(0, -1);

  if (STATIC_ROUTES.has(clean)) {
    return clean;
  }

  // Tenant overview and checkout routes (scrub raw tokens and invoice IDs)
  if (/^\/(?:t|tenant)\/[^/]+\/invoices\/[^/]+\/checkout\/?$/.test(clean)) {
    return '/tenant/[token]/invoices/[invoice_id]/checkout';
  }
  if (/^\/(?:t|tenant)\/[^/]+\/?$/.test(clean)) {
    return '/tenant/[token]';
  }

  // Generic sanitization: mask GUIDs, hex tokens, and numeric IDs
  const sanitized = clean
    .replace(/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/g, '[id]')
    .replace(/[0-9a-fA-F]{16,64}/g, '[token]')
    .replace(/\/\d+(?=\/|$)/g, '/[id]');

  return sanitized.slice(0, 64);
}

// PII scrub patterns
const EMAIL_REGEX = /[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/g;
const PHONE_REGEX = /(?:\+?84|0)[3|5|7|8|9]\d{8}/g;
const OTP_REGEX = /\b\d{4,6}\b/g;
const TOKEN_REGEX = /\b(ey[A-Za-z0-9-_=]{8,}(?:\.[A-Za-z0-9-_=]+)*|[0-9a-fA-F]{32,64})\b/gi;
const SUSPICIOUS_KEYS = /password|token|secret|otp|cookie|auth|card|bank|phone|email|pin|bearer/i;

export function scrubString(str: string): string {
  if (typeof str !== 'string') return '';
  let sanitized = str
    .replace(EMAIL_REGEX, '[REDACTED_EMAIL]')
    .replace(PHONE_REGEX, '[REDACTED_PHONE]')
    .replace(TOKEN_REGEX, '[REDACTED_TOKEN]');
  if (/otp|code|pin|secret/i.test(sanitized)) {
    sanitized = sanitized.replace(OTP_REGEX, '[REDACTED_OTP]');
  }
  return sanitized.slice(0, MAX_VAL_LENGTH);
}

export function scrubPii(value: unknown, seen = new WeakSet<object>(), depth = 0): unknown {
  if (depth > 5) return null;
  if (value === null || value === undefined) return null;

  if (typeof value === 'string') {
    return scrubString(value);
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return value;
  }
  if (typeof value !== 'object') {
    return null;
  }

  // Detect and prevent infinite cycles
  if (seen.has(value)) {
    return '[CIRCULAR]';
  }
  seen.add(value);

  if (Array.isArray(value)) {
    return value.slice(0, 10).map((item) => scrubPii(item, seen, depth + 1));
  }

  const cleaned: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(value)) {
    if (typeof k === 'string' && k.length <= MAX_KEY_LENGTH) {
      if (SUSPICIOUS_KEYS.test(k)) {
        cleaned[k] = '[REDACTED]';
      } else {
        cleaned[k] = scrubPii(v, seen, depth + 1);
      }
    }
  }
  return cleaned;
}

// Bounded label sanitization for UTMs and CTAs matching server regex ^[a-zA-Z0-9_\-\.]{1,64}$
export function sanitizeLabel(label?: string | null): string | undefined {
  if (!label || typeof label !== 'string') return undefined;
  const scrubbed = scrubString(label.trim());
  const validCharsOnly = scrubbed.replace(/[^a-zA-Z0-9_\-\.]/g, '_').slice(0, 64);
  return validCharsOnly.length > 0 ? validCharsOnly : undefined;
}

// Strip query parameters except safe analytics keys to guarantee no PII/tokens in landing paths
export function sanitizePath(rawUrlOrPath: string): string {
  try {
    const url = new URL(rawUrlOrPath, 'https://roomie.local');
    const normalizedPath = normalizeRoute(url.pathname);
    const safeParams = new URLSearchParams();
    const allowedParams = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content'];
    for (const key of allowedParams) {
      const val = url.searchParams.get(key);
      if (val) {
        const cleanedVal = sanitizeLabel(val);
        if (cleanedVal) safeParams.set(key, cleanedVal);
      }
    }
    const query = safeParams.toString();
    return normalizedPath + (query ? `?${query}` : '');
  } catch {
    return '/';
  }
}

export function parseAttributionFromUrl(
  search: string,
  referrer: string,
  pathname: string,
  now = Date.now()
): AttributionData {
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(search);
  } catch {
    params = new URLSearchParams();
  }

  const rawSource = params.get('utm_source');
  const rawMedium = params.get('utm_medium');
  const rawCampaign = params.get('utm_campaign');
  const rawTerm = params.get('utm_term');
  const rawContent = params.get('utm_content');

  // Scrub and sanitize labels BEFORE storing in attribution
  const utmSource = sanitizeLabel(rawSource);
  const utmMedium = sanitizeLabel(rawMedium);
  const utmCampaign = sanitizeLabel(rawCampaign);
  const utmTerm = sanitizeLabel(rawTerm);
  const utmContent = sanitizeLabel(rawContent);

  let referrerHost = '';
  if (referrer) {
    try {
      referrerHost = new URL(referrer).hostname;
    } catch {
      referrerHost = '';
    }
  }

  const currentHost = isBrowser() ? window.location.hostname : '';
  const isExternalReferrer = Boolean(referrerHost && (!currentHost || referrerHost !== currentHost));

  let source = '(direct)';
  let medium: string | undefined = undefined;

  if (utmSource) {
    source = utmSource;
    medium = utmMedium || 'cpc';
  } else if (isExternalReferrer) {
    source = sanitizeLabel(referrerHost) || '(referral)';
    medium = 'referral';
  }

  const attribution: AttributionData = {
    source,
    timestamp: now,
    landingPath: sanitizePath(pathname + (search ? (search.startsWith('?') ? search : `?${search}`) : '')),
  };
  if (medium) attribution.medium = medium;
  if (utmCampaign) attribution.campaign = utmCampaign;
  if (utmTerm) attribution.term = utmTerm;
  if (utmContent) attribution.content = utmContent;

  return attribution;
}

// Initialize and record attribution on page load/visit
export function recordAttribution(now = Date.now()): void {
  if (!isBrowser() || isBot()) return;

  try {
    const attribution = parseAttributionFromUrl(
      window.location.search,
      document.referrer,
      window.location.pathname,
      now
    );

    // 1. First Touch: set once and preserve for up to 30 days
    const existingFirstTouchRaw = safeGetStorage(STORAGE_KEYS.FIRST_TOUCH);
    if (!existingFirstTouchRaw) {
      safeSetStorage(STORAGE_KEYS.FIRST_TOUCH, JSON.stringify(attribution));
    } else {
      try {
        const existing = JSON.parse(existingFirstTouchRaw) as AttributionData;
        // If missing timestamp, future timestamp, or expired (> 30 days), refresh
        if (
          !existing ||
          typeof existing.timestamp !== 'number' ||
          existing.timestamp > now ||
          now - existing.timestamp > ATTRIBUTION_WINDOW_MS
        ) {
          safeSetStorage(STORAGE_KEYS.FIRST_TOUCH, JSON.stringify(attribution));
        }
        // Otherwise: preserve first touch!
      } catch {
        safeSetStorage(STORAGE_KEYS.FIRST_TOUCH, JSON.stringify(attribution));
      }
    }

    // 2. Last Non-Direct: record if this visit is non-direct
    if (attribution.source !== '(direct)' && attribution.source !== 'direct') {
      safeSetStorage(STORAGE_KEYS.LAST_NON_DIRECT, JSON.stringify(attribution));
    }
  } catch {
    // Non-blocking best-effort
  }
}

// Retrieve valid last non-direct source within 30 days before registration
export function getLastNonDirectSource(now = Date.now()): AttributionData | null {
  const raw = safeGetStorage(STORAGE_KEYS.LAST_NON_DIRECT);
  if (!raw) return null;
  try {
    const data = JSON.parse(raw) as AttributionData;
    if (
      data &&
      typeof data.timestamp === 'number' &&
      data.timestamp <= now &&
      now - data.timestamp <= ATTRIBUTION_WINDOW_MS
    ) {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

// Retrieve first touch source within 30 days
export function getFirstTouchSource(now = Date.now()): AttributionData | null {
  const raw = safeGetStorage(STORAGE_KEYS.FIRST_TOUCH);
  if (!raw) return null;
  try {
    const data = JSON.parse(raw) as AttributionData;
    if (
      data &&
      typeof data.timestamp === 'number' &&
      data.timestamp <= now &&
      now - data.timestamp <= ATTRIBUTION_WINDOW_MS
    ) {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

// Returns full attribution context for registration
export function getRegistrationAttributionContext(now = Date.now()): RegistrationAttributionContext {
  const browserId = getOrCreateBrowserId();
  const { sessionId } = getOrCreateSessionId(now);
  const firstTouch = getFirstTouchSource(now);
  const lastNonDirect = getLastNonDirectSource(now);

  return {
    browserId,
    sessionId,
    firstTouch,
    lastNonDirect,
  };
}

// Returns the formatted DTO to pass in authApi.register
export function getRegistrationAttributionForApi(now = Date.now()): RegistrationAttributionDto {
  const browserId = getOrCreateBrowserId();
  const lastNonDirect = getLastNonDirectSource(now);
  const firstTouch = getFirstTouchSource(now);
  const active = lastNonDirect || firstTouch;

  const firstTouchDto: AttributionTouchDto | undefined = firstTouch
    ? {
        source: firstTouch.source !== '(direct)' ? firstTouch.source : undefined,
        medium: firstTouch.medium,
        campaign: firstTouch.campaign,
        content: firstTouch.content,
        term: firstTouch.term,
        landingPage: firstTouch.landingPath ? normalizeRoute(firstTouch.landingPath) : undefined,
        occurredAt: new Date(firstTouch.timestamp).toISOString(),
      }
    : undefined;

  const lastTouchDto: AttributionTouchDto | undefined = lastNonDirect
    ? {
        source: lastNonDirect.source !== '(direct)' ? lastNonDirect.source : undefined,
        medium: lastNonDirect.medium,
        campaign: lastNonDirect.campaign,
        content: lastNonDirect.content,
        term: lastNonDirect.term,
        landingPage: lastNonDirect.landingPath ? normalizeRoute(lastNonDirect.landingPath) : undefined,
        occurredAt: new Date(lastNonDirect.timestamp).toISOString(),
      }
    : undefined;

  return {
    browserId,
    firstTouch: firstTouchDto,
    lastTouch: lastTouchDto,
    utmSource: active?.source !== '(direct)' ? active?.source : undefined,
    utmMedium: active?.medium,
    utmCampaign: active?.campaign,
    utmContent: active?.content,
    utmTerm: active?.term,
    landingPage: active?.landingPath ? normalizeRoute(active.landingPath) : undefined,
    referrer: isBrowser() && document.referrer ? normalizeRoute(document.referrer) : undefined,
  };
}

// Backend-aligned client analytics event item
export interface AnalyticsEventItem {
  eventId: string;
  eventType: AllowlistedEventType;
  eventName?: string;
  path: string;
  referrerPath?: string;
  ctaId?: string;
  utmSource?: string;
  utmMedium?: string;
  utmCampaign?: string;
  utmContent?: string;
  utmTerm?: string;
  clientTimestamp?: string;
}

// Backend-aligned batch request
export interface AnalyticsEventBatchRequest {
  browserId: string;
  sessionId: string;
  events: AnalyticsEventItem[];
}

function checkRateLimit(now = Date.now()): boolean {
  if (now - windowStartTime >= 60000) {
    windowStartTime = now;
    eventCountInWindow = 0;
  }
  if (eventCountInWindow >= MAX_EVENTS_PER_MINUTE) {
    return false;
  }
  eventCountInWindow++;
  return true;
}

export interface TrackEventOptions {
  eventName?: string;
  path?: string;
  referrerPath?: string;
  ctaId?: string;
  utmSource?: string;
  utmMedium?: string;
  utmCampaign?: string;
  utmContent?: string;
  utmTerm?: string;
  properties?: Record<string, unknown>;
}

export function trackEvent(
  eventType: AllowlistedEventType,
  options: TrackEventOptions = {}
): boolean {
  try {
    if (!isBrowser() || isBot()) return false;
    if (!ALLOWED_EVENT_TYPES.has(eventType)) return false;

    const now = Date.now();
    if (!checkRateLimit(now)) return false;

    const browserId = getOrCreateBrowserId();
    const { sessionId } = getOrCreateSessionId(now);

    const activeAttribution = getLastNonDirectSource(now) || getFirstTouchSource(now);

    const eventItem: AnalyticsEventItem = {
      eventId: 'evt-' + generateId(),
      eventType,
      eventName: options.eventName ? scrubString(options.eventName) : undefined,
      path: normalizeRoute(options.path || window.location.pathname),
      referrerPath: options.referrerPath
        ? normalizeRoute(options.referrerPath)
        : document.referrer
          ? normalizeRoute(document.referrer)
          : undefined,
      ctaId: sanitizeLabel(options.ctaId),
      utmSource: sanitizeLabel(options.utmSource || activeAttribution?.source !== '(direct)' ? options.utmSource || activeAttribution?.source : undefined),
      utmMedium: sanitizeLabel(options.utmMedium || activeAttribution?.medium),
      utmCampaign: sanitizeLabel(options.utmCampaign || activeAttribution?.campaign),
      utmContent: sanitizeLabel(options.utmContent || activeAttribution?.content),
      utmTerm: sanitizeLabel(options.utmTerm || activeAttribution?.term),
      clientTimestamp: new Date(now).toISOString(),
    };

    const batchRequest: AnalyticsEventBatchRequest = {
      browserId,
      sessionId,
      events: [eventItem],
    };

    const json = JSON.stringify(batchRequest);
    if (json.length > MAX_PAYLOAD_BYTES) {
      return false;
    }

    if (typeof navigator !== 'undefined' && typeof navigator.sendBeacon === 'function') {
      const blob = new Blob([json], { type: 'application/json' });
      const sent = navigator.sendBeacon('/api/analytics/events', blob);
      if (sent) return true;
    }

    if (typeof fetch === 'function') {
      void fetch('/api/analytics/events', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: json,
        keepalive: true,
        credentials: 'same-origin',
      }).catch(() => {
        // Must never break application logic
      });
      return true;
    }
  } catch {
    // Fail silently
  }
  return false;
}

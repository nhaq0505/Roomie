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

export type AllowlistedEventName =
  | 'page_view'
  | 'cta_click'
  | 'session_start'
  | 'registration_start'
  | 'registration_complete'
  | 'otp_request'
  | 'otp_verify'
  | 'checkout_start'
  | 'checkout_complete';

export const ALLOWED_EVENT_NAMES = new Set<string>([
  'page_view',
  'cta_click',
  'session_start',
  'registration_start',
  'registration_complete',
  'otp_request',
  'otp_verify',
  'checkout_start',
  'checkout_complete',
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
const MAX_EVENTS_PER_MINUTE = 30;
const MAX_KEY_LENGTH = 32;
const MAX_VAL_LENGTH = 128;
const MAX_PAYLOAD_BYTES = 2048;

// In-memory rate limiting state
let eventCountInWindow = 0;
let windowStartTime = 0;

function isBrowser(): boolean {
  return typeof window !== 'undefined' && typeof document !== 'undefined';
}

function safeGetStorage(key: string): string | null {
  if (!isBrowser()) return null;
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeSetStorage(key: string, value: string): void {
  if (!isBrowser()) return;
  try {
    localStorage.setItem(key, value);
  } catch {
    // Gracefully handle storage quota or private-browsing restrictions
  }
}

function generateId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    try {
      return crypto.randomUUID();
    } catch {
      // Fallback
    }
  }
  const bytes = new Uint8Array(16);
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256);
  }
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
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

  const isExpired = !currentSessionId || isNaN(lastActive) || (now - lastActive > SESSION_IDLE_TIMEOUT_MS);

  if (isExpired) {
    const newSessionId = generateId();
    safeSetStorage(STORAGE_KEYS.SESSION_ID, newSessionId);
    safeSetStorage(STORAGE_KEYS.LAST_ACTIVE, String(now));
    return { sessionId: newSessionId, isNew: true };
  }

  safeSetStorage(STORAGE_KEYS.LAST_ACTIVE, String(now));
  return { sessionId: currentSessionId, isNew: false };
}

// Strip query parameters except safe analytics keys to guarantee no PII/tokens in URLs
export function sanitizePath(rawUrlOrPath: string): string {
  try {
    const url = new URL(rawUrlOrPath, 'https://roomie.local');
    const safeParams = new URLSearchParams();
    const allowedParams = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_term', 'utm_content', 'lang'];
    for (const key of allowedParams) {
      const val = url.searchParams.get(key);
      if (val) safeParams.set(key, val.slice(0, MAX_VAL_LENGTH));
    }
    const query = safeParams.toString();
    return url.pathname + (query ? `?${query}` : '');
  } catch {
    return '/';
  }
}

// PII scrub patterns
const EMAIL_REGEX = /[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}/g;
const PHONE_REGEX = /(?:\+?84|0)[3|5|7|8|9]\d{8}/g;
const OTP_REGEX = /\b\d{4,6}\b/g;
const TOKEN_REGEX = /\b(ey[A-Za-z0-9-_=]+\.[A-Za-z0-9-_=]+\.?[A-Za-z0-9-_.+/=]*|[0-9a-f]{32,64})\b/gi;

export function scrubPii(value: unknown): unknown {
  if (typeof value === 'string') {
    let sanitized = value
      .replace(EMAIL_REGEX, '[REDACTED_EMAIL]')
      .replace(PHONE_REGEX, '[REDACTED_PHONE]')
      .replace(TOKEN_REGEX, '[REDACTED_TOKEN]');
    if (/otp|code|pin|password|secret|bearer/i.test(value)) {
      sanitized = sanitized.replace(OTP_REGEX, '[REDACTED_OTP]');
    }
    return sanitized.slice(0, MAX_VAL_LENGTH);
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return value;
  }
  if (Array.isArray(value)) {
    return value.slice(0, 10).map(scrubPii);
  }
  if (value && typeof value === 'object') {
    const cleaned: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value)) {
      if (typeof k === 'string' && k.length <= MAX_KEY_LENGTH) {
        if (/password|token|secret|otp|cookie|auth|card|bank|phone|email/i.test(k)) {
          cleaned[k] = '[REDACTED]';
        } else {
          cleaned[k] = scrubPii(v);
        }
      }
    }
    return cleaned;
  }
  return null;
}

export function parseAttributionFromUrl(
  search: string,
  referrer: string,
  pathname: string,
  now = Date.now()
): AttributionData {
  const params = new URLSearchParams(search);
  const utmSource = params.get('utm_source')?.trim();
  const utmMedium = params.get('utm_medium')?.trim();
  const utmCampaign = params.get('utm_campaign')?.trim();
  const utmTerm = params.get('utm_term')?.trim();
  const utmContent = params.get('utm_content')?.trim();

  let referrerHost = '';
  if (referrer) {
    try {
      referrerHost = new URL(referrer).hostname;
    } catch {
      referrerHost = '';
    }
  }

  // Check if referrer is external
  const currentHost = isBrowser() ? window.location.hostname : '';
  const isExternalReferrer = Boolean(referrerHost && (!currentHost || referrerHost !== currentHost));

  let source = '(direct)';
  let medium: string | undefined = undefined;

  if (utmSource) {
    source = utmSource.slice(0, MAX_VAL_LENGTH);
    medium = utmMedium ? utmMedium.slice(0, MAX_VAL_LENGTH) : 'cpc';
  } else if (isExternalReferrer) {
    source = referrerHost.slice(0, MAX_VAL_LENGTH);
    medium = 'referral';
  }

  const attribution: AttributionData = {
    source,
    timestamp: now,
    landingPath: sanitizePath(pathname + (search ? (search.startsWith('?') ? search : `?${search}`) : '')),
  };
  if (medium) attribution.medium = medium;
  if (utmCampaign) attribution.campaign = utmCampaign.slice(0, MAX_VAL_LENGTH);
  if (utmTerm) attribution.term = utmTerm.slice(0, MAX_VAL_LENGTH);
  if (utmContent) attribution.content = utmContent.slice(0, MAX_VAL_LENGTH);

  return attribution;
}

// Initialize and record attribution on page load/visit
export function recordAttribution(now = Date.now()): void {
  if (!isBrowser()) return;

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
      // If expired (> 30 days), refresh
      if (now - existing.timestamp > ATTRIBUTION_WINDOW_MS) {
        safeSetStorage(STORAGE_KEYS.FIRST_TOUCH, JSON.stringify(attribution));
      }
    } catch {
      safeSetStorage(STORAGE_KEYS.FIRST_TOUCH, JSON.stringify(attribution));
    }
  }

  // 2. Last Non-Direct: record if this visit is non-direct
  if (attribution.source !== '(direct)' && attribution.source !== 'direct') {
    safeSetStorage(STORAGE_KEYS.LAST_NON_DIRECT, JSON.stringify(attribution));
  }
}

// Retrieve valid last non-direct source within 30 days before registration
export function getLastNonDirectSource(now = Date.now()): AttributionData | null {
  const raw = safeGetStorage(STORAGE_KEYS.LAST_NON_DIRECT);
  if (!raw) return null;
  try {
    const data = JSON.parse(raw) as AttributionData;
    if (now - data.timestamp <= ATTRIBUTION_WINDOW_MS) {
      return data;
    }
    return null; // Expired beyond 30 days
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
    if (now - data.timestamp <= ATTRIBUTION_WINDOW_MS) {
      return data;
    }
    return null;
  } catch {
    return null;
  }
}

// Returns full attribution context to pass along during registration/OTP verification
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

// Event Tracking Primitive
export interface EventPayload {
  eventName: AllowlistedEventName;
  properties?: Record<string, unknown>;
  browserId?: string;
  sessionId?: string;
  timestamp?: number;
  url?: string;
}

function checkRateLimit(now = Date.now()): boolean {
  if (now - windowStartTime >= 60000) {
    windowStartTime = now;
    eventCountInWindow = 0;
  }
  if (eventCountInWindow >= MAX_EVENTS_PER_MINUTE) {
    return false; // Throttled
  }
  eventCountInWindow++;
  return true;
}

export function trackEvent(
  eventName: AllowlistedEventName,
  properties: Record<string, unknown> = {}
): boolean {
  if (!isBrowser()) return false;
  if (!ALLOWED_EVENT_NAMES.has(eventName)) return false;

  const now = Date.now();
  if (!checkRateLimit(now)) return false;

  const browserId = getOrCreateBrowserId();
  const { sessionId } = getOrCreateSessionId(now);

  const cleanProps = scrubPii(properties) as Record<string, unknown>;
  const payload: EventPayload = {
    eventName,
    properties: cleanProps,
    browserId,
    sessionId,
    timestamp: now,
    url: sanitizePath(window.location.pathname + window.location.search),
  };

  const json = JSON.stringify(payload);
  if (json.length > MAX_PAYLOAD_BYTES) {
    return false; // Bounded payload protection
  }

  // Non-blocking best-effort transmission via sendBeacon or fetch keepalive
  try {
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
        // Analytics must never break or interrupt caller application logic
      });
      return true;
    }
  } catch {
    // Fail silently
  }
  return false;
}

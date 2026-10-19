import { errorText } from '../i18n/errors';
import { getLocale, t } from '../i18n/runtime';

export class ApiError extends Error {
  status: number;
  details?: unknown;

  constructor(message: string, status: number, details?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
    Object.defineProperty(this, 'message', { configurable: true, get: () => errorText(message, status) });
  }
}

function unwrap<T>(value: unknown): T {
  if (value && typeof value === 'object' && 'data' in value && (value as { data?: unknown }).data !== undefined) {
    return (value as { data: T }).data;
  }
  return value as T;
}

export function listFrom<T>(value: unknown): T[] {
  const result = unwrap<unknown>(value);
  if (Array.isArray(result)) return result as T[];
  if (result && typeof result === 'object') {
    const object = result as { items?: unknown; results?: unknown; records?: unknown };
    if (Array.isArray(object.items)) return object.items as T[];
    if (Array.isArray(object.results)) return object.results as T[];
    if (Array.isArray(object.records)) return object.records as T[];
  }
  return [];
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  headers.set('Accept', 'application/json');
  headers.set('Accept-Language', getLocale());
  let response: Response;
  try {
    response = await fetch(path, { ...init, signal: init.signal ?? AbortSignal.timeout(35000), headers, credentials: 'same-origin' });
  } catch {
    throw new ApiError(t("Không thể kết nối máy chủ. Vui lòng kiểm tra kết nối và thử lại."), 0);
  }
  const text = await response.text();
  let payload: unknown;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = text;
    }
  }
  if (!response.ok) {
    const message = payload && typeof payload === 'object' && 'message' in payload
      ? String((payload as { message: unknown }).message)
      : typeof payload === 'string' && payload
        ? payload
        : t("Yêu cầu chưa thành công ({0}).", [response.status]);
    throw new ApiError(message, response.status, payload);
  }
  return unwrap<T>(payload);
}

export async function requestBlob(path: string): Promise<Blob> {
  const headers = new Headers({ Accept: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  headers.set('Accept-Language', getLocale());
  let response: Response;
  try { response = await fetch(path, { headers, signal: AbortSignal.timeout(90000), credentials: 'same-origin' }); }
  catch { throw new ApiError(t("Không thể kết nối máy chủ. Vui lòng kiểm tra kết nối và thử lại."), 0); }
  if (!response.ok) {
    const text = await response.text();
    let payload: unknown = text;
    try { payload = text ? JSON.parse(text) : null; } catch { /* Keep plain-text API errors. */ }
    const message = payload && typeof payload === 'object' && 'message' in payload
      ? String((payload as { message: unknown }).message)
      : typeof payload === 'string' && payload ? payload : t("Yêu cầu chưa thành công ({0}).", [response.status]);
    throw new ApiError(message, response.status, payload);
  }
  return response.blob();
}

export const json = (body: unknown): string => JSON.stringify(body);
export const post = (body?: unknown): RequestInit => ({ method: 'POST', ...(body === undefined ? {} : { body: json(body) }) });

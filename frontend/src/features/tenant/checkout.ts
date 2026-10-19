import { getLocale } from '../../shared/i18n/runtime';
import { type Invoice } from '../invoices/index';
import { type TenantCheckout } from '../payments/index';

export type CheckoutStatus = 'pending' | 'paid' | 'expired' | 'cancelled' | 'failed';
export type CheckoutSession = TenantCheckout & { invoiceId: string; generation: number; status: CheckoutStatus; verificationPending: boolean };

export function checkoutStatus(value: string): CheckoutStatus {
  const status = value.trim().toLowerCase();
  if (status === 'paid') return 'paid';
  if (status === 'expired') return 'expired';
  if (status === 'cancelled' || status === 'canceled') return 'cancelled';
  if (status === 'failed') return 'failed';
  return 'pending';
}
export function invoiceIsPaid(invoice?: Invoice) {
  return !!invoice && (invoice.status.toLowerCase() === 'paid' || invoice.outstanding <= 0);
}
export function httpStatus(error: unknown) {
  return error && typeof error === 'object' && 'status' in error ? Number((error as { status?: number }).status) : 0;
}
export function isAccessError(error: unknown) {
  return [401, 403, 404].includes(httpStatus(error));
}
export function isHostedPayOsUrl(value: string) {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname.toLowerCase() === 'pay.payos.vn' && !url.username && !url.password && (!url.port || url.port === '443');
  } catch {
    return false;
  }
}
export function checkoutExpiration(value: number) {
  return new Intl.DateTimeFormat(getLocale(), { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(value * 1000));
}

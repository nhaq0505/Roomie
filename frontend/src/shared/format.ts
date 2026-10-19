import { getLocale } from './i18n/runtime';

export const formatVnd = (amount?: number) => new Intl.NumberFormat(getLocale(), { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(amount ?? 0);
export const currentPeriod = () => {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit' }).formatToParts(new Date());
  const year = parts.find((part) => part.type === 'year')?.value ?? '';
  const month = parts.find((part) => part.type === 'month')?.value ?? '';
  return `${year}-${month}`;
};
export const periodLabel = (period: string) => {
  const [year, month] = period.split('-');
  if (!month || !year || !/^\d{4}-\d{2}$/.test(period) || Number(month) < 1 || Number(month) > 12) return period;
  return new Intl.DateTimeFormat(getLocale(), { year: 'numeric', month: 'long', timeZone: 'UTC' }).format(new Date(Date.UTC(Number(year), Number(month) - 1, 1)));
};

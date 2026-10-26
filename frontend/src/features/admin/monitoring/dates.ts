import dayjs from 'dayjs';

export function getInitialDates(now: Date = new Date()): [string, string] {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(now);

  const year = parts.find((p) => p.type === 'year')?.value ?? '2026';
  const month = parts.find((p) => p.type === 'month')?.value ?? '10';
  const day = parts.find((p) => p.type === 'day')?.value ?? '20';
  const todayStr = `${year}-${month}-${day}`;

  // Default: 30 days in Vietnam calendar
  const fromStr = dayjs(todayStr).subtract(29, 'day').format('YYYY-MM-DD');
  return [fromStr, todayStr];
}

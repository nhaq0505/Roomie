import en from './en.json';
import zh from './zh.json';
import ko from './ko.json';
import hi from './hi.json';

export const languages = [
  { value: 'vi', label: 'Tiếng Việt', locale: 'vi-VN' },
  { value: 'en', label: 'English', locale: 'en-US' },
  { value: 'zh', label: '简体中文', locale: 'zh-CN' },
  { value: 'ko', label: '한국어', locale: 'ko-KR' },
  { value: 'hi', label: 'हिन्दी', locale: 'hi-IN' },
] as const;
export type Language = typeof languages[number]['value'];
const catalogs: Record<string, Record<string, string>> = { en, zh, ko, hi };
const key = 'rehouse.language';
const listeners = new Set<() => void>();
export const isLanguage = (value: unknown): value is Language => languages.some(lang => lang.value === value);
function initialLanguage(): Language {
  if (typeof window === 'undefined') return 'vi';
  const requested = new URLSearchParams(window.location.search).get('lang');
  if (isLanguage(requested)) {
    try { localStorage.setItem(key, requested); } catch { /* URL choice works without storage. */ }
    return requested;
  }
  try { const saved = localStorage.getItem(key); if (isLanguage(saved)) return saved; } catch { /* Private browsers may block storage. */ }
  return 'vi';
}
let language = initialLanguage();
export const getLanguage = () => language;
export const getLocale = () => languages.find(item => item.value === language)!.locale;
export function setLanguage(value: Language) {
  if (!isLanguage(value)) return;
  language = value;
  try { localStorage.setItem(key, value); } catch { /* Session choice still works. */ }
  if (typeof window !== 'undefined') {
    const url = new URL(window.location.href);
    if (url.searchParams.has('lang')) { url.searchParams.set('lang', value); window.history.replaceState(window.history.state, '', url); }
    document.documentElement.lang = getLocale();
  }
  listeners.forEach(listener => listener());
}
export function subscribeLanguage(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
// Source-language messages are stable catalog keys; interpolation never interprets HTML.
export function t(message: string, values: readonly unknown[] = []): string {
  const translated = language === 'vi' ? message : catalogs[language]?.[message] ?? message;
  return translated.replace(/\{(\d+)\}/g, (match, index) => values[Number(index)] === undefined ? match : String(values[Number(index)]));
}
// For cached UI notices only; never pass tenant names or invoice descriptions here.
const messageSources = new Map<string, string>();
for (const catalog of Object.values(catalogs)) {
  for (const [source, translated] of Object.entries(catalog)) {
    messageSources.set(source, source);
    if (!messageSources.has(translated)) messageSources.set(translated, source);
  }
}
export const hasMessage = (message: string) => messageSources.has(message);
export const translateMessage = (message: string) => t(messageSources.get(message) ?? message);
export const dateFormat = () => language === 'en' ? 'MM/DD/YYYY' : language === 'ko' ? 'YYYY.MM.DD' : language === 'zh' ? 'YYYY/MM/DD' : 'DD/MM/YYYY';
export function formatDate(value?: string) {
  if (!value) return '—';
  // Date-only business values remain calendar dates, independent of browser timezone.
  const dateOnly = /^\d{4}-\d{2}-\d{2}$/.test(value);
  const date = new Date(dateOnly ? `${value}T00:00:00Z` : value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(getLocale(), { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: dateOnly ? 'UTC' : 'Asia/Ho_Chi_Minh' }).format(date);
}

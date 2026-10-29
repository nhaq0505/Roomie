import { useSyncExternalStore, type ReactNode } from 'react';
import { Select } from 'antd';
import { getLanguage, languages, setLanguage, subscribeLanguage, type Language } from './runtime';

function FlagIcon({ code }: { code: Language }) {
  const style = {
    width: 18,
    height: 13,
    borderRadius: 2,
    flexShrink: 0,
    boxShadow: '0 0 0 1px rgba(19, 48, 55, 0.12)',
    display: ' block',
  } as const;

  if (code === 'vi') {
    return (
      <svg viewBox="0 0 30 20" style={style} aria-hidden="true">
        <rect width="30" height="20" fill="#da251d" />
        <polygon
          points="15,4 16.47,8.52 21.22,8.52 17.38,11.31 18.85,15.83 15,13.04 11.15,15.83 12.62,11.31 8.78,8.52 13.53,8.52"
          fill="#ffff00"
        />
      </svg>
    );
  }
  if (code === 'en') {
    return (
      <svg viewBox="0 0 30 20" style={style} aria-hidden="true">
        <rect width="30" height="20" fill="#ffffff" />
        <path d="M0,0H30V2.86H0ZM0,5.71H30V8.57H0ZM0,11.43H30V14.29H0ZM0,17.14H30V20H0Z" fill="#b31942" />
        <rect width="13" height="11.43" fill="#0a3161" />
        <g fill="#ffffff">
          <circle cx="2.6" cy="2.3" r="0.9" />
          <circle cx="6.5" cy="2.3" r="0.9" />
          <circle cx="10.4" cy="2.3" r="0.9" />
          <circle cx="4.5" cy="5.7" r="0.9" />
          <circle cx="8.5" cy="5.7" r="0.9" />
          <circle cx="2.6" cy="9.1" r="0.9" />
          <circle cx="6.5" cy="9.1" r="0.9" />
          <circle cx="10.4" cy="9.1" r="0.9" />
        </g>
      </svg>
    );
  }
  if (code === 'zh') {
    return (
      <svg viewBox="0 0 30 20" style={style} aria-hidden="true">
        <rect width="30" height="20" fill="#ee1c25" />
        <polygon
          points="5,2.2 5.7,4.4 8,4.4 6.1,5.7 6.8,7.9 5,6.6 3.2,7.9 3.9,5.7 2,4.4 4.3,4.4"
          fill="#ffff00"
        />
        <g fill="#ffff00">
          <circle cx="10" cy="2" r="0.75" />
          <circle cx="11.6" cy="3.8" r="0.75" />
          <circle cx="11.6" cy="6.4" r="0.75" />
          <circle cx="10" cy="8.2" r="0.75" />
        </g>
      </svg>
    );
  }
  if (code === 'ko') {
    return (
      <svg viewBox="0 0 30 20" style={style} aria-hidden="true">
        <rect width="30" height="20" fill="#ffffff" />
        <circle cx="15" cy="10" r="4.8" fill="#cd2e3a" />
        <path d="M10.2,10 A4.8,4.8 0 0,0 19.8,10 A2.4,2.4 0 0,1 15,10 A2.4,2.4 0 0,0 10.2,10 Z" fill="#0047a0" />
        <g stroke="#111" strokeWidth="1" strokeLinecap="round">
          <line x1="6.5" y1="4.5" x2="8.8" y2="3" />
          <line x1="7.3" y1="5.7" x2="9.6" y2="4.2" />
          <line x1="21.2" y1="3" x2="23.5" y2="4.5" />
          <line x1="20.4" y1="4.2" x2="22.7" y2="5.7" />
          <line x1="6.5" y1="15.5" x2="8.8" y2="17" />
          <line x1="21.2" y1="17" x2="23.5" y2="15.5" />
        </g>
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 30 20" style={style} aria-hidden="true">
      <rect width="30" height="6.67" fill="#ff9933" />
      <rect y="6.67" width="30" height="6.67" fill="#ffffff" />
      <rect y="13.33" width="30" height="6.67" fill="#138808" />
      <circle cx="15" cy="10" r="2.4" fill="none" stroke="#000080" strokeWidth="0.8" />
      <circle cx="15" cy="10" r="0.7" fill="#000080" />
    </svg>
  );
}

export const useLanguage = () => useSyncExternalStore(subscribeLanguage, getLanguage, () => 'vi' as const);

export default function LanguageSelector() {
  const language = useLanguage();
  const labels = { vi: 'Ngôn ngữ', en: 'Language', zh: '语言', ko: '언어', hi: 'भाषा' };
  const options: Array<{ value: Language; label: ReactNode }> = languages.map((item) => ({
    value: item.value,
    label: (
      <span className="language-option">
        <FlagIcon code={item.value} />
        <span>{item.label}</span>
      </span>
    ),
  }));
  return (
    <Select
      className="language-selector"
      aria-label={labels[language]}
      value={language}
      onChange={setLanguage}
      options={options}
      popupMatchSelectWidth={168}
    />
  );
}

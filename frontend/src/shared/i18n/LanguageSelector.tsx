import { useSyncExternalStore } from 'react';
import { Select } from 'antd';
import { GlobalOutlined } from '@ant-design/icons';
import { getLanguage, languages, setLanguage, subscribeLanguage } from './runtime';

export const useLanguage = () => useSyncExternalStore(subscribeLanguage, getLanguage, () => 'vi' as const);
export default function LanguageSelector() {
  const language = useLanguage();
  const labels = { vi: 'Ngôn ngữ', en: 'Language', zh: '语言', ko: '언어', hi: 'भाषा' };
  return <Select className="language-selector" aria-label={labels[language]} value={language} onChange={setLanguage} options={[...languages]} suffixIcon={<GlobalOutlined />} popupMatchSelectWidth={160} />;
}

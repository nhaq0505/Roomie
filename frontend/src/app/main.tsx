import React from 'react';
import ReactDOM from 'react-dom/client';
import { App as AntApp, ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import enUS from 'antd/locale/en_US';
import zhCN from 'antd/locale/zh_CN';
import koKR from 'antd/locale/ko_KR';
import hiIN from 'antd/locale/hi_IN';
import dayjs from 'dayjs';
import 'dayjs/locale/vi';
import 'dayjs/locale/zh-cn';
import 'dayjs/locale/ko';
import 'dayjs/locale/hi';
import { useLanguage } from '../shared/i18n/LanguageSelector';
import { getLocale } from '../shared/i18n/runtime';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { AuthProvider } from '../features/auth/session';
import App from './App';
import './styles.css';
import { appTheme } from '../shared/design/antdTheme';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 20_000 },
    mutations: { retry: 0 },
  },
});

function LocalizedRoot() {
  const language = useLanguage();
  const locales = { vi: viVN, en: enUS, zh: zhCN, ko: koKR, hi: hiIN };
  dayjs.locale(language === 'zh' ? 'zh-cn' : language);
  document.documentElement.lang = getLocale();
  return (
    <ConfigProvider
      locale={locales[language]}
      theme={appTheme}
    >
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <AntApp><AuthProvider><App /></AuthProvider></AntApp>
        </BrowserRouter>
      </QueryClientProvider>
    </ConfigProvider>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><LocalizedRoot /></React.StrictMode>);

import { renderToString } from 'react-dom/server';
import { StaticRouter } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import { createCache, extractStyle, StyleProvider } from '@ant-design/cssinjs';
import { appTheme } from '../../shared/design/antdTheme';
import { LandingContent } from '../../features/marketing/LandingPage';
export { publicPages, site, structuredData } from './site';

export function renderPublicPage(path: string) {
  if (path !== '/') throw new Error(`No public renderer registered for ${path}`);
  const cache = createCache();
  const html = renderToString(<StaticRouter location={path}><StyleProvider cache={cache}><ConfigProvider locale={viVN} theme={appTheme}>
    <LandingContent />
  </ConfigProvider></StyleProvider></StaticRouter>);
  return { html, styles: extractStyle(cache) };
}

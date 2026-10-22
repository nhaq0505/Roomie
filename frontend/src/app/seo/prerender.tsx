import { renderToString } from 'react-dom/server';
import { StaticRouter } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import { createCache, extractStyle, StyleProvider } from '@ant-design/cssinjs';
import { appTheme } from '../../shared/design/antdTheme';
import { LandingContent, type LandingPageKind } from '../../features/marketing/LandingPage';
export { publicPages, site, structuredData } from './site';

const pageByPath: Record<string, LandingPageKind> = {
  '/': 'home',
  '/features': 'features',
  '/pricing': 'pricing',
  '/huong-dan': 'guides',
};

export function renderPublicPage(path: string) {
  const kind = pageByPath[path];
  if (!kind) throw new Error(`No public renderer registered for ${path}`);
  const cache = createCache();
  const html = renderToString(<StaticRouter location={path}><StyleProvider cache={cache}><ConfigProvider locale={viVN} theme={appTheme}>
    <LandingContent page={kind} />
  </ConfigProvider></StyleProvider></StaticRouter>);
  return { html, styles: extractStyle(cache) };
}

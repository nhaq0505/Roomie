import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useLanguage } from '../../shared/i18n/LanguageSelector';
import { metadataFor, site, structuredData } from './site';

function meta(key: string, value: string, property = false) {
  const attribute = property ? 'property' : 'name';
  let element = document.head.querySelector<HTMLMetaElement>(`meta[${attribute}="${key}"]`);
  if (!element) {
    element = document.createElement('meta');
    element.setAttribute(attribute, key);
    document.head.appendChild(element);
  }
  element.content = value;
}

export function SeoMetadata() {
  const { pathname, search } = useLocation();
  const language = useLanguage();
  useEffect(() => {
    const page = metadataFor(pathname);
    const requestedLanguage = new URLSearchParams(search).get('lang');
    const indexable = page.public && language === 'vi' && (!requestedLanguage || requestedLanguage === 'vi')
      && window.location.hostname === new URL(site.origin).hostname;
    document.title = page.title;
    meta('description', page.description);
    meta('robots', indexable ? 'index, follow' : 'noindex, follow');
    meta('og:title', page.title, true);
    meta('og:description', page.description, true);
    meta('og:type', 'website', true);
    meta('og:site_name', 'Roomie', true);
    meta('og:image', site.image, true);
    meta('og:image:width', '1200', true);
    meta('og:image:height', '630', true);
    meta('og:image:alt', 'Roomie — Quản lý phòng trọ', true);
    meta('og:locale', 'vi_VN', true);
    meta('twitter:card', 'summary_large_image');
    meta('twitter:title', page.title);
    meta('twitter:description', page.description);
    meta('twitter:image', site.image);

    let canonical = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (page.canonical) {
      if (!canonical) {
        canonical = document.createElement('link');
        canonical.rel = 'canonical';
        document.head.appendChild(canonical);
      }
      canonical.href = page.canonical;
      meta('og:url', page.canonical, true);
    } else {
      canonical?.remove();
      // Never expose a guest token or account URL through social metadata.
      document.head.querySelector('meta[property="og:url"]')?.remove();
    }

    let json = document.getElementById('site-structured-data');
    if (page.public) {
      if (!json) {
        json = document.createElement('script');
        json.id = 'site-structured-data';
        json.setAttribute('type', 'application/ld+json');
        document.head.appendChild(json);
      }
      json.textContent = JSON.stringify(structuredData);
    } else json?.remove();
  }, [pathname, search, language]);
  return null;
}

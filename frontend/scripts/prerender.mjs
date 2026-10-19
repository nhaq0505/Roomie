import { readFile, writeFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import react from '@vitejs/plugin-react';

const root = fileURLToPath(new URL('../', import.meta.url));
const dist = new URL('../dist/', import.meta.url);
const escape = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
let template = await readFile(new URL('index.html', dist), 'utf8');
const manifest = JSON.parse(await readFile(new URL('.vite/manifest.json', dist), 'utf8'));
const publicCss = manifest['src/features/marketing/LandingPage.tsx']?.css ?? [];
if (!manifest['index.html']?.css?.length) throw new Error('Entry CSS missing from build manifest');
// The private SPA must never inherit the public page's content/canonical.
await writeFile(new URL('app.html', dist), template.replace('</head>', '    <meta name="robots" content="noindex, follow" />\n  </head>'));

// Bundle the build-only renderer so Node never needs browser ESM package shims.
await build({
  root, configFile: false, plugins: [react()], logLevel: 'warn',
  build: { ssr: 'src/app/seo/prerender.tsx', outDir: 'dist/.prerender', emptyOutDir: true },
  ssr: { noExternal: true, resolve: { conditions: ['module', 'module-sync', 'node'] } },
});
try {
  const { renderPublicPage, publicPages, site, structuredData } = await import(new URL('.prerender/prerender.js', dist));
  template = template.replace('</head>', `${publicCss.map(file => `<link rel="stylesheet" href="/${file}" />`).join('\n')}\n</head>`);
  for (const page of publicPages) {
    // Explicit mapping prevents accidentally replacing app.html with a public page.
    if (page.path !== '/') throw new Error('Register output paths before adding public pages');
    const canonical = `${site.origin}${page.path}`;
    const { html: content, styles } = renderPublicPage(page.path);
    const head = [
      ['name', 'robots', 'index, follow'],
      ['property', 'og:type', 'website'], ['property', 'og:site_name', 'Roomie'],
      ['property', 'og:title', page.title], ['property', 'og:description', page.description],
      ['property', 'og:url', canonical], ['property', 'og:locale', 'vi_VN'],
      ['property', 'og:image', site.image], ['property', 'og:image:width', '1200'],
      ['property', 'og:image:height', '630'], ['property', 'og:image:alt', 'Roomie — Quản lý phòng trọ'],
      ['name', 'twitter:card', 'summary_large_image'], ['name', 'twitter:title', page.title],
      ['name', 'twitter:description', page.description], ['name', 'twitter:image', site.image],
    ].map(([attribute, key, value]) => `<meta ${attribute}="${key}" content="${escape(value)}" />`).join('\n    ');
    const html = template
      .replace(/<title>.*?<\/title>/, `<title>${escape(page.title)}</title>`)
      .replace(/<meta name="description" content="[^"]*"\s*\/>/, `<meta name="description" content="${escape(page.description)}" />`)
      .replace('</head>', `    <link rel="canonical" href="${canonical}" />\n    ${head}\n    <script id="site-structured-data" type="application/ld+json">${JSON.stringify(structuredData).replaceAll('<', '\\u003c')}</script>\n${styles}\n  </head>`)
      .replace('<div id="root"></div>', `<div id="root">${content}</div>`);
    if (!html.includes('<h1>') || !html.includes('id="features"')) throw new Error('Public HTML is missing content');
    await writeFile(new URL('index.html', dist), html);
  }
  // Vite copies public/robots.txt unchanged. Do not override the user's crawl block.
  const urls = publicPages.map(page => `  <url><loc>${site.origin}${page.path}</loc></url>`).join('\n');
  await writeFile(new URL('sitemap.xml', dist), `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls}\n</urlset>\n`);
  console.log(`Prerendered ${publicPages.length} public page(s); app shell and sitemap.xml generated; public/robots.txt preserved.`);
} finally {
  await rm(new URL('.prerender/', dist), { recursive: true, force: true });
}

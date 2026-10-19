// The canonical origin is intentionally fixed, never derived from Host/user input.
export const site = {
  origin: 'https://roomie.io.vn',
  title: 'Roomie — Phần mềm quản lý phòng trọ',
  description: 'Roomie giúp chủ trọ quản lý phòng và người thuê, ghi chỉ số điện, lập hóa đơn, theo dõi công nợ và nhận thanh toán qua payOS.',
  image: 'https://roomie.io.vn/roomie-social.png',
};

// Only published public pages belong here; weeks 3–6 will extend this registry.
export const publicPages = [{ path: '/', title: site.title, description: site.description }];

export const privatePageTitles: Record<string, string> = {
  '/login': 'Đăng nhập', '/register': 'Đăng ký làm chủ trọ',
  '/reset-password': 'Đặt lại mật khẩu', '/change-password': 'Đổi mật khẩu',
  '/account-status': 'Trạng thái tài khoản', '/dashboard': 'Tổng quan',
  '/buildings': 'Dãy trọ', '/rooms': 'Phòng & người thuê', '/readings': 'Chỉ số điện',
  '/invoices': 'Hóa đơn', '/reports': 'Báo cáo',
  '/settings/payment': 'Cấu hình thanh toán', '/admin/owners': 'Quản lý chủ trọ',
};

export function metadataFor(pathname: string) {
  const published = publicPages.find(page => page.path === pathname);
  if (published) return { ...published, canonical: `${site.origin}${published.path}`, public: true };
  const guest = /^\/(t|tenant)\/[^/]+(?:\/invoices\/[^/]+\/checkout)?\/?$/.test(pathname);
  const title = guest ? (pathname.includes('/checkout') ? 'Thanh toán hóa đơn' : 'Thông tin phòng thuê')
    : privatePageTitles[pathname.replace(/\/$/, '')] ?? 'Không tìm thấy trang';
  return { title: `${title} | Roomie`, description: 'Khu vực tài khoản và quản lý phòng trọ Roomie.', canonical: null, public: false };
}

export const structuredData = {
  '@context': 'https://schema.org',
  '@graph': [
    { '@type': 'Organization', '@id': `${site.origin}/#organization`, name: 'Roomie', url: `${site.origin}/`, logo: `${site.origin}/roomie-logo.svg` },
    { '@type': 'WebSite', '@id': `${site.origin}/#website`, name: 'Roomie', url: `${site.origin}/`, inLanguage: 'vi-VN', publisher: { '@id': `${site.origin}/#organization` } },
  ],
};

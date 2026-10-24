import { ArrowRightOutlined, GiftOutlined } from '@ant-design/icons';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { post, request } from '../http/client';
import { t } from '../i18n/runtime';

type ActiveBannerDto = {
  id: string;
  title: string;
  message: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  promoCode?: string | null;
  placement: string;
  targetSegment: string;
  themeColor: string;
};

export function ActiveBannerStrip({
  placement,
}: {
  placement: 'LandingTop' | 'OwnerDashboard';
}) {
  const navigate = useNavigate();
  const [banners, setBanners] = useState<ActiveBannerDto[]>([]);

  useEffect(() => {
    let active = true;
    request<ActiveBannerDto[]>(`/api/banners/active?placement=${encodeURIComponent(placement)}`)
      .then((res) => {
        if (active && Array.isArray(res)) {
          setBanners(res);
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [placement]);

  if (banners.length === 0) return null;

  const banner = banners[0];
  const themeClass =
    banner.themeColor === 'gold'
      ? 'theme-gold'
      : banner.themeColor === 'blue'
      ? 'theme-blue'
      : banner.themeColor === 'coral'
      ? 'theme-coral'
      : 'theme-teal';

  const handleClick = () => {
    void request(`/api/banners/${encodeURIComponent(banner.id)}/click`, post({})).catch(() => {});
    const target = banner.ctaUrl || (banner.promoCode ? `/register?promo=${encodeURIComponent(banner.promoCode)}` : '/register');
    if (target.startsWith('http://') || target.startsWith('https://')) {
      window.location.href = target;
    } else {
      navigate(target);
    }
  };

  const content = (
    <div
      className={`active-marketing-banner ${themeClass}`}
      style={placement === 'LandingTop' ? { marginBottom: 0 } : undefined}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap', flex: 1 }}>
        <GiftOutlined style={{ fontSize: 20 }} />
        <div>
          <div style={{ fontWeight: 700, fontSize: 15 }}>
            {banner.title}
            {banner.promoCode && (
              <span
                style={{
                  marginLeft: 8,
                  padding: '2px 8px',
                  borderRadius: 6,
                  background: '#fef3c7',
                  color: '#92400e',
                  fontSize: 12,
                  fontWeight: 700,
                }}
              >
                {banner.promoCode}
              </span>
            )}
          </div>
          <div style={{ fontSize: 13, opacity: 0.92 }}>{banner.message}</div>
        </div>
      </div>
      {(banner.ctaText || banner.ctaUrl || banner.promoCode) && (
        <button
          type="button"
          className="banner-cta-btn"
          onClick={handleClick}
          style={{
            fontWeight: 600,
            borderRadius: 8,
            minHeight: 38,
            padding: '8px 14px',
            background: '#ffffff',
            color: '#0f292d',
            border: 'none',
            cursor: 'pointer',
            display: 'inline-flex',
            alignItems: 'center',
            gap: 6,
            fontSize: 13,
          }}
        >
          <span>{banner.ctaText || t("Nhận ưu đãi ngay")}</span>
          <ArrowRightOutlined />
        </button>
      )}
    </div>
  );

  if (placement === 'LandingTop') {
    return (
      <div className="landing-banner-strip">
        <div className="landing-banner-inner">{content}</div>
      </div>
    );
  }

  return content;
}

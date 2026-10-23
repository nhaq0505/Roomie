import {
  ArrowRightOutlined,
  BellOutlined,
  BookOutlined,
  CheckCircleFilled,
  CreditCardOutlined,
  CrownOutlined,
  FileTextOutlined,
  HomeOutlined,
  LockOutlined,
  SafetyCertificateOutlined,
} from '@ant-design/icons';
import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { recordAttribution, trackEvent } from '../../shared/analytics';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import { ActiveBannerStrip } from '../../shared/ui/ActiveBannerStrip';
import { useAuth } from '../auth/session';
import { type User } from '../auth/index';
import './LandingPage.css';
import {
  ComparisonSection,
  FaqSection,
  GuidesSection,
  HeroHighlightsStrip,
  PricingSection,
} from './LandingSections';
import {
  BillingIllustration,
  Brand,
  DashboardPreview,
  RoomsIllustration,
  TenantPhoneIllustration,
  ZaloAndReportsIllustration,
} from './LandingVisuals';
import { useLandingMotion } from './useLandingMotion';

export type LandingPageKind = 'home' | 'features' | 'pricing' | 'guides';

export function LandingContent({
  user = null,
  page = 'home',
}: {
  user?: User | null;
  page?: LandingPageKind;
}) {
  const rootRef = useLandingMotion(page);
  const workspace =
    user?.role === 'Admin'
      ? '/admin/owners'
      : user?.role === 'Marketer'
      ? '/admin/monitoring'
      : '/dashboard';
  const destination = user ? workspace : '/register';
  const actionLabel = user ? t("Bảng điều khiển") : t("Đăng ký làm chủ trọ");

  useEffect(() => {
    try {
      window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
      recordAttribution();
      if (page === 'pricing' || window.location.hash === '#pricing') {
        trackEvent('pricing_view', { eventName: 'Pricing Page' });
      } else {
        trackEvent('landing_view', { eventName: `Landing ${page}` });
      }
    } catch {
      // Non-blocking best-effort
    }
  }, [page]);

  const handleCta = (ctaId: string, label: string) => {
    try {
      trackEvent('cta_click', { ctaId, eventName: label });
    } catch {
      // Non-blocking best-effort
    }
  };

  return (
    <div className="landing-page" ref={rootRef}>
      <header className="landing-header">
        <div className="landing-header-inner">
          <Brand />
          <nav className="landing-nav" aria-label={t("Điều hướng chính")}>
            <Link to="/" className={page === 'home' ? 'is-active' : undefined} aria-current={page === 'home' ? 'page' : undefined}>
              {t("Trang chủ")}
            </Link>
            <Link to="/features" className={page === 'features' ? 'is-active' : undefined} aria-current={page === 'features' ? 'page' : undefined}>
              {t("Tính năng")}
            </Link>
            <Link to="/pricing" className={page === 'pricing' ? 'is-active' : undefined} aria-current={page === 'pricing' ? 'page' : undefined}>
              {t("Bảng giá")}
            </Link>
            <Link to="/huong-dan" className={page === 'guides' ? 'is-active' : undefined} aria-current={page === 'guides' ? 'page' : undefined}>
              {t("Hướng dẫn")}
            </Link>
          </nav>
          <div className="landing-header-actions">
            <LanguageSelector />
            {!user && (
              <Link
                to="/login"
                className="landing-btn landing-btn-outline landing-login"
                onClick={() => handleCta('cta_header_login', 'Header Login')}
              >
                {t("Đăng nhập")}
              </Link>
            )}
            <Link
              to={destination}
              className="landing-btn landing-btn-light"
              onClick={() => handleCta('cta_header_action', actionLabel)}
            >
              <span className="btn-text-full">{actionLabel}</span>
              <span className="btn-text-short">{user ? t("Tổng quan") : t("Đăng ký")}</span>
            </Link>
          </div>
        </div>
      </header>

      <nav className="landing-subnav" aria-label={t("Chuyển trang nhanh")}>
        <Link to="/" className={page === 'home' ? 'is-active' : undefined}>{t("Trang chủ")}</Link>
        <Link to="/features" className={page === 'features' ? 'is-active' : undefined}>{t("Tính năng")}</Link>
        <Link to="/pricing" className={page === 'pricing' ? 'is-active' : undefined}>{t("Bảng giá")}</Link>
        <Link to="/huong-dan" className={page === 'guides' ? 'is-active' : undefined}>{t("Hướng dẫn")}</Link>
      </nav>

      <main>
        <ActiveBannerStrip placement="LandingTop" />
        {page === 'home' && (
          <>
            <section className="landing-hero" id="hero">
              <div className="hero-atmosphere" aria-hidden="true" />
              <div className="hero-content">
                <div className="hero-kicker"><span />{t("QUẢN LÝ PHÒNG TRỌ")}</div>
                <h1>
                  <span>{t("Gọn gàng từng phòng.")}</span>
                  <span>{t("Rõ ràng từng khoản.")}</span>
                </h1>
                <p>{t("Theo dõi người thuê, chốt chỉ số điện và gửi hóa đơn trong một nơi duy nhất.")}</p>
                <div className="hero-actions">
                  <Link
                    to={destination}
                    className="landing-btn landing-btn-light landing-btn-lg"
                    onClick={() => handleCta('btn_hero_signup', actionLabel)}
                  >
                    <span>{actionLabel}</span>
                    <ArrowRightOutlined aria-hidden="true" />
                  </Link>
                  <Link
                    to="/pricing"
                    className="hero-secondary-link"
                    onClick={() => handleCta('btn_hero_pricing', 'View Pricing')}
                  >
                    <span>{t("Bảng giá")}</span>
                  </Link>
                </div>
                <div className="hero-proof">
                  <CheckCircleFilled />
                  <span>{t("Người thuê xem hóa đơn, không cần cài app.")}</span>
                </div>
              </div>
              <DashboardPreview />
              <HeroHighlightsStrip />
            </section>

            <section id="features" className="landing-features">
              <div className="landing-section-heading">
                <span className="section-eyebrow">{t("TÍNH NĂNG NỔI BẬT")}</span>
                <h2>{t("Từ phòng trống đến hóa đơn đã thu.")}</h2>
                <p>{t("Đơn giản hóa việc quản lý mỗi ngày")}</p>
              </div>

              <div className="product-story">
                <div className="story-copy">
                  <span className="story-index">01 / <HomeOutlined />{t("Phòng & người thuê")}</span>
                  <h3>{t("Mỗi căn phòng, luôn trong tầm mắt.")}</h3>
                  <p>{t("Quản lý nhiều dãy trọ và đơn giá riêng cho từng khu.")}</p>
                  <ul>
                    <li><CheckCircleFilled />{t("Nắm phòng trống, người đang thuê và tiền cọc.")}</li>
                    <li><CheckCircleFilled />{t("Theo dõi doanh thu, công nợ, tiền cọc và xuất Excel.")}</li>
                  </ul>
                  <Link
                    to={destination}
                    className="landing-text-link"
                    onClick={() => handleCta('cta_features_action', actionLabel)}
                  >
                    {actionLabel}
                    <ArrowRightOutlined aria-hidden="true" />
                  </Link>
                </div>
                <RoomsIllustration />
              </div>

              <div className="story-band">
                <div className="product-story product-story--reverse">
                  <div className="story-copy">
                    <span className="story-index">02 / <FileTextOutlined />{t("Hóa đơn")}</span>
                    <h3>{t("Một hóa đơn. Rõ từng khoản.")}</h3>
                    <p>{t("Tính tiền phòng, điện nước và chia sẻ hóa đơn chi tiết.")}</p>
                    <ul>
                      <li><CheckCircleFilled />{t("Ghi chỉ số hàng tháng, kiểm tra số liệu trước khi lưu.")}</li>
                      <li><CheckCircleFilled />{t("Chốt điện, tính tiền và lập hóa đơn cùng một nơi.")}</li>
                    </ul>
                  </div>
                  <BillingIllustration />
                </div>
              </div>
            </section>

            <section className="product-story tenant-story" id="how-it-works">
              <div className="story-copy">
                <span className="story-index">03 / <CreditCardOutlined />{t("Thanh toán trực tuyến (payOS)")} &amp; VietQR</span>
                <h2>{t("Tiện lợi cho khách, nhàn hạ cho chủ trọ")}</h2>
                <p>{t("Nhận tiền linh hoạt qua chế độ VietQR thủ công hoặc gạch nợ tự động với payOS.")}</p>
                <ol className="tenant-steps">
                  <li>
                    <span>01</span>
                    <div>
                      <h3>{t("Nhận liên kết riêng tư")}</h3>
                      <p>{t("Mở liên kết do chủ trọ gửi ngay trên điện thoại.")}</p>
                    </div>
                  </li>
                  <li>
                    <span>02</span>
                    <div>
                      <h3>{t("Xem hóa đơn minh bạch")}</h3>
                      <p>{t("Kiểm tra tiền phòng, điện nước và từng khoản phí.")}</p>
                    </div>
                  </li>
                  <li>
                    <span>03</span>
                    <div>
                      <h3>{t("Thanh toán bằng mã QR")}</h3>
                      <p>{t("Quét VietQR bằng ứng dụng ngân hàng. Hóa đơn cập nhật khi thanh toán được xác nhận.")}</p>
                    </div>
                  </li>
                </ol>
              </div>
              <TenantPhoneIllustration />
            </section>

            <section className="landing-explore" aria-label={t("Khám phá chi tiết")}>
              <div className="explore-grid">
                <Link to="/features" className="explore-card">
                  <span className="explore-icon"><BellOutlined /></span>
                  <div>
                    <strong>{t("Tính năng & So sánh")}</strong>
                    <p>{t("Xem chi tiết nhắc lịch Zalo, xuất báo cáo Excel và bảng so sánh với sổ sách truyền thống.")}</p>
                  </div>
                  <span className="explore-link">{t("Xem chi tiết")} <ArrowRightOutlined /></span>
                </Link>
                <Link to="/pricing" className="explore-card">
                  <span className="explore-icon"><CrownOutlined /></span>
                  <div>
                    <strong>{t("Bảng giá & Hỏi đáp")}</strong>
                    <p>{t("Gói Miễn phí tối đa 5 phòng, các kỳ hạn gói Pro và giải đáp thắc mắc thường gặp.")}</p>
                  </div>
                  <span className="explore-link">{t("Xem bảng giá")} <ArrowRightOutlined /></span>
                </Link>
                <Link to="/huong-dan" className="explore-card">
                  <span className="explore-icon"><BookOutlined /></span>
                  <div>
                    <strong>{t("Cẩm nang hướng dẫn")}</strong>
                    <p>{t("4 quy trình vận hành chuẩn từ lúc thêm phòng, chốt điện đến khi thu tiền qua QR.")}</p>
                  </div>
                  <span className="explore-link">{t("Xem hướng dẫn")} <ArrowRightOutlined /></span>
                </Link>
              </div>
            </section>
          </>
        )}

        {page === 'features' && (
          <>
            <section className="landing-hero landing-hero--subpage" id="hero">
              <div className="hero-atmosphere" aria-hidden="true" />
              <div className="hero-content">
                <div className="hero-kicker"><span />{t("TÍNH NĂNG NỔI BẬT")}</div>
                <h1>
                  <span>{t("Mọi nghiệp vụ nhà trọ.")}</span>
                  <span>{t("Gói gọn trong một nơi.")}</span>
                </h1>
                <p>{t("Từ quản lý phòng, sổ cái tiền cọc, chốt chỉ số điện, nhắc lịch Zalo đến đối soát thanh toán VietQR và payOS.")}</p>
                <div className="hero-actions">
                  <Link
                    to={destination}
                    className="landing-btn landing-btn-light landing-btn-lg"
                    onClick={() => handleCta('btn_features_signup', actionLabel)}
                  >
                    <span>{actionLabel}</span>
                    <ArrowRightOutlined aria-hidden="true" />
                  </Link>
                  <Link to="/pricing" className="hero-secondary-link">
                    <span>{t("Xem bảng giá")}</span>
                  </Link>
                </div>
              </div>
            </section>

            <section id="features" className="landing-features">
              <div className="product-story">
                <div className="story-copy">
                  <span className="story-index">01 / <HomeOutlined />{t("Phòng & người thuê")}</span>
                  <h3>{t("Mỗi căn phòng, luôn trong tầm mắt.")}</h3>
                  <p>{t("Quản lý nhiều dãy trọ và đơn giá riêng cho từng khu.")}</p>
                  <ul>
                    <li><CheckCircleFilled />{t("Nắm phòng trống, người đang thuê và tiền cọc.")}</li>
                    <li><CheckCircleFilled />{t("Theo dõi doanh thu, công nợ, tiền cọc và xuất Excel.")}</li>
                    <li><CheckCircleFilled />{t("Quyết toán trả phòng chặt chẽ, tự động thu hồi liên kết khách cũ.")}</li>
                  </ul>
                  <Link
                    to={destination}
                    className="landing-text-link"
                    onClick={() => handleCta('cta_features_action', actionLabel)}
                  >
                    {actionLabel}
                    <ArrowRightOutlined aria-hidden="true" />
                  </Link>
                </div>
                <RoomsIllustration />
              </div>

              <div className="story-band">
                <div className="product-story product-story--reverse">
                  <div className="story-copy">
                    <span className="story-index">02 / <FileTextOutlined />{t("Hóa đơn")}</span>
                    <h3>{t("Một hóa đơn. Rõ từng khoản.")}</h3>
                    <p>{t("Tính tiền phòng, điện nước và chia sẻ hóa đơn chi tiết.")}</p>
                    <ul>
                      <li><CheckCircleFilled />{t("Ghi chỉ số hàng tháng, kiểm tra số liệu trước khi lưu.")}</li>
                      <li><CheckCircleFilled />{t("Chốt điện, tính tiền và lập hóa đơn cùng một nơi.")}</li>
                      <li><CheckCircleFilled />{t("Lưu cố định đơn giá lịch sử, không nhảy số khi đổi giá phòng.")}</li>
                    </ul>
                  </div>
                  <BillingIllustration />
                </div>
              </div>

              <div className="product-story">
                <div className="story-copy">
                  <span className="story-index">03 / <BellOutlined />{t("Nhắc hóa đơn qua Zalo")}</span>
                  <h3>{t("Nhắc lịch đúng giờ. Báo cáo chuẩn số liệu.")}</h3>
                  <p>{t("Tự động gửi thông báo hóa đơn qua Zalo và tổng hợp sổ sách cuối tháng chỉ với vài thao tác.")}</p>
                  <ul>
                    <li><CheckCircleFilled />{t("Tự động gửi liên kết hóa đơn riêng tư trong khung giờ văn minh 07:00 – 21:00.")}</li>
                    <li><CheckCircleFilled />{t("Khóa chống gửi trùng lặp và tự động dừng nhắc khi hóa đơn đã thu đủ.")}</li>
                    <li><CheckCircleFilled />{t("Xuất báo cáo Excel chuẩn kế toán, tách bạch doanh thu thuê trọ và tiền cọc.")}</li>
                  </ul>
                </div>
                <ZaloAndReportsIllustration />
              </div>
            </section>

            <ComparisonSection />

            <section className="product-story tenant-story" id="how-it-works">
              <div className="story-copy">
                <span className="story-index">04 / <CreditCardOutlined />{t("Thanh toán trực tuyến (payOS)")} &amp; VietQR</span>
                <h2>{t("Tiện lợi cho khách, nhàn hạ cho chủ trọ")}</h2>
                <p>{t("Nhận tiền linh hoạt qua chế độ VietQR thủ công hoặc gạch nợ tự động với payOS.")}</p>
                <ol className="tenant-steps">
                  <li>
                    <span>01</span>
                    <div>
                      <h3>{t("Nhận liên kết riêng tư")}</h3>
                      <p>{t("Mở liên kết do chủ trọ gửi ngay trên điện thoại.")}</p>
                    </div>
                  </li>
                  <li>
                    <span>02</span>
                    <div>
                      <h3>{t("Xem hóa đơn minh bạch")}</h3>
                      <p>{t("Kiểm tra tiền phòng, điện nước và từng khoản phí.")}</p>
                    </div>
                  </li>
                  <li>
                    <span>03</span>
                    <div>
                      <h3>{t("Thanh toán bằng mã QR")}</h3>
                      <p>{t("Quét VietQR bằng ứng dụng ngân hàng. Hóa đơn cập nhật khi thanh toán được xác nhận.")}</p>
                    </div>
                  </li>
                </ol>
              </div>
              <TenantPhoneIllustration />
            </section>

            <section className="landing-security" aria-labelledby="landing-security-title">
              <div className="security-intro">
                <SafetyCertificateOutlined />
                <h2 id="landing-security-title">{t("Bảo mật giao dịch")}</h2>
              </div>
              <div className="security-points">
                <p><LockOutlined />{t("Khóa kết nối thanh toán được mã hóa trên máy chủ.")}</p>
                <p><CheckCircleFilled />{t("Kiểm tra xác nhận từ payOS trước khi ghi nhận thanh toán.")}</p>
                <p><SafetyCertificateOutlined />{t("Liên kết của lượt thuê cũ hết hiệu lực sau khi trả phòng.")}</p>
              </div>
            </section>
          </>
        )}

        {page === 'pricing' && (
          <>
            <section className="landing-hero landing-hero--subpage" id="hero">
              <div className="hero-atmosphere" aria-hidden="true" />
              <div className="hero-content">
                <div className="hero-kicker"><span />{t("BẢNG GIÁ MINH BẠCH")}</div>
                <h1>
                  <span>{t("Miễn phí tới 5 phòng.")}</span>
                  <span>{t("Mở rộng không giới hạn.")}</span>
                </h1>
                <p>{t("Trải nghiệm đầy đủ mọi tính năng cốt lõi ngay từ gói Miễn phí. Chỉ nâng cấp khi quy mô vượt quá 5 phòng.")}</p>
              </div>
            </section>

            <PricingSection
              destination={destination}
              actionLabel={actionLabel}
              isAuthenticated={Boolean(user)}
              onCta={handleCta}
            />

            <FaqSection />

            <section className="landing-security" aria-labelledby="landing-security-title">
              <div className="security-intro">
                <SafetyCertificateOutlined />
                <h2 id="landing-security-title">{t("Bảo mật giao dịch")}</h2>
              </div>
              <div className="security-points">
                <p><LockOutlined />{t("Khóa kết nối thanh toán được mã hóa trên máy chủ.")}</p>
                <p><CheckCircleFilled />{t("Kiểm tra xác nhận từ payOS trước khi ghi nhận thanh toán.")}</p>
                <p><SafetyCertificateOutlined />{t("Liên kết của lượt thuê cũ hết hiệu lực sau khi trả phòng.")}</p>
              </div>
            </section>
          </>
        )}

        {page === 'guides' && (
          <>
            <section className="landing-hero landing-hero--subpage" id="hero">
              <div className="hero-atmosphere" aria-hidden="true" />
              <div className="hero-content">
                <div className="hero-kicker"><span />{t("CẨM NANG CHỦ TRỌ")}</div>
                <h1>
                  <span>{t("Bắt đầu nhanh chóng.")}</span>
                  <span>{t("Vận hành chuẩn từng bước.")}</span>
                </h1>
                <p>{t("Hướng dẫn chi tiết từng bước từ lúc thiết lập phòng đầu tiên đến khi chốt điện, nhắc nợ Zalo và đối soát cuối tháng.")}</p>
              </div>
            </section>

            <GuidesSection />
          </>
        )}

        <section id="get-started" className="landing-cta">
          <div className="cta-content">
            <span className="section-eyebrow">Roomie</span>
            <h2>{t("Nhẹ đầu hơn từ tháng này.")}</h2>
            <p>{t("Đăng ký, xác nhận email và bắt đầu quản lý nhà trọ với gói Miễn phí.")}</p>
            <Link
              to={destination}
              className="landing-btn landing-btn-light landing-btn-lg"
              onClick={() => handleCta('cta_bottom_action', actionLabel)}
            >
              <span>{user ? t("Bảng điều khiển") : t("Đăng ký làm chủ trọ ngay")}</span>
              <ArrowRightOutlined aria-hidden="true" />
            </Link>
          </div>
          <ol className="setup-steps">
            <li>
              <strong>{t("1. Đăng ký thông tin")}</strong>
              <span>{t("Điền thông tin tài khoản.")}</span>
            </li>
            <li>
              <strong>{t("2. Xác thực OTP email")}</strong>
              <span>{t("Xác nhận bằng mã gửi qua email.")}</span>
            </li>
            <li>
              <strong>{t("3. Thiết lập phòng & đón khách")}</strong>
              <span>{t("Thêm dãy trọ và phòng đầu tiên.")}</span>
            </li>
          </ol>
        </section>
      </main>

      <footer className="landing-footer">
        <div className="footer-inner">
          <div className="footer-brand">
            <Brand footer />
            <p>{t("Đơn giản hóa việc quản lý nhà trọ mỗi ngày.")}</p>
          </div>
          <div className="footer-links">
            <strong>{t("Tính năng")}</strong>
            <Link to="/">{t("Trang chủ")}</Link>
            <Link to="/features">{t("Tính năng")}</Link>
            <Link to="/pricing">{t("Bảng giá")}</Link>
            <Link to="/huong-dan">{t("Hướng dẫn")}</Link>
          </div>
          <div className="footer-links">
            <strong>{t("Chủ trọ")}</strong>
            <Link to="/login">{t("Đăng nhập")}</Link>
            <Link to="/register">{t("Đăng ký làm chủ trọ")}</Link>
            <Link to="/pricing">{t("Gói Pro")}</Link>
          </div>
        </div>
        <div className="footer-bottom">
          <span>© 2026 Roomie</span>
          <span>{t("Đơn giản hóa việc quản lý mỗi ngày")}</span>
        </div>
      </footer>
    </div>
  );
}

export default function LandingPage({ page = 'home' }: { page?: LandingPageKind }) {
  const { user } = useAuth();
  return <LandingContent user={user} page={page} />;
}

import {
ArrowRightOutlined, BarChartOutlined, CheckCircleFilled, CreditCardOutlined,
DashboardOutlined, FileTextOutlined, HomeOutlined, LockOutlined,
QrcodeOutlined, SafetyCertificateOutlined, TeamOutlined, ThunderboltOutlined,
} from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';
import { useAuth } from '../auth/session';
import { type User } from '../auth/index';
import './LandingPage.css';

function Brand({ footer = false }: { footer?: boolean }) {
  return <Link to="/" className={`landing-brand${footer ? ' landing-brand--footer' : ''}`} aria-label="Roomie">
    <span className="brand-mark"><RoomieMark /></span><strong>Roomie</strong>
  </Link>;
}

function SampleInvoice({ phone = false }: { phone?: boolean }) {
  return <div className={`sample-invoice${phone ? ' sample-invoice--phone' : ''}`}>
    <div className="sample-invoice-heading"><div><small>{t("Hóa đơn")}</small><strong>P101 <span>· 10/2026</span></strong></div><span className="sample-paid"><CheckCircleFilled />{t("Đã thanh toán")}</span></div>
    <div className="sample-invoice-lines">
      <div><span>{t("Tiền thuê")}</span><strong>{formatVnd(3500000)}</strong></div>
      <div><span>{t("Tiền điện")} <small>124 kWh</small></span><strong>{formatVnd(434000)}</strong></div>
      <div><span>{t("Tiền nước")}</span><strong>{formatVnd(100000)}</strong></div>
      <div><span>{t("Phí dịch vụ")}</span><strong>{formatVnd(50000)}</strong></div>
    </div>
    <div className="sample-invoice-total"><span>{t("Tổng cộng:")}</span><strong>{formatVnd(4084000)}</strong></div>
    {phone && <div className="sample-phone-confirm"><CheckCircleFilled /><strong>{t("Đã thanh toán")}</strong><span><QrcodeOutlined /> VietQR · payOS</span></div>}
  </div>;
}

function DashboardPreview() {
  return <figure className="landing-dashboard">
    <div className="dashboard-chrome"><span className="window-dots" aria-hidden="true"><i /><i /><i /></span><span>Roomie</span><span>{t("Khu vực làm việc")}</span></div>
    <div className="dashboard-layout">
      <div className="dashboard-nav" aria-hidden="true">
        <div className="dashboard-nav-brand"><span className="brand-mark"><RoomieMark /></span><strong>Roomie</strong></div>
        <span className="dashboard-nav-active"><DashboardOutlined /><span>{t("Tổng quan")}</span></span>
        <span><HomeOutlined /><span>{t("Dãy trọ")}</span></span>
        <span><TeamOutlined /><span>{t("Phòng & người thuê")}</span></span>
        <span><ThunderboltOutlined /><span>{t("Chỉ số điện")}</span></span>
        <span><FileTextOutlined /><span>{t("Hóa đơn")}</span></span>
        <span><BarChartOutlined /><span>{t("Báo cáo")}</span></span>
      </div>
      <div className="dashboard-content">
        <div className="dashboard-heading"><div><small>{t("Tình hình phòng trọ")}</small><strong>{t("Tổng quan")}</strong></div><span className="dashboard-period">10 / 2026</span></div>
        <div className="dashboard-stats">
          <div><HomeOutlined /><small>{t("Tổng số phòng")}</small><strong>12</strong></div>
          <div><TeamOutlined /><small>{t("Đang thuê")}</small><strong>11</strong></div>
          <div><DashboardOutlined /><small>{t("Phòng trống")}</small><strong>1</strong></div>
          <div><BarChartOutlined /><small>{t("Tỷ lệ lấp đầy")}</small><strong>92<span>%</span></strong></div>
        </div>
        <div className="dashboard-panels">
          <div className="dashboard-rooms">
            <div className="sample-panel-title"><strong>{t("Phòng & người thuê")}</strong><span>{t("Dãy trọ")} A</span></div>
            {['P101', 'P102', 'P103', 'P104'].map((room, index) => <div className="dashboard-room-row" key={room}><span className="sample-room-icon"><HomeOutlined /></span><strong>{room}</strong><span className={index === 3 ? 'sample-vacant' : 'sample-occupied'}>{index === 3 ? t("Phòng trống") : t("Đang thuê")}</span></div>)}
            <div className="dashboard-revenue"><span>{t("Doanh thu tháng")}</span><strong>{formatVnd(42500000)}</strong></div>
          </div>
          <SampleInvoice />
        </div>
      </div>
    </div>
    <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
  </figure>;
}

function RoomsIllustration() {
  return <figure className="story-art rooms-art">
    <div className="rooms-art-window">
      <div className="sample-panel-title"><strong>{t("Dãy trọ")} A</strong><span>12 {t("Phòng")}</span></div>
      <div className="sample-room-grid">{['P101', 'P102', 'P103', 'P104'].map((room, index) => <div className={`sample-room-tile${index === 3 ? ' sample-room-tile--vacant' : ''}`} key={room}>
        <HomeOutlined /><strong>{room}</strong><span>{index === 3 ? t("Phòng trống") : t("Đang thuê")}</span>
      </div>)}</div>
      <div className="occupancy-caption"><span>{t("Tỷ lệ lấp đầy")}</span><strong>92%</strong></div><div className="occupancy-track"><span /></div>
    </div><figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
  </figure>;
}

function BillingIllustration() {
  return <figure className="story-art billing-art">
    <div className="billing-reading"><span className="billing-reading-icon"><ThunderboltOutlined /></span><div><span>{t("Chỉ số điện")}</span><strong>1.240 <ArrowRightOutlined /> 1.364 <small>kWh</small></strong></div><CheckCircleFilled /></div>
    <SampleInvoice /><figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
  </figure>;
}

export function LandingContent({ user = null }: { user?: User | null }) {
  const workspace = user?.role === 'Admin' ? '/admin/owners' : '/dashboard';
  const destination = user ? workspace : '/register';
  const actionLabel = user ? t("Bảng điều khiển") : t("Đăng ký làm chủ trọ");

  return <div className="landing-page">
    <header className="landing-header">
      <div className="landing-header-inner">
        <Brand />
        <nav className="landing-nav" aria-label={t("Điều hướng chính")}><a href="#features">{t("Tính năng")}</a><a href="#how-it-works">{t("Cách hoạt động")}</a></nav>
        <div className="landing-header-actions">
          <LanguageSelector />
          {!user && <Link to="/login" className="landing-login">{t("Đăng nhập")}</Link>}
          <Link to={destination} className="landing-btn landing-btn-light"><span className="btn-text-full">{actionLabel}</span><span className="btn-text-short">{user ? t("Tổng quan") : t("Đăng ký")}</span></Link>
        </div>
      </div>
    </header>
    <main>
      <section className="landing-hero" id="hero">
        <div className="hero-atmosphere" aria-hidden="true" />
        <div className="hero-content">
          <div className="hero-kicker"><span />{t("QUẢN LÝ PHÒNG TRỌ")}</div>
          <h1><span>{t("Gọn gàng từng phòng.")}</span><span>{t("Rõ ràng từng khoản.")}</span></h1>
          <p>{t("Theo dõi người thuê, chốt chỉ số điện và gửi hóa đơn trong một nơi duy nhất.")}</p>
          <Link to={destination} className="landing-btn landing-btn-light landing-btn-lg"><span>{actionLabel}</span><ArrowRightOutlined aria-hidden="true" /></Link>
          <div className="hero-proof"><CheckCircleFilled /><span>{t("Người thuê xem hóa đơn, không cần cài app.")}</span></div>
        </div>
        <DashboardPreview />
      </section>

      <section id="features" className="landing-features">
        <div className="landing-section-heading"><span className="section-eyebrow">{t("TÍNH NĂNG NỔI BẬT")}</span><h2>{t("Từ phòng trống đến hóa đơn đã thu.")}</h2><p>{t("Đơn giản hóa việc quản lý mỗi ngày")}</p></div>
        <div className="product-story">
          <div className="story-copy"><span className="story-index">01 / <HomeOutlined />{t("Phòng & người thuê")}</span><h3>{t("Mỗi căn phòng, luôn trong tầm mắt.")}</h3><p>{t("Quản lý nhiều dãy trọ và đơn giá riêng cho từng khu.")}</p><ul><li><CheckCircleFilled />{t("Nắm phòng trống, người đang thuê và tiền cọc.")}</li><li><CheckCircleFilled />{t("Theo dõi doanh thu, công nợ, tiền cọc và xuất Excel.")}</li></ul><Link to={destination} className="landing-text-link">{actionLabel}<ArrowRightOutlined aria-hidden="true" /></Link></div>
          <RoomsIllustration />
        </div>
        <div className="story-band"><div className="product-story product-story--reverse">
          <div className="story-copy"><span className="story-index">02 / <FileTextOutlined />{t("Hóa đơn")}</span><h3>{t("Một hóa đơn. Rõ từng khoản.")}</h3><p>{t("Tính tiền phòng, điện nước và chia sẻ hóa đơn chi tiết.")}</p><ul><li><CheckCircleFilled />{t("Ghi chỉ số hàng tháng, kiểm tra số liệu trước khi lưu.")}</li><li><CheckCircleFilled />{t("Chốt điện, tính tiền và lập hóa đơn cùng một nơi.")}</li></ul></div>
          <BillingIllustration />
        </div></div>
      </section>

      <section className="product-story tenant-story" id="how-it-works">
        <div className="story-copy"><span className="story-index">03 / <CreditCardOutlined />{t("Thanh toán trực tuyến (payOS)")}</span><h2>{t("Tiện lợi cho khách, nhàn hạ cho chủ trọ")}</h2><p>{t("Nhận tiền qua VietQR với payOS và cập nhật trạng thái thanh toán.")}</p>
          <ol className="tenant-steps">
            <li><span>01</span><div><h3>{t("Nhận liên kết riêng tư")}</h3><p>{t("Mở liên kết do chủ trọ gửi ngay trên điện thoại.")}</p></div></li>
            <li><span>02</span><div><h3>{t("Xem hóa đơn minh bạch")}</h3><p>{t("Kiểm tra tiền phòng, điện nước và từng khoản phí.")}</p></div></li>
            <li><span>03</span><div><h3>{t("Thanh toán bằng mã QR")}</h3><p>{t("Quét VietQR bằng ứng dụng ngân hàng. Hóa đơn cập nhật khi thanh toán được xác nhận.")}</p></div></li>
          </ol>
        </div>
        <figure className="story-art tenant-art"><div className="sample-phone"><div className="sample-phone-top" aria-hidden="true"><span>9:41</span><span>••• ▰</span></div><div className="sample-phone-brand"><span className="brand-mark"><RoomieMark /></span><strong>Roomie</strong><LockOutlined /></div><SampleInvoice phone /><div className="sample-phone-private"><LockOutlined />{t("Liên kết riêng tư")}</div></div><figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption></figure>
      </section>

      <section className="landing-security" aria-labelledby="landing-security-title">
        <div className="security-intro"><SafetyCertificateOutlined /><h2 id="landing-security-title">{t("Bảo mật giao dịch")}</h2></div>
        <div className="security-points"><p><LockOutlined />{t("Khóa kết nối thanh toán được mã hóa trên máy chủ.")}</p><p><CheckCircleFilled />{t("Kiểm tra xác nhận từ payOS trước khi ghi nhận thanh toán.")}</p><p><SafetyCertificateOutlined />{t("Liên kết của lượt thuê cũ hết hiệu lực sau khi trả phòng.")}</p></div>
      </section>

      <section className="landing-cta">
        <div className="cta-content"><span className="section-eyebrow">Roomie</span><h2>{t("Nhẹ đầu hơn từ tháng này.")}</h2><p>{t("Đăng ký, xác nhận email và bắt đầu quản lý nhà trọ với gói Miễn phí.")}</p><Link to={destination} className="landing-btn landing-btn-light landing-btn-lg"><span>{user ? t("Bảng điều khiển") : t("Đăng ký làm chủ trọ ngay")}</span><ArrowRightOutlined aria-hidden="true" /></Link></div>
        <ol className="setup-steps"><li><strong>{t("1. Đăng ký thông tin")}</strong><span>{t("Điền thông tin tài khoản.")}</span></li><li><strong>{t("2. Xác thực OTP email")}</strong><span>{t("Xác nhận bằng mã gửi qua email.")}</span></li><li><strong>{t("3. Thiết lập phòng & đón khách")}</strong><span>{t("Thêm dãy trọ và phòng đầu tiên.")}</span></li></ol>
      </section>
    </main>
    <footer className="landing-footer"><div className="footer-inner"><div className="footer-brand"><Brand footer /><p>{t("Đơn giản hóa việc quản lý nhà trọ mỗi ngày.")}</p></div><div className="footer-links"><strong>{t("Tính năng")}</strong><a href="#hero">{t("Trang chủ")}</a><a href="#features">{t("Tính năng")}</a><a href="#how-it-works">{t("Cách hoạt động")}</a></div><div className="footer-links"><strong>{t("Chủ trọ")}</strong><Link to="/login">{t("Đăng nhập")}</Link><Link to="/register">{t("Đăng ký làm chủ trọ")}</Link><Link to="/">{t("Trang chủ")}</Link></div></div><div className="footer-bottom"><span>© 2026 Roomie</span><span>{t("Đơn giản hóa việc quản lý mỗi ngày")}</span></div></footer>
  </div>;
}

export default function LandingPage() {
  const { user } = useAuth();
  return <LandingContent user={user} />;
}

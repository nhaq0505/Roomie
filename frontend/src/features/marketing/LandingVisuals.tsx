import {
  ArrowRightOutlined,
  BarChartOutlined,
  BellOutlined,
  CheckCircleFilled,
  DashboardOutlined,
  FileExcelOutlined,
  FileTextOutlined,
  HomeOutlined,
  LockOutlined,
  QrcodeOutlined,
  SafetyCertificateOutlined,
  TeamOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';

export function Brand({ footer = false }: { footer?: boolean }) {
  return (
    <Link to="/" className={`landing-brand${footer ? ' landing-brand--footer' : ''}`} aria-label="Roomie">
      <span className="brand-mark"><RoomieMark /></span>
      <strong>Roomie</strong>
    </Link>
  );
}

export function SampleInvoice({ phone = false }: { phone?: boolean }) {
  return (
    <div className={`sample-invoice${phone ? ' sample-invoice--phone' : ''}`}>
      <div className="sample-invoice-heading">
        <div>
          <small>{t("Hóa đơn")}</small>
          <strong>P101 <span>· 10/2026</span></strong>
        </div>
        <span className="sample-paid"><CheckCircleFilled />{t("Đã thanh toán")}</span>
      </div>
      <div className="sample-invoice-lines">
        <div><span>{t("Tiền thuê")}</span><strong>{formatVnd(3500000)}</strong></div>
        <div><span>{t("Tiền điện")} <small>124 kWh</small></span><strong>{formatVnd(434000)}</strong></div>
        <div><span>{t("Tiền nước")}</span><strong>{formatVnd(100000)}</strong></div>
        <div><span>{t("Phí dịch vụ")}</span><strong>{formatVnd(50000)}</strong></div>
      </div>
      <div className="sample-invoice-total">
        <span>{t("Tổng cộng:")}</span>
        <strong>{formatVnd(4084000)}</strong>
      </div>
      {phone && (
        <div className="sample-phone-confirm">
          <CheckCircleFilled />
          <strong>{t("Đã thanh toán")}</strong>
          <span><QrcodeOutlined /> VietQR · payOS</span>
        </div>
      )}
    </div>
  );
}

export function DashboardPreview() {
  return (
    <figure className="landing-dashboard">
      <div className="dashboard-chrome">
        <span className="window-dots" aria-hidden="true"><i /><i /><i /></span>
        <span>Roomie</span>
        <span>{t("Khu vực làm việc")}</span>
      </div>
      <div className="dashboard-layout">
        <div className="dashboard-nav" aria-hidden="true">
          <div className="dashboard-nav-brand">
            <span className="brand-mark"><RoomieMark /></span>
            <strong>Roomie</strong>
          </div>
          <span className="dashboard-nav-active"><DashboardOutlined /><span>{t("Tổng quan")}</span></span>
          <span><HomeOutlined /><span>{t("Dãy trọ")}</span></span>
          <span><TeamOutlined /><span>{t("Phòng & người thuê")}</span></span>
          <span><ThunderboltOutlined /><span>{t("Lập hóa đơn")}</span></span>
          <span><FileTextOutlined /><span>{t("Hóa đơn")}</span></span>
          <span><BarChartOutlined /><span>{t("Báo cáo")}</span></span>
        </div>
        <div className="dashboard-content">
          <div className="dashboard-heading">
            <div>
              <small>{t("Tình hình phòng trọ")}</small>
              <strong>{t("Tổng quan")}</strong>
            </div>
            <span className="dashboard-period">10 / 2026</span>
          </div>
          <div className="dashboard-stats">
            <div><HomeOutlined /><small>{t("Tổng số phòng")}</small><strong>12</strong></div>
            <div><TeamOutlined /><small>{t("Đang thuê")}</small><strong>11</strong></div>
            <div><DashboardOutlined /><small>{t("Phòng trống")}</small><strong>1</strong></div>
            <div><BarChartOutlined /><small>{t("Tỷ lệ lấp đầy")}</small><strong>92<span>%</span></strong></div>
          </div>
          <div className="dashboard-panels">
            <div className="dashboard-rooms">
              <div className="sample-panel-title">
                <strong>{t("Phòng & người thuê")}</strong>
                <span>{t("Dãy trọ")} A</span>
              </div>
              {['P101', 'P102', 'P103', 'P104'].map((room, index) => (
                <div className="dashboard-room-row" key={room}>
                  <span className="sample-room-icon"><HomeOutlined /></span>
                  <strong>{room}</strong>
                  <span className={index === 3 ? 'sample-vacant' : 'sample-occupied'}>
                    {index === 3 ? t("Phòng trống") : t("Đang thuê")}
                  </span>
                </div>
              ))}
              <div className="dashboard-revenue">
                <span>{t("Doanh thu tháng")}</span>
                <strong>{formatVnd(42500000)}</strong>
              </div>
            </div>
            <SampleInvoice />
          </div>
        </div>
      </div>
      <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
    </figure>
  );
}

export function RoomsIllustration() {
  return (
    <figure className="story-art rooms-art">
      <div className="rooms-art-window">
        <div className="sample-panel-title">
          <strong>{t("Dãy trọ")} A</strong>
          <span>12 {t("Phòng")}</span>
        </div>
        <div className="sample-room-grid">
          {['P101', 'P102', 'P103', 'P104'].map((room, index) => (
            <div className={`sample-room-tile${index === 3 ? ' sample-room-tile--vacant' : ''}`} key={room}>
              <HomeOutlined />
              <strong>{room}</strong>
              <span>{index === 3 ? t("Phòng trống") : t("Đang thuê")}</span>
            </div>
          ))}
        </div>
        <div className="occupancy-caption">
          <span>{t("Tỷ lệ lấp đầy")}</span>
          <strong>92%</strong>
        </div>
        <div className="occupancy-track"><span /></div>
        <div className="rooms-deposit-strip">
          <div>
            <small>{t("Tiền cọc đang giữ")}</small>
            <strong>{formatVnd(33000000)}</strong>
          </div>
          <span className="rooms-deposit-badge"><SafetyCertificateOutlined />{t("Tách biệt doanh thu")}</span>
        </div>
      </div>
      <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
    </figure>
  );
}

export function BillingIllustration() {
  return (
    <figure className="story-art billing-art">
      <div className="billing-reading">
        <span className="billing-reading-icon"><ThunderboltOutlined /></span>
        <div>
          <span>{t("Chỉ số điện")}</span>
          <strong>1.240 <ArrowRightOutlined /> 1.364 <small>kWh</small></strong>
        </div>
        <CheckCircleFilled />
      </div>
      <SampleInvoice />
      <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
    </figure>
  );
}

export function ZaloAndReportsIllustration() {
  return (
    <figure className="story-art zalo-art">
      <div className="zalo-art-stack">
        <div className="zalo-preview-card">
          <div className="zalo-preview-header">
            <span className="zalo-preview-icon"><BellOutlined /></span>
            <div>
              <strong>{t("Nhắc hóa đơn qua Zalo")}</strong>
              <small>{t("Khung giờ gửi 07:00 – 21:00")}</small>
            </div>
            <span className="zalo-preview-status"><CheckCircleFilled />{t("Đã gửi")}</span>
          </div>
          <div className="zalo-preview-bubble">
            <p>{t("Chào bạn, hóa đơn P101 kỳ 10/2026 đã được phát hành. Tổng thanh toán:")} <strong>{formatVnd(4084000)}</strong>.</p>
            <div className="zalo-preview-link">
              <LockOutlined />
              <span>roomie.io.vn/t/p101-k10</span>
            </div>
          </div>
        </div>
        <div className="excel-preview-card">
          <div className="excel-preview-info">
            <span className="excel-preview-icon"><FileExcelOutlined /></span>
            <div>
              <strong>{t("Báo cáo & đối soát công nợ")}</strong>
              <small>{t("Tổng hợp tiền phòng, điện nước, tiền cọc theo kỳ")}</small>
            </div>
          </div>
          <span className="excel-preview-pill">.XLSX</span>
        </div>
      </div>
      <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
    </figure>
  );
}

export function TenantPhoneIllustration() {
  return (
    <figure className="story-art tenant-art">
      <div className="sample-phone">
        <div className="sample-phone-top" aria-hidden="true">
          <span>9:41</span>
          <span>••• ▰</span>
        </div>
        <div className="sample-phone-brand">
          <span className="brand-mark"><RoomieMark /></span>
          <strong>Roomie</strong>
          <LockOutlined />
        </div>
        <SampleInvoice phone />
        <div className="sample-phone-modes">
          <span><QrcodeOutlined /> {t("Chuyển khoản VietQR")}</span>
          <span><CheckCircleFilled /> {t("Tự động payOS")}</span>
        </div>
        <div className="sample-phone-private">
          <LockOutlined />{t("Liên kết riêng tư")}
        </div>
      </div>
      <figcaption>{t("Giao diện minh họa · Số liệu mẫu")}</figcaption>
    </figure>
  );
}

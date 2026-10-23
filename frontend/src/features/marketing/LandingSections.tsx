import {
  ArrowRightOutlined,
  BellOutlined,
  BookOutlined,
  CheckCircleFilled,
  CloseCircleOutlined,
  CrownOutlined,
  FileTextOutlined,
  HomeOutlined,
  QrcodeOutlined,
  SafetyCertificateOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { request } from '../../shared/http/client';
import { t } from '../../shared/i18n/runtime';

export function HeroHighlightsStrip() {
  return (
    <div className="hero-highlights">
      <div className="hero-highlight-item">
        <span className="hero-highlight-icon"><HomeOutlined /></span>
        <div>
          <strong>{t("Miễn phí tới 5 phòng")}</strong>
          <span>{t("Khởi tạo nhanh, không giới hạn thời gian")}</span>
        </div>
      </div>
      <div className="hero-highlight-item">
        <span className="hero-highlight-icon"><SafetyCertificateOutlined /></span>
        <div>
          <strong>{t("Khách không cần cài app")}</strong>
          <span>{t("Mở hóa đơn trực tiếp qua liên kết riêng tư")}</span>
        </div>
      </div>
      <div className="hero-highlight-item">
        <span className="hero-highlight-icon"><QrcodeOutlined /></span>
        <div>
          <strong>{t("VietQR & payOS linh hoạt")}</strong>
          <span>{t("Xác nhận thủ công hoặc gạch nợ tự động")}</span>
        </div>
      </div>
      <div className="hero-highlight-item">
        <span className="hero-highlight-icon"><BellOutlined /></span>
        <div>
          <strong>{t("Nhắc lịch Zalo văn minh")}</strong>
          <span>{t("Tự động gửi trong khung giờ 07:00 – 21:00")}</span>
        </div>
      </div>
    </div>
  );
}

export function ComparisonSection() {
  return (
    <section id="comparison" className="landing-comparison">
      <div className="landing-section-heading">
        <span className="section-eyebrow">{t("SO SÁNH HIỆU QUẢ")}</span>
        <h2>{t("Tạm biệt sổ tay, bảng tính rời rạc và tin nhắn thất lạc.")}</h2>
        <p>{t("Chuyển từ cách ghi chép thủ công dễ nhầm lẫn sang quy trình khép kín trên một nền tảng duy nhất.")}</p>
      </div>
      <div className="comparison-grid">
        <article className="comparison-card comparison-card--before">
          <div className="comparison-card-header">
            <span className="comparison-badge comparison-badge--muted">{t("Cách quản lý cũ")}</span>
            <h3>{t("Sổ tay, file Excel & nhắn Zalo rời rạc")}</h3>
          </div>
          <ul className="comparison-list">
            <li>
              <CloseCircleOutlined />
              <span>{t("Dễ nhầm chỉ số điện kỳ trước - kỳ này, phải tự cộng trừ tiền phòng và dịch vụ từng căn.")}</span>
            </li>
            <li>
              <CloseCircleOutlined />
              <span>{t("Chụp ảnh phiếu thu gửi từng khách qua Zalo, tốn nhiều giờ đối chiếu mỗi đầu tháng.")}</span>
            </li>
            <li>
              <CloseCircleOutlined />
              <span>{t("Lẫn lộn tiền cọc đang giữ với doanh thu thuê phòng; khó kiểm soát ai đã trả, ai còn nợ.")}</span>
            </li>
            <li>
              <CloseCircleOutlined />
              <span>{t("Khách cũ đã trả phòng nhưng vẫn giữ đường dẫn bảng tính hoặc ảnh chụp thông tin phòng.")}</span>
            </li>
          </ul>
        </article>

        <article className="comparison-card comparison-card--after">
          <div className="comparison-card-header">
            <span className="comparison-badge comparison-badge--brand">{t("Khi dùng Roomie")}</span>
            <h3>{t("Chuẩn hóa từ lúc chốt điện đến khi thu đủ tiền")}</h3>
          </div>
          <ul className="comparison-list">
            <li>
              <CheckCircleFilled />
              <span>{t("Tự động lấy chỉ số cũ, chặn nhập lùi công tơ và tính thành tiền chính xác ngay khi gõ.")}</span>
            </li>
            <li>
              <CheckCircleFilled />
              <span>{t("Tự động lên lịch nhắc hóa đơn qua Zalo kèm liên kết riêng tư trong khung giờ 07:00 – 21:00.")}</span>
            </li>
            <li>
              <CheckCircleFilled />
              <span>{t("Tách bạch sổ cái tiền cọc khỏi doanh thu; hỗ trợ cả chuyển khoản VietQR lẫn gạch nợ payOS.")}</span>
            </li>
            <li>
              <CheckCircleFilled />
              <span>{t("Quyết toán trả phòng chặt chẽ, tự động vô hiệu hóa liên kết của lượt thuê cũ ngay lập tức.")}</span>
            </li>
          </ul>
        </article>
      </div>
    </section>
  );
}

interface PricingSectionProps {
  destination: string;
  actionLabel: string;
  isAuthenticated: boolean;
  onCta: (ctaId: string, label: string) => void;
}

export function PricingSection({ destination, actionLabel, isAuthenticated, onCta }: PricingSectionProps) {
  return (
    <section id="pricing" className="landing-pricing">
      <div className="landing-section-heading">
        <span className="section-eyebrow">{t("BẢNG GIÁ MINH BẠCH")}</span>
        <h2>{t("Bắt đầu miễn phí. Nâng cấp khi nhà trọ mở rộng.")}</h2>
        <p>{t("Trải nghiệm đầy đủ mọi tính năng cốt lõi ngay từ gói Miễn phí. Chỉ nâng cấp khi quy mô vượt quá 5 phòng.")}</p>
      </div>

      <div className="pricing-grid">
        <article className="pricing-card">
          <div className="pricing-card-top">
            <span className="pricing-tier-badge">{t("Gói Miễn phí")}</span>
            <div className="pricing-price">
              <strong>{formatVnd(0)}</strong>
              <span>/ {t("tháng")}</span>
            </div>
            <p className="pricing-quota">{t("Tối đa 5 phòng đang sử dụng")}</p>
            <p className="pricing-desc">{t("Phù hợp cho chủ trọ quy mô nhỏ hoặc muốn dùng thử toàn bộ quy trình vận hành thực tế.")}</p>
          </div>
          <ul className="pricing-features">
            <li><CheckCircleFilled /><span>{t("Quản lý dãy trọ, phòng, người thuê và sổ cái tiền cọc")}</span></li>
            <li><CheckCircleFilled /><span>{t("Ghi chỉ số điện, lập nháp và phát hành hóa đơn hàng tháng")}</span></li>
            <li><CheckCircleFilled /><span>{t("Cấp liên kết hóa đơn riêng tư, người thuê xem không cần cài app")}</span></li>
            <li><CheckCircleFilled /><span>{t("Nhận thanh toán qua VietQR thủ công hoặc cổng tự động payOS")}</span></li>
            <li><CheckCircleFilled /><span>{t("Nhắc lịch hóa đơn qua Zalo và xuất báo cáo Excel (.xlsx)")}</span></li>
          </ul>
          <Link
            to={destination}
            className="landing-btn landing-btn-teal landing-btn-lg pricing-cta"
            onClick={() => onCta('cta_pricing_free', actionLabel)}
          >
            <span>{isAuthenticated ? actionLabel : t("Bắt đầu với gói Miễn phí")}</span>
            <ArrowRightOutlined aria-hidden="true" />
          </Link>
        </article>

        <article className="pricing-card pricing-card--featured">
          <div className="pricing-popular-tag"><CrownOutlined /> {t("Khuyên dùng cho chủ trọ nhiều phòng")}</div>
          <div className="pricing-card-top">
            <span className="pricing-tier-badge pricing-tier-badge--pro">{t("Gói Pro")}</span>
            <div className="pricing-price">
              <strong>{formatVnd(99000)}</strong>
              <span>/ {t("30 ngày")}</span>
            </div>
            <p className="pricing-quota pricing-quota--pro">{t("Không giới hạn số phòng và dãy trọ")}</p>
            <p className="pricing-desc">{t("Mở khóa toàn bộ giới hạn phòng cùng các gói kỳ hạn linh hoạt giúp tiết kiệm chi phí vận hành.")}</p>
          </div>

          <div className="pricing-packages" aria-label={t("Các kỳ hạn gói Pro")}>
            <div className="pricing-package-row">
              <div>
                <strong>{t("Gói Pro 1 tháng")}</strong>
                <small>{t("30 ngày sử dụng")}</small>
              </div>
              <strong className="pricing-package-amount">{formatVnd(99000)}</strong>
            </div>
            <div className="pricing-package-row">
              <div>
                <strong>{t("Gói Pro 3 tháng")}</strong>
                <span className="pricing-save-pill">{t("Tiết kiệm 10%")}</span>
              </div>
              <strong className="pricing-package-amount">{formatVnd(269000)}</strong>
            </div>
            <div className="pricing-package-row">
              <div>
                <strong>{t("Gói Pro 1 năm")}</strong>
                <span className="pricing-save-pill pricing-save-pill--best">{t("Tiết kiệm 25%")}</span>
              </div>
              <strong className="pricing-package-amount">{formatVnd(899000)}</strong>
            </div>
          </div>

          <ul className="pricing-features">
            <li><CheckCircleFilled /><span>{t("Bao gồm toàn bộ tính năng của gói Miễn phí")}</span></li>
            <li><CheckCircleFilled /><span>{t("Thêm không giới hạn số lượng dãy trọ và phòng đang thuê")}</span></li>
            <li><CheckCircleFilled /><span>{t("Hỗ trợ người thuê thanh toán gộp nhiều hóa đơn cùng lúc")}</span></li>
            <li><CheckCircleFilled /><span>{t("Hỗ trợ kỹ thuật ưu tiên trong suốt thời gian đăng ký")}</span></li>
          </ul>
          <Link
            to={destination}
            className="landing-btn landing-btn-light landing-btn-lg pricing-cta"
            onClick={() => onCta('cta_pricing_pro', actionLabel)}
          >
            <span>{isAuthenticated ? actionLabel : t("Đăng ký & nâng cấp Pro")}</span>
            <ArrowRightOutlined aria-hidden="true" />
          </Link>
        </article>
      </div>
    </section>
  );
}

export function FaqSection() {
  const faqs = [
    {
      q: t("Người thuê phòng có cần tải ứng dụng hay tạo tài khoản không?"),
      a: t("Không. Chủ trọ chỉ cần chia sẻ liên kết riêng tư của phòng hoặc bật nhắc hóa đơn qua Zalo. Người thuê mở trực tiếp trên trình duyệt điện thoại để xem chi tiết tiền thuê, chỉ số điện nước và quét mã QR thanh toán."),
    },
    {
      q: t("Tôi muốn nhận tiền vào tài khoản ngân hàng cá nhân mà không qua payOS có được không?"),
      a: t("Hoàn toàn được. Roomie hỗ trợ 2 chế độ: Chế độ VietQR thủ công tạo mã QR chuyển khoản thẳng vào tài khoản ngân hàng của bạn để bạn đối chiếu và bấm xác nhận 1 chạm; hoặc Chế độ tự động qua payOS để hệ thống tự gạch nợ ngay khi giao dịch hoàn tất."),
    },
    {
      q: t("Gói Miễn phí giới hạn như thế nào và khi nào tôi cần nâng cấp lên gói Pro?"),
      a: t("Sau khi đăng ký và xác thực OTP qua email, tài khoản chủ trọ được kích hoạt ngay gói Miễn phí với đầy đủ tính năng cho tối đa 5 phòng đang sử dụng. Khi bạn quản lý từ phòng thứ 6 trở lên, bạn có thể nâng cấp gói Pro theo kỳ hạn 1 tháng, 3 tháng hoặc 1 năm."),
    },
    {
      q: t("Roomie quản lý tiền cọc và quy trình trả phòng ra sao?"),
      a: t("Tiền cọc được theo dõi trên sổ cái riêng (thu cọc, khấu trừ vào hóa đơn hoặc hoàn cọc), không bị cộng lẫn vào doanh thu tiền thuê. Khi khách trả phòng, hệ thống kiểm tra công nợ, chốt điện cuối và tự động thu hồi liên kết của lượt thuê cũ."),
    },
    {
      q: t("Tính năng nhắc hóa đơn qua Zalo hoạt động như thế nào để không làm phiền khách?"),
      a: t("Khi phát hành hóa đơn hoặc đến lịch hẹn hàng tháng, Roomie gửi tin nhắn Zalo dẫn về liên kết xem hóa đơn của khách. Hệ thống chỉ gửi tin trong khung giờ văn minh từ 07:00 đến trước 21:00 và có cơ chế khóa chống gửi trùng lặp."),
    },
    {
      q: t("Khi tôi thay đổi giá thuê hoặc đơn giá điện nước thì hóa đơn cũ có bị nhảy số không?"),
      a: t("Không. Mỗi hóa đơn khi phát hành đều lưu bản chụp cố định (snapshot) của giá phòng, đơn giá điện nước, số người và chỉ số công tơ tại kỳ đó. Việc cập nhật giá mới cho phòng sẽ không làm biến đổi lịch sử các hóa đơn đã phát hành."),
    },
  ];

  return (
    <section id="faq" className="landing-faq">
      <div className="landing-section-heading">
        <span className="section-eyebrow">{t("CÂU HỎI THƯỜNG GẶP")}</span>
        <h2>{t("Giải đáp rõ ràng trước khi bạn bắt đầu.")}</h2>
        <p>{t("Mọi quy tắc vận hành trên Roomie đều được thiết kế sát với thực tế quản lý nhà trọ tại Việt Nam.")}</p>
      </div>
      <div className="faq-list">
        {faqs.map((item, idx) => (
          <details className="faq-item" key={item.q} open={idx === 0}>
            <summary className="faq-question">
              <span>{item.q}</span>
              <span className="faq-toggle" aria-hidden="true" />
            </summary>
            <div className="faq-answer">
              <p>{item.a}</p>
            </div>
          </details>
        ))}
      </div>
    </section>
  );
}

type PublicGuideSummaryItem = {
  slug: string;
  title: string;
  category: string;
  readTimeLabel?: string;
  summary: string;
  publishedAt?: string | null;
};

export function GuidesSection() {
  const [cmsGuides, setCmsGuides] = useState<PublicGuideSummaryItem[]>([]);

  const guides = [
    {
      index: '01',
      slug: 'quan-ly-phong-tien-coc-va-tra-phong',
      icon: <HomeOutlined />,
      category: t("Phòng & người thuê"),
      title: t("Quy trình quản lý phòng, tiền cọc và trả phòng"),
      summary: t("Cách tổ chức dãy trọ, tiếp nhận người thuê mới, theo dõi sổ cái tiền cọc riêng biệt và quyết toán trả phòng an toàn."),
      steps: [
        t("Tạo dãy trọ và thiết lập đơn giá thuê, giá điện, nước, dịch vụ mặc định cho từng phòng."),
        t("Khi khách nhận phòng: nhập chỉ số công tơ điện bàn giao và số tiền cọc yêu cầu trên sổ cái riêng."),
        t("Khi khách trả phòng: đối chiếu công nợ, khấu trừ hoặc hoàn cọc, chốt điện cuối và tự động thu hồi liên kết cũ."),
      ],
    },
    {
      index: '02',
      slug: 'cach-chot-chi-so-dien-va-lap-hoa-don',
      icon: <ThunderboltOutlined />,
      category: t("Lập hóa đơn"),
      title: t("Cách chốt chỉ số điện và lập hóa đơn hàng tháng"),
      summary: t("Quy trình ghi chỉ số công tơ nhanh theo dãy, kiểm tra chênh lệch kWh và phát hành hóa đơn minh bạch."),
      steps: [
        t("Chọn kỳ thanh toán (tháng/năm): hệ thống tự động điền sẵn chỉ số điện kỳ trước của từng phòng đang thuê."),
        t("Nhập chỉ số điện mới: bộ kiểm tra tự động chặn nhập lùi chỉ số và tính ngay sản lượng tiêu thụ (kWh)."),
        t("Lưu nháp để rà soát hoặc phát hành hàng loạt: hóa đơn phát hành lưu cố định đơn giá lịch sử của kỳ đó."),
      ],
    },
    {
      index: '03',
      slug: 'quan-ly-cong-no-ghi-thu-va-xuat-excel',
      icon: <FileTextOutlined />,
      category: t("Báo cáo"),
      title: t("Quản lý công nợ, ghi thu và xuất báo cáo Excel"),
      summary: t("Theo dõi sát các hóa đơn chưa thu, đối soát khoản chuyển khoản của khách và xuất sổ kế toán cuối tháng."),
      steps: [
        t("Lọc danh sách hóa đơn theo dãy trọ, kỳ tháng hoặc trạng thái Chưa thanh toán / Đã thanh toán."),
        t("Ghi nhận khoản thu thủ công bằng toàn bộ dư nợ hoặc duyệt yêu cầu chuyển khoản VietQR từ người thuê."),
        t("Xuất file báo cáo Excel (.xlsx) giữ nguyên định dạng số tiền để lưu trữ và tổng kết doanh thu."),
      ],
    },
    {
      index: '04',
      slug: 'thu-tien-phong-qua-ma-qr-va-nhac-zalo',
      icon: <QrcodeOutlined />,
      category: t("Cài đặt thanh toán"),
      title: t("Thu tiền phòng qua mã QR & nhắc lịch Zalo tự động"),
      summary: t("Thiết lập tài khoản nhận tiền VietQR hoặc payOS, cấp liên kết cho khách và bật lịch nhắc Zalo đúng giờ."),
      steps: [
        t("Tại mục Cài đặt thanh toán: chọn chế độ VietQR thủ công (ngân hàng cá nhân) hoặc kết nối cổng tự động payOS."),
        t("Chia sẻ liên kết riêng tư (/t/token) cho người thuê hoặc kết nối Zalo để gửi tin nhắn nhắc tự động."),
        t("Người thuê mở liên kết trên điện thoại, xem chi tiết từng khoản và quét mã QR thanh toán không cần cài app."),
      ],
    },
  ];

  useEffect(() => {
    let active = true;
    request<PublicGuideSummaryItem[]>('/api/public/guides')
      .then((items) => {
        if (!active || !Array.isArray(items)) return;
        const staticSlugs = new Set(guides.map((g) => g.slug));
        setCmsGuides(items.filter((item) => item.slug && !staticSlugs.has(item.slug)));
      })
      .catch(() => {
        // Keep static guides when offline or during static prerender
      });
    return () => {
      active = false;
    };
  }, []);

  return (
    <section id="guides-list" className="landing-guides">
      <div className="landing-section-heading">
        <span className="section-eyebrow"><BookOutlined /> {t("CẨM NANG CHỦ TRỌ")}</span>
        <h2>{t("4 quy trình cốt lõi vận hành nhà trọ trên Roomie.")}</h2>
        <p>{t("Hướng dẫn chi tiết từng bước từ lúc thiết lập phòng đầu tiên đến khi chốt điện, nhắc nợ Zalo và đối soát cuối tháng.")}</p>
      </div>
      <div className="guides-grid">
        {guides.map((guide) => (
          <article className="guide-card" key={guide.index}>
            <div className="guide-card-top">
              <span className="guide-index">{guide.index} / {guide.icon} {guide.category}</span>
              <h3>
                <Link to={`/huong-dan/${guide.slug}`} style={{ color: 'inherit', textDecoration: 'none' }}>
                  {guide.title}
                </Link>
              </h3>
              <p>{guide.summary}</p>
            </div>
            <ol className="guide-steps">
              {guide.steps.map((step, i) => (
                <li key={step}>
                  <span>{i + 1}</span>
                  <p>{step}</p>
                </li>
              ))}
            </ol>
            <div style={{ marginTop: 16 }}>
              <Link to={`/huong-dan/${guide.slug}`} className="landing-text-link">
                {t("Đọc bài viết chi tiết")} <ArrowRightOutlined />
              </Link>
            </div>
          </article>
        ))}
      </div>

      {cmsGuides.length > 0 && (
        <div style={{ marginTop: 40 }}>
          <div className="landing-section-heading" style={{ marginBottom: 20 }}>
            <span className="section-eyebrow"><BookOutlined /> {t("BÀI VIẾT CHUYÊN SÂU")}</span>
            <h3>{t("Kinh nghiệm quản lý & tối ưu vận hành nhà trọ")}</h3>
          </div>
          <div className="guides-grid">
            {cmsGuides.map((article, idx) => (
              <article className="guide-card" key={article.slug}>
                <div className="guide-card-top">
                  <span className="guide-index">
                    {String(idx + 5).padStart(2, '0')} / <BookOutlined /> {article.category}
                    {article.readTimeLabel ? ` · ${article.readTimeLabel}` : ''}
                  </span>
                  <h3>
                    <Link to={`/huong-dan/${article.slug}`} style={{ color: 'inherit', textDecoration: 'none' }}>
                      {article.title}
                    </Link>
                  </h3>
                  <p>{article.summary}</p>
                </div>
                <div style={{ marginTop: 16 }}>
                  <Link to={`/huong-dan/${article.slug}`} className="landing-text-link">
                    {t("Đọc bài viết chi tiết")} <ArrowRightOutlined />
                  </Link>
                </div>
              </article>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

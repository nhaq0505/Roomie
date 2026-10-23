import { ArrowLeftOutlined, ArrowRightOutlined, BookOutlined, ClockCircleOutlined, OrderedListOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Button, Spin, Tag } from 'antd';
import { Link, useNavigate, useParams } from 'react-router-dom';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import { post, request } from '../../shared/http/client';
import './LandingPage.css';
import { Brand } from './LandingVisuals';

type PublicGuideArticle = {
  id?: string;
  slug: string;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: number;
  contentMarkdown: string;
  ctaText?: string | null;
  ctaUrl?: string | null;
  authorName: string;
  publishedAt?: string | null;
};

function getStaticFallbackGuides(): Record<string, PublicGuideArticle> {
  return {
    'quan-ly-phong-tien-coc-va-tra-phong': {
      slug: 'quan-ly-phong-tien-coc-va-tra-phong',
      title: t("Quy trình quản lý phòng, tiền cọc và trả phòng"),
      summary: t("Cách tổ chức dãy trọ, tiếp nhận người thuê mới, theo dõi sổ cái tiền cọc riêng biệt và quyết toán trả phòng an toàn."),
      category: t("Phòng & người thuê"),
      readingTimeMinutes: 5,
      authorName: 'Roomie Marketing',
      ctaText: t("Dùng thử miễn phí cho 5 phòng"),
      ctaUrl: '/register?utm_source=guide&utm_medium=seo&utm_campaign=quan_ly_phong_coc',
      contentMarkdown: [
        `## 1. ${t("Tạo dãy trọ và thiết lập đơn giá thuê, giá điện, nước, dịch vụ mặc định cho từng phòng.")}`,
        `- ${t("Khi khách nhận phòng: nhập chỉ số công tơ điện bàn giao và số tiền cọc yêu cầu trên sổ cái riêng.")}`,
        `## 2. ${t("Khi khách trả phòng: đối chiếu công nợ, khấu trừ hoặc hoàn cọc, chốt điện cuối và tự động thu hồi liên kết cũ.")}`,
      ].join('\n\n'),
    },
    'cach-chot-chi-so-dien-va-lap-hoa-don': {
      slug: 'cach-chot-chi-so-dien-va-lap-hoa-don',
      title: t("Cách chốt chỉ số điện và lập hóa đơn hàng tháng"),
      summary: t("Quy trình ghi chỉ số công tơ nhanh theo dãy, kiểm tra chênh lệch kWh và phát hành hóa đơn minh bạch."),
      category: t("Lập hóa đơn"),
      readingTimeMinutes: 4,
      authorName: 'Roomie Marketing',
      ctaText: t("Dùng thử miễn phí cho 5 phòng"),
      ctaUrl: '/register?utm_source=guide&utm_medium=seo&utm_campaign=chot_dien_hoa_don',
      contentMarkdown: [
        `## 1. ${t("Chọn kỳ thanh toán (tháng/năm): hệ thống tự động điền sẵn chỉ số điện kỳ trước của từng phòng đang thuê.")}`,
        `- ${t("Nhập chỉ số điện mới: bộ kiểm tra tự động chặn nhập lùi chỉ số và tính ngay sản lượng tiêu thụ (kWh).")}`,
        `## 2. ${t("Lưu nháp để rà soát hoặc phát hành hàng loạt: hóa đơn phát hành lưu cố định đơn giá lịch sử của kỳ đó.")}`,
      ].join('\n\n'),
    },
    'quan-ly-cong-no-ghi-thu-va-xuat-excel': {
      slug: 'quan-ly-cong-no-ghi-thu-va-xuat-excel',
      title: t("Quản lý công nợ, ghi thu và xuất báo cáo Excel"),
      summary: t("Theo dõi sát các hóa đơn chưa thu, đối soát khoản chuyển khoản của khách và xuất sổ kế toán cuối tháng."),
      category: t("Báo cáo"),
      readingTimeMinutes: 5,
      authorName: 'Roomie Marketing',
      ctaText: t("Dùng thử miễn phí cho 5 phòng"),
      ctaUrl: '/register?utm_source=guide&utm_medium=seo&utm_campaign=bao_cao_excel',
      contentMarkdown: [
        `## 1. ${t("Lọc danh sách hóa đơn theo dãy trọ, kỳ tháng hoặc trạng thái Chưa thanh toán / Đã thanh toán.")}`,
        `- ${t("Ghi nhận khoản thu thủ công bằng toàn bộ dư nợ hoặc duyệt yêu cầu chuyển khoản VietQR từ người thuê.")}`,
        `## 2. ${t("Xuất file báo cáo Excel (.xlsx) giữ nguyên định dạng số tiền để lưu trữ và tổng kết doanh thu.")}`,
      ].join('\n\n'),
    },
    'thu-tien-phong-qua-ma-qr-va-nhac-zalo': {
      slug: 'thu-tien-phong-qua-ma-qr-va-nhac-zalo',
      title: t("Thu tiền phòng qua mã QR & nhắc lịch Zalo tự động"),
      summary: t("Thiết lập tài khoản nhận tiền VietQR hoặc payOS, cấp liên kết cho khách và bật lịch nhắc Zalo đúng giờ."),
      category: t("Cài đặt thanh toán"),
      readingTimeMinutes: 6,
      authorName: 'Roomie Marketing',
      ctaText: t("Dùng thử miễn phí cho 5 phòng"),
      ctaUrl: '/register?utm_source=guide&utm_medium=seo&utm_campaign=vietqr_payos_zalo',
      contentMarkdown: [
        `## 1. ${t("Tại mục Cài đặt thanh toán: chọn chế độ VietQR thủ công (ngân hàng cá nhân) hoặc kết nối cổng tự động payOS.")}`,
        `- ${t("Chia sẻ liên kết riêng tư (/t/token) cho người thuê hoặc kết nối Zalo để gửi tin nhắn nhắc tự động.")}`,
        `## 2. ${t("Người thuê mở liên kết trên điện thoại, xem chi tiết từng khoản và quét mã QR thanh toán không cần cài app.")}`,
      ].join('\n\n'),
    },
  };
}

function extractHeadings(markdown: string): { id: string; text: string }[] {
  const headings: { id: string; text: string }[] = [];
  let hIdx = 0;
  markdown.split(/\r?\n/).forEach((rawLine) => {
    const line = rawLine.trim();
    if (line.startsWith('## ')) {
      headings.push({ id: `section-${hIdx++}`, text: line.slice(3) });
    }
  });
  return headings;
}

function renderMarkdownBlocks(markdown: string) {
  const lines = markdown.split(/\r?\n/);
  const elements: React.ReactNode[] = [];
  let listItems: string[] = [];
  let hIdx = 0;

  const flushList = (keyPrefix: number) => {
    if (listItems.length > 0) {
      elements.push(
        <ul key={`list-${keyPrefix}`} style={{ paddingLeft: 22, marginBottom: 16 }}>
          {listItems.map((li, idx) => (
            <li key={idx} style={{ marginBottom: 6 }}>
              {li}
            </li>
          ))}
        </ul>
      );
      listItems = [];
    }
  };

  lines.forEach((rawLine, idx) => {
    const line = rawLine.trim();
    if (!line) {
      flushList(idx);
      return;
    }
    if (line.startsWith('## ')) {
      flushList(idx);
      const secId = `section-${hIdx++}`;
      elements.push(
        <h2 id={secId} key={`h2-${idx}`}>
          {line.slice(3)}
        </h2>
      );
    } else if (line.startsWith('### ')) {
      flushList(idx);
      elements.push(<h3 key={`h3-${idx}`}>{line.slice(4)}</h3>);
    } else if (line.startsWith('- ') || line.startsWith('* ')) {
      listItems.push(line.slice(2));
    } else {
      flushList(idx);
      elements.push(<p key={`p-${idx}`}>{line}</p>);
    }
  });
  flushList(lines.length);
  return elements;
}

export default function GuideArticlePage() {
  const { slug = '' } = useParams<{ slug: string }>();
  const navigate = useNavigate();

  const query = useQuery({
    queryKey: ['public-guide', slug],
    queryFn: () => request<PublicGuideArticle>(`/api/public/guides/${encodeURIComponent(slug)}`),
    enabled: Boolean(slug),
    retry: false,
  });

  const validApiArticle =
    query.data && typeof query.data.title === 'string' && typeof query.data.contentMarkdown === 'string'
      ? query.data
      : undefined;
  const article = validApiArticle ?? getStaticFallbackGuides()[slug.toLowerCase()];
  const headings = article ? extractHeadings(article.contentMarkdown) : [];

  const handleCtaClick = () => {
    if (!article) return;
    void request(`/api/public/guides/${encodeURIComponent(article.slug)}/cta-click`, post({})).catch(() => {});
    const target = article.ctaUrl || '/register';
    if (target.startsWith('http://') || target.startsWith('https://')) {
      window.location.href = target;
    } else {
      navigate(target);
    }
  };

  return (
    <div className="landing-page" style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <header className="landing-header">
        <div className="landing-header-inner">
          <Brand />
          <nav className="landing-nav" aria-label={t("Điều hướng chính")}>
            <Link to="/">{t("Trang chủ")}</Link>
            <Link to="/features">{t("Tính năng")}</Link>
            <Link to="/pricing">{t("Bảng giá")}</Link>
            <Link to="/huong-dan" className="is-active">
              {t("Hướng dẫn")}
            </Link>
          </nav>
          <div className="landing-header-actions">
            <LanguageSelector />
            <Link to="/register" className="landing-btn landing-btn-light">
              <span>{t("Đăng ký")}</span>
            </Link>
          </div>
        </div>
      </header>

      <main className="guide-article-shell">
        <div style={{ marginBottom: 20 }}>
          <Link to="/huong-dan" style={{ color: '#0f766e', fontWeight: 600, textDecoration: 'none' }}>
            <ArrowLeftOutlined style={{ marginRight: 6 }} />
            {t("Quay lại Cẩm nang hướng dẫn")}
          </Link>
        </div>

        {query.isLoading && !article ? (
          <div style={{ textAlign: 'center', padding: 64 }}>
            <Spin size="large" />
          </div>
        ) : !article ? (
          <div className="guide-article-card" style={{ textAlign: 'center' }}>
            <h2>{t("Không tìm thấy bài viết hướng dẫn")}</h2>
            <p>{t("Bài viết này có thể đã được cập nhật hoặc chuyển sang đường dẫn khác.")}</p>
            <Button type="primary" onClick={() => navigate('/huong-dan')}>
              {t("Xem toàn bộ Cẩm nang")}
            </Button>
          </div>
        ) : (
          <article className="guide-article-card">
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 14, flexWrap: 'wrap' }}>
              <Tag color="cyan">
                <BookOutlined style={{ marginRight: 4 }} />
                {article.category}
              </Tag>
              <span style={{ fontSize: 13, color: '#64748b' }}>
                <ClockCircleOutlined style={{ marginRight: 4 }} />
                {article.readingTimeMinutes} {t("phút đọc")}
              </span>
            </div>

            <h1 style={{ fontSize: 30, fontWeight: 800, color: '#0f292d', lineHeight: 1.3, marginBottom: 16 }}>
              {article.title}
            </h1>

            <p style={{ fontSize: 17, color: '#475569', lineHeight: 1.65, fontWeight: 500, marginBottom: 20 }}>
              {article.summary}
            </p>

            {headings.length > 0 && (
              <nav
                aria-label={t("Mục lục bài viết")}
                style={{
                  background: '#f1f5f9',
                  borderRadius: 10,
                  padding: '12px 16px',
                  marginBottom: 20,
                  border: '1px solid #e2e8f0',
                }}
              >
                <div style={{ fontWeight: 700, fontSize: 13, color: '#0f292d', marginBottom: 8 }}>
                  <OrderedListOutlined style={{ marginRight: 6, color: '#0f766e' }} />
                  {t("Mục lục bài viết")}
                </div>
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {headings.map((h) => (
                    <li key={h.id} style={{ marginBottom: 4, fontSize: 14 }}>
                      <a href={`#${h.id}`} style={{ color: '#0f766e', textDecoration: 'none' }}>
                        {h.text}
                      </a>
                    </li>
                  ))}
                </ul>
              </nav>
            )}

            <hr style={{ border: 'none', borderTop: '1px solid #e2e8f0', margin: '24px 0' }} />

            <div className="guide-article-body">{renderMarkdownBlocks(article.contentMarkdown)}</div>

            <div className="guide-article-cta">
              <div style={{ flex: 1, minWidth: 220 }}>
                <div style={{ fontWeight: 700, fontSize: 17, color: '#0f292d' }}>
                  {t("Sẵn sàng tự động hóa quản lý nhà trọ cùng Roomie?")}
                </div>
                <div style={{ fontSize: 14, color: '#334155', marginTop: 4 }}>
                  {t("Miễn phí trọn đời cho tối đa 5 phòng đang sử dụng. Chốt điện, lập hóa đơn VietQR và nhắc lịch Zalo tự động.")}
                </div>
              </div>
              <Button
                type="primary"
                size="large"
                icon={<ArrowRightOutlined />}
                onClick={handleCtaClick}
                style={{ background: '#0f766e', borderColor: '#0f766e', fontWeight: 600 }}
              >
                {article.ctaText || t("Dùng thử miễn phí cho 5 phòng")}
              </Button>
            </div>
          </article>
        )}
      </main>
    </div>
  );
}

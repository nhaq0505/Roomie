import { t } from '../i18n/runtime';
import { Alert, Button, Empty, Result, Space, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';

export function PageTitle({ eyebrow, title, subtitle, extra }: { eyebrow?: string; title: string; subtitle?: string; extra?: ReactNode }) {
  return <div className="page-heading"><div><div className="eyebrow">{eyebrow}</div><Typography.Title level={2}>{title}</Typography.Title>{subtitle && <Typography.Paragraph className="page-subtitle">{subtitle}</Typography.Paragraph>}</div>{extra && <div className="page-heading-extra">{extra}</div>}</div>;
}

export function LoadError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  const message = error instanceof Error ? error.message : t("Đã có lỗi khi tải dữ liệu.");
  return <Alert type="error" showIcon message={t("Chưa tải được dữ liệu")} description={message} action={<Button size="small" icon={<ReloadOutlined />} onClick={onRetry}>{t("Thử lại")}</Button>} />;
}

export function EmptyPanel({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div className="empty-panel"><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={<span><strong>{title}</strong>{description && <span className="empty-description">{description}</span>}</span>} />{action && <div className="empty-action">{action}</div>}</div>;
}

export function FullPageError({ title, subTitle, onBack }: { title: string; subTitle?: string; onBack?: ReactNode }) {
  return <div className="center-page"><Result status="404" title={title} subTitle={subTitle} extra={<Space>{onBack}</Space>} /></div>;
}

export function SectionCard({ children, title, extra, className = '' }: { children: ReactNode; title?: string; extra?: ReactNode; className?: string }) {
  return <section className={`section-card ${className}`}><div className="section-card-header">{title && <Typography.Title level={4}>{title}</Typography.Title>}{extra}</div>{children}</section>;
}

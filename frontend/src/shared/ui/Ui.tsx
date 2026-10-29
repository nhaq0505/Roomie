import { t } from '../i18n/runtime';
import { Alert, Button, Empty, Result, Space, Typography } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';

export interface WorkspaceSubTabItem {
  key: string;
  to: string;
  icon?: ReactNode;
  label: ReactNode;
  badge?: ReactNode;
  active: boolean;
}

export function WorkspaceSubTabs({ items, ariaLabel, extra }: { items: WorkspaceSubTabItem[]; ariaLabel: string; extra?: ReactNode }) {
  return (
    <div className="workspace-subtabs-bar">
      <nav className="workspace-subtabs" aria-label={ariaLabel}>
        {items.map((item) => (
          <Link
            key={item.key}
            to={item.to}
            className={`workspace-subtab ${item.active ? 'is-active' : ''}`}
            aria-current={item.active ? 'page' : undefined}
          >
            {item.icon && <span className="workspace-subtab-icon" aria-hidden="true">{item.icon}</span>}
            <span className="workspace-subtab-label">{item.label}</span>
            {item.badge !== undefined && item.badge !== null && (
              <span className="workspace-subtab-badge">{item.badge}</span>
            )}
          </Link>
        ))}
      </nav>
      {extra && <div className="workspace-subtabs-extra">{extra}</div>}
    </div>
  );
}

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

import { Alert, Card, Space, Tag, Tooltip } from 'antd';
import { ClockCircleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import { t } from '../../../shared/i18n/runtime';
import type { HealthStatus } from './types';

export function UnknownMetric({ explanation }: { explanation?: string }) {
  const content = (
    <span className="unknown-metric-value" style={{ color: '#8c9ea2', fontStyle: 'normal' }}>
      — <small style={{ fontSize: '12px', fontWeight: 400, color: '#a0aeb2' }}>({t("Chưa xác định")})</small>
    </span>
  );
  if (!explanation) return content;
  return (
    <Tooltip title={explanation}>
      <span style={{ cursor: 'help', display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        {content}
        <QuestionCircleOutlined style={{ fontSize: 13, color: '#8c9ea2' }} />
      </span>
    </Tooltip>
  );
}

export function StaleIndicator({ ageText }: { ageText: string }) {
  return (
    <Tag color="warning" icon={<ClockCircleOutlined style={{ marginRight: 4 }} />} className="stale-tag">
      {t("Dữ liệu cũ ({0})", [ageText])}
    </Tag>
  );
}

export function UnconfiguredNotice() {
  return (
    <Alert
      className="monitoring-unconfigured-notice"
      type="info"
      showIcon
      message={<strong>{t("Chờ kết nối backend (Task T2)")}</strong>}
      description={t(
        "Giao diện giám sát đã sẵn sàng. Dữ liệu thực tế sẽ được kết nối sau khi hoàn thiện hợp đồng API tại Task T2."
      )}
      style={{ marginBottom: 20, borderRadius: 12 }}
    />
  );
}

export function MonitoringStatCard({
  title,
  value,
  unit,
  subtext,
  icon,
  colorClass = 'stat-teal',
  isStale = false,
  staleAgeText,
  explanation,
}: {
  title: string;
  value: ReactNode | null | undefined;
  unit?: string;
  subtext?: ReactNode;
  icon?: ReactNode;
  colorClass?: string;
  isStale?: boolean;
  staleAgeText?: string;
  explanation?: string;
}) {
  return (
    <Card className="stat-card monitoring-stat-card" bordered={false}>
      {icon && <div className={`stat-icon ${colorClass}`}>{icon}</div>}
      <div className="ant-statistic-title">
        <Space size={6}>
          <span>{title}</span>
          {isStale && staleAgeText && <StaleIndicator ageText={staleAgeText} />}
        </Space>
      </div>
      <div className="ant-statistic-content">
        {value === null || value === undefined ? (
          <UnknownMetric explanation={explanation} />
        ) : (
          <span className="ant-statistic-content-value">
            {value}
            {unit && <span className="stat-unit">{unit}</span>}
          </span>
        )}
      </div>
      {subtext && <div className="stat-foot">{subtext}</div>}
    </Card>
  );
}

export function HealthTag({ status }: { status: HealthStatus }) {
  switch (status) {
    case 'healthy':
      return <Tag color="success">{t("Bình thường")}</Tag>;
    case 'warning':
      return <Tag color="warning">{t("Cảnh báo")}</Tag>;
    case 'critical':
      return <Tag color="error">{t("Nghiêm trọng")}</Tag>;
    case 'stale':
      return <Tag color="orange">{t("Chưa xác định")}</Tag>;
    case 'unknown':
    default:
      return <Tag color="default">{t("Chưa xác định")}</Tag>;
  }
}

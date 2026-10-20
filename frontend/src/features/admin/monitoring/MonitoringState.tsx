import { Alert, Button, Card, Empty, Skeleton, Space, Tag, Tooltip } from 'antd';
import { ClockCircleOutlined, QuestionCircleOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import { t } from '../../../shared/i18n/runtime';
import type { Cac30NullReason, HealthStatus } from './types';

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

export function StaleIndicator({ ageText }: { ageText?: string }) {
  return (
    <Tag color="warning" icon={<ClockCircleOutlined style={{ marginRight: 4 }} />} className="stale-tag">
      {ageText ? t("Dữ liệu cũ ({0})", [ageText]) : t("Dữ liệu cũ")}
    </Tag>
  );
}

export function UnconfiguredNotice({
  message,
  description,
}: {
  message?: ReactNode;
  description?: ReactNode;
}) {
  return (
    <Alert
      className="monitoring-unconfigured-notice"
      type="info"
      showIcon
      message={message ?? <strong>{t("Chờ kết nối backend (Task T2)")}</strong>}
      description={
        description ??
        t(
          "Giao diện giám sát đã sẵn sàng. Dữ liệu thực tế sẽ được kết nối sau khi hoàn thiện hợp đồng API tại Task T2."
        )
      }
      style={{ marginBottom: 20, borderRadius: 12 }}
    />
  );
}

export function MonitoringErrorState({
  error,
  onRetry,
}: {
  error?: Error | unknown;
  onRetry?: () => void;
}) {
  const message = error instanceof Error ? error.message : t("Không thể tải dữ liệu.");
  return (
    <Alert
      type="error"
      showIcon
      message={<strong>{t("Lỗi tải dữ liệu")}</strong>}
      description={
        <div style={{ marginTop: 6 }}>
          <p style={{ marginBottom: 8 }}>{message}</p>
          {onRetry && (
            <Button size="small" icon={<ReloadOutlined />} onClick={onRetry}>
              {t("Thử lại")}
            </Button>
          )}
        </div>
      }
      style={{ marginBottom: 20, borderRadius: 12 }}
    />
  );
}

export function MonitoringEmptyState({ description }: { description?: string }) {
  return (
    <div style={{ padding: '32px 0' }}>
      <Empty description={description ?? t("Chưa có dữ liệu")} />
    </div>
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
  isLoading = false,
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
  isLoading?: boolean;
}) {
  return (
    <Card className="stat-card monitoring-stat-card" bordered={false}>
      {icon && <div className={`stat-icon ${colorClass}`}>{icon}</div>}
      <div className="ant-statistic-title">
        <Space size={6}>
          <span>{title}</span>
          {isStale && <StaleIndicator ageText={staleAgeText} />}
        </Space>
      </div>
      <div className="ant-statistic-content">
        {isLoading ? (
          <Skeleton.Input active size="small" style={{ width: 120, height: 28 }} />
        ) : value === null || value === undefined ? (
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

export function HealthTag({ status }: { status?: HealthStatus | string | null }) {
  const norm = (status || 'unknown').toLowerCase();
  switch (norm) {
    case 'healthy':
    case 'ok':
      return <Tag color="success">{t("Bình thường")}</Tag>;
    case 'warning':
    case 'degraded':
      return <Tag color="warning">{t("Cảnh báo")}</Tag>;
    case 'critical':
    case 'failed':
      return <Tag color="error">{t("Nghiêm trọng")}</Tag>;
    case 'stale':
      return <Tag color="orange">{t("Chưa xác định")}</Tag>;
    case 'unknown':
    default:
      return <Tag color="default">{t("Chưa xác định")}</Tag>;
  }
}

export function CacNullReasonTag({ reason }: { reason?: Cac30NullReason | null }) {
  if (!reason) return <UnknownMetric />;
  switch (reason) {
    case 'MissingCostData':
    case 'missing_spend':
      return <Tag color="warning">{t("Chưa nhập chi phí")}</Tag>;
    case 'ZeroDenominator':
    case 'zero_cohort':
      return <Tag color="default">{t("Chưa có chủ trọ trả phí")}</Tag>;
    case 'CohortImmature':
    case 'unmature_cohort':
      return <Tag color="blue">{t("Chưa đủ 30 ngày trưởng thành")}</Tag>;
    case 'NoRegistrations':
      return <Tag color="default">{t("Chưa có chủ trọ trả phí")}</Tag>;
    default:
      return <UnknownMetric explanation={String(reason)} />;
  }
}

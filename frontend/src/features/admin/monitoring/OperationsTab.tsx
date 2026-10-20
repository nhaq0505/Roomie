import {
  AlertOutlined,
  CheckCircleOutlined,
  CloudServerOutlined,
  HddOutlined,
  MailOutlined,
  SafetyCertificateOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import { Badge, Card, Col, Progress, Row, Space, Table, Tag } from 'antd';
import type { TableProps } from 'antd';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import { HealthTag, StaleIndicator, UnknownMetric } from './MonitoringState';
import { NativeLatencyHistogram } from './NativeCharts';
import type {
  BackgroundWorkerItem,
  BackupWatchdogMetric,
  DatabaseHealthMetric,
  IncidentAlertItem,
  PayOsReconcileMetrics,
  RouteMetricItem,
  SmtpMetrics,
  SystemdHostMetrics,
  ZaloQueueMetrics,
} from './types';

interface OperationsTabProps {
  host?: SystemdHostMetrics;
  db?: DatabaseHealthMetric;
  routes?: RouteMetricItem[];
  smtp?: SmtpMetrics;
  payOs?: PayOsReconcileMetrics;
  zalo?: ZaloQueueMetrics;
  workers?: BackgroundWorkerItem[];
  watchdog?: BackupWatchdogMetric;
  incidents?: IncidentAlertItem[];
}

export function OperationsTab({
  host,
  db,
  routes,
  smtp,
  payOs,
  zalo,
  workers = [],
  watchdog,
  incidents = [],
}: OperationsTabProps) {
  // Workers table columns
  const workerColumns: TableProps<BackgroundWorkerItem>['columns'] = [
    { title: t("Tiến trình"), dataIndex: 'name', key: 'name', render: (val: string) => <strong>{val}</strong> },
    {
      title: t("Trạng thái"),
      dataIndex: 'status',
      key: 'status',
      render: (st: string, r) => {
        if (!r.enabled) return <Tag color="default">{t("Bình thường")} (Disabled)</Tag>;
        if (st === 'running') return <Tag color="processing" icon={<SyncOutlined spin />}>{t("Đang hoạt động")}</Tag>;
        if (st === 'failed') return <Tag color="error">{t("Nghiêm trọng")}</Tag>;
        return <Tag color="success">{t("Bình thường")}</Tag>;
      },
    },
    { title: t("Lần chạy cuối"), dataIndex: 'lastStart', key: 'lastStart', render: (val?: string) => val || '—' },
    { title: t("Thành công cuối"), dataIndex: 'lastSuccess', key: 'lastSuccess', render: (val?: string) => val || '—' },
    { title: t("Lỗi cuối"), dataIndex: 'lastError', key: 'lastError', render: (val?: string) => val || '—' },
  ];

  // Route metrics columns
  const routeColumns: TableProps<RouteMetricItem>['columns'] = [
    { title: t("Mẫu định tuyến"), dataIndex: 'routeTemplate', key: 'route', render: (val: string) => <code>{val}</code> },
    { title: t("Phương thức"), dataIndex: 'method', key: 'method', render: (val: string) => <Tag>{val}</Tag> },
    { title: t("Tổng yêu cầu"), dataIndex: 'totalRequests', key: 'req', align: 'right' },
    {
      title: t("Tỷ lệ lỗi 5xx"),
      dataIndex: 'errorRatePercent',
      key: 'err',
      align: 'right',
      render: (val: number) => (
        <span style={{ color: val > 5 ? '#9f443b' : '#334d53', fontWeight: val > 5 ? 700 : 400 }}>
          {val.toFixed(2)}%
        </span>
      ),
    },
    {
      title: t("Độ trễ p95"),
      dataIndex: 'p95LatencyMs',
      key: 'p95',
      align: 'right',
      render: (val: number | null) => (val !== null ? `${val} ms` : <UnknownMetric />),
    },
    {
      title: t("Độ trễ trung bình"),
      dataIndex: 'avgLatencyMs',
      key: 'avg',
      align: 'right',
      render: (val: number | null) => (val !== null ? `${val} ms` : <UnknownMetric />),
    },
  ];

  // Incident columns
  const incidentColumns: TableProps<IncidentAlertItem>['columns'] = [
    {
      title: t("Mức độ"),
      dataIndex: 'severity',
      key: 'sev',
      render: (sev: 'warning' | 'critical') => (
        <Tag color={sev === 'critical' ? 'error' : 'warning'}>
          {sev === 'critical' ? t("Nghiêm trọng") : t("Cảnh báo")}
        </Tag>
      ),
    },
    { title: t("Sự cố & Cảnh báo"), dataIndex: 'title', key: 'title', render: (val: string) => <strong>{val}</strong> },
    { title: t("Ghi chú"), dataIndex: 'description', key: 'desc' },
    { title: t("Thời gian kích hoạt"), dataIndex: 'triggeredAt', key: 'time' },
    {
      title: t("Thời gian chờ nhắc lại"),
      dataIndex: 'cooldownRemainingMinutes',
      key: 'cooldown',
      render: (val?: number) => (val ? `${val} min` : '—'),
    },
    {
      title: t("Trạng thái"),
      dataIndex: 'state',
      key: 'state',
      render: (st: string) => (
        <Tag color={st === 'resolved' ? 'success' : st === 'cooldown' ? 'orange' : 'red'}>
          {st === 'resolved' ? t("Đã xử lý") : st === 'cooldown' ? t("Trong thời gian chờ") : t("Đang hoạt động")}
        </Tag>
      ),
    },
  ];

  // Latency histogram buckets mock/default
  const histogramBuckets = [
    { range: '<50ms', count: 1240 },
    { range: '50-100ms', count: 860 },
    { range: '100-250ms', count: 320 },
    { range: '250-500ms', count: 95 },
    { range: '500ms-1s', count: 24 },
    { range: '1-2s', count: 6 },
    { range: '>2s', count: 1, isViolating: true },
  ];

  return (
    <div className="operations-tab-content">
      {/* 1. Host Infrastructure (Ubuntu systemd collector) */}
      <SectionCard
        title={t("Hạ tầng máy chủ (Host Systemd)")}
        extra={
          <Space size={8}>
            {host?.isStale && <StaleIndicator ageText=">1m" />}
            <span style={{ fontSize: 13, color: '#7a8c91' }}>
              {t("Thu thập mỗi phút: CPU, RAM, ổ đĩa, containers từ host JSON")}
            </span>
          </Space>
        }
      >
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={12} lg={6}>
            <Card size="small" bordered={false} className="host-stat-mini-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <span style={{ color: '#7b8d92', fontSize: 13 }}>{t("Sử dụng CPU")}</span>
                <CloudServerOutlined style={{ color: '#17a486' }} />
              </div>
              <div style={{ fontSize: 22, fontWeight: 700, color: '#2d4750', marginBottom: 8 }}>
                {host?.cpuPercent !== null && host?.cpuPercent !== undefined ? (
                  `${host.cpuPercent}%`
                ) : (
                  <UnknownMetric />
                )}
              </div>
              <Progress
                percent={host?.cpuPercent ?? 0}
                size="small"
                status={host && host.cpuPercent !== null && host.cpuPercent > 85 ? 'exception' : 'normal'}
                showInfo={false}
              />
            </Card>
          </Col>

          <Col xs={24} sm={12} lg={6}>
            <Card size="small" bordered={false} className="host-stat-mini-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <span style={{ color: '#7b8d92', fontSize: 13 }}>{t("Sử dụng RAM")}</span>
                <HddOutlined style={{ color: '#527fa5' }} />
              </div>
              <div style={{ fontSize: 22, fontWeight: 700, color: '#2d4750', marginBottom: 8 }}>
                {host?.ramPercent !== null && host?.ramPercent !== undefined ? (
                  `${host.ramPercent}%`
                ) : (
                  <UnknownMetric />
                )}
              </div>
              <Progress
                percent={host?.ramPercent ?? 0}
                size="small"
                status={host && host.ramPercent !== null && host.ramPercent > 85 ? 'exception' : 'normal'}
                showInfo={false}
              />
            </Card>
          </Col>

          <Col xs={24} sm={12} lg={6}>
            <Card size="small" bordered={false} className="host-stat-mini-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <span style={{ color: '#7b8d92', fontSize: 13 }}>{t("Dung lượng ổ đĩa")}</span>
                <HddOutlined style={{ color: '#bb8133' }} />
              </div>
              <div style={{ fontSize: 22, fontWeight: 700, color: '#2d4750', marginBottom: 8 }}>
                {host?.diskPercent !== null && host?.diskPercent !== undefined ? (
                  `${host.diskPercent}%`
                ) : (
                  <UnknownMetric />
                )}
              </div>
              <Progress
                percent={host?.diskPercent ?? 0}
                size="small"
                status={host && host.diskPercent !== null && host.diskPercent > 85 ? 'exception' : 'normal'}
                showInfo={false}
              />
            </Card>
          </Col>

          <Col xs={24} sm={12} lg={6}>
            <Card size="small" bordered={false} className="host-stat-mini-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <span style={{ color: '#7b8d92', fontSize: 13 }}>{t("Trạng thái container")}</span>
                <CheckCircleOutlined style={{ color: '#17a486' }} />
              </div>
              <div style={{ fontSize: 18, fontWeight: 700, color: '#2d4750', marginBottom: 8 }}>
                {host?.containerStatus ? (
                  <Tag color={host.containerStatus === 'running' ? 'success' : 'error'}>
                    {host.containerStatus}
                  </Tag>
                ) : (
                  <UnknownMetric />
                )}
              </div>
              <div style={{ fontSize: 12, color: '#7a8c91' }}>
                {t("Kiểm tra /api/health")}:{' '}
                {host?.apiHealthOk ? (
                  <Badge status="success" text="200 OK" />
                ) : (
                  <UnknownMetric />
                )}
              </div>
            </Card>
          </Col>
        </Row>
      </SectionCard>

      {/* 2. API & DB Performance */}
      <Row gutter={[16, 16]} style={{ marginTop: 18 }}>
        <Col xs={24} lg={15}>
          <SectionCard title={t("Hiệu năng API & Cơ sở dữ liệu")}>
            <Table
              size="small"
              rowKey="routeTemplate"
              columns={routeColumns}
              dataSource={routes ?? []}
              pagination={false}
              locale={{ emptyText: t("Chưa có dữ liệu") }}
            />
          </SectionCard>
        </Col>

        <Col xs={24} lg={9}>
          <SectionCard title={t("Phân phối độ trễ (Histogram p95)")}>
            <div style={{ padding: '8px 0 16px' }}>
              <NativeLatencyHistogram buckets={histogramBuckets} />
            </div>
            <div style={{ borderTop: '1px solid #eef2f1', paddingTop: 12 }}>
              <Space direction="vertical" style={{ width: '100%' }} size={4}>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 13, color: '#7a8c91' }}>{t("Độ trễ CSDL")}:</span>
                  <strong>{db?.latencyMs !== null && db?.latencyMs !== undefined ? `${db.latencyMs} ms` : <UnknownMetric />}</strong>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 13, color: '#7a8c91' }}>{t("Trạng thái")}:</span>
                  <HealthTag status={db?.status ?? 'unknown'} />
                </div>
              </Space>
            </div>
          </SectionCard>
        </Col>
      </Row>

      {/* 3. Integrations & Queues */}
      <Row gutter={[16, 16]} style={{ marginTop: 18 }}>
        <Col xs={24} sm={12} lg={6}>
          <Card size="small" bordered={false} className="integration-card">
            <Space direction="vertical" style={{ width: '100%' }} size={6}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <strong style={{ fontSize: 13 }}>{t("Email SMTP (Đã nhận / Đã gửi)")}</strong>
                <MailOutlined />
              </div>
              <div>
                <span>{smtp?.acceptedCount ?? 0}</span> / <strong>{smtp?.deliveredCount ?? 0}</strong>
              </div>
              <HealthTag status={smtp?.status ?? 'unknown'} />
            </Space>
          </Card>
        </Col>

        <Col xs={24} sm={12} lg={6}>
          <Card size="small" bordered={false} className="integration-card">
            <Space direction="vertical" style={{ width: '100%' }} size={6}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <strong style={{ fontSize: 13 }}>{t("Đối soát payOS & Lỗi")}</strong>
                <SyncOutlined />
              </div>
              <div>
                <span>{payOs?.reconciledCount ?? 0}</span> ({payOs?.errorCount ?? 0} errors)
              </div>
              <HealthTag status={payOs?.status ?? 'unknown'} />
            </Space>
          </Card>
        </Col>

        <Col xs={24} sm={12} lg={6}>
          <Card size="small" bordered={false} className="integration-card">
            <Space direction="vertical" style={{ width: '100%' }} size={6}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <strong style={{ fontSize: 13 }}>{t("Hàng đợi Zalo (Khung giờ 07:00-21:00)")}</strong>
                <AlertOutlined />
              </div>
              <div>
                <span>Queue: {zalo?.queuedCount ?? 0}</span> | <span>Sent: {zalo?.sentCount ?? 0}</span>
              </div>
              <HealthTag status={zalo?.status ?? 'unknown'} />
            </Space>
          </Card>
        </Col>

        <Col xs={24} sm={12} lg={6}>
          <Card size="small" bordered={false} className="integration-card">
            <Space direction="vertical" style={{ width: '100%' }} size={6}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <strong style={{ fontSize: 13 }}>{t("Chó canh gác sao lưu (Watchdog 27h)")}</strong>
                <SafetyCertificateOutlined />
              </div>
              <div style={{ fontSize: 13, color: '#314e56' }}>
                {watchdog?.isWithin27Hours ? (
                  <span style={{ color: '#218364' }}>{t("Đã sao lưu hợp lệ trong vòng 27 giờ qua")}</span>
                ) : (
                  <span style={{ color: '#9f443b' }}>{t("Cần kiểm tra bản sao lưu hệ thống")}</span>
                )}
              </div>
              <HealthTag status={watchdog?.status ?? 'unknown'} />
            </Space>
          </Card>
        </Col>
      </Row>

      {/* 4. Background Workers */}
      <div style={{ marginTop: 18 }}>
        <SectionCard title={t("Tiến trình nền (Background Workers)")}>
          <Table
            size="small"
            rowKey="name"
            columns={workerColumns}
            dataSource={workers}
            pagination={false}
            locale={{ emptyText: t("Chưa có dữ liệu") }}
          />
        </SectionCard>
      </div>

      {/* 5. Incidents & Alerts */}
      <div style={{ marginTop: 18 }}>
        <SectionCard title={t("Sự cố & Cảnh báo")}>
          <Table
            size="small"
            rowKey="id"
            columns={incidentColumns}
            dataSource={incidents}
            pagination={false}
            locale={{ emptyText: t("Không có sự cố nào đang diễn ra") }}
          />
        </SectionCard>
      </div>
    </div>
  );
}

import {
  AlertOutlined,
  CheckCircleOutlined,
  CloudServerOutlined,
  HddOutlined,
  MailOutlined,
  SafetyCertificateOutlined,
  SyncOutlined,
} from '@ant-design/icons';
import { Button, Card, Col, Progress, Row, Space, Table, Tag } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import {
  HealthTag,
  MonitoringErrorState,
  MonitoringStatCard,
  StaleIndicator,
  UnconfiguredNotice,
  UnknownMetric,
} from './MonitoringState';
import { NativeLatencyHistogram } from './NativeCharts';
import type {
  AdminMonitoringOperationsResponse,
  IncidentDto,
  OpsWindowPreset,
  RouteLatencyMetricDto,
  WorkerStatusDto,
} from './types';

interface OperationsTabProps {
  data?: AdminMonitoringOperationsResponse | null;
  incidents?: IncidentDto[];
  window?: OpsWindowPreset;
  isLoading?: boolean;
  error?: Error | unknown;
  onRetry?: () => void;
  is501?: boolean;
  onUpdateIncidentState?: (id: string, state: string) => void;
  isUpdatingIncident?: boolean;
}

export function OperationsTab({
  data,
  incidents = [],
  window = '1h',
  isLoading,
  error,
  onRetry,
  is501,
  onUpdateIncidentState,
  isUpdatingIncident,
}: OperationsTabProps) {
  const windowText =
    window === '7d'
      ? t("Cửa sổ vận hành 7 ngày")
      : window === '24h'
        ? t("Cửa sổ vận hành 24 giờ")
        : t("Cửa sổ vận hành 1 giờ");

  // Workers table columns
  const workerColumns: TableProps<WorkerStatusDto>['columns'] = [
    {
      title: t("Tiến trình"),
      dataIndex: 'workerName',
      key: 'workerName',
      render: (val: string) => <strong>{val}</strong>,
    },
    {
      title: t("Trạng thái"),
      dataIndex: 'status',
      key: 'status',
      render: (st: string, r) => {
        if (!r.isEnabled) return <Tag color="default">{t("Đã tắt")}</Tag>;
        if (r.isStale) return <Tag color="warning">{t("Dữ liệu cũ")}</Tag>;
        return <HealthTag status={st} />;
      },
    },
    {
      title: t("Lần chạy cuối"),
      dataIndex: 'lastStartedAt',
      key: 'lastStartedAt',
      render: (val?: string | null) => (val ? dayjs(val).format('YYYY-MM-DD HH:mm:ss') : '—'),
    },
    {
      title: t("Thành công cuối"),
      dataIndex: 'lastSuccessAt',
      key: 'lastSuccessAt',
      render: (val?: string | null) => (val ? dayjs(val).format('YYYY-MM-DD HH:mm:ss') : '—'),
    },
    {
      title: t("Lỗi cuối"),
      dataIndex: 'lastErrorAt',
      key: 'lastErrorAt',
      render: (val?: string | null, r?: WorkerStatusDto) => (
        <span>
          {val ? dayjs(val).format('YYYY-MM-DD HH:mm:ss') : '—'}
          {r?.lastErrorMessage && <small style={{ display: 'block', color: '#9f443b' }}>{r.lastErrorMessage}</small>}
        </span>
      ),
    },
  ];

  // Route metrics columns
  const routeColumns: TableProps<RouteLatencyMetricDto>['columns'] = [
    { title: t("Mẫu định tuyến"), dataIndex: 'routeTemplate', key: 'route', render: (val: string) => <code>{val}</code> },
    { title: t("Phương thức"), dataIndex: 'httpMethod', key: 'method', render: (val: string) => <Tag>{val}</Tag> },
    { title: t("Tổng yêu cầu"), dataIndex: 'requestCount', key: 'req', align: 'right' },
    {
      title: t("Tỷ lệ lỗi 5xx"),
      key: 'errRate',
      align: 'right',
      render: (_, r) => {
        const rate = r.requestCount > 0 ? (r.status5xxCount / r.requestCount) * 100 : 0;
        return (
          <span style={{ color: rate > 5 ? '#9f443b' : '#334d53', fontWeight: rate > 5 ? 700 : 400 }}>
            {rate.toFixed(2)}%
          </span>
        );
      },
    },
    {
      title: t("Độ trễ p95"),
      dataIndex: 'p95LatencyMs',
      key: 'p95',
      align: 'right',
      render: (val: number | null) => (val !== null ? `${val.toFixed(1)} ms` : <UnknownMetric />),
    },
    {
      title: t("Độ trễ trung bình"),
      dataIndex: 'avgLatencyMs',
      key: 'avg',
      align: 'right',
      render: (val: number | null) => (val !== null ? `${val.toFixed(1)} ms` : <UnknownMetric />),
    },
  ];

  if (is501) {
    return (
      <div className="operations-tab-content">
        <UnconfiguredNotice
          message={<strong>{t("Đang cập nhật chỉ số vận hành")}</strong>}
          description={t(
            "Backend dịch vụ thu thập hệ số vận hành, telemetry và systemd collector đang được kết nối theo Task T3. Giao diện dưới đây đã sẵn sàng tiếp nhận telemetry."
          )}
        />
      </div>
    );
  }

  if (error) {
    return <MonitoringErrorState error={error} onRetry={onRetry} />;
  }

  const overview = data?.overview;
  const host = data?.hostMetrics;
  const workers = data?.workers ?? [];
  const routes = data?.routeMetrics ?? [];
  const queues = data?.queues;

  const backupWorker = workers.find((w) => {
    const name = w.workerName.toLowerCase();
    return name.includes('backup') || name.includes('watchdog');
  });

  return (
    <div className="operations-tab-content">
      {/* Overview Stat Cards */}
      <Row gutter={[16, 16]} className="stat-grid" style={{ marginBottom: 18 }}>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("Trạng thái chung")}
            value={<HealthTag status={overview?.overallStatus} />}
            icon={<CheckCircleOutlined />}
            colorClass="stat-teal"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Kiểm tra cơ sở dữ liệu và máy chủ")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={5}>
          <MonitoringStatCard
            title={t("Tỷ lệ lỗi 5xx")}
            value={
              overview?.api5xxRatePercent !== undefined && overview?.api5xxRatePercent !== null
                ? `${overview.api5xxRatePercent.toFixed(2)}%`
                : null
            }
            icon={<AlertOutlined />}
            colorClass={
              overview && overview.api5xxRatePercent !== undefined && overview.api5xxRatePercent !== null && overview.api5xxRatePercent > 5
                ? 'stat-red'
                : 'stat-green'
            }
            isLoading={isLoading}
            explanation={t("Ngưỡng cảnh báo: > 5% trong 5 phút")}
            subtext={
              <span className="stat-muted">
                {t("Tổng yêu cầu")}:{' '}
                {overview?.totalRequestsLastHour !== undefined && overview?.totalRequestsLastHour !== null
                  ? overview.totalRequestsLastHour
                  : <UnknownMetric />}
              </span>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={5}>
          <MonitoringStatCard
            title={t("Độ trễ p95")}
            value={
              overview?.latencyP95Ms !== undefined && overview?.latencyP95Ms !== null
                ? `${overview.latencyP95Ms.toFixed(1)} ms`
                : null
            }
            icon={<SyncOutlined />}
            colorClass={
              overview && overview.latencyP95Ms !== undefined && overview.latencyP95Ms !== null && overview.latencyP95Ms > 2000
                ? 'stat-amber'
                : 'stat-teal'
            }
            isLoading={isLoading}
            explanation={t("Ngưỡng cảnh báo: p95 > 2s trong 10 phút")}
            subtext={<span className="stat-muted">{windowText}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={5}>
          <MonitoringStatCard
            title={t("Cơ sở dữ liệu")}
            value={overview?.databaseStatus ? <HealthTag status={overview.databaseStatus} /> : null}
            icon={<HddOutlined />}
            colorClass="stat-blue"
            isLoading={isLoading}
            subtext={
              <span className="stat-muted">
                {overview?.databaseConnected === true
                  ? t("Đã kết nối")
                  : overview?.databaseConnected === false
                    ? t("Mất kết nối")
                    : t("Chưa xác định")}
              </span>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={5}>
          <MonitoringStatCard
            title={t("Chó canh gác sao lưu (Watchdog 27h)")}
            value={backupWorker ? <HealthTag status={backupWorker.status} /> : null}
            icon={<SafetyCertificateOutlined />}
            colorClass={
              backupWorker?.status?.toLowerCase() === 'healthy'
                ? 'stat-green'
                : backupWorker?.status?.toLowerCase() === 'critical'
                  ? 'stat-red'
                  : backupWorker
                    ? 'stat-amber'
                    : 'stat-slate'
            }
            isLoading={isLoading}
            explanation={t("Chưa có dữ liệu trạng thái sao lưu")}
            subtext={
              <span className="stat-muted">
                {backupWorker?.lastSuccessAt
                  ? t("Sao lưu gần nhất: {0}", [dayjs(backupWorker.lastSuccessAt).format('YYYY-MM-DD HH:mm')])
                  : t("Chó canh gác 27h")}
              </span>
            }
          />
        </Col>
      </Row>

      {/* Host Metrics & Integrations Grid */}
      <Row gutter={[16, 16]} style={{ marginBottom: 18 }}>
        <Col xs={24} lg={12}>
          <SectionCard
            title={t("Hạ tầng máy chủ (Host Systemd)")}
            extra={
              host?.isStale ? (
                <StaleIndicator ageText={host.recordedAt ? dayjs(host.recordedAt).format('HH:mm:ss') : undefined} />
              ) : host?.recordedAt ? (
                <span style={{ fontSize: 12, color: '#7a8c91' }}>
                  {dayjs(host.recordedAt).format('HH:mm:ss')}
                </span>
              ) : null
            }
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, fontSize: 13 }}>
                  <span>{t("Sử dụng CPU")}</span>
                  <strong>
                    {host?.cpuPercent !== undefined && host?.cpuPercent !== null ? (
                      `${host.cpuPercent.toFixed(1)}%`
                    ) : (
                      <UnknownMetric />
                    )}
                  </strong>
                </div>
                {host?.cpuPercent !== undefined && host?.cpuPercent !== null ? (
                  <Progress
                    percent={host.cpuPercent}
                    status={host.cpuPercent > 85 ? 'exception' : 'active'}
                    strokeColor={host.cpuPercent > 85 ? '#9f443b' : '#17a486'}
                  />
                ) : (
                  <div style={{ height: 8, background: '#eef2f1', borderRadius: 4, margin: '6px 0' }} />
                )}
              </div>

              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, fontSize: 13 }}>
                  <span>{t("Sử dụng RAM")}</span>
                  <strong>
                    {host?.memoryPercent !== undefined && host?.memoryPercent !== null ? (
                      `${host.memoryPercent.toFixed(1)}%`
                    ) : (
                      <UnknownMetric />
                    )}
                  </strong>
                </div>
                {host?.memoryPercent !== undefined && host?.memoryPercent !== null ? (
                  <Progress
                    percent={host.memoryPercent}
                    status={host.memoryPercent > 85 ? 'exception' : 'active'}
                    strokeColor={host.memoryPercent > 85 ? '#9f443b' : '#34757c'}
                  />
                ) : (
                  <div style={{ height: 8, background: '#eef2f1', borderRadius: 4, margin: '6px 0' }} />
                )}
              </div>

              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, fontSize: 13 }}>
                  <span>{t("Dung lượng ổ đĩa")}</span>
                  <strong>
                    {host?.diskPercent !== undefined && host?.diskPercent !== null ? (
                      `${host.diskPercent.toFixed(1)}%`
                    ) : (
                      <UnknownMetric />
                    )}
                  </strong>
                </div>
                {host?.diskPercent !== undefined && host?.diskPercent !== null ? (
                  <Progress
                    percent={host.diskPercent}
                    status={host.diskPercent > 85 ? 'exception' : 'active'}
                    strokeColor={host.diskPercent > 85 ? '#9f443b' : '#bb8133'}
                  />
                ) : (
                  <div style={{ height: 8, background: '#eef2f1', borderRadius: 4, margin: '6px 0' }} />
                )}
              </div>

              <div style={{ display: 'flex', justifyContent: 'space-between', paddingTop: 8, borderTop: '1px solid #f0f4f3', fontSize: 13 }}>
                <span>
                  {t("Trạng thái container")}:{' '}
                  <strong>
                    {host?.containerCount !== undefined && host?.containerCount !== null ? (
                      t("{0} đang chạy", [host.containerCount])
                    ) : (
                      <UnknownMetric />
                    )}
                  </strong>
                </span>
                <span>
                  /api/health: <HealthTag status={host?.healthEndpointStatus} />
                </span>
              </div>
            </div>
          </SectionCard>
        </Col>

        <Col xs={24} lg={12}>
          <SectionCard title={t("Dịch vụ tích hợp & Hàng đợi")}>
            <Row gutter={[12, 12]}>
              <Col span={12}>
                <Card size="small" bordered={false} style={{ background: '#f8faf9', borderRadius: 8 }}>
                  <div style={{ fontSize: 12, color: '#7a8c91', marginBottom: 4 }}>
                    <MailOutlined style={{ marginRight: 4 }} />
                    {t("Email SMTP (Đã nhận / Đã gửi)")}
                  </div>
                  <div style={{ fontSize: 18, fontWeight: 700, color: '#17383d' }}>
                    {queues?.smtpAcceptedNotDeliveredCount !== undefined && queues?.smtpAcceptedNotDeliveredCount !== null ? (
                      queues.smtpAcceptedNotDeliveredCount
                    ) : (
                      <UnknownMetric />
                    )}
                  </div>
                  <small style={{ color: '#7a8c91' }}>{t("Đã nhận nhưng chưa gửi")}</small>
                </Card>
              </Col>

              <Col span={12}>
                <Card size="small" bordered={false} style={{ background: '#f8faf9', borderRadius: 8 }}>
                  <div style={{ fontSize: 12, color: '#7a8c91', marginBottom: 4 }}>
                    <CloudServerOutlined style={{ marginRight: 4 }} />
                    {t("Đối soát payOS & Lỗi")}
                  </div>
                  <div style={{ fontSize: 18, fontWeight: 700, color: '#17383d' }}>
                    {queues?.payOsPendingReconcileCount !== undefined && queues?.payOsPendingReconcileCount !== null ? (
                      queues.payOsPendingReconcileCount
                    ) : (
                      <UnknownMetric />
                    )}
                  </div>
                  <small style={{ color: '#7a8c91' }}>{t("Đang chờ đối soát")}</small>
                </Card>
              </Col>

              <Col span={24}>
                <Card size="small" bordered={false} style={{ background: '#f8faf9', borderRadius: 8 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div>
                      <div style={{ fontSize: 12, color: '#7a8c91', marginBottom: 4 }}>
                        {t("Hàng đợi Zalo (Khung giờ 07:00-21:00)")}
                      </div>
                      <div style={{ fontSize: 18, fontWeight: 700, color: '#17383d' }}>
                        {queues?.zaloPendingQueueCount !== undefined && queues?.zaloPendingQueueCount !== null ? (
                          t("{0} đang chờ", [queues.zaloPendingQueueCount])
                        ) : (
                          <UnknownMetric />
                        )}
                      </div>
                      <small style={{ color: '#7a8c91' }}>
                        {queues?.zaloEligibleWindowCount !== undefined && queues?.zaloEligibleWindowCount !== null ? (
                          t("{0} trong khung giờ hợp lệ", [queues.zaloEligibleWindowCount])
                        ) : (
                          <UnknownMetric />
                        )}
                      </small>
                    </div>
                    <Tag color="cyan">07:00 - 21:00</Tag>
                  </div>
                </Card>
              </Col>
            </Row>
          </SectionCard>
        </Col>
      </Row>

      {/* Latency Histogram */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Phân phối độ trễ (Histogram p95)")}
          extra={<span style={{ fontSize: 12, color: '#7a8c91' }}>{windowText}</span>}
        >
          {data?.histogramBuckets && data.histogramBuckets.length > 0 ? (
            <NativeLatencyHistogram
              buckets={data.histogramBuckets}
              height={160}
            />
          ) : (
            <div
              style={{
                padding: '28px 16px',
                textAlign: 'center',
                background: '#fafbfc',
                borderRadius: 8,
                border: '1px dashed #d9e2ec',
              }}
            >
              <div style={{ color: '#627d98', fontWeight: 500, marginBottom: 4 }}>
                {t("Chưa có dữ liệu phân phối độ trễ")}
              </div>
              <div style={{ fontSize: 12, color: '#9fb3c8' }}>
                {t("Dữ liệu phân phối độ trễ chi tiết theo bucket chưa được thu thập trong phiên bản hiện tại.")}
              </div>
            </div>
          )}
        </SectionCard>
      </div>

      {/* Background Workers Table */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard title={t("Tiến trình nền (Background Workers)")}>
          <div className="monitoring-table-wrapper">
            <Table
              size="small"
              rowKey="workerName"
              columns={workerColumns}
              dataSource={workers}
              pagination={false}
              locale={{ emptyText: t("Chưa có dữ liệu") }}
              scroll={{ x: 600 }}
            />
          </div>
        </SectionCard>
      </div>

      {/* Route Metrics Table */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard title={t("Hiệu năng API & Cơ sở dữ liệu")}>
          <div className="monitoring-table-wrapper">
            <Table
              size="small"
              rowKey={(r) => `${r.httpMethod}-${r.routeTemplate}`}
              columns={routeColumns}
              dataSource={routes}
              pagination={{ pageSize: 5 }}
              locale={{ emptyText: t("Chưa có dữ liệu") }}
              scroll={{ x: 650 }}
            />
          </div>
        </SectionCard>
      </div>

      {/* Incidents Section */}
      <SectionCard
        title={t("Sự cố & Cảnh báo")}
        extra={
          incidents.length > 0 ? (
            <Tag color="error">{t("{0} sự cố", [incidents.length])}</Tag>
          ) : (
            <Tag color="success">{t("Không có sự cố nào đang diễn ra")}</Tag>
          )
        }
      >
        {incidents.length === 0 ? (
          <div style={{ padding: '24px 0', textAlign: 'center', color: '#7a8c91' }}>
            <CheckCircleOutlined style={{ fontSize: 32, color: '#17a486', marginBottom: 8 }} />
            <div>{t("Không có sự cố nào đang diễn ra")}</div>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {incidents.map((inc) => (
              <div
                key={inc.id}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  padding: '12px 16px',
                  background: inc.severity === 'Critical' ? '#fff5f5' : '#fffbf0',
                  borderRadius: 8,
                  border: `1px solid ${inc.severity === 'Critical' ? '#fed7d7' : '#feebc8'}`,
                }}
              >
                <div>
                  <Space size={8}>
                    <Tag color={inc.severity === 'Critical' ? 'error' : 'warning'}>
                      {inc.severity}
                    </Tag>
                    <strong>{inc.ruleName}</strong>
                    <Tag>{inc.state}</Tag>
                  </Space>
                  <div style={{ marginTop: 4, fontSize: 13, color: '#334d53' }}>
                    {inc.summary}
                  </div>
                  <small style={{ color: '#7a8c91' }}>
                    {t("Thời gian kích hoạt")}: {dayjs(inc.startedAt).format('YYYY-MM-DD HH:mm:ss')}
                    {inc.notificationCount > 0 && t(" ({0} lần thông báo)", [inc.notificationCount])}
                  </small>
                </div>

                <Space size={8}>
                  {inc.state !== 'Resolved' && (
                    <Button
                      size="small"
                      type="primary"
                      loading={isUpdatingIncident}
                      onClick={() => onUpdateIncidentState?.(inc.id, 'Resolved')}
                    >
                      {t("Đã xử lý")}
                    </Button>
                  )}
                </Space>
              </div>
            ))}
          </div>
        )}
      </SectionCard>
    </div>
  );
}

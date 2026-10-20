import {
  AppstoreOutlined,
  BarChartOutlined,
  CheckCircleOutlined,
  LineChartOutlined,
  UserAddOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { Col, Row, Space, Table, Tag } from 'antd';
import type { TableProps } from 'antd';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import {
  MonitoringEmptyState,
  MonitoringErrorState,
  MonitoringStatCard,
  UnconfiguredNotice,
  UnknownMetric,
} from './MonitoringState';
import { NativeConversionFunnel, NativeMultiLineChart } from './NativeCharts';
import type {
  AdminMonitoringGrowthResponse,
  CohortRetentionRowDto,
  CtaPerformanceDto,
  TrafficSourceStatsDto,
} from './types';

interface GrowthTabProps {
  data?: AdminMonitoringGrowthResponse | null;
  isLoading?: boolean;
  error?: Error | unknown;
  onRetry?: () => void;
  is501?: boolean;
  requestedFromDate?: string;
  requestedToDate?: string;
}

export function GrowthTab({
  data,
  isLoading,
  error,
  onRetry,
  is501,
  requestedFromDate,
  requestedToDate,
}: GrowthTabProps) {
  // Source table columns
  const sourceColumns: TableProps<TrafficSourceStatsDto>['columns'] = [
    { title: t("Nguồn"), dataIndex: 'source', key: 'source', render: (val: string) => <strong>{val}</strong> },
    { title: t("Kênh"), dataIndex: 'medium', key: 'medium', render: (val?: string | null) => val || '—' },
    {
      title: t("Phiên truy cập"),
      dataIndex: 'sessions',
      key: 'sessions',
      align: 'right',
      render: (val?: number | null) => (val !== null && val !== undefined ? val : '—'),
    },
    { title: t("Đăng ký"), dataIndex: 'registrations', key: 'registrations', align: 'right' },
    { title: t("Chủ trọ kích hoạt"), dataIndex: 'activatedOwners', key: 'activated', align: 'right' },
    {
      title: t("Tỷ lệ chuyển đổi"),
      key: 'convRate',
      align: 'right',
      render: (_, r) => {
        if (r.sessions !== null && r.sessions !== undefined && r.sessions > 0) {
          const rate = (r.registrations / r.sessions) * 100;
          return `${rate.toFixed(1)}%`;
        }
        return r.sessions === 0 ? '0.0%' : '—';
      },
    },
  ];

  // CTA table columns
  const ctaColumns: TableProps<CtaPerformanceDto>['columns'] = [
    { title: 'CTA', dataIndex: 'ctaId', key: 'ctaId', render: (val: string) => <code>{val}</code> },
    { title: t("Vị trí"), dataIndex: 'path', key: 'path' },
    { title: t("Lượt nhấn"), dataIndex: 'clicks', key: 'clicks', align: 'right' },
  ];

  // Cohort retention columns
  const cohortColumns: TableProps<CohortRetentionRowDto>['columns'] = [
    { title: t("Kỳ kích hoạt"), dataIndex: 'cohortPeriod', key: 'cohortPeriod', render: (val: string) => <strong>{val}</strong> },
    { title: t("Quy mô"), dataIndex: 'cohortSize', key: 'cohortSize', align: 'right' },
    {
      title: t("Tháng 0"),
      key: 'm0',
      align: 'right',
      render: (_, r) =>
        r.retentionPercentages && r.retentionPercentages.length > 0 && r.retentionPercentages[0] !== null && r.retentionPercentages[0] !== undefined ? (
          `${r.retentionPercentages[0]}%`
        ) : (
          <UnknownMetric explanation={t("Chưa có dữ liệu kỳ kích hoạt")} />
        ),
    },
    {
      title: t("Tháng 1"),
      key: 'm1',
      align: 'right',
      render: (_, r) =>
        r.retentionPercentages && r.retentionPercentages.length > 1 && r.retentionPercentages[1] !== null && r.retentionPercentages[1] !== undefined ? (
          <span style={{ fontWeight: 600 }}>{r.retentionPercentages[1]}%</span>
        ) : (
          <UnknownMetric explanation={t("Tháng dương lịch tiếp theo chưa hoàn thành hoặc chưa đủ điều kiện")} />
        ),
    },
    {
      title: t("Tháng 2"),
      key: 'm2',
      align: 'right',
      render: (_, r) =>
        r.retentionPercentages && r.retentionPercentages.length > 2 && r.retentionPercentages[2] !== null && r.retentionPercentages[2] !== undefined ? (
          <span style={{ fontWeight: 600 }}>{r.retentionPercentages[2]}%</span>
        ) : (
          <UnknownMetric explanation={t("Tháng dương lịch tiếp theo chưa hoàn thành hoặc chưa đủ điều kiện")} />
        ),
    },
  ];

  if (is501) {
    return (
      <div className="growth-tab-content">
        <UnconfiguredNotice
          message={<strong>{t("Đang cập nhật chỉ số tăng trưởng")}</strong>}
          description={t(
            "Backend dịch vụ thống kê tăng trưởng đang được xử lý theo hợp đồng Task T1. Dưới đây là khung hiển thị chuẩn bị sẵn."
          )}
        />
      </div>
    );
  }

  if (error) {
    return <MonitoringErrorState error={error} onRetry={onRetry} />;
  }

  const summary = data?.summary;
  const sources = data?.sources ?? [];
  const topCtas = data?.topCtas ?? [];
  const retention = data?.retentionMatrix ?? [];
  const maturity = data?.cohortMaturity;
  const dailySeries = data?.dailySeries ?? [];

  // Trend chart series
  const dates = dailySeries.map((d) => d.date);
  const trendSeries = [
    { name: t("Phiên truy cập"), color: '#17a486', data: dailySeries.map((d) => d.sessions) },
    { name: t("Đăng ký"), color: '#34757c', data: dailySeries.map((d) => d.registrations) },
    { name: t("Chủ trọ kích hoạt"), color: '#bb8133', data: dailySeries.map((d) => d.activations) },
    { name: t("Hoạt động"), color: '#457b9d', data: dailySeries.map((d) => d.activeOwners) },
  ];

  // Funnel stages (honest null handling, never fabricate 0)
  const funnelStages = [
    { label: t("Phiên truy cập"), count: summary?.totalSessions ?? null, color: '#17a486' },
    { label: t("Đăng ký"), count: summary?.registrations ?? null, color: '#34757c' },
    { label: t("Chủ trọ kích hoạt"), count: summary?.activatedOwners ?? null, color: '#bb8133' },
  ];

  const isClamped = Boolean(
    (data?.effectiveFromDate && requestedFromDate && data.effectiveFromDate !== requestedFromDate) ||
    (data?.effectiveToDate && requestedToDate && data.effectiveToDate !== requestedToDate)
  );

  return (
    <div className="growth-tab-content">
      {/* Effective date range boundary indicator */}
      {data?.effectiveFromDate && data?.effectiveToDate && (
        <div className="monitoring-effective-range" style={{ marginBottom: 14 }}>
          <Tag color={isClamped ? "warning" : "processing"} style={{ borderRadius: 6, padding: '3px 8px' }}>
            {t("Dữ liệu thực tế: {0} đến {1}", [data.effectiveFromDate, data.effectiveToDate])}
          </Tag>
        </div>
      )}

      {/* High-level KPI Row */}
      <Row gutter={[16, 16]} className="stat-grid" style={{ marginBottom: 18 }}>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("Phiên truy cập")}
            value={summary?.totalSessions}
            icon={<BarChartOutlined />}
            colorClass="stat-teal"
            isLoading={isLoading}
            subtext={
              <Space size={4}>
                <span>{t("Phiên 30 phút không hoạt động")}</span>
                <span className="stat-muted">•</span>
                <span>{t("Thu thập trực tiếp")}</span>
              </Space>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("Khách duy nhất")}
            value={summary?.uniqueVisitors}
            icon={<UserOutlined />}
            colorClass="stat-blue"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Không liên kết chéo thiết bị")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("Đăng ký")}
            value={summary?.registrations}
            icon={<UserAddOutlined />}
            colorClass="stat-green"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Chủ trọ mới")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("Kích hoạt")}
            value={summary?.activatedOwners}
            icon={<CheckCircleOutlined />}
            colorClass="stat-amber"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Hóa đơn tháng đầu tiên phát hành")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("WAU (7 ngày)")}
            value={summary?.currentWau}
            icon={<LineChartOutlined />}
            colorClass="stat-green"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Chỉ tính hành động chủ trọ")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={4}>
          <MonitoringStatCard
            title={t("MAU (30 ngày)")}
            value={summary?.currentMau}
            icon={<AppstoreOutlined />}
            colorClass="stat-teal"
            isLoading={isLoading}
            subtext={<span className="stat-muted">{t("Chỉ tính hành động chủ trọ")}</span>}
          />
        </Col>
      </Row>

      {/* Funnel & Maturity Section */}
      <Row gutter={[16, 16]} style={{ marginBottom: 18 }}>
        <Col xs={24} lg={12}>
          <SectionCard title={t("Phễu chuyển đổi")} extra={<span style={{ fontSize: 12, color: '#7a8c91' }}>{t("Tỷ lệ qua từng giai đoạn")}</span>}>
            {data?.funnel ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                <NativeConversionFunnel
                  stages={[
                    { label: t("Bắt đầu đăng ký"), count: data.funnel.startedCount, color: '#34757c' },
                    { label: t("Gửi lại mã OTP"), count: data.funnel.codeResentCount, color: '#bb8133' },
                    { label: t("Xác thực hoàn tất"), count: data.funnel.verifiedCount, color: '#17a486' },
                    { label: t("Bỏ qua mã OTP"), count: data.funnel.bypassedCount, color: '#457b9d' },
                  ]}
                />
                {data.funnel.unknownAttemptCount > 0 && (
                  <div style={{ fontSize: 12, color: '#7a8c91', borderTop: '1px solid #f0f4f3', paddingTop: 6 }}>
                    <span>{t("Lượt đăng ký chưa rõ nguồn")}: <strong>{data.funnel.unknownAttemptCount}</strong></span>
                  </div>
                )}
              </div>
            ) : summary ? (
              <NativeConversionFunnel stages={funnelStages} />
            ) : (
              <MonitoringEmptyState />
            )}
          </SectionCard>
        </Col>
        <Col xs={24} lg={12}>
          <SectionCard title={t("Độ trưởng thành Cohort")}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div style={{ padding: '12px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #eef2f1' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                  <strong>{t("Tỷ lệ kích hoạt 7 ngày")}:</strong>
                  {maturity?.activationRate7Day !== null && maturity?.activationRate7Day !== undefined ? (
                    <Space size={8}>
                      <span style={{ fontSize: 18, fontWeight: 700, color: '#17a486' }}>
                        {maturity.activationRate7Day.toFixed(1)}%
                      </span>
                      <Tag color="success">{t("Trưởng thành")}</Tag>
                    </Space>
                  ) : (
                    <UnknownMetric explanation={t("Chưa đủ 7 ngày trưởng thành")} />
                  )}
                </div>
                <div style={{ fontSize: 12, color: '#7a8c91' }}>
                  {t("Số cohort đã đủ điều kiện")}: <strong>{maturity?.activationCohortMaturedCount ?? 0}</strong>
                </div>
              </div>

              <div style={{ padding: '12px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #eef2f1' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
                  <strong>{t("Tỷ lệ trả phí lần đầu 30 ngày")}:</strong>
                  {maturity?.firstPaidRate30Day !== null && maturity?.firstPaidRate30Day !== undefined ? (
                    <Space size={8}>
                      <span style={{ fontSize: 18, fontWeight: 700, color: '#34757c' }}>
                        {maturity.firstPaidRate30Day.toFixed(1)}%
                      </span>
                      <Tag color="blue">{t("Trưởng thành")}</Tag>
                    </Space>
                  ) : (
                    <UnknownMetric explanation={t("Chưa đủ 30 ngày trưởng thành")} />
                  )}
                </div>
                <div style={{ fontSize: 12, color: '#7a8c91' }}>
                  {t("Số cohort đã đủ điều kiện")}: <strong>{maturity?.paidCohortMaturedCount ?? 0}</strong>
                </div>
              </div>
            </div>
          </SectionCard>
        </Col>
      </Row>

      {/* Daily Growth Trends Chart */}
      {dailySeries.length > 0 && (
        <div style={{ marginBottom: 18 }}>
          <SectionCard title={t("Xu hướng tăng trưởng hàng ngày")}>
            <NativeMultiLineChart dates={dates} series={trendSeries} height={220} />
          </SectionCard>
        </div>
      )}

      {/* Traffic & UTM Section */}
      <Row gutter={[16, 16]} style={{ marginBottom: 18 }}>
        <Col xs={24} lg={14}>
          <SectionCard title={t("Thống kê nguồn & UTM")}>
            <div className="monitoring-table-wrapper">
              <Table
                size="small"
                rowKey={(r) => `${r.source}-${r.medium || ''}`}
                columns={sourceColumns}
                dataSource={sources}
                pagination={{ pageSize: 5 }}
                locale={{ emptyText: t("Chưa có dữ liệu") }}
                scroll={{ x: 600 }}
              />
            </div>
          </SectionCard>
        </Col>
        <Col xs={24} lg={10}>
          <SectionCard title={t("Tương tác CTA")}>
            <div className="monitoring-table-wrapper">
              <Table
                size="small"
                rowKey="ctaId"
                columns={ctaColumns}
                dataSource={topCtas}
                pagination={{ pageSize: 5 }}
                locale={{ emptyText: t("Chưa có dữ liệu") }}
                scroll={{ x: 380 }}
              />
            </div>
          </SectionCard>
        </Col>
      </Row>

      {/* Activation Cohort & Retention Matrix */}
      <SectionCard
        title={t("Mức độ kích hoạt (Activation)")}
        extra={
          <span style={{ fontSize: 13, color: '#7a8c91' }}>
            {t("Hóa đơn tháng đầu tiên chuyển trạng thái Đã phát hành")}
          </span>
        }
      >
        <div className="monitoring-table-wrapper">
          <Table
            size="middle"
            rowKey="cohortPeriod"
            columns={cohortColumns}
            dataSource={retention}
            pagination={{ pageSize: 5 }}
            locale={{ emptyText: t("Chưa có dữ liệu") }}
            scroll={{ x: 500 }}
          />
        </div>
      </SectionCard>
    </div>
  );
}

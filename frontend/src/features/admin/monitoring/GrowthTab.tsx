import { Col, Row, Space, Table, Tag } from 'antd';
import type { TableProps } from 'antd';
import { AppstoreOutlined, BarChartOutlined, LineChartOutlined, UserOutlined } from '@ant-design/icons';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import { MonitoringStatCard, UnknownMetric } from './MonitoringState';
import type {
  ActivationCohortRow,
  ActiveUsersMetric,
  CtaEngagementItem,
  SessionSummary,
  SourceTrafficItem,
} from './types';

interface GrowthTabProps {
  sessions?: SessionSummary;
  activeUsers?: ActiveUsersMetric;
  sources?: SourceTrafficItem[];
  ctas?: CtaEngagementItem[];
  cohorts?: ActivationCohortRow[];
}

export function GrowthTab({ sessions, activeUsers, sources, ctas, cohorts }: GrowthTabProps) {
  // Source table columns
  const sourceColumns: TableProps<SourceTrafficItem>['columns'] = [
    { title: t("Nguồn"), dataIndex: 'source', key: 'source', render: (val: string) => <strong>{val}</strong> },
    { title: t("Kênh"), dataIndex: 'medium', key: 'medium', render: (val?: string) => val || '—' },
    { title: t("Chiến dịch"), dataIndex: 'campaign', key: 'campaign', render: (val?: string) => val || '—' },
    { title: t("Lượt xem"), dataIndex: 'views', key: 'views', align: 'right' },
    { title: t("Đăng ký"), dataIndex: 'signups', key: 'signups', align: 'right' },
    {
      title: t("Tỷ lệ chuyển đổi"),
      dataIndex: 'conversionRatePercent',
      key: 'rate',
      align: 'right',
      render: (val: number) => `${val.toFixed(1)}%`,
    },
  ];

  // CTA table columns
  const ctaColumns: TableProps<CtaEngagementItem>['columns'] = [
    { title: 'CTA', dataIndex: 'label', key: 'label', render: (val: string) => <strong>{val}</strong> },
    { title: t("Vị trí"), dataIndex: 'placement', key: 'placement' },
    { title: t("Lượt nhấn"), dataIndex: 'clicks', key: 'clicks', align: 'right' },
    { title: t("Đăng ký"), dataIndex: 'conversions', key: 'conversions', align: 'right' },
    {
      title: t("Tỷ lệ chuyển đổi"),
      dataIndex: 'conversionRatePercent',
      key: 'rate',
      align: 'right',
      render: (val: number) => `${val.toFixed(1)}%`,
    },
  ];

  // Cohort table columns
  const cohortColumns: TableProps<ActivationCohortRow>['columns'] = [
    { title: t("Kỳ kích hoạt"), dataIndex: 'cohortMonth', key: 'cohortMonth', render: (val: string) => <strong>{val}</strong> },
    { title: t("Quy mô"), dataIndex: 'registeredOwners', key: 'registeredOwners', align: 'right' },
    {
      title: t("Kích hoạt 7 ngày"),
      key: 'activation7d',
      align: 'right',
      render: (_, row) => (
        <Space size={6}>
          <span>{row.activated7DayCount} ({row.activated7DayRate.toFixed(1)}%)</span>
          {row.isMature7Day ? (
            <Tag color="success">{t("Trưởng thành")}</Tag>
          ) : (
            <Tag color="default">{t("Chưa đủ 30 ngày trưởng thành")}</Tag>
          )}
        </Space>
      ),
    },
    {
      title: t("Giữ chân tháng tiếp theo"),
      key: 'retentionNextMonth',
      align: 'right',
      render: (_, row) =>
        row.nextMonthRetainedCount !== null && row.nextMonthRetentionRate !== null ? (
          <Space size={6}>
            <span>{row.nextMonthRetainedCount} ({row.nextMonthRetentionRate.toFixed(1)}%)</span>
            {row.isMatureRetention && <Tag color="blue">{t("Trưởng thành")}</Tag>}
          </Space>
        ) : (
          <UnknownMetric explanation={t("Chưa đủ 30 ngày trưởng thành")} />
        ),
    },
  ];

  return (
    <div className="growth-tab-content">
      {/* High-level KPI Row */}
      <Row gutter={[16, 16]} className="stat-grid">
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Phiên truy cập")}
            value={sessions?.totalSessions}
            icon={<BarChartOutlined />}
            colorClass="stat-teal"
            subtext={
              <Space size={4}>
                <span>30min idle</span>
                <span className="stat-muted">•</span>
                <span>first-party ID</span>
              </Space>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Khách truy cập duy nhất")}
            value={sessions?.uniqueVisitors}
            icon={<UserOutlined />}
            colorClass="stat-blue"
            subtext={<span className="stat-muted">No cross-device link</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Hoạt động 7 ngày (WAU7)")}
            value={activeUsers?.wau7}
            icon={<LineChartOutlined />}
            colorClass="stat-green"
            subtext={<span className="stat-muted">{t("Hành động chủ trọ có ý nghĩa (không tính Admin/Hệ thống)")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Hoạt động 30 ngày (MAU30)")}
            value={activeUsers?.mau30}
            icon={<AppstoreOutlined />}
            colorClass="stat-amber"
            subtext={<span className="stat-muted">{t("Hành động chủ trọ có ý nghĩa (không tính Admin/Hệ thống)")}</span>}
          />
        </Col>
      </Row>

      {/* Traffic & UTM Section */}
      <Row gutter={[16, 16]} style={{ marginBottom: 18 }}>
        <Col xs={24} lg={14}>
          <SectionCard title={t("Thống kê nguồn & UTM")}>
            <Table
              size="small"
              rowKey={(r) => `${r.source}-${r.medium || ''}-${r.campaign || ''}`}
              columns={sourceColumns}
              dataSource={sources ?? []}
              pagination={false}
              locale={{ emptyText: t("Chưa có dữ liệu") }}
            />
          </SectionCard>
        </Col>
        <Col xs={24} lg={10}>
          <SectionCard title={t("Tương tác CTA")}>
            <Table
              size="small"
              rowKey="ctaId"
              columns={ctaColumns}
              dataSource={ctas ?? []}
              pagination={false}
              locale={{ emptyText: t("Chưa có dữ liệu") }}
            />
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
        <Table
          size="middle"
          rowKey="cohortMonth"
          columns={cohortColumns}
          dataSource={cohorts ?? []}
          pagination={false}
          locale={{ emptyText: t("Chưa có dữ liệu") }}
        />
      </SectionCard>
    </div>
  );
}

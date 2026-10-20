import { PlusOutlined, WalletOutlined } from '@ant-design/icons';
import { Button, Col, Row, Table, Tag } from 'antd';
import type { TableProps } from 'antd';
import { useState } from 'react';
import { formatVnd } from '../../../shared/format';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import { CampaignCostModal } from './CampaignCostModal';
import { MonitoringStatCard, UnknownMetric } from './MonitoringState';
import type { Cac30CohortItem, CampaignCostEntry, CreateCampaignCostInput, RevenueSummary } from './types';

interface RevenueTabProps {
  revenue?: RevenueSummary;
  cacCohorts?: Cac30CohortItem[];
  campaignCosts?: CampaignCostEntry[];
  onAddCampaignCost?: (input: CreateCampaignCostInput) => void;
}

export function RevenueTab({
  revenue,
  cacCohorts,
  campaignCosts = [],
  onAddCampaignCost,
}: RevenueTabProps) {
  const [modalOpen, setModalOpen] = useState(false);

  // CAC Cohort Table columns
  const cacColumns: TableProps<Cac30CohortItem>['columns'] = [
    { title: t("Kỳ kích hoạt"), dataIndex: 'cohortMonth', key: 'cohortMonth', render: (val: string) => <strong>{val}</strong> },
    {
      title: t("Tổng chi phí"),
      dataIndex: 'totalSpendVnd',
      key: 'spend',
      align: 'right',
      render: (val: number | null) => (val !== null ? formatVnd(val) : <UnknownMetric explanation={t("Chưa nhập chi phí")} />),
    },
    { title: t("Lượt thanh toán Pro"), dataIndex: 'newFirstPaidOwnersCount', key: 'owners', align: 'right' },
    {
      title: t("Chi phí chuyển đổi chủ trọ (CAC30)"),
      key: 'cac',
      align: 'right',
      render: (_, row) => {
        if (row.cacVnd !== null) {
          return <strong>{formatVnd(row.cacVnd)}</strong>;
        }
        if (row.missingReason === 'missing_spend') {
          return <Tag color="warning">{t("Chưa nhập chi phí")}</Tag>;
        }
        if (row.missingReason === 'zero_cohort') {
          return <Tag color="default">{t("Chưa có chủ trọ trả phí")}</Tag>;
        }
        if (row.missingReason === 'unmature_cohort') {
          return <Tag color="blue">{t("Chưa đủ 30 ngày trưởng thành")}</Tag>;
        }
        return <UnknownMetric />;
      },
    },
    {
      title: t("Trưởng thành"),
      dataIndex: 'isMature30Day',
      key: 'maturity',
      align: 'center',
      render: (val: boolean) => (
        <Tag color={val ? 'success' : 'default'}>
          {val ? t("Trưởng thành") : t("Chưa đủ 30 ngày trưởng thành")}
        </Tag>
      ),
    },
  ];

  // Campaign cost entries columns
  const costColumns: TableProps<CampaignCostEntry>['columns'] = [
    { title: t("Tên chiến dịch"), dataIndex: 'campaignName', key: 'name', render: (val: string) => <strong>{val}</strong> },
    { title: t("Kênh"), dataIndex: 'channel', key: 'channel', render: (val?: string) => val || '—' },
    { title: t("Ngày ghi nhận"), dataIndex: 'recordedDate', key: 'date' },
    {
      title: t("Chi phí tiền mặt (VND)"),
      dataIndex: 'cashExpenseVnd',
      key: 'cash',
      align: 'right',
      render: (val: number) => formatVnd(val),
    },
    {
      title: t("Số giờ công"),
      dataIndex: 'personHours',
      key: 'hours',
      align: 'right',
      render: (val: number, r) => `${val}h${r.hourlyRateVnd ? ` @ ${formatVnd(r.hourlyRateVnd)}/h` : ''}`,
    },
    {
      title: t("Tổng chi phí"),
      dataIndex: 'totalCostVnd',
      key: 'total',
      align: 'right',
      render: (val: number) => <strong style={{ color: '#17a486' }}>{formatVnd(val)}</strong>,
    },
    { title: t("Người cập nhật"), dataIndex: 'updatedBy', key: 'updater' },
    { title: t("Ghi chú"), dataIndex: 'notes', key: 'notes', render: (val?: string) => val || '—' },
  ];

  return (
    <div className="revenue-tab-content">
      <CampaignCostModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        onSubmit={(input) => {
          onAddCampaignCost?.(input);
        }}
      />

      {/* KPI Cards */}
      <Row gutter={[16, 16]} className="stat-grid">
        <Col xs={24} sm={12} lg={8}>
          <MonitoringStatCard
            title={t("Doanh thu tháng (MRR)")}
            value={revenue?.mrrVnd !== null && revenue?.mrrVnd !== undefined ? formatVnd(revenue.mrrVnd) : null}
            icon={<WalletOutlined />}
            colorClass="stat-teal"
            subtext={
              <span className="stat-muted">
                {t("Chỉ tính gói Pro thanh toán thành công qua cổng (đã đối soát, trừ gói Admin tặng và tiền thuê phòng)")}
              </span>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={8}>
          <MonitoringStatCard
            title={t("Doanh thu Pro")}
            value={
              revenue?.totalProRevenueVnd !== null && revenue?.totalProRevenueVnd !== undefined
                ? formatVnd(revenue.totalProRevenueVnd)
                : null
            }
            icon={<WalletOutlined />}
            colorClass="stat-blue"
            subtext={<span className="stat-muted">{t("Chỉ tính gói Pro thanh toán thành công qua cổng (đã đối soát, trừ gói Admin tặng và tiền thuê phòng)")}</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={8}>
          <MonitoringStatCard
            title={t("Lượt thanh toán Pro")}
            value={revenue?.paidSubscriptionCount}
            icon={<WalletOutlined />}
            colorClass="stat-green"
            subtext={<span className="stat-muted">Central webhook / reconcile deduped</span>}
          />
        </Col>
      </Row>

      {/* CAC30 Cohorts Section */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Chi phí chuyển đổi chủ trọ (CAC30)")}
          extra={
            <span style={{ fontSize: 13, color: '#7a8c91' }}>
              {t("Tổng chi phí kỳ / Số chủ trọ Pro mới trong 30 ngày")}
            </span>
          }
        >
          <Table
            size="middle"
            rowKey="cohortMonth"
            columns={cacColumns}
            dataSource={cacCohorts ?? []}
            pagination={false}
            locale={{ emptyText: t("Chưa có dữ liệu") }}
          />
        </SectionCard>
      </div>

      {/* Manual Campaign Cost Management */}
      <SectionCard
        title={t("Quản lý chi phí thủ công")}
        extra={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => setModalOpen(true)}
            style={{ background: '#17a486', borderColor: '#17a486' }}
          >
            {t("Thêm chi phí chiến dịch")}
          </Button>
        }
      >
        <Table
          size="small"
          rowKey="id"
          columns={costColumns}
          dataSource={campaignCosts}
          pagination={{ pageSize: 5 }}
          locale={{ emptyText: t("Chưa ghi nhận chi phí nào") }}
        />
      </SectionCard>
    </div>
  );
}

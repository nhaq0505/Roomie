import {
  EditOutlined,
  LinkOutlined,
  PlusOutlined,
  UsergroupAddOutlined,
  WalletOutlined,
} from '@ant-design/icons';
import { Button, Col, Row, Space, Table } from 'antd';
import type { TableProps } from 'antd';
import { useState } from 'react';
import { formatVnd } from '../../../shared/format';
import { t } from '../../../shared/i18n/runtime';
import { SectionCard } from '../../../shared/ui/Ui';
import { CampaignCostModal } from './CampaignCostModal';
import {
  CacNullReasonTag,
  MonitoringErrorState,
  MonitoringStatCard,
  UnconfiguredNotice,
} from './MonitoringState';
import { NativeMultiLineChart } from './NativeCharts';
import { UtmLinkBuilderModal } from './UtmLinkBuilderModal';
import type {
  AdminCampaignCostItemDto,
  AdminMonitoringRevenueResponse,
  SubscriptionOrderRevenueDto,
  UpsertCampaignCostRequest,
} from './types';

interface RevenueTabProps {
  data?: AdminMonitoringRevenueResponse | null;
  campaignCostsList?: AdminCampaignCostItemDto[];
  isLoading?: boolean;
  error?: Error | unknown;
  onRetry?: () => void;
  is501?: boolean;
  onUpsertCampaignCost?: (input: UpsertCampaignCostRequest) => void;
  isSavingCost?: boolean;
}

export function RevenueTab({
  data,
  campaignCostsList = [],
  isLoading,
  error,
  onRetry,
  is501,
  onUpsertCampaignCost,
  isSavingCost,
}: RevenueTabProps) {
  const [costModalOpen, setCostModalOpen] = useState(false);
  const [utmModalOpen, setUtmModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<AdminCampaignCostItemDto | null>(null);

  // Subscription Orders table columns
  const orderColumns: TableProps<SubscriptionOrderRevenueDto>['columns'] = [
    {
      title: t("Mã đơn hàng"),
      dataIndex: 'orderCode',
      key: 'orderCode',
      render: (val: number) => <strong>#{val}</strong>,
    },
    {
      title: t("Chủ trọ"),
      dataIndex: 'ownerId',
      key: 'ownerId',
      render: (val: string) => <code>{val ? `${val.slice(0, 8)}...` : '—'}</code>,
    },
    {
      title: t("Số tiền"),
      dataIndex: 'amountVnd',
      key: 'amountVnd',
      align: 'right',
      render: (val: number) => <strong style={{ color: '#17a486' }}>{formatVnd(val)}</strong>,
    },
    {
      title: t("Thời hạn (ngày)"),
      dataIndex: 'days',
      key: 'days',
      align: 'right',
      render: (val?: number | null) => (val ? `${val} ${t("ngày")}` : '—'),
    },
    {
      title: t("Thời gian thanh toán"),
      dataIndex: 'paidAt',
      key: 'paidAt',
      render: (val: string) => (val ? new Date(val).toLocaleString('vi-VN') : '—'),
    },
  ];

  // Campaign cost entries columns
  const costColumns: TableProps<AdminCampaignCostItemDto>['columns'] = [
    { title: t("Tên chiến dịch"), dataIndex: 'campaign', key: 'campaign', render: (val: string) => <strong>{val}</strong> },
    { title: t("Kênh"), dataIndex: 'channel', key: 'channel', render: (val?: string | null) => val || '—' },
    { title: t("Nguồn"), dataIndex: 'source', key: 'source', render: (val?: string | null) => val || '—' },
    { title: t("Ngày ghi nhận"), dataIndex: 'date', key: 'date' },
    {
      title: t("Tiền mặt (VND)"),
      dataIndex: 'cashVnd',
      key: 'cash',
      align: 'right',
      render: (val: number) => formatVnd(val),
    },
    {
      title: t("Giờ công"),
      dataIndex: 'personHours',
      key: 'hours',
      align: 'right',
      render: (val: number, r) => `${val}h${r.hourlyRate ? ` @ ${formatVnd(r.hourlyRate)}/h` : ''}`,
    },
    {
      title: t("Tổng chi phí"),
      key: 'totalCost',
      align: 'right',
      render: (_, r) => {
        const total = (r.cashVnd || 0) + (r.personHours || 0) * (r.hourlyRate || 0);
        return <strong style={{ color: '#17a486' }}>{formatVnd(total)}</strong>;
      },
    },
    { title: t("Người cập nhật"), dataIndex: 'updatedByName', key: 'updater' },
    {
      title: t("Hành động"),
      key: 'actions',
      align: 'center',
      render: (_, record) => (
        <Button
          size="small"
          icon={<EditOutlined />}
          onClick={() => {
            setEditingItem(record);
            setCostModalOpen(true);
          }}
        >
          {t("Sửa")}
        </Button>
      ),
    },
  ];

  if (is501) {
    return (
      <div className="revenue-tab-content">
        <UnconfiguredNotice
          message={<strong>{t("Đang cập nhật chỉ số doanh thu")}</strong>}
          description={t(
            "Backend dịch vụ thống kê doanh thu và chi phí đang được kết nối theo Task T1. Quản lý chi phí chiến dịch độc lập dưới đây đã sẵn sàng."
          )}
        />
      </div>
    );
  }

  if (error) {
    return <MonitoringErrorState error={error} onRetry={onRetry} />;
  }

  const summary = data?.summary;
  const orders = data?.subscriptionOrders ?? [];
  const dailySeries = data?.dailySeries ?? [];

  // Trends chart
  const dates = dailySeries.map((d) => d.date);
  const trendSeries = [
    { name: t("Doanh thu Pro"), color: '#17a486', data: dailySeries.map((d) => d.subscriptionRevenueVnd) },
    { name: t("Chi phí tiếp thị"), color: '#9f443b', data: dailySeries.map((d) => d.marketingSpendVnd) },
  ];

  return (
    <div className="revenue-tab-content">
      <CampaignCostModal
        open={costModalOpen}
        onClose={() => {
          setCostModalOpen(false);
          setEditingItem(null);
        }}
        initialItem={editingItem}
        onSubmit={(input) => {
          onUpsertCampaignCost?.(input);
        }}
        isLoading={isSavingCost}
      />

      <UtmLinkBuilderModal
        open={utmModalOpen}
        onClose={() => setUtmModalOpen(false)}
      />

      {/* KPI Cards */}
      <Row gutter={[16, 16]} className="stat-grid" style={{ marginBottom: 18 }}>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Doanh thu Pro")}
            value={summary?.totalSubscriptionRevenueVnd !== undefined ? formatVnd(summary.totalSubscriptionRevenueVnd) : null}
            icon={<WalletOutlined />}
            colorClass="stat-teal"
            isLoading={isLoading}
            subtext={
              <span className="stat-muted">
                {t("Chỉ tính gói Pro thanh toán thành công qua cổng (đã đối soát, trừ gói Admin tặng và tiền thuê phòng)")}
              </span>
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Lượt thanh toán Pro")}
            value={summary?.paidSubscriptionCount}
            icon={<WalletOutlined />}
            colorClass="stat-green"
            isLoading={isLoading}
            subtext={<span className="stat-muted">Central webhook / reconcile deduped</span>}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Tổng chi phí tiếp thị")}
            value={summary?.totalMarketingSpendVnd !== undefined ? formatVnd(summary.totalMarketingSpendVnd) : null}
            icon={<UsergroupAddOutlined />}
            colorClass="stat-amber"
            isLoading={isLoading}
            subtext={
              summary?.totalMarketingHours ? (
                <span className="stat-muted">{summary.totalMarketingHours} {t("giờ công")}</span>
              ) : undefined
            }
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <MonitoringStatCard
            title={t("Chi phí chuyển đổi chủ trọ (CAC30)")}
            value={
              summary?.cac30?.isAvailable && summary.cac30.valueVnd !== null
                ? formatVnd(summary.cac30.valueVnd)
                : summary?.cac30?.nullReason ? (
                  <CacNullReasonTag reason={summary.cac30.nullReason} />
                ) : null
            }
            icon={<WalletOutlined />}
            colorClass="stat-blue"
            isLoading={isLoading}
            subtext={
              <span className="stat-muted">
                {t("Tổng chi phí kỳ / Số chủ trọ Pro mới trong 30 ngày")}
              </span>
            }
          />
        </Col>
      </Row>

      {/* Daily Revenue Trends Chart */}
      {dailySeries.length > 0 && (
        <div style={{ marginBottom: 18 }}>
          <SectionCard title={t("Xu hướng doanh thu & chi phí")}>
            <NativeMultiLineChart dates={dates} series={trendSeries} height={220} />
          </SectionCard>
        </div>
      )}

      {/* Manual Campaign Cost Management */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Quản lý chi phí thủ công")}
          extra={
            <Space size={8}>
              <Button
                icon={<LinkOutlined />}
                onClick={() => setUtmModalOpen(true)}
              >
                {t("Tạo liên kết UTM")}
              </Button>
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => {
                  setEditingItem(null);
                  setCostModalOpen(true);
                }}
                style={{ background: '#17a486', borderColor: '#17a486' }}
              >
                {t("Thêm chi phí chiến dịch")}
              </Button>
            </Space>
          }
        >
          <Table
            size="small"
            rowKey="id"
            columns={costColumns}
            dataSource={campaignCostsList}
            pagination={{ pageSize: 5 }}
            locale={{ emptyText: t("Chưa ghi nhận chi phí nào") }}
          />
        </SectionCard>
      </div>

      {/* Subscription Orders */}
      <SectionCard
        title={t("Đơn đăng ký gói Pro")}
        extra={
          <span style={{ fontSize: 13, color: '#7a8c91' }}>
            {t("Đã đối soát qua cổng thanh toán")}
          </span>
        }
      >
        <Table
          size="small"
          rowKey="orderCode"
          columns={orderColumns}
          dataSource={orders}
          pagination={{ pageSize: 5 }}
          locale={{ emptyText: t("Chưa có dữ liệu") }}
        />
      </SectionCard>
    </div>
  );
}

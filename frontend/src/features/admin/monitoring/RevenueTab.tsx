import {
  CopyOutlined,
  DownloadOutlined,
  EditOutlined,
  LinkOutlined,
  PlusOutlined,
  QrcodeOutlined,
  UsergroupAddOutlined,
  WalletOutlined,
} from '@ant-design/icons';
import { App, Button, Col, Form, Input, Modal, QRCode, Row, Space, Switch, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import { useRef, useState } from 'react';
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
  CampaignRoasRowDto,
  CreateCampaignRequest,
  MarketingCampaignDto,
  SubscriptionOrderRevenueDto,
  UpdateCampaignRequest,
  UpsertCampaignCostRequest,
} from './types';

interface RevenueTabProps {
  data?: AdminMonitoringRevenueResponse | null;
  campaignCostsList?: AdminCampaignCostItemDto[];
  campaignsList?: MarketingCampaignDto[];
  isLoading?: boolean;
  error?: Error | unknown;
  onRetry?: () => void;
  is501?: boolean;
  onUpsertCampaignCost?: (input: UpsertCampaignCostRequest) => void;
  isSavingCost?: boolean;
  onCreateCampaign?: (input: CreateCampaignRequest) => Promise<void>;
  onUpdateCampaign?: (id: string, input: UpdateCampaignRequest) => Promise<void>;
  isSavingCampaign?: boolean;
  requestedFromDate?: string;
  requestedToDate?: string;
}

export function RevenueTab({
  data,
  campaignCostsList = [],
  campaignsList = [],
  isLoading,
  error,
  onRetry,
  is501,
  onUpsertCampaignCost,
  isSavingCost,
  onCreateCampaign,
  onUpdateCampaign,
  isSavingCampaign,
  requestedFromDate,
  requestedToDate,
}: RevenueTabProps) {
  const { message } = App.useApp();
  const [costModalOpen, setCostModalOpen] = useState(false);
  const [utmModalOpen, setUtmModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<AdminCampaignCostItemDto | null>(null);
  const [campaignModalOpen, setCampaignModalOpen] = useState(false);
  const [editingCampaign, setEditingCampaign] = useState<MarketingCampaignDto | null>(null);
  const [qrPreviewCampaign, setQrPreviewCampaign] = useState<MarketingCampaignDto | null>(null);
  const [campaignForm] = Form.useForm();
  const qrSvgContainerRef = useRef<HTMLDivElement | null>(null);

  const openCreateCampaignModal = () => {
    setEditingCampaign(null);
    campaignForm.setFieldsValue({
      code: '',
      name: '',
      channel: 'paid_social',
      source: 'facebook',
      utmMedium: 'cpc',
      promoCode: '',
      targetUrl: '/register',
      isActive: true,
    });
    setCampaignModalOpen(true);
  };

  const openEditCampaignModal = (record: MarketingCampaignDto) => {
    setEditingCampaign(record);
    campaignForm.setFieldsValue({
      code: record.code,
      name: record.name,
      channel: record.channel || '',
      source: record.source || '',
      utmMedium: record.utmMedium || '',
      promoCode: record.promoCode || '',
      targetUrl: record.targetUrl || '/register',
      isActive: record.isActive,
    });
    setCampaignModalOpen(true);
  };

  const handleSaveCampaign = async () => {
    try {
      const vals = await campaignForm.validateFields();
      if (editingCampaign) {
        await onUpdateCampaign?.(editingCampaign.id, {
          name: vals.name,
          channel: vals.channel || null,
          source: vals.source || null,
          utmMedium: vals.utmMedium || null,
          promoCode: vals.promoCode ? vals.promoCode.trim().toUpperCase() : null,
          targetUrl: vals.targetUrl || '/register',
          isActive: Boolean(vals.isActive),
        });
      } else {
        await onCreateCampaign?.({
          code: vals.code.trim().toLowerCase(),
          name: vals.name.trim(),
          channel: vals.channel || null,
          source: vals.source || null,
          utmMedium: vals.utmMedium || null,
          promoCode: vals.promoCode ? vals.promoCode.trim().toUpperCase() : null,
          targetUrl: vals.targetUrl || '/register',
        });
      }
      setCampaignModalOpen(false);
      setEditingCampaign(null);
    } catch (err) {
      if (err instanceof Error && err.message) {
        message.error(err.message);
      }
    }
  };

  const handleDownloadCampaignQrSvg = (code: string) => {
    const svgEl = qrSvgContainerRef.current?.querySelector('svg');
    if (!svgEl) return;
    const svgData = new XMLSerializer().serializeToString(svgEl);
    const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `qr-${code}.svg`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const roasColumns: TableProps<CampaignRoasRowDto>['columns'] = [
    {
      title: t("Chiến dịch"),
      dataIndex: 'campaign',
      key: 'campaign',
      render: (val: string, r: CampaignRoasRowDto) => (
        <div>
          <strong>{r.name && r.name !== val ? r.name : val}</strong>
          <div style={{ fontSize: 12, color: '#64748b' }}>
            <code>{val}</code>
            {r.promoCode ? (
              <Tag color="gold" style={{ marginLeft: 6 }}>
                {r.promoCode}
              </Tag>
            ) : null}
          </div>
        </div>
      ),
    },
    {
      title: t("Kênh / Nguồn"),
      key: 'channelSource',
      render: (_: unknown, r: CampaignRoasRowDto) => (
        <span>
          {[r.channel, r.source].filter(Boolean).join(' / ') || '—'}
        </span>
      ),
    },
    {
      title: t("Đăng ký"),
      dataIndex: 'registrations',
      key: 'registrations',
      align: 'right',
    },
    {
      title: t("Kích hoạt"),
      dataIndex: 'activatedOwners',
      key: 'activatedOwners',
      align: 'right',
    },
    {
      title: t("Mua Pro"),
      dataIndex: 'paidOrders',
      key: 'paidOrders',
      align: 'right',
      render: (val: number) => <strong>{val}</strong>,
    },
    {
      title: t("Chi phí"),
      dataIndex: 'totalCostVnd',
      key: 'totalCostVnd',
      align: 'right',
      render: (val: number) => formatVnd(val),
    },
    {
      title: t("Doanh thu Pro"),
      dataIndex: 'revenueVnd',
      key: 'revenueVnd',
      align: 'right',
      render: (val: number) => <strong style={{ color: '#17a486' }}>{formatVnd(val)}</strong>,
    },
    {
      title: t("CPL / CAC"),
      dataIndex: 'cplVnd',
      key: 'cplVnd',
      align: 'right',
      render: (val?: number | null) => (val !== null && val !== undefined ? formatVnd(val) : '—'),
    },
    {
      title: t("ROAS"),
      dataIndex: 'roasPercent',
      key: 'roasPercent',
      align: 'right',
      render: (val?: number | null) =>
        val !== null && val !== undefined ? (
          <Tag color={val >= 100 ? 'green' : 'orange'}>
            <strong>{val}%</strong>
          </Tag>
        ) : (
          '—'
        ),
    },
  ];

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
      render: (val: number, r) => `${val}h${r.hourlyRate !== null && r.hourlyRate !== undefined ? ` @ ${formatVnd(r.hourlyRate)}/h` : ''}`,
    },
    {
      title: t("Tổng chi phí"),
      key: 'totalCost',
      align: 'right',
      render: (_, r) => {
        if (r.personHours > 0 && (r.hourlyRate === null || r.hourlyRate === undefined)) {
          return (
            <Space size={4}>
              <strong style={{ color: '#17a486' }}>{formatVnd(r.cashVnd || 0)}</strong>
              <Tag color="warning">{t("Thiếu đơn giá giờ công")}</Tag>
            </Space>
          );
        }
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

  const campaignColumns: TableProps<MarketingCampaignDto>['columns'] = [
    {
      title: t("Chiến dịch"),
      key: 'nameCode',
      render: (_, r) => (
        <div>
          <strong>{r.name}</strong>
          <div style={{ fontSize: 12, color: '#64748b' }}>
            <code>{r.code}</code>
          </div>
        </div>
      ),
    },
    {
      title: t("Kênh / Nguồn / Medium"),
      key: 'channelSourceMedium',
      render: (_, r) => (
        <span>
          {[r.channel, r.source, r.utmMedium].filter(Boolean).join(' / ') || '—'}
        </span>
      ),
    },
    {
      title: t("Mã Auto-Promo"),
      dataIndex: 'promoCode',
      key: 'promoCode',
      render: (val?: string | null) =>
        val ? <Tag color="gold">{val}</Tag> : <span style={{ color: '#94a3b8' }}>—</span>,
    },
    {
      title: t("Trạng thái"),
      dataIndex: 'isActive',
      key: 'isActive',
      render: (val: boolean) => (
        <Tag color={val ? 'green' : 'default'}>{val ? t("Đang chạy") : t("Tạm dừng")}</Tag>
      ),
    },
    {
      title: t("Liên kết theo dõi & QR"),
      key: 'trackingUrl',
      render: (_, r) => (
        <Space size={6} wrap>
          <Button
            size="small"
            icon={<CopyOutlined />}
            onClick={async () => {
              try {
                if (typeof navigator !== 'undefined' && navigator.clipboard) {
                  await navigator.clipboard.writeText(r.trackingUrl || 'https://roomie.io.vn');
                }
                message.success(t("Đã sao chép liên kết."));
              } catch {
                message.error(t("Không thể sao chép liên kết."));
              }
            }}
          >
            {t("Sao chép link")}
          </Button>
          <Button
            size="small"
            icon={<QrcodeOutlined />}
            onClick={() => setQrPreviewCampaign(r)}
          >
            QR SVG
          </Button>
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={() => openEditCampaignModal(r)}
          >
            {t("Sửa")}
          </Button>
        </Space>
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
  const campaignRoas = data?.campaignRoas ?? [];

  // Trends chart
  const dates = dailySeries.map((d) => d.date);
  const trendSeries = [
    { name: t("Doanh thu Pro"), color: '#17a486', data: dailySeries.map((d) => d.subscriptionRevenueVnd) },
    { name: t("Chi phí tiếp thị"), color: '#9f443b', data: dailySeries.map((d) => d.marketingSpendVnd) },
  ];

  const isClamped = Boolean(
    (data?.effectiveFromDate && requestedFromDate && data.effectiveFromDate !== requestedFromDate) ||
    (data?.effectiveToDate && requestedToDate && data.effectiveToDate !== requestedToDate)
  );

  return (
    <div className="revenue-tab-content">
      {/* Effective date range boundary indicator */}
      {data?.effectiveFromDate && data?.effectiveToDate && (
        <div className="monitoring-effective-range" style={{ marginBottom: 14 }}>
          <Tag color={isClamped ? "warning" : "processing"} style={{ borderRadius: 6, padding: '3px 8px' }}>
            {t("Dữ liệu thực tế: {0} đến {1}", [data.effectiveFromDate, data.effectiveToDate])}
          </Tag>
        </div>
      )}

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

      <Modal
        title={editingCampaign ? t("Cập nhật chiến dịch tiếp thị") : t("Tạo chiến dịch tiếp thị mới")}
        open={campaignModalOpen}
        onCancel={() => {
          setCampaignModalOpen(false);
          setEditingCampaign(null);
        }}
        onOk={() => void handleSaveCampaign()}
        confirmLoading={isSavingCampaign}
        okText={t("Lưu chiến dịch")}
        cancelText={t("Hủy")}
        destroyOnClose
      >
        <Form form={campaignForm} layout="vertical" style={{ marginTop: 12 }}>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item
                name="code"
                label={t("Mã chiến dịch (utm_campaign)")}
                rules={[{ required: true, message: t("Vui lòng nhập mã chiến dịch") }]}
              >
                <Input disabled={Boolean(editingCampaign)} placeholder="vd: fb_group_t10" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="name"
                label={t("Tên chiến dịch")}
                rules={[{ required: true, message: t("Vui lòng nhập tên chiến dịch") }]}
              >
                <Input placeholder={t("vd: Hội chủ trọ Facebook T10")} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={8}>
              <Form.Item name="channel" label={t("Kênh")}>
                <Input placeholder="vd: paid_social" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="source" label={t("Nguồn (utm_source)")}>
                <Input placeholder="vd: facebook" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="utmMedium" label={t("Medium (utm_medium)")}>
                <Input placeholder="vd: cpc" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="promoCode" label={t("Mã ưu đãi tự động (promo)")}>
                <Input placeholder="vd: TET2026" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="targetUrl" label={t("Trang đích")}>
                <Input placeholder="/register" />
              </Form.Item>
            </Col>
          </Row>
          {editingCampaign && (
            <Form.Item name="isActive" label={t("Đang hoạt động")} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
        </Form>
      </Modal>

      <Modal
        title={qrPreviewCampaign ? `${t("Mã QR & Liên kết chiến dịch")}: ${qrPreviewCampaign.name}` : t("Mã QR")}
        open={Boolean(qrPreviewCampaign)}
        onCancel={() => setQrPreviewCampaign(null)}
        footer={[
          <Button key="close" onClick={() => setQrPreviewCampaign(null)}>
            {t("Đóng")}
          </Button>,
          <Button
            key="download"
            type="primary"
            icon={<DownloadOutlined />}
            onClick={() => qrPreviewCampaign && handleDownloadCampaignQrSvg(qrPreviewCampaign.code)}
            style={{ background: '#17a486', borderColor: '#17a486' }}
          >
            {t("Tải QR SVG")}
          </Button>,
        ]}
      >
        {qrPreviewCampaign && (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 16, padding: '12px 0' }}>
            <div ref={qrSvgContainerRef} style={{ background: '#fff', padding: 12, borderRadius: 12, border: '1px solid #d3e7e2' }}>
              <QRCode type="svg" value={qrPreviewCampaign.trackingUrl || 'https://roomie.io.vn'} size={180} bordered={false} />
            </div>
            <Typography.Paragraph code copyable style={{ wordBreak: 'break-all', marginBottom: 0 }}>
              {qrPreviewCampaign.trackingUrl || 'https://roomie.io.vn'}
            </Typography.Paragraph>
          </div>
        )}
      </Modal>

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
            subtext={<span className="stat-muted">{t("Đã đối soát & loại trừ trùng lặp")}</span>}
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

      {/* Campaign ROAS & ROI Table */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Hiệu quả chiến dịch (ROAS & ROI)")}
          extra={
            <span style={{ fontSize: 13, color: '#7a8c91' }}>
              {t("Đối chiếu chi phí, lượt kích hoạt và doanh thu Pro theo utm_campaign")}
            </span>
          }
        >
          <div className="monitoring-table-wrapper">
            <Table
              size="small"
              rowKey="campaign"
              columns={roasColumns}
              dataSource={campaignRoas}
              pagination={{ pageSize: 5 }}
              locale={{ emptyText: t("Chưa có dữ liệu chiến dịch trong kỳ") }}
              scroll={{ x: 800 }}
            />
          </div>
        </SectionCard>
      </div>

      {/* Campaign Directory & Tracking Links */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Danh mục chiến dịch & Liên kết Auto-Promo / QR")}
          extra={
            <Space size={8} wrap>
              <Button icon={<LinkOutlined />} onClick={() => setUtmModalOpen(true)}>
                {t("Trình tạo UTM tự do")}
              </Button>
              <Button
                type="primary"
                icon={<PlusOutlined />}
                onClick={openCreateCampaignModal}
                style={{ background: '#17a486', borderColor: '#17a486' }}
              >
                {t("Tạo chiến dịch mới")}
              </Button>
            </Space>
          }
        >
          <div className="monitoring-table-wrapper">
            <Table
              size="small"
              rowKey="id"
              columns={campaignColumns}
              dataSource={campaignsList}
              pagination={{ pageSize: 5 }}
              locale={{ emptyText: t("Chưa có chiến dịch nào được lưu") }}
              scroll={{ x: 800 }}
            />
          </div>
        </SectionCard>
      </div>

      {/* Manual Campaign Cost Management */}
      <div style={{ marginBottom: 18 }}>
        <SectionCard
          title={t("Quản lý chi phí thủ công")}
          extra={
            <Space size={8} wrap>
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
          <div className="monitoring-table-wrapper">
            <Table
              size="small"
              rowKey="id"
              columns={costColumns}
              dataSource={campaignCostsList}
              pagination={{ pageSize: 5 }}
              locale={{ emptyText: t("Chưa ghi nhận chi phí nào") }}
              scroll={{ x: 800 }}
            />
          </div>
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
        <div className="monitoring-table-wrapper">
          <Table
            size="small"
            rowKey="orderCode"
            columns={orderColumns}
            dataSource={orders}
            pagination={{ pageSize: 5 }}
            locale={{ emptyText: t("Chưa có dữ liệu") }}
            scroll={{ x: 550 }}
          />
        </div>
      </SectionCard>
    </div>
  );
}

import {
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  GiftOutlined,
  LinkOutlined,
  PlusOutlined,
  TagOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  App,
  Button,
  Col,
  DatePicker,
  Drawer,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { promotionsApi, type PromotionCode, type PromotionRedemptionItem } from './index';

export default function AdminPromotionsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();

  const query = useQuery({
    queryKey: ['admin-promotions'],
    queryFn: promotionsApi.get,
  });

  const [welcomeForm] = Form.useForm();
  useEffect(() => {
    if (query.data?.welcome) {
      const w = query.data.welcome;
      welcomeForm.setFieldsValue({
        isEnabled: w.isEnabled,
        rewardMonths: w.rewardMonths,
        dateRange: w.startsAt && w.endsAt ? [dayjs(w.startsAt), dayjs(w.endsAt)] : undefined,
      });
    }
  }, [query.data?.welcome, welcomeForm]);

  const welcomeMutation = useMutation({
    mutationFn: (values: { isEnabled: boolean; rewardMonths: number; dateRange?: [dayjs.Dayjs, dayjs.Dayjs] }) =>
      promotionsApi.updateWelcome({
        isEnabled: values.isEnabled,
        rewardMonths: values.rewardMonths,
        startsAt: values.dateRange?.[0] ? values.dateRange[0].startOf('day').toISOString() : null,
        endsAt: values.dateRange?.[1] ? values.dateRange[1].endOf('day').toISOString() : null,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-promotions'] });
      message.success(t("Đã cập nhật chiến dịch chào mừng thành công."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  // Voucher Code Modal State
  const [modalOpen, setModalOpen] = useState(false);
  const [editingCode, setEditingCode] = useState<PromotionCode | null>(null);
  const [viewingRedemptionsCode, setViewingRedemptionsCode] = useState<PromotionCode | null>(null);
  const [codeForm] = Form.useForm();

  const redemptionsQuery = useQuery({
    queryKey: ['admin-promotion-redemptions', viewingRedemptionsCode?.id],
    queryFn: () => promotionsApi.redemptions(viewingRedemptionsCode!.id),
    enabled: Boolean(viewingRedemptionsCode?.id),
  });

  const openCreateModal = () => {
    setEditingCode(null);
    codeForm.resetFields();
    codeForm.setFieldsValue({
      rewardMonths: 1,
      isActive: true,
    });
    setModalOpen(true);
  };

  const openEditModal = (record: PromotionCode) => {
    setEditingCode(record);
    codeForm.resetFields();
    codeForm.setFieldsValue({
      code: record.code,
      description: record.description,
      rewardMonths: record.rewardMonths,
      maxRedemptions: record.maxRedemptions,
      expiresAt: record.expiresAt ? dayjs(record.expiresAt) : undefined,
      isActive: record.isActive,
    });
    setModalOpen(true);
  };

  const saveCodeMutation = useMutation({
    mutationFn: (values: {
      code: string;
      description?: string;
      rewardMonths: number;
      maxRedemptions?: number | null;
      expiresAt?: dayjs.Dayjs | null;
      isActive: boolean;
    }) => {
      const payload = {
        description: values.description?.trim(),
        rewardMonths: values.rewardMonths,
        maxRedemptions: values.maxRedemptions ?? null,
        expiresAt: values.expiresAt ? values.expiresAt.endOf('day').toISOString() : null,
        isActive: values.isActive ?? true,
      };
      if (editingCode) {
        return promotionsApi.updateCode(editingCode.id, payload);
      }
      return promotionsApi.createCode({
        code: values.code.trim().toUpperCase(),
        ...payload,
      });
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-promotions'] });
      message.success(editingCode ? t("Đã cập nhật mã khuyến mãi.") : t("Đã tạo mã khuyến mãi mới."));
      setModalOpen(false);
    },
    onError: (e: Error) => message.error(e.message),
  });

  const toggleStatusMutation = useMutation({
    mutationFn: (record: PromotionCode) =>
      promotionsApi.updateCode(record.id, {
        description: record.description ?? undefined,
        rewardMonths: record.rewardMonths,
        maxRedemptions: record.maxRedemptions,
        expiresAt: record.expiresAt,
        isActive: !record.isActive,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-promotions'] });
      message.success(t("Đã cập nhật trạng thái mã."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const deleteCodeMutation = useMutation({
    mutationFn: (id: string) => promotionsApi.deleteCode(id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-promotions'] });
      message.success(t("Đã xóa mã khuyến mãi."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  if (query.isError) {
    return <LoadError error={query.error} onRetry={() => void query.refetch()} />;
  }

  const copyRegisterPromoLink = async (code: string) => {
    const origin = typeof window !== 'undefined' ? window.location.origin : 'https://roomie.io.vn';
    const link = `${origin}/register?promo=${encodeURIComponent(code)}`;
    try {
      await navigator.clipboard.writeText(link);
      message.success(t("Đã sao chép link đăng ký kèm mã {0}", [code]));
    } catch {
      message.info(link);
    }
  };

  const columns = [
    {
      title: t("Mã voucher"),
      dataIndex: 'code',
      key: 'code',
      render: (code: string) => (
        <Space size={6}>
          <Tag color="blue" style={{ fontSize: 13, padding: '2px 8px', fontWeight: 600 }}>
            <TagOutlined style={{ marginRight: 4 }} />
            {code}
          </Tag>
          <Tooltip title={t("Sao chép mã {0}", [code])}>
            <Button
              type="text"
              size="small"
              icon={<CopyOutlined />}
              aria-label={t("Sao chép mã {0}", [code])}
              onClick={async () => {
                try {
                  await navigator.clipboard.writeText(code);
                  message.success(t("Đã sao chép mã {0}", [code]));
                } catch {
                  message.info(code);
                }
              }}
            />
          </Tooltip>
          <Tooltip title={t("Sao chép link đăng ký tự gắn mã")}>
            <Button
              type="text"
              size="small"
              icon={<LinkOutlined />}
              aria-label={t("Sao chép link đăng ký kèm mã {0}", [code])}
              onClick={() => void copyRegisterPromoLink(code)}
            />
          </Tooltip>
        </Space>
      ),
    },
    {
      title: t("Mô tả"),
      dataIndex: 'description',
      key: 'description',
      render: (desc?: string) => desc || <Typography.Text type="secondary">—</Typography.Text>,
    },
    {
      title: t("Tặng thưởng"),
      dataIndex: 'rewardMonths',
      key: 'rewardMonths',
      render: (months: number) => (
        <Tag color="gold">
          <strong>{t("+{0} tháng Pro", [months])}</strong>
        </Tag>
      ),
    },
    {
      title: t("Lượt sử dụng"),
      key: 'usage',
      render: (_: unknown, record: PromotionCode) => (
        <Space size={6}>
          <span>
            <strong>{record.redeemedCount}</strong> / {record.maxRedemptions ? record.maxRedemptions : '∞'}
          </span>
          <Button
            type="link"
            size="small"
            icon={<TeamOutlined />}
            onClick={() => setViewingRedemptionsCode(record)}
          >
            {t("Danh sách")}
          </Button>
        </Space>
      ),
    },
    {
      title: t("Hạn dùng"),
      dataIndex: 'expiresAt',
      key: 'expiresAt',
      render: (exp?: string) =>
        exp ? dayjs(exp).format(dateFormat()) : <Typography.Text type="secondary">{t("Không thời hạn")}</Typography.Text>,
    },
    {
      title: t("Kích hoạt"),
      dataIndex: 'isActive',
      key: 'isActive',
      render: (active: boolean, record: PromotionCode) => (
        <Switch
          checked={active}
          loading={toggleStatusMutation.isPending && toggleStatusMutation.variables?.id === record.id}
          onChange={() => toggleStatusMutation.mutate(record)}
        />
      ),
    },
    {
      title: t("THAO TÁC"),
      key: 'actions',
      render: (_: unknown, record: PromotionCode) => (
        <Space size={8}>
          <Button
            type="text"
            icon={<EditOutlined />}
            aria-label={t("Sửa mã {0}", [record.code])}
            onClick={() => openEditModal(record)}
          />
          <Popconfirm
            title={t("Xóa mã khuyến mãi này?")}
            description={t("Thao tác này sẽ xóa mã và toàn bộ lịch sử kích hoạt của mã.")}
            okText={t("Xóa")}
            cancelText={t("Hủy")}
            okButtonProps={{ danger: true }}
            onConfirm={() => deleteCodeMutation.mutate(record.id)}
          >
            <Button
              type="text"
              danger
              icon={<DeleteOutlined />}
              aria-label={t("Xóa mã {0}", [record.code])}
              loading={deleteCodeMutation.isPending && deleteCodeMutation.variables === record.id}
            />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <PageTitle
        eyebrow={t("QUẢN TRỊ HỆ THỐNG")}
        title={t("Chương trình Khuyến mãi")}
        subtitle={t("Cấu hình ưu đãi tặng Pro cho thành viên mới và phát hành mã voucher.")}
      />

      <div style={{ marginBottom: 24 }}>
        <SectionCard
          title={t("Chiến dịch chào mừng: Tặng Pro khi đăng ký mới")}
        >
          <Form
            form={welcomeForm}
            layout="vertical"
            onFinish={(values) => welcomeMutation.mutate(values)}
          >
            <Row gutter={[24, 16]}>
              <Col xs={24} sm={8}>
                <Form.Item
                  label={t("Trạng thái chiến dịch")}
                  name="isEnabled"
                  valuePropName="checked"
                >
                  <Switch
                    checkedChildren={t("Đang bật")}
                    unCheckedChildren={t("Đang tắt")}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={8}>
                <Form.Item
                  label={t("Số tháng Pro tặng kèm")}
                  name="rewardMonths"
                  rules={[{ required: true, message: t("Vui lòng nhập số tháng tặng.") }]}
                >
                  <InputNumber
                    min={1}
                    max={120}
                    addonAfter={t("tháng")}
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} sm={8}>
                <Form.Item
                  label={t("Thời gian áp dụng (Bỏ trống = Vô thời hạn)")}
                  name="dateRange"
                >
                  <DatePicker.RangePicker
                    format={dateFormat()}
                    placeholder={[t("Từ ngày"), t("Đến ngày")]}
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              </Col>
            </Row>

            <Button
              type="primary"
              htmlType="submit"
              loading={welcomeMutation.isPending}
            >
              {t("Lưu cấu hình chiến dịch")}
            </Button>
          </Form>
        </SectionCard>
      </div>

      <SectionCard
        title={t("Danh sách Mã khuyến mãi / Voucher")}
        extra={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openCreateModal}
          >
            {t("Tạo mã mới")}
          </Button>
        }
      >
        <Table<PromotionCode>
          rowKey="id"
          columns={columns}
          dataSource={query.data?.codes ?? []}
          loading={query.isLoading}
          pagination={{ pageSize: 15 }}
          scroll={{ x: 750 }}
        />
      </SectionCard>

      <Modal
        open={modalOpen}
        title={editingCode ? t("Sửa mã khuyến mãi") : t("Tạo mã khuyến mãi mới")}
        okText={editingCode ? t("Lưu thay đổi") : t("Tạo mã")}
        cancelText={t("Hủy")}
        confirmLoading={saveCodeMutation.isPending}
        onCancel={() => setModalOpen(false)}
        onOk={() => codeForm.submit()}
        destroyOnClose
      >
        <Form
          form={codeForm}
          layout="vertical"
          style={{ marginTop: 16 }}
          onFinish={(values) => saveCodeMutation.mutate(values)}
        >
          <Form.Item
            label={t("Mã khuyến mãi")}
            name="code"
            normalize={(value) => (typeof value === 'string' ? value.toUpperCase() : value)}
            rules={[
              { required: true, message: t("Vui lòng nhập mã khuyến mãi.") },
              { min: 3, max: 32, message: t("Mã từ 3 đến 32 ký tự.") },
            ]}
          >
            <Input
              disabled={!!editingCode}
              placeholder={t("Ví dụ: TET2026, BANMOI")}
              prefix={<GiftOutlined style={{ color: '#1890ff' }} />}
            />
          </Form.Item>

          <Form.Item label={t("Ghi chú")} name="description">
            <Input.TextArea maxLength={255} rows={2} placeholder={t("Chiến dịch Tết 2026, tặng đối tác...")} />
          </Form.Item>

          <Row gutter={16}>
            <Col span={12}>
              <Form.Item
                label={t("Số tháng Pro tặng kèm")}
                name="rewardMonths"
                rules={[{ required: true, message: t("Vui lòng nhập số tháng tặng.") }]}
              >
                <InputNumber min={1} max={120} addonAfter={t("tháng")} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label={t("Giới hạn lượt dùng")} name="maxRedemptions">
                <InputNumber min={1} placeholder={t("Không giới hạn")} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>

          <Row gutter={16}>
            <Col span={16}>
              <Form.Item label={t("Ngày hết hạn")} name="expiresAt">
                <DatePicker
                  format={dateFormat()}
                  placeholder={t("Không thời hạn")}
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item label={t("Kích hoạt")} name="isActive" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>

      <Drawer
        open={Boolean(viewingRedemptionsCode)}
        title={
          viewingRedemptionsCode
            ? t("Danh sách chủ trọ đã nhận mã {0}", [viewingRedemptionsCode.code])
            : t("Danh sách kích hoạt mã")
        }
        onClose={() => setViewingRedemptionsCode(null)}
        width={680}
        destroyOnClose
      >
        <Table<PromotionRedemptionItem>
          rowKey="id"
          loading={redemptionsQuery.isLoading}
          dataSource={redemptionsQuery.data ?? []}
          pagination={{ pageSize: 15 }}
          locale={{ emptyText: t("Chưa có chủ trọ nào kích hoạt mã này.") }}
          columns={[
            {
              title: t("Chủ trọ"),
              key: 'owner',
              render: (_: unknown, r: PromotionRedemptionItem) => (
                <div>
                  <div style={{ fontWeight: 600 }}>{r.ownerName}</div>
                  <div style={{ fontSize: 12, color: '#64748b' }}>{r.ownerEmail}</div>
                </div>
              ),
            },
            {
              title: t("Số điện thoại"),
              dataIndex: 'ownerPhone',
              key: 'ownerPhone',
              render: (phone?: string | null) => phone || '—',
            },
            {
              title: t("Tặng thưởng & Trạng thái"),
              key: 'rewardStatus',
              render: (_: unknown, r: PromotionRedemptionItem) => (
                <Space size={4} wrap>
                  <Tag color="gold">
                    <strong>{t("+{0} tháng Pro", [viewingRedemptionsCode?.rewardMonths ?? 1])}</strong>
                  </Tag>
                  <Tag color={r.isActivated ? 'green' : 'default'}>
                    {r.isActivated ? t("Đã kích hoạt") : t("Mới đăng ký")}
                  </Tag>
                </Space>
              ),
            },
            {
              title: t("Thời gian nhận"),
              dataIndex: 'redeemedAt',
              key: 'redeemedAt',
              render: (val: string) => dayjs(val).format(`${dateFormat()} HH:mm`),
            },
          ]}
        />
      </Drawer>
    </>
  );
}

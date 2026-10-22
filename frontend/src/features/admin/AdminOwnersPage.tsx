import { CheckOutlined, CloseOutlined, CrownOutlined, EllipsisOutlined, KeyOutlined, UserOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { MenuProps, TableProps } from 'antd';
import { App, Button, Dropdown, Form, Input, InputNumber, Modal, Radio, Select, Space, Tag, Tooltip, Typography } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { type AccountStatus } from '../auth/index';
import { type SubscriptionTier } from '../subscription/index';
import { ownersApi, type OwnerRequest } from './index';

const statusClass: Record<AccountStatus, string> = { Pending: 'status-pending', Approved: 'status-paid', Rejected: 'status-unpaid', Suspended: 'status-suspended' };

export default function AdminOwnersPage() {
  const statusLabel: Record<AccountStatus, string> = { Pending: t("Chờ duyệt"), Approved: t("Đã duyệt"), Rejected: t("Từ chối"), Suspended: t("Tạm khóa") };
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const [resetOwner, setResetOwner] = useState<OwnerRequest | null>(null);
  const [resetResult, setResetResult] = useState<{ token: string; expiresAt: string } | null>(null);
  const [resetForm] = Form.useForm();
  const resetMutation = useMutation({
    mutationFn: (password: string) => ownersApi.resetPassword(resetOwner!.id, password),
    onSuccess: (result) => { resetForm.resetFields(); setResetResult(result); },
    onError: (error: Error) => message.error(error.message),
  });

  const [subOwner, setSubOwner] = useState<OwnerRequest | null>(null);
  const [subTier, setSubTier] = useState<SubscriptionTier>('Pro');
  const [subForm] = Form.useForm<{ tier: SubscriptionTier; days?: number; reason?: string }>();
  const subMutation = useMutation({
    mutationFn: (values: { tier: SubscriptionTier; days?: number; reason?: string }) =>
      ownersApi.updateSubscription(subOwner!.id, values),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-owners'] });
      message.success(t("Đã cập nhật gói cước cho chủ trọ."));
      setSubOwner(null);
      subForm.resetFields();
    },
    onError: (e: Error) => message.error(e.message),
  });

  const openSubModal = (owner: OwnerRequest) => {
    setSubOwner(owner);
    const initialTier = owner.isPro ? 'Pro' : 'Pro';
    setSubTier(initialTier);
    subForm.resetFields();
    subForm.setFieldsValue({ tier: initialTier, days: 30 });
  };

  const [filter, setFilter] = useState<'All' | AccountStatus>('All');
  const [reasonTarget, setReasonTarget] = useState<{ owner: OwnerRequest; status: AccountStatus } | null>(null);
  const [form] = Form.useForm<{ reason: string }>();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [filter]);
  const ownersQuery = useQuery({ queryKey: ['admin-owners', page, filter], queryFn: () => ownersApi.page(page, filter) });
  const updateMutation = useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: AccountStatus; reason?: string }) => ownersApi.updateStatus(id, { status, reason }),
    onSuccess: async (_, variables) => {
      await client.invalidateQueries({ queryKey: ['admin-owners'] });
      message.success(variables.status === 'Approved' ? t("Đã duyệt tài khoản chủ trọ.") : variables.status === 'Rejected' ? t("Đã từ chối yêu cầu đăng ký.") : t("Đã cập nhật trạng thái tài khoản."));
      setReasonTarget(null);
      form.resetFields();
    },
    onError: (e: Error) => message.error(e.message),
  });

  const owners = ownersQuery.data?.items ?? [];
  const pendingCount = ownersQuery.data?.pendingCount ?? 0;
  const approve = (owner: OwnerRequest) => modal.confirm({ title: t("Duyệt tài khoản {0}?", [owner.name]), content: t("Chủ trọ sẽ có quyền truy cập khu vực quản lý sau khi được duyệt."), okText: t("Duyệt tài khoản"), cancelText: t("Hủy"), onOk: () => updateMutation.mutateAsync({ id: owner.id, status: 'Approved' }) });
  const requestReason = (owner: OwnerRequest, status: AccountStatus) => { setReasonTarget({ owner, status }); form.resetFields(); };
  const submitReason = (values: { reason: string }) => { if (reasonTarget) updateMutation.mutate({ id: reasonTarget.owner.id, status: reasonTarget.status, reason: values.reason.trim() }); };

  const columns: TableProps<OwnerRequest>['columns'] = [
    { title: t("CHỦ TRỌ"), key: 'owner', render: (_, owner) => <div className="owner-name-cell"><div className="owner-avatar"><UserOutlined /></div><div><strong>{owner.name}</strong><span>{owner.email}</span></div></div> },
    { title: t("ĐIỆN THOẠI"), dataIndex: 'phone', render: (phone?: string) => phone || <span className="muted-dash">{t("Chưa cung cấp")}</span> },
    {
      title: t("GÓI CƯỚC"),
      key: 'subscriptionTier',
      render: (_, owner) => owner.isPro ? (
        <Tooltip title={owner.subscriptionExpiresAt ? t("Hết hạn: {0}", [dayjs(owner.subscriptionExpiresAt).format('HH:mm DD/MM/YYYY')]) : t("Đang hoạt động")}>
          <Tag color="gold" icon={<CrownOutlined />}>PRO</Tag>
        </Tooltip>
      ) : (
        <Tag>{t("Miễn phí")}</Tag>
      ),
    },
    {
      title: t("SỐ PHÒNG"),
      key: 'roomCount',
      render: (_, owner) => <span>{owner.roomCount ?? 0} / {owner.isPro ? '∞' : (owner.maxRooms ?? 5)}</span>,
    },
    { title: t("NGÀY ĐĂNG KÝ"), dataIndex: 'createdAt', render: (value?: string) => value ? dayjs(value).format(dateFormat()) : <span className="muted-dash">—</span> },
    { title: t("TRẠNG THÁI"), dataIndex: 'status', render: (status: AccountStatus, owner) => <Tooltip title={owner.reason || undefined}><Tag className={`status-pill ${statusClass[status]}`}>{statusLabel[status]}</Tag></Tooltip> },
    {
      title: '',
      key: 'actions',
      fixed: 'right',
      width: 160,
      align: 'right',
      render: (_, owner) => {
        const moreItems: MenuProps['items'] = [
          {
            key: 'sub',
            icon: <CrownOutlined />,
            label: t("Gói cước"),
            onClick: () => openSubModal(owner),
          },
          {
            key: 'reset',
            icon: <KeyOutlined />,
            label: t("Đặt lại mật khẩu"),
            onClick: () => {
              resetForm.resetFields();
              setResetResult(null);
              setResetOwner(owner);
            },
          },
        ];
        return (
          <Space size={4}>
            {owner.status === 'Pending' ? (
              <>
                <Button type="primary" size="small" icon={<CheckOutlined />} loading={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => approve(owner)}>
                  {t("Duyệt")}
                </Button>
                <Button danger size="small" icon={<CloseOutlined />} disabled={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => requestReason(owner, 'Rejected')}>
                  {t("Từ chối")}
                </Button>
              </>
            ) : owner.status === 'Approved' ? (
              <Button size="small" loading={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => requestReason(owner, 'Suspended')}>
                {t("Tạm khóa")}
              </Button>
            ) : (
              <Button size="small" type={owner.status === 'Suspended' ? 'primary' : 'default'} loading={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => approve(owner)}>
                {owner.status === 'Suspended' ? t("Mở khóa") : t("Duyệt lại")}
              </Button>
            )}
            <Dropdown menu={{ items: moreItems }} trigger={['click']} placement="bottomRight">
              <Button size="small" icon={<EllipsisOutlined />} aria-label={t("Tác vụ {0}", [owner.name])} />
            </Dropdown>
          </Space>
        );
      },
    },
  ];

  return <>
    <PageTitle eyebrow={t("QUẢN TRỊ HỆ THỐNG")} title={t("Tài khoản chủ trọ")} subtitle={t("Xem xét yêu cầu đăng ký và quản lý trạng thái truy cập.")} />
    {ownersQuery.isError ? <LoadError error={ownersQuery.error} onRetry={() => void ownersQuery.refetch()} /> : ownersQuery.isPending ? <div className="content-card"><div className="loading-placeholder" /></div> : <>
      <div className="admin-summary-card"><div className="admin-summary-symbol"><UserOutlined /></div><div><span>{t("Yêu cầu cần xem xét")}</span><strong>{pendingCount}</strong></div><div className="admin-summary-note">{pendingCount ? t("Đang chờ bạn xử lý") : t("Bạn đã xem hết yêu cầu mới")}</div></div>
      <SectionCard className="owner-list-card" title={t("Danh sách tài khoản")} extra={<Select aria-label={t("Lọc trạng thái chủ trọ")} value={filter} onChange={setFilter} options={[{ value: 'All', label: t("Tất cả trạng thái") }, { value: 'Pending', label: t("Chờ duyệt") }, { value: 'Approved', label: t("Đã duyệt") }, { value: 'Rejected', label: t("Từ chối") }, { value: 'Suspended', label: t("Tạm khóa") }]} style={{ width: 170 }} />}>
        {owners.length ? <ResponsiveTable className="owner-table" rowKey="id" columns={columns} dataSource={owners} pagination={{ current:page, total:ownersQuery.data?.total ?? 0, onChange:setPage, pageSize:10, showSizeChanger:false, hideOnSinglePage:true }} scroll={{ x: 820 }} /> : <EmptyPanel title={filter === 'All' ? t("Chưa có tài khoản chủ trọ") : t("Không có tài khoản {0}", [statusLabel[filter].toLowerCase()])} description={filter === 'Pending' ? t("Khi có yêu cầu đăng ký mới, bạn sẽ thấy ở đây.") : undefined} />}
      </SectionCard>
    </>}

    {/* Reset Password Modal */}
    <Modal title={t("Đặt lại mật khẩu")} open={!!resetOwner} maskClosable={false} onCancel={() => { if (!resetMutation.isPending) { setResetOwner(null); setResetResult(null); resetForm.resetFields(); } }} footer={resetResult ? <Button onClick={() => { setResetOwner(null); setResetResult(null); }}>{t("Đóng")}</Button> : undefined} onOk={() => resetForm.submit()} confirmLoading={resetMutation.isPending} okText={t("Tạo mã đặt lại")} cancelText={t("Hủy")} cancelButtonProps={{ disabled: resetMutation.isPending }} closable={!resetMutation.isPending} destroyOnClose>
      <Typography.Paragraph>{resetOwner?.email}</Typography.Paragraph>
      {resetResult ? <><Typography.Paragraph>{t("Gửi mã này riêng cho chủ trọ sau khi xác minh danh tính. Mã chỉ hiển thị lần này.")}</Typography.Paragraph><Input.TextArea readOnly aria-label={t("Mã đặt lại mật khẩu")} value={resetResult.token} autoSize /><Typography.Paragraph>{t("Hết hạn: {0}", [dayjs(resetResult.expiresAt).format('HH:mm DD/MM/YYYY')])}</Typography.Paragraph><Typography.Paragraph>{t("Chủ trọ mở trang Đặt lại mật khẩu ở màn đăng nhập để nhập mã.")}</Typography.Paragraph></> : <><Typography.Paragraph>{t("Tạo mã sẽ kết thúc các phiên của chủ trọ và yêu cầu đặt mật khẩu mới trước khi đăng nhập.")}</Typography.Paragraph><Form form={resetForm} layout="vertical" onFinish={(values) => { if (!resetMutation.isPending) resetMutation.mutate(values.currentPassword); }}><Form.Item name="currentPassword" label={t("Mật khẩu hiện tại")} extra={t("Nhập mật khẩu tài khoản Quản trị viên của bạn để xác nhận cấp mã.")} rules={[{ required: true }]}><Input.Password autoComplete="current-password" maxLength={256} /></Form.Item></Form></>}
    </Modal>

    {/* Reason Target Modal (Reject / Suspend) */}
    <Modal title={reasonTarget?.status === 'Rejected' ? t("Từ chối yêu cầu đăng ký") : t("Tạm khóa tài khoản")} open={!!reasonTarget} onCancel={() => setReasonTarget(null)} onOk={() => form.submit()} okText={t("Xác nhận")} cancelText={t("Hủy")} confirmLoading={updateMutation.isPending} destroyOnClose>
      <Typography.Paragraph className="modal-intro">{reasonTarget?.owner.name}  {t("sẽ nhận được thông báo về trạng thái tài khoản.")}</Typography.Paragraph><Form form={form} layout="vertical" onFinish={submitReason}><Form.Item label={t("Lý do")} name="reason" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập lý do.") }]}><Input.TextArea autoSize={{ minRows: 3, maxRows: 6 }} maxLength={500} placeholder={t("Nhập lý do để chủ trọ hiểu quyết định này")} /></Form.Item></Form>
    </Modal>

    {/* Subscription Management Modal */}
    <Modal
      title={t("Quản lý gói cước · {0}", [subOwner?.name ?? ''])}
      open={!!subOwner}
      onCancel={() => {
        if (!subMutation.isPending) {
          setSubOwner(null);
          subForm.resetFields();
        }
      }}
      onOk={() => subForm.submit()}
      okText={t("Cập nhật gói cước")}
      cancelText={t("Hủy")}
      confirmLoading={subMutation.isPending}
      destroyOnClose
    >
      <div style={{ marginBottom: 16 }}>
        <Typography.Text type="secondary">{t("Chủ trọ:")} </Typography.Text>
        <strong>{subOwner?.name}</strong> ({subOwner?.email})
        <br />
        <Typography.Text type="secondary">{t("Trạng thái hiện tại:")} </Typography.Text>
        {subOwner?.isPro ? (
          <Tag color="gold" icon={<CrownOutlined />}>PRO</Tag>
        ) : (
          <Tag>{t("Miễn phí")}</Tag>
        )}
        {subOwner?.subscriptionExpiresAt && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            ({t("Hết hạn: {0}", [dayjs(subOwner.subscriptionExpiresAt).format('HH:mm DD/MM/YYYY')])})
          </Typography.Text>
        )}
      </div>

      <Form
        form={subForm}
        layout="vertical"
        onFinish={(values) => subMutation.mutate(values)}
      >
        <Form.Item label={t("Gói dịch vụ")} name="tier" rules={[{ required: true }]}>
          <Radio.Group onChange={(e) => setSubTier(e.target.value)}>
            <Radio value="Pro">{t("Gói Pro (Không giới hạn phòng)")}</Radio>
            <Radio value="Free">{t("Gói Miễn phí (Tối đa 5 phòng)")}</Radio>
          </Radio.Group>
        </Form.Item>

        {subTier === 'Pro' && (
          <Form.Item
            label={t("Cộng thêm số ngày")}
            name="days"
            rules={[{ required: true, message: t("Vui lòng nhập số ngày.") }]}
            extra={
              <div>
                <Typography.Text type="secondary" style={{ display: 'block', marginBottom: 4 }}>
                  {t("Nhập 0 ngày để cấp gói Pro vô thời hạn.")}
                </Typography.Text>
                <Space wrap style={{ marginTop: 2 }}>
                  <Button size="small" onClick={() => subForm.setFieldValue('days', 0)}>{t("Vô thời hạn")}</Button>
                  <Button size="small" onClick={() => subForm.setFieldValue('days', 30)}>{t("+30 ngày (1 tháng)")}</Button>
                  <Button size="small" onClick={() => subForm.setFieldValue('days', 90)}>{t("+90 ngày (3 tháng)")}</Button>
                  <Button size="small" onClick={() => subForm.setFieldValue('days', 365)}>{t("+365 ngày (1 năm)")}</Button>
                </Space>
              </div>
            }
          >
            <InputNumber min={0} max={3650} style={{ width: '100%' }} addonAfter={t("ngày")} />
          </Form.Item>
        )}

        <Form.Item label={t("Ghi chú / Lý do thay đổi")} name="reason">
          <Input.TextArea rows={2} placeholder={t("Ví dụ: Khuyến mãi sự kiện, thanh toán trực tiếp qua ngân hàng...")} />
        </Form.Item>
      </Form>
    </Modal>
  </>;
}

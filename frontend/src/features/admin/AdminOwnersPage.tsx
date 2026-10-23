import { CheckOutlined, CloseOutlined, CrownOutlined, EllipsisOutlined, KeyOutlined, LockOutlined, MailOutlined, PhoneOutlined, PlusOutlined, TeamOutlined, UserOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { MenuProps, TableProps } from 'antd';
import { App, Button, Dropdown, Form, Input, InputNumber, Modal, Radio, Select, Space, Tabs, Tag, Tooltip, Typography } from 'antd';
import dayjs from 'dayjs';
import { useEffect, useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { EmptyPanel, LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { type AccountStatus } from '../auth/index';
import { type SubscriptionTier } from '../subscription/index';
import { marketersApi, ownersApi, type CreateMarketerRequest, type MarketerAccount, type OwnerRequest } from './index';

const statusClass: Record<AccountStatus, string> = { Pending: 'status-pending', Approved: 'status-paid', Rejected: 'status-unpaid', Suspended: 'status-suspended' };

export default function AdminOwnersPage() {
  const statusLabel: Record<AccountStatus, string> = { Pending: t("Chờ duyệt"), Approved: t("Đã duyệt"), Rejected: t("Từ chối"), Suspended: t("Tạm khóa") };
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const [activeTab, setActiveTab] = useState<'owners' | 'marketers'>('owners');

  const [resetTarget, setResetTarget] = useState<{ id: string; email: string; role: 'Owner' | 'Marketer' } | null>(null);
  const [resetResult, setResetResult] = useState<{ token: string; expiresAt: string } | null>(null);
  const [resetForm] = Form.useForm();
  const resetMutation = useMutation({
    mutationFn: (password: string) =>
      resetTarget?.role === 'Marketer'
        ? marketersApi.resetPassword(resetTarget.id, password)
        : ownersApi.resetPassword(resetTarget!.id, password),
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
  const [reasonTarget, setReasonTarget] = useState<{ id: string; name: string; status: AccountStatus; role: 'Owner' | 'Marketer' } | null>(null);
  const [form] = Form.useForm<{ reason: string }>();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [filter]);
  const ownersQuery = useQuery({ queryKey: ['admin-owners', page, filter], queryFn: () => ownersApi.page(page, filter) });
  const marketersQuery = useQuery({
    queryKey: ['admin-marketers'],
    queryFn: () => marketersApi.list(),
    enabled: activeTab === 'marketers',
  });

  const [createMarketerOpen, setCreateMarketerOpen] = useState(false);
  const [marketerForm] = Form.useForm<CreateMarketerRequest>();
  const createMarketerMutation = useMutation({
    mutationFn: (values: CreateMarketerRequest) => marketersApi.create(values),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['admin-marketers'] });
      message.success(t("Đã tạo tài khoản nhân sự Marketing."));
      setCreateMarketerOpen(false);
      marketerForm.resetFields();
    },
    onError: (e: Error) => message.error(e.message),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, status, reason, role }: { id: string; status: AccountStatus; reason?: string; role: 'Owner' | 'Marketer' }) =>
      role === 'Marketer'
        ? marketersApi.updateStatus(id, { status, reason })
        : ownersApi.updateStatus(id, { status, reason }),
    onSuccess: async (_, variables) => {
      if (variables.role === 'Marketer') {
        await client.invalidateQueries({ queryKey: ['admin-marketers'] });
      } else {
        await client.invalidateQueries({ queryKey: ['admin-owners'] });
      }
      message.success(variables.status === 'Approved' ? t("Đã duyệt tài khoản chủ trọ.") : variables.status === 'Rejected' ? t("Đã từ chối yêu cầu đăng ký.") : t("Đã cập nhật trạng thái tài khoản."));
      setReasonTarget(null);
      form.resetFields();
    },
    onError: (e: Error) => message.error(e.message),
  });

  const owners = ownersQuery.data?.items ?? [];
  const pendingCount = ownersQuery.data?.pendingCount ?? 0;
  const marketers = marketersQuery.data ?? [];

  const approve = (owner: OwnerRequest) => modal.confirm({ title: t("Duyệt tài khoản {0}?", [owner.name]), content: t("Chủ trọ sẽ có quyền truy cập khu vực quản lý sau khi được duyệt."), okText: t("Duyệt tài khoản"), cancelText: t("Hủy"), onOk: () => updateMutation.mutateAsync({ id: owner.id, status: 'Approved', role: 'Owner' }) });
  const requestReason = (target: { id: string; name: string }, status: AccountStatus, role: 'Owner' | 'Marketer' = 'Owner') => { setReasonTarget({ id: target.id, name: target.name, status, role }); form.resetFields(); };
  const submitReason = (values: { reason: string }) => { if (reasonTarget) updateMutation.mutate({ id: reasonTarget.id, status: reasonTarget.status, reason: values.reason.trim(), role: reasonTarget.role }); };

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
              setResetTarget({ id: owner.id, email: owner.email, role: 'Owner' });
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
                <Button danger size="small" icon={<CloseOutlined />} disabled={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => requestReason(owner, 'Rejected', 'Owner')}>
                  {t("Từ chối")}
                </Button>
              </>
            ) : owner.status === 'Approved' ? (
              <Button size="small" loading={updateMutation.isPending && updateMutation.variables?.id === owner.id} onClick={() => requestReason(owner, 'Suspended', 'Owner')}>
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

  const marketerColumns: TableProps<MarketerAccount>['columns'] = [
    { title: t("NHÂN SỰ MARKETING"), key: 'marketer', render: (_, m) => <div className="owner-name-cell"><div className="owner-avatar"><TeamOutlined /></div><div><strong>{m.name}</strong><span>{m.email}</span></div></div> },
    { title: t("ĐIỆN THOẠI"), dataIndex: 'phone', render: (phone?: string | null) => phone || <span className="muted-dash">{t("Chưa cung cấp")}</span> },
    { title: t("VAI TRÒ"), key: 'role', render: () => <Tag color="cyan">Marketer</Tag> },
    { title: t("NGÀY TẠO"), dataIndex: 'createdAt', render: (value?: string) => value ? dayjs(value).format(dateFormat()) : <span className="muted-dash">—</span> },
    { title: t("TRẠNG THÁI"), dataIndex: 'status', render: (status: AccountStatus, m) => <Tooltip title={m.reason || undefined}><Tag className={`status-pill ${statusClass[status]}`}>{statusLabel[status]}</Tag></Tooltip> },
    {
      title: '',
      key: 'actions',
      fixed: 'right',
      width: 200,
      align: 'right',
      render: (_, m) => (
        <Space size={6}>
          {m.status === 'Approved' ? (
            <Button size="small" loading={updateMutation.isPending && updateMutation.variables?.id === m.id} onClick={() => requestReason(m, 'Suspended', 'Marketer')}>
              {t("Tạm khóa")}
            </Button>
          ) : (
            <Button size="small" type="primary" loading={updateMutation.isPending && updateMutation.variables?.id === m.id} onClick={() => updateMutation.mutate({ id: m.id, status: 'Approved', role: 'Marketer' })}>
              {t("Mở khóa")}
            </Button>
          )}
          <Button
            size="small"
            icon={<KeyOutlined />}
            onClick={() => {
              resetForm.resetFields();
              setResetResult(null);
              setResetTarget({ id: m.id, email: m.email, role: 'Marketer' });
            }}
          >
            {t("Đặt lại mật khẩu")}
          </Button>
        </Space>
      ),
    },
  ];

  return <>
    <PageTitle eyebrow={t("QUẢN TRỊ HỆ THỐNG")} title={t("Tài khoản chủ trọ")} subtitle={t("Xem xét yêu cầu đăng ký và quản lý trạng thái truy cập.")} />

    <Tabs
      activeKey={activeTab}
      onChange={(key) => setActiveTab(key as 'owners' | 'marketers')}
      style={{ marginBottom: 16 }}
      items={[
        {
          key: 'owners',
          label: (
            <span>
              <UserOutlined style={{ marginRight: 6 }} />
              {t("Chủ trọ (Owners)")}
            </span>
          ),
        },
        {
          key: 'marketers',
          label: (
            <span>
              <TeamOutlined style={{ marginRight: 6 }} />
              {t("Nhân sự Marketing (Marketer)")}
            </span>
          ),
        },
      ]}
    />

    {activeTab === 'owners' ? (
      ownersQuery.isError ? <LoadError error={ownersQuery.error} onRetry={() => void ownersQuery.refetch()} /> : ownersQuery.isPending ? <div className="content-card"><div className="loading-placeholder" /></div> : <>
        <div className="admin-summary-card"><div className="admin-summary-symbol"><UserOutlined /></div><div><span>{t("Yêu cầu cần xem xét")}</span><strong>{pendingCount}</strong></div><div className="admin-summary-note">{pendingCount ? t("Đang chờ bạn xử lý") : t("Bạn đã xem hết yêu cầu mới")}</div></div>
        <SectionCard className="owner-list-card" title={t("Danh sách tài khoản")} extra={<Select aria-label={t("Lọc trạng thái chủ trọ")} value={filter} onChange={setFilter} options={[{ value: 'All', label: t("Tất cả trạng thái") }, { value: 'Pending', label: t("Chờ duyệt") }, { value: 'Approved', label: t("Đã duyệt") }, { value: 'Rejected', label: t("Từ chối") }, { value: 'Suspended', label: t("Tạm khóa") }]} style={{ width: 170 }} />}>
          {owners.length ? <ResponsiveTable className="owner-table" rowKey="id" columns={columns} dataSource={owners} pagination={{ current:page, total:ownersQuery.data?.total ?? 0, onChange:setPage, pageSize:10, showSizeChanger:false, hideOnSinglePage:true }} scroll={{ x: 820 }} /> : <EmptyPanel title={filter === 'All' ? t("Chưa có tài khoản chủ trọ") : t("Không có tài khoản {0}", [statusLabel[filter].toLowerCase()])} description={filter === 'Pending' ? t("Khi có yêu cầu đăng ký mới, bạn sẽ thấy ở đây.") : undefined} />}
        </SectionCard>
      </>
    ) : (
      marketersQuery.isError ? <LoadError error={marketersQuery.error} onRetry={() => void marketersQuery.refetch()} /> : (
        <SectionCard
          className="owner-list-card"
          title={t("Danh sách nhân sự Marketing")}
          extra={
            <Button type="primary" icon={<PlusOutlined />} onClick={() => { marketerForm.resetFields(); setCreateMarketerOpen(true); }}>
              {t("Thêm nhân sự Marketing")}
            </Button>
          }
        >
          {marketers.length ? (
            <ResponsiveTable className="owner-table" rowKey="id" columns={marketerColumns} dataSource={marketers} pagination={false} scroll={{ x: 760 }} />
          ) : (
            <EmptyPanel title={t("Chưa có tài khoản nhân sự Marketing")} description={t("Tạo tài khoản Marketer để phân quyền quản lý chiến dịch, nội dung và chăm sóc chủ trọ.")} />
          )}
        </SectionCard>
      )
    )}

    {/* Create Marketer Modal */}
    <Modal
      title={t("Thêm nhân sự Marketing")}
      open={createMarketerOpen}
      onCancel={() => { if (!createMarketerMutation.isPending) setCreateMarketerOpen(false); }}
      onOk={() => marketerForm.submit()}
      okText={t("Tạo tài khoản")}
      cancelText={t("Hủy")}
      confirmLoading={createMarketerMutation.isPending}
      destroyOnClose
    >
      <Typography.Paragraph type="secondary">
        {t("Nhân sự Marketing được truy cập công cụ tăng trưởng, chiến dịch, voucher, CMS cẩm nang và CRM chủ trọ (không có quyền xem CCCD, hợp đồng hay thông tin ngân hàng).")}
      </Typography.Paragraph>
      <Form form={marketerForm} layout="vertical" onFinish={(values) => createMarketerMutation.mutate(values)}>
        <Form.Item label={t("Họ và tên")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên.") }]}>
          <Input prefix={<UserOutlined />} placeholder={t("Nguyễn Thị Mai")} />
        </Form.Item>
        <Form.Item label={t("Email đăng nhập")} name="email" rules={[{ required: true, type: 'email', message: t("Email chưa đúng định dạng.") }]}>
          <Input prefix={<MailOutlined />} placeholder="marketer@roomie.io.vn" />
        </Form.Item>
        <Form.Item label={t("Số điện thoại")} name="phone">
          <Input prefix={<PhoneOutlined />} placeholder="09xx xxx xxx" />
        </Form.Item>
        <Form.Item label={t("Mật khẩu khởi tạo")} name="password" rules={[{ required: true, min: 9, max: 256, message: t("Mật khẩu mới cần từ 9 đến 256 ký tự.") }]}>
          <Input.Password prefix={<LockOutlined />} placeholder={t("Mật khẩu mới")} />
        </Form.Item>
      </Form>
    </Modal>

    {/* Reset Password Modal */}
    <Modal title={t("Đặt lại mật khẩu")} open={!!resetTarget} maskClosable={false} onCancel={() => { if (!resetMutation.isPending) { setResetTarget(null); setResetResult(null); resetForm.resetFields(); } }} footer={resetResult ? <Button onClick={() => { setResetTarget(null); setResetResult(null); }}>{t("Đóng")}</Button> : undefined} onOk={() => resetForm.submit()} confirmLoading={resetMutation.isPending} okText={t("Tạo mã đặt lại")} cancelText={t("Hủy")} cancelButtonProps={{ disabled: resetMutation.isPending }} closable={!resetMutation.isPending} destroyOnClose>
      <Typography.Paragraph>{resetTarget?.email}</Typography.Paragraph>
      {resetResult ? <><Typography.Paragraph>{t("Gửi mã này riêng cho chủ trọ sau khi xác minh danh tính. Mã chỉ hiển thị lần này.")}</Typography.Paragraph><Input.TextArea readOnly aria-label={t("Mã đặt lại mật khẩu")} value={resetResult.token} autoSize /><Typography.Paragraph>{t("Hết hạn: {0}", [dayjs(resetResult.expiresAt).format('HH:mm DD/MM/YYYY')])}</Typography.Paragraph><Typography.Paragraph>{t("Chủ trọ mở trang Đặt lại mật khẩu ở màn đăng nhập để nhập mã.")}</Typography.Paragraph></> : <><Typography.Paragraph>{t("Tạo mã sẽ kết thúc các phiên của chủ trọ và yêu cầu đặt mật khẩu mới trước khi đăng nhập.")}</Typography.Paragraph><Form form={resetForm} layout="vertical" onFinish={(values) => { if (!resetMutation.isPending) resetMutation.mutate(values.currentPassword); }}><Form.Item name="currentPassword" label={t("Mật khẩu hiện tại")} extra={t("Nhập mật khẩu tài khoản Quản trị viên của bạn để xác nhận cấp mã.")} rules={[{ required: true }]}><Input.Password autoComplete="current-password" maxLength={256} /></Form.Item></Form></>}
    </Modal>

    {/* Reason Target Modal (Reject / Suspend) */}
    <Modal title={reasonTarget?.status === 'Rejected' ? t("Từ chối yêu cầu đăng ký") : t("Tạm khóa tài khoản")} open={!!reasonTarget} onCancel={() => setReasonTarget(null)} onOk={() => form.submit()} okText={t("Xác nhận")} cancelText={t("Hủy")} confirmLoading={updateMutation.isPending} destroyOnClose>
      <Typography.Paragraph className="modal-intro">{reasonTarget?.name}  {t("sẽ nhận được thông báo về trạng thái tài khoản.")}</Typography.Paragraph><Form form={form} layout="vertical" onFinish={submitReason}><Form.Item label={t("Lý do")} name="reason" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập lý do.") }]}><Input.TextArea autoSize={{ minRows: 3, maxRows: 6 }} maxLength={500} placeholder={t("Nhập lý do để chủ trọ hiểu quyết định này")} /></Form.Item></Form>
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

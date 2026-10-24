import {
  CheckCircleOutlined,
  CopyOutlined,
  FileTextOutlined,
  PhoneOutlined,
  SearchOutlined,
  SendOutlined,
  TagOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  App,
  Badge,
  Button,
  Col,
  DatePicker,
  Drawer,
  Form,
  Input,
  List,
  Row,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { marketerApi } from './api';
import type { MarketerLeadItem, MarketerLeadNoteItem } from './types';

export default function MarketerLeadsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();

  const [segment, setSegment] = useState<string>('All');
  const [leadStatus, setLeadStatus] = useState<string>('');
  const [search, setSearch] = useState<string>('');
  const [page, setPage] = useState<number>(1);
  const [selectedLead, setSelectedLead] = useState<MarketerLeadItem | null>(null);
  const [noteText, setNoteText] = useState<string>('');
  const [profileForm] = Form.useForm();

  const leadsQuery = useQuery({
    queryKey: ['marketer-leads', segment, leadStatus, search, page],
    queryFn: () =>
      marketerApi.listLeads({
        segment,
        leadStatus: leadStatus || undefined,
        search: search.trim() || undefined,
        page,
        pageSize: 20,
      }),
  });

  const notesQuery = useQuery({
    queryKey: ['marketer-lead-notes', selectedLead?.ownerId],
    queryFn: () => marketerApi.listLeadNotes(selectedLead!.ownerId),
    enabled: Boolean(selectedLead?.ownerId),
  });

  const updateProfileMutation = useMutation({
    mutationFn: (values: {
      leadStatus: string;
      tags?: string;
      nextFollowUpAt?: dayjs.Dayjs | null;
      markContactedNow?: boolean;
    }) =>
      marketerApi.updateLeadProfile(selectedLead!.ownerId, {
        leadStatus: values.leadStatus,
        tags: values.tags?.trim() || null,
        nextFollowUpAt: values.nextFollowUpAt ? values.nextFollowUpAt.toISOString() : null,
        markContactedNow: values.markContactedNow ?? true,
      }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['marketer-leads'] });
      message.success(t("Đã cập nhật hồ sơ chăm sóc chủ trọ."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const addNoteMutation = useMutation({
    mutationFn: (content: string) => marketerApi.addLeadNote(selectedLead!.ownerId, content),
    onSuccess: async () => {
      setNoteText('');
      await Promise.all([
        client.invalidateQueries({ queryKey: ['marketer-lead-notes', selectedLead?.ownerId] }),
        client.invalidateQueries({ queryKey: ['marketer-leads'] }),
      ]);
      message.success(t("Đã lưu ghi chú chăm sóc."));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const openLeadDrawer = (lead: MarketerLeadItem) => {
    setSelectedLead(lead);
    profileForm.setFieldsValue({
      leadStatus: lead.leadStatus || 'New',
      tags: lead.tags || '',
      nextFollowUpAt: lead.nextFollowUpAt ? dayjs(lead.nextFollowUpAt) : null,
    });
  };

  if (leadsQuery.isError) {
    return <LoadError error={leadsQuery.error} onRetry={() => void leadsQuery.refetch()} />;
  }

  const counts = leadsQuery.data?.segmentCounts ?? {};
  const segments = [
    { key: 'All', label: t("Tất cả chủ trọ"), color: '#0f766e', count: counts.All ?? 0 },
    { key: 'StuckOnboarding', label: t("Kẹt kích hoạt"), color: '#d97706', count: counts.StuckOnboarding ?? 0 },
    { key: 'FreeNearLimit', label: t("Sắp chạm trần Free (4-5 phòng)"), color: '#2563eb', count: counts.FreeNearLimit ?? 0 },
    { key: 'ProExpiringSoon', label: t("Pro sắp hết hạn (≤14 ngày)"), color: '#dc2626', count: counts.ProExpiringSoon ?? 0 },
    { key: 'InactiveOwners', label: t("Không hoạt động (>14 ngày)"), color: '#64748b', count: counts.InactiveOwners ?? 0 },
    { key: 'ActivePro', label: t("Đang dùng Pro"), color: '#16a34a', count: counts.ActivePro ?? 0 },
  ];

  const leadStatusTag = (status: string) => {
    switch (status) {
      case 'Contacted':
        return <Tag color="processing">{t("Đã liên hệ")}</Tag>;
      case 'Onboarded':
        return <Tag color="cyan">{t("Đã hướng dẫn")}</Tag>;
      case 'Nurture':
        return <Tag color="purple">{t("Đang nuôi dưỡng")}</Tag>;
      case 'Interested':
        return <Tag color="gold">{t("Quan tâm Pro")}</Tag>;
      case 'Converted':
        return <Tag color="success">{t("Đã lên Pro")}</Tag>;
      case 'Churned':
        return <Tag color="error">{t("Ngừng dùng")}</Tag>;
      default:
        return <Tag>{t("Mới")}</Tag>;
    }
  };

  const copyText = async (val: string, label: string) => {
    try {
      await navigator.clipboard.writeText(val);
      message.success(t("Đã sao chép {0}", [label]));
    } catch {
      message.info(val);
    }
  };

  return (
    <div className="marketer-page">
      <PageTitle
        eyebrow="Marketing CRM"
        title={t("Chăm sóc & Phân khúc Chủ trọ")}
        subtitle={t("Theo dõi tiến độ kích hoạt, phân khúc tự động và ghi chú chăm sóc chuyển đổi lên gói Pro (ẩn dữ liệu nhạy cảm của khách thuê).")}
      />

      <Row gutter={[12, 12]} style={{ marginBottom: 20 }}>
        {segments.map((seg) => {
          const active = segment === seg.key;
          return (
            <Col xs={24} sm={12} md={8} lg={4} key={seg.key}>
              <div
                role="button"
                tabIndex={0}
                onClick={() => {
                  setSegment(seg.key);
                  setPage(1);
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    setSegment(seg.key);
                    setPage(1);
                  }
                }}
                className={`marketer-segment-card ${active ? 'active' : ''}`}
                style={{
                  borderColor: active ? seg.color : undefined,
                  cursor: 'pointer',
                }}
              >
                <div className="marketer-segment-count" style={{ color: seg.color }}>
                  {seg.count}
                </div>
                <div className="marketer-segment-label">{seg.label}</div>
              </div>
            </Col>
          );
        })}
      </Row>

      <SectionCard
        title={t("Danh sách Chủ trọ theo phân khúc")}
        extra={
          <Space wrap style={{ width: '100%' }}>
            <Input
              allowClear
              prefix={<SearchOutlined />}
              placeholder={t("Tìm tên, email, SĐT, UTM...")}
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              style={{ width: 220, maxWidth: '100%' }}
            />
            <Select
              allowClear
              placeholder={t("Trạng thái chăm sóc")}
              value={leadStatus || undefined}
              onChange={(val) => {
                setLeadStatus(val || '');
                setPage(1);
              }}
              style={{ width: 170, maxWidth: '100%' }}
              options={[
                { value: 'New', label: t("Mới") },
                { value: 'Contacted', label: t("Đã liên hệ") },
                { value: 'Onboarded', label: t("Đã hướng dẫn") },
                { value: 'Nurture', label: t("Đang nuôi dưỡng") },
                { value: 'Interested', label: t("Quan tâm Pro") },
                { value: 'Converted', label: t("Đã lên Pro") },
                { value: 'Churned', label: t("Ngừng dùng") },
              ]}
            />
          </Space>
        }
      >
        <ResponsiveTable<MarketerLeadItem>
          rowKey="ownerId"
          loading={leadsQuery.isLoading}
          dataSource={leadsQuery.data?.items ?? []}
          scroll={{ x: 1050 }}
          pagination={{
            current: page,
            pageSize: 20,
            total: leadsQuery.data?.total ?? 0,
            onChange: (p) => setPage(p),
          }}
          columns={[
            {
              title: t("Chủ trọ"),
              key: 'owner',
              render: (_: unknown, r: MarketerLeadItem) => (
                <div>
                  <Space size={6}>
                    <strong>{r.name}</strong>
                    <Tooltip title={t("Sao chép email")}>
                      <Button
                        type="text"
                        size="small"
                        icon={<CopyOutlined />}
                        onClick={() => void copyText(r.email, r.email)}
                      />
                    </Tooltip>
                  </Space>
                  <div style={{ fontSize: 12, color: '#64748b' }}>{r.email}</div>
                  {r.phone && (
                    <Space size={4} style={{ fontSize: 12, color: '#0f766e', marginTop: 2 }}>
                      <PhoneOutlined />
                      <span>{r.phone}</span>
                      <Button
                        type="text"
                        size="small"
                        icon={<CopyOutlined />}
                        onClick={() => void copyText(r.phone!, r.phone!)}
                      />
                      <a
                        href={`https://zalo.me/${r.phone.replace(/\D/g, '')}`}
                        target="_blank"
                        rel="noreferrer"
                        style={{ fontSize: 11, fontWeight: 600, color: '#0284c7' }}
                      >
                        Zalo
                      </a>
                    </Space>
                  )}
                </div>
              ),
            },
            {
              title: t("Tiến độ sử dụng"),
              key: 'usage',
              render: (_: unknown, r: MarketerLeadItem) => (
                <div>
                  <div>
                    <strong>{r.buildingCount}</strong> {t("dãy")} ·{' '}
                    <strong style={{ color: !r.isPro && r.roomCount >= 4 ? '#d97706' : undefined }}>
                      {r.roomCount}/{r.maxRooms === 2147483647 ? '∞' : r.maxRooms}
                    </strong>{' '}
                    {t("phòng")}
                  </div>
                  <div style={{ marginTop: 4 }}>
                    {r.isActivated ? (
                      <Tag color="green" icon={<CheckCircleOutlined />}>
                        {t("Đã phát hành HĐ")}
                      </Tag>
                    ) : (
                      <Tag color="warning">{t("Chưa phát hành HĐ")}</Tag>
                    )}
                  </div>
                </div>
              ),
            },
            {
              title: t("Gói & Ưu đãi"),
              key: 'plan',
              render: (_: unknown, r: MarketerLeadItem) => (
                <div>
                  <Tag color={r.isPro ? 'gold' : 'default'}>
                    <strong>{r.isPro ? 'PRO' : 'FREE'}</strong>
                  </Tag>
                  {r.subscriptionExpiresAt && (
                    <div style={{ fontSize: 12, color: '#64748b', marginTop: 2 }}>
                      {t("Hết hạn")}: {dayjs(r.subscriptionExpiresAt).format(dateFormat())}
                    </div>
                  )}
                  {r.redeemedPromoCodes.length > 0 && (
                    <div style={{ marginTop: 4 }}>
                      {r.redeemedPromoCodes.map((code) => (
                        <Tag key={code} color="blue" style={{ fontSize: 11 }}>
                          <TagOutlined /> {code}
                        </Tag>
                      ))}
                    </div>
                  )}
                </div>
              ),
            },
            {
              title: t("Nguồn tiếp cận"),
              key: 'utm',
              render: (_: unknown, r: MarketerLeadItem) => {
                if (!r.utmSource && !r.utmMedium && !r.utmCampaign) {
                  return <Typography.Text type="secondary">{t("Trực tiếp / Tự nhiên")}</Typography.Text>;
                }
                return (
                  <div style={{ fontSize: 12 }}>
                    <div>
                      <strong>{[r.utmSource, r.utmMedium].filter(Boolean).join(' / ')}</strong>
                    </div>
                    {r.utmCampaign && <div style={{ color: '#0f766e' }}>{r.utmCampaign}</div>}
                  </div>
                );
              },
            },
            {
              title: t("Chăm sóc CRM"),
              key: 'crm',
              render: (_: unknown, r: MarketerLeadItem) => (
                <div>
                  <Space size={6}>
                    {leadStatusTag(r.leadStatus)}
                    {r.notesCount > 0 && (
                      <Badge count={r.notesCount} size="small" color="#0f766e" />
                    )}
                  </Space>
                  {r.tags && (
                    <div style={{ fontSize: 12, color: '#475569', marginTop: 4 }}>
                      {r.tags}
                    </div>
                  )}
                  {r.nextFollowUpAt && (
                    <div style={{ fontSize: 11, color: '#d97706', marginTop: 2 }}>
                      {t("Hẹn gọi lại")}: {dayjs(r.nextFollowUpAt).format(dateFormat())}
                    </div>
                  )}
                </div>
              ),
            },
            {
              title: t("THAO TÁC"),
              key: 'actions',
              render: (_: unknown, r: MarketerLeadItem) => (
                <Button
                  type="primary"
                  ghost
                  size="small"
                  icon={<FileTextOutlined />}
                  onClick={() => openLeadDrawer(r)}
                >
                  {t("Chăm sóc")}
                </Button>
              ),
            },
          ]}
        />
      </SectionCard>

      <Drawer
        open={Boolean(selectedLead)}
        title={
          selectedLead
            ? `${t("Hồ sơ chăm sóc")}: ${selectedLead.name}`
            : t("Hồ sơ chăm sóc")
        }
        onClose={() => setSelectedLead(null)}
        width={560}
        destroyOnClose
      >
        {selectedLead && (
          <div>
            <div
              style={{
                padding: 12,
                background: '#f8fafc',
                borderRadius: 8,
                border: '1px solid #e2e8f0',
                marginBottom: 16,
              }}
            >
              <div>
                <UserOutlined style={{ marginRight: 6 }} />
                <strong>{selectedLead.name}</strong> ({selectedLead.email})
              </div>
              {selectedLead.phone && (
                <div style={{ marginTop: 4, color: '#0f766e', display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span>
                    <PhoneOutlined style={{ marginRight: 6 }} />
                    <strong>{selectedLead.phone}</strong>
                  </span>
                  <a
                    href={`https://zalo.me/${selectedLead.phone.replace(/\D/g, '')}`}
                    target="_blank"
                    rel="noreferrer"
                    style={{ fontSize: 12, fontWeight: 600, color: '#0284c7' }}
                  >
                    {t("Nhắn Zalo")}
                  </a>
                </div>
              )}
              <div style={{ marginTop: 6, fontSize: 12, color: '#64748b' }}>
                {t("Đăng ký ngày")}: {dayjs(selectedLead.createdAt).format(dateFormat())} ·{' '}
                {t("Hoạt động gần nhất")}:{' '}
                {selectedLead.lastActiveAt ? dayjs(selectedLead.lastActiveAt).format(dateFormat()) : '—'}
              </div>
              <div style={{ marginTop: 8, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                <Tag color={selectedLead.isPro ? 'gold' : 'default'}>
                  {selectedLead.isPro ? 'PRO' : 'FREE'} ({selectedLead.roomCount} {t("phòng")})
                </Tag>
                <Tag color={selectedLead.isActivated ? 'green' : 'warning'}>
                  {selectedLead.isActivated ? t("Đã phát hành HĐ") : t("Chưa phát hành HĐ")}
                </Tag>
                {(selectedLead.utmSource || selectedLead.utmCampaign) && (
                  <Tag color="cyan">
                    UTM: {[selectedLead.utmSource, selectedLead.utmMedium, selectedLead.utmCampaign].filter(Boolean).join(' / ')}
                  </Tag>
                )}
              </div>
            </div>

            <Form
              form={profileForm}
              layout="vertical"
              onFinish={(values) => updateProfileMutation.mutate(values)}
            >
              <Row gutter={12}>
                <Col xs={24} sm={12}>
                  <Form.Item label={t("Trạng thái Lead")} name="leadStatus" required>
                    <Select
                      options={[
                        { value: 'New', label: t("Mới") },
                        { value: 'Contacted', label: t("Đã liên hệ") },
                        { value: 'Onboarded', label: t("Đã hướng dẫn") },
                        { value: 'Nurture', label: t("Đang nuôi dưỡng") },
                        { value: 'Interested', label: t("Quan tâm Pro") },
                        { value: 'Converted', label: t("Đã lên Pro") },
                        { value: 'Churned', label: t("Ngừng dùng") },
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={12}>
                  <Form.Item label={t("Ngày hẹn chăm sóc tiếp")} name="nextFollowUpAt">
                    <DatePicker format={dateFormat()} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>

              <Form.Item label={t("Thẻ phân loại (Tags)")} name="tags">
                <Input placeholder={t("Ví dụ: chu-tro-20-phong, can-goi-zalo, quan-tam-vietqr")} />
              </Form.Item>

              <Button
                type="primary"
                htmlType="submit"
                loading={updateProfileMutation.isPending}
                block
              >
                {t("Cập nhật trạng thái chăm sóc")}
              </Button>
            </Form>

            <div style={{ marginTop: 24 }}>
              <Typography.Title level={5}>{t("Nhật ký ghi chú chăm sóc")}</Typography.Title>
              <Space.Compact style={{ width: '100%', marginBottom: 16 }}>
                <Input
                  placeholder={t("Nhập nội dung cuộc gọi / trao đổi Zalo với chủ trọ...")}
                  value={noteText}
                  onChange={(e) => setNoteText(e.target.value)}
                  onPressEnter={() => {
                    if (noteText.trim()) addNoteMutation.mutate(noteText.trim());
                  }}
                />
                <Button
                  type="primary"
                  icon={<SendOutlined />}
                  loading={addNoteMutation.isPending}
                  onClick={() => {
                    if (noteText.trim()) addNoteMutation.mutate(noteText.trim());
                  }}
                >
                  {t("Lưu")}
                </Button>
              </Space.Compact>

              <List<MarketerLeadNoteItem>
                loading={notesQuery.isLoading}
                dataSource={notesQuery.data ?? []}
                locale={{ emptyText: t("Chưa có ghi chú chăm sóc nào.") }}
                renderItem={(item) => (
                  <List.Item style={{ padding: '10px 0' }}>
                    <List.Item.Meta
                      title={
                        <Space size={8}>
                          <strong>{item.authorName}</strong>
                          <span style={{ fontSize: 12, color: '#64748b' }}>
                            {dayjs(item.createdAt).format(`${dateFormat()} HH:mm`)}
                          </span>
                        </Space>
                      }
                      description={<div style={{ color: '#1e293b', whiteSpace: 'pre-wrap' }}>{item.content}</div>}
                    />
                  </List.Item>
                )}
              />
            </div>
          </div>
        )}
      </Drawer>
    </div>
  );
}

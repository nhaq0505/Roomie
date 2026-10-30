import {
  EditOutlined,
  MailOutlined,
  PlusOutlined,
  SendOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App,
  Button,
  Col,
  Form,
  Input,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Tag,
} from 'antd';
import dayjs from 'dayjs';
import { useState } from 'react';
import { dateFormat, t } from '../../shared/i18n/runtime';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import { marketerApi } from './api';
import type { MarketerEmailCampaignItem, UpsertMarketerEmailCampaignRequest } from './types';

export default function MarketerEmailsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();

  const [modalOpen, setModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<MarketerEmailCampaignItem | null>(null);
  const [testModalItem, setTestModalItem] = useState<MarketerEmailCampaignItem | null>(null);
  const [testEmailAddress, setTestEmailAddress] = useState<string>('');
  const [form] = Form.useForm();

  const emailsQuery = useQuery({
    queryKey: ['marketer-emails'],
    queryFn: marketerApi.listEmails,
    // The server dispatches campaigns in the background; keep polling while any is still sending.
    refetchInterval: (query) =>
      query.state.data?.some((item) => item.status === 'Sending') ? 3000 : false,
  });

  const leadCountsQuery = useQuery({
    queryKey: ['marketer-leads-counts-for-email'],
    queryFn: () => marketerApi.listLeads({ segment: 'All', page: 1, pageSize: 1 }),
  });
  const segCounts = leadCountsQuery.data?.segmentCounts ?? {};

  const saveMutation = useMutation({
    mutationFn: (values: UpsertMarketerEmailCampaignRequest) => {
      if (editingItem) {
        return marketerApi.updateEmail(editingItem.id, values);
      }
      return marketerApi.createEmail(values);
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['marketer-emails'] });
      message.success(
        editingItem ? t("Đã cập nhật chiến dịch email.") : t("Đã lưu chiến dịch email nháp.")
      );
      setModalOpen(false);
    },
    onError: (e: Error) => message.error(e.message),
  });

  const sendTestMutation = useMutation({
    mutationFn: ({ id, toEmail }: { id: string; toEmail: string }) =>
      marketerApi.sendTestEmail(id, toEmail),
    onSuccess: (res) => {
      message.success(res.message);
      setTestModalItem(null);
    },
    onError: (e: Error) => message.error(e.message),
  });

  const sendCampaignMutation = useMutation({
    mutationFn: (id: string) => marketerApi.sendEmailCampaign(id),
    onSuccess: async (res) => {
      await client.invalidateQueries({ queryKey: ['marketer-emails'] });
      // 202 without a body or a "Sending" status means the background dispatch is still running.
      if (!res || res.status === 'Sending') {
        message.info(t("Chiến dịch đang được gửi nền. Kết quả sẽ tự cập nhật."));
        return;
      }
      if (res.status === 'Failed' || res.failedCount > 0) {
        message.warning(
          t("Đã gửi tới {0}/{1} chủ trọ, {2} người nhận bị lỗi. Bấm Gửi lại để thử lại phần lỗi.", [
            res.sentCount,
            res.recipientCount,
            res.failedCount,
          ])
        );
        return;
      }
      message.success(
        t("Đã gửi chiến dịch thành công tới {0}/{1} chủ trọ.", [res.sentCount, res.recipientCount])
      );
    },
    onError: (e: Error) => message.error(e.message),
  });

  const applyTemplatePreset = (presetKey: string) => {
    if (presetKey === 'StuckOnboarding') {
      form.setFieldsValue({
        name: t("Chăm sóc chủ trọ mới chưa tạo phòng"),
        segment: 'StuckOnboarding',
        subject: t("Chỉ mất 3 phút để lập hóa đơn tiền phòng đầu tiên trên Roomie"),
        bodyHtml: `<p>${t("Chào {{OwnerName}},")}</p><p>${t("Roomie nhận thấy bạn đã đăng ký tài khoản nhưng chưa khởi tạo phòng trọ hoặc hóa đơn đầu tiên.")}</p><p>${t("Chỉ cần nhập chỉ số điện nước, hệ thống sẽ tự tính tiền và tạo link hóa đơn VietQR gửi qua Zalo cho khách thuê.")}</p>`,
        ctaText: t("Tạo phòng trọ đầu tiên ngay"),
        ctaUrl: 'https://roomie.io.vn/buildings',
      });
    } else if (presetKey === 'FreeNearLimit') {
      form.setFieldsValue({
        name: t("Ưu đãi nâng cấp Pro cho chủ trọ 4-5 phòng"),
        segment: 'FreeNearLimit',
        subject: t("Tặng mã ưu đãi nâng cấp Roomie Pro không giới hạn số phòng"),
        bodyHtml: `<p>${t("Chào {{OwnerName}},")}</p><p>${t("Nhà trọ của bạn hiện đã có {{RoomCount}}/5 phòng trên gói Miễn phí.")}</p><p>${t("Sử dụng mã ưu đãi {{PromoCode}} để mở khóa không giới hạn số phòng, tự động nhắc nợ Zalo và đối soát thanh toán tự động.")}</p>`,
        ctaText: t("Nâng cấp gói Pro ngay"),
        ctaUrl: 'https://roomie.io.vn/subscription',
      });
    } else if (presetKey === 'ProExpiringSoon') {
      form.setFieldsValue({
        name: t("Nhắc gia hạn gói Pro sắp hết hạn"),
        segment: 'ProExpiringSoon',
        subject: t("Gói Roomie Pro của bạn sắp hết hạn — Gia hạn để không gián đoạn nhắc Zalo"),
        bodyHtml: `<p>${t("Chào {{OwnerName}},")}</p><p>${t("Gói Roomie Pro cho {{RoomCount}} phòng trọ của bạn sẽ hết hạn trong ít ngày tới.")}</p><p>${t("Hãy gia hạn sớm hoặc nhập mã ưu đãi {{PromoCode}} để duy trì tự động hóa đơn và nhắc tiền phòng qua Zalo.")}</p>`,
        ctaText: t("Gia hạn Roomie Pro"),
        ctaUrl: 'https://roomie.io.vn/subscription',
      });
    } else if (presetKey === 'InactiveOwners') {
      form.setFieldsValue({
        name: t("Mời chủ trọ quay lại trải nghiệm tính năng mới"),
        segment: 'InactiveOwners',
        subject: t("Roomie vừa cập nhật xuất hợp đồng Word & quét CCCD tự động"),
        bodyHtml: `<p>${t("Chào {{OwnerName}},")}</p><p>${t("Roomie vừa bổ sung tính năng quét QR CCCD lập hợp đồng thuê trọ và mẫu khai báo lưu trú CT01 chỉ trong 30 giây.")}</p><p>${t("Đăng nhập lại ngay hôm nay để quản lý nhà trọ nhàn hơn mỗi kỳ thu tiền.")}</p>`,
        ctaText: t("Đăng nhập Roomie"),
        ctaUrl: 'https://roomie.io.vn/login',
      });
    }
  };

  const openCreateModal = () => {
    setEditingItem(null);
    form.resetFields();
    applyTemplatePreset('StuckOnboarding');
    setModalOpen(true);
  };

  const openEditModal = (item: MarketerEmailCampaignItem) => {
    setEditingItem(item);
    form.resetFields();
    form.setFieldsValue({
      name: item.name,
      segment: item.segment,
      subject: item.subject,
      bodyHtml: item.bodyHtml,
      ctaText: item.ctaText ?? '',
      ctaUrl: item.ctaUrl ?? '',
      promoCode: item.promoCode ?? '',
    });
    setModalOpen(true);
  };

  if (emailsQuery.isError) {
    return <LoadError error={emailsQuery.error} onRetry={() => void emailsQuery.refetch()} />;
  }

  return (
    <div className="marketer-page">
      <PageTitle
        eyebrow="Email Automation"
        title={t("Chiến dịch Email theo Phân khúc")}
        subtitle={t("Gửi email chăm sóc cá nhân hóa tới từng nhóm chủ trọ (Kẹt kích hoạt, Sắp chạm trần 5 phòng, Pro sắp hết hạn).")}
      />

      <SectionCard
        title={t("Danh sách Chiến dịch Email")}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
            {t("Tạo chiến dịch Email")}
          </Button>
        }
      >
        <ResponsiveTable<MarketerEmailCampaignItem>
          rowKey="id"
          loading={emailsQuery.isLoading}
          dataSource={emailsQuery.data ?? []}
          scroll={{ x: 900 }}
          pagination={{ pageSize: 10 }}
          columns={[
            {
              title: t("Tên chiến dịch & Tiêu đề"),
              key: 'name',
              render: (_: unknown, r: MarketerEmailCampaignItem) => (
                <div>
                  <div style={{ fontWeight: 600 }}>{r.name}</div>
                  <div style={{ fontSize: 12, color: '#64748b' }}>{r.subject}</div>
                </div>
              ),
            },
            {
              title: t("Phân khúc nhận"),
              dataIndex: 'segment',
              key: 'segment',
              render: (seg: string) => <Tag color="blue">{seg}</Tag>,
            },
            {
              title: t("Mã KM đính kèm"),
              dataIndex: 'promoCode',
              key: 'promoCode',
              render: (code?: string | null) =>
                code ? <Tag color="gold">{code}</Tag> : '—',
            },
            {
              title: t("Trạng thái"),
              dataIndex: 'status',
              key: 'status',
              render: (st: string) => {
                if (st === 'Completed') return <Tag color="green">{t("Đã gửi hoàn tất")}</Tag>;
                if (st === 'Sending') return <Tag color="processing">{t("Đang gửi")}</Tag>;
                if (st === 'Failed') return <Tag color="error">{t("Gửi lỗi")}</Tag>;
                return <Tag>{t("Bản nháp")}</Tag>;
              },
            },
            {
              title: t("Kết quả gửi"),
              key: 'stats',
              align: 'right',
              render: (_: unknown, r: MarketerEmailCampaignItem) => (
                <span>
                  <strong style={{ color: '#0f766e' }}>{r.sentCount}</strong> / {r.recipientCount}
                  {r.failedCount > 0 && (
                    <span style={{ color: '#dc2626', marginLeft: 4 }}>
                      ({r.failedCount} {t("lỗi")})
                    </span>
                  )}
                </span>
              ),
            },
            {
              title: t("Thời gian gửi"),
              dataIndex: 'sentAt',
              key: 'sentAt',
              render: (val?: string | null) =>
                val ? dayjs(val).format(`${dateFormat()} HH:mm`) : '—',
            },
            {
              title: t("THAO TÁC"),
              key: 'actions',
              render: (_: unknown, r: MarketerEmailCampaignItem) => (
                <Space size={6} wrap>
                  <Button
                    size="small"
                    icon={<MailOutlined />}
                    onClick={() => {
                      setTestModalItem(r);
                      setTestEmailAddress('');
                    }}
                  >
                    {t("Gửi thử")}
                  </Button>
                  {r.status !== 'Completed' && r.status !== 'Sending' && (
                    <>
                      <Button
                        size="small"
                        icon={<EditOutlined />}
                        onClick={() => openEditModal(r)}
                      >
                        {t("Sửa")}
                      </Button>
                      <Popconfirm
                        title={t("Gửi chiến dịch email này tới toàn bộ phân khúc đã chọn?")}
                        description={t("Hệ thống sẽ chống gửi trùng lặp cho từng chủ trọ trong cùng chiến dịch.")}
                        okText={t("Gửi ngay")}
                        cancelText={t("Hủy")}
                        onConfirm={() => sendCampaignMutation.mutate(r.id)}
                      >
                        <Button
                          size="small"
                          type="primary"
                          icon={<SendOutlined />}
                          loading={
                            sendCampaignMutation.isPending &&
                            sendCampaignMutation.variables === r.id
                          }
                        >
                          {r.status === 'Failed' ? t("Gửi lại") : t("Phát hành")}
                        </Button>
                      </Popconfirm>
                    </>
                  )}
                </Space>
              ),
            },
          ]}
        />
      </SectionCard>

      {/* Create / Edit Campaign Modal */}
      <Modal
        open={modalOpen}
        title={editingItem ? t("Chỉnh sửa Chiến dịch Email") : t("Tạo Chiến dịch Email theo Phân khúc")}
        okText={editingItem ? t("Lưu thay đổi") : t("Lưu chiến dịch")}
        cancelText={t("Hủy")}
        confirmLoading={saveMutation.isPending}
        onCancel={() => setModalOpen(false)}
        onOk={() => form.submit()}
        width={760}
        destroyOnClose
      >
        <div style={{ marginBottom: 14 }}>
          <Space size={6} wrap>
            <span style={{ fontSize: 13, color: '#475569' }}>
              <ThunderboltOutlined style={{ color: '#d97706', marginRight: 4 }} />
              {t("Mẫu kịch bản có sẵn")}:
            </span>
            <Button size="small" onClick={() => applyTemplatePreset('StuckOnboarding')}>
              {t("1. Hướng dẫn tạo phòng")}
            </Button>
            <Button size="small" onClick={() => applyTemplatePreset('FreeNearLimit')}>
              {t("2. Chạm trần 5 phòng Free")}
            </Button>
            <Button size="small" onClick={() => applyTemplatePreset('ProExpiringSoon')}>
              {t("3. Nhắc gia hạn Pro")}
            </Button>
            <Button size="small" onClick={() => applyTemplatePreset('InactiveOwners')}>
              {t("4. Mời quay lại")}
            </Button>
          </Space>
        </div>

        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message={t("Biến cá nhân hóa tự động: {{OwnerName}} (Tên chủ trọ), {{RoomCount}} (Số phòng hiện có), {{PromoCode}} (Mã ưu đãi).")}
        />

        <Form
          form={form}
          layout="vertical"
          onFinish={(values) => saveMutation.mutate(values)}
        >
          <Row gutter={16}>
            <Col xs={24} sm={14}>
              <Form.Item
                label={t("Tên chiến dịch nội bộ")}
                name="name"
                rules={[{ required: true, message: t("Vui lòng nhập tên chiến dịch.") }]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} sm={10}>
              <Form.Item label={t("Phân khúc chủ trọ nhận")} name="segment" required>
                <Select
                  options={[
                    {
                      value: 'StuckOnboarding',
                      label: `${t("Kẹt kích hoạt (Chưa phát hành HĐ)")} (${segCounts.StuckOnboarding ?? 0})`,
                    },
                    {
                      value: 'FreeNearLimit',
                      label: `${t("Sắp chạm trần Free (4-5 phòng)")} (${segCounts.FreeNearLimit ?? 0})`,
                    },
                    {
                      value: 'ProExpiringSoon',
                      label: `${t("Pro sắp hết hạn (≤14 ngày)")} (${segCounts.ProExpiringSoon ?? 0})`,
                    },
                    {
                      value: 'InactiveOwners',
                      label: `${t("Không hoạt động (>14 ngày)")} (${segCounts.InactiveOwners ?? 0})`,
                    },
                    {
                      value: 'AllFreeOwners',
                      label: `${t("Tất cả chủ trọ gói Free")} (${Math.max(0, (segCounts.All ?? 0) - (segCounts.ActivePro ?? 0))})`,
                    },
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item
            label={t("Tiêu đề Email (Subject)")}
            name="subject"
            rules={[{ required: true, message: t("Vui lòng nhập tiêu đề email.") }]}
          >
            <Input />
          </Form.Item>

          <Form.Item
            label={t("Nội dung Email (HTML cơ bản)")}
            name="bodyHtml"
            rules={[{ required: true, message: t("Vui lòng nhập nội dung email.") }]}
          >
            <Input.TextArea rows={7} />
          </Form.Item>

          <Row gutter={16}>
            <Col xs={24} sm={8}>
              <Form.Item label={t("Nhãn nút CTA")} name="ctaText">
                <Input placeholder={t("Nâng cấp gói Pro ngay")} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={10}>
              <Form.Item label={t("Đường dẫn CTA")} name="ctaUrl">
                <Input placeholder="https://roomie.io.vn/subscription" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={6}>
              <Form.Item label={t("Mã ưu đãi ({{PromoCode}})")} name="promoCode">
                <Input placeholder="TET2026" />
              </Form.Item>
            </Col>
          </Row>

          <Form.Item noStyle shouldUpdate>
            {() => {
              const rawSubject = (form.getFieldValue('subject') as string) || '';
              const rawBody = (form.getFieldValue('bodyHtml') as string) || '';
              const promo = (form.getFieldValue('promoCode') as string) || 'TET2026';
              const ctaText = (form.getFieldValue('ctaText') as string) || '';
              const replaceTokens = (txt: string) =>
                txt
                  .replace(/\{\{OwnerName\}\}/g, t("Nguyễn Văn Chủ Trọ"))
                  .replace(/\{\{RoomCount\}\}/g, '4')
                  .replace(/\{\{PromoCode\}\}/g, promo);
              return (
                <div
                  style={{
                    marginTop: 6,
                    padding: '12px 16px',
                    background: '#f8fafc',
                    borderRadius: 8,
                    border: '1px solid #e2e8f0',
                  }}
                >
                  <div style={{ fontSize: 12, fontWeight: 700, color: '#334155', marginBottom: 6 }}>
                    {t("Xem trước Email cá nhân hóa")}: <strong>{replaceTokens(rawSubject)}</strong>
                  </div>
                  <div
                    style={{ fontSize: 13, color: '#1e293b', lineHeight: 1.6 }}
                    dangerouslySetInnerHTML={{ __html: replaceTokens(rawBody) }}
                  />
                  {ctaText && (
                    <div style={{ marginTop: 10 }}>
                      <Tag color="cyan" style={{ padding: '4px 10px', fontWeight: 600 }}>
                        {ctaText} →
                      </Tag>
                    </div>
                  )}
                </div>
              );
            }}
          </Form.Item>
        </Form>
      </Modal>

      {/* Send Test Email Modal */}
      <Modal
        open={Boolean(testModalItem)}
        title={t("Gửi email thử nghiệm")}
        okText={t("Gửi thử")}
        cancelText={t("Đóng")}
        confirmLoading={sendTestMutation.isPending}
        onCancel={() => setTestModalItem(null)}
        onOk={() => {
          if (testModalItem && testEmailAddress.trim()) {
            sendTestMutation.mutate({ id: testModalItem.id, toEmail: testEmailAddress.trim() });
          }
        }}
        destroyOnClose
      >
        <p>{t("Nhập địa chỉ email của bạn để kiểm tra giao diện và biến cá nhân hóa trước khi gửi hàng loạt:")}</p>
        <Input
          type="email"
          placeholder="marketer@phongtro.local"
          value={testEmailAddress}
          onChange={(e) => setTestEmailAddress(e.target.value)}
        />
      </Modal>
    </div>
  );
}

import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Form, InputNumber, Modal, Popconfirm, Skeleton, Space, Switch, Tag, Typography } from 'antd';
import { MessageOutlined, QrcodeOutlined } from '@ant-design/icons';
import { getLocale, t } from '../../shared/i18n/runtime';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { whatsAppApi, type WhatsAppReminder } from './index';

function connectionLabel(status: string) {
  switch (status) {
    case 'Connected': return t("Đã kết nối WhatsApp");
    case 'QrReady': return t("Quét QR bằng ứng dụng WhatsApp");
    case 'AwaitingConfirmation': return t("Xác nhận đăng nhập trên điện thoại");
    case 'Connecting': case 'Restoring': return t("Đang kết nối WhatsApp…");
    case 'Expired': return t("QR đã hết hạn. Hãy tạo mã mới.");
    case 'AccountInUse': return t("WhatsApp này đã kết nối với một chủ trọ khác.");
    case 'Unavailable': return t("Dịch vụ WhatsApp chưa sẵn sàng");
    default: return t("Chưa kết nối WhatsApp");
  }
}
function statusLabel(status: string) {
  switch (status) {
    case 'Pending': return t("Chờ gửi");
    case 'Sending': return t("Đang gửi");
    case 'Sent': return t("Đã gửi");
    case 'Skipped': return t("Đã bỏ qua");
    case 'Failed': return t("Không gửi được");
    case 'Disconnected': return t("Cần kết nối lại");
    default: return t("Chưa rõ kết quả");
  }
}
function detailLabel(detail?: string | null) {
  switch (detail) {
    case 'DeliveryUnknown': return t("Có thể tin đã gửi. Kiểm tra WhatsApp trước khi thêm lại sau 20 giờ.");
    case 'RecipientRejected': return t("Không tìm thấy hoặc không thể gửi đến số điện thoại này.");
    case 'ReconnectRequired': return t("Quét QR lại để tiếp tục kết nối.");
    case 'TenantLinkUnavailable': return t("Link người thuê không còn hợp lệ. Kiểm tra hoặc cấp lại link trong trang phòng.");
    case 'InvoiceIneligible': return t("Hóa đơn đã trả đủ, chưa phát hành hoặc người thuê không còn phù hợp.");
    case 'DisconnectedByOwner': return t("Đã dừng theo yêu cầu ngắt kết nối.");
    default: return '';
  }
}

export default function WhatsAppSettingsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();
  const [page, setPage] = useState(1);
  const [guide, setGuide] = useState(false);
  const settings = useQuery({ queryKey: ['whatsapp-settings'], queryFn: whatsAppApi.settings });
  const connection = useQuery({ queryKey: ['whatsapp-connection'], queryFn: whatsAppApi.connection, refetchInterval: 3000 });
  const history = useQuery({ queryKey: ['whatsapp-history', page], queryFn: () => whatsAppApi.history(page), refetchInterval: 5000 });
  const refresh = () => { void client.invalidateQueries({ queryKey: ['whatsapp-connection'] }); void client.invalidateQueries({ queryKey: ['whatsapp-history'] }); };
  const onError = (error: Error) => { void message.error(error.message); };
  const connect = useMutation({ mutationFn: whatsAppApi.connect, onError, onSuccess: refresh });
  const disconnect = useMutation({ mutationFn: whatsAppApi.disconnect, onError, onSuccess: () => { refresh(); void client.invalidateQueries({ queryKey: ['whatsapp-settings'] }); } });
  const queue = useMutation({ mutationFn: whatsAppApi.queue, onError, onSuccess: result => { void message.success(t("Đã thêm {0} hóa đơn vào hàng đợi.", [result.queued])); setPage(1); refresh(); } });

  const [scheduleEnabled, setScheduleEnabled] = useState(false);
  const [dayOfMonth, setDayOfMonth] = useState(25);
  const [hour, setHour] = useState(8);
  const [minute, setMinute] = useState(0);

  useEffect(() => {
    if (settings.data) {
      setScheduleEnabled(settings.data.scheduleEnabled ?? false);
      setDayOfMonth(settings.data.dayOfMonth || 25);
      setHour(settings.data.hour ?? 8);
      setMinute(settings.data.minute ?? 0);
    }
  }, [settings.data]);

  const saveSettings = useMutation({
    mutationFn: async (body: { scheduleEnabled: boolean; dayOfMonth: number; hour: number; minute: number }) => {
      const current = settings.data ?? {
        scheduleEnabled: false,
        dayOfMonth: 25,
        hour: 8,
        minute: 0,
        bankBin: '',
        bankAccount: '',
        bankAccountName: '',
        gatewayEnabled: false,
      };
      return whatsAppApi.save({
        ...current,
        ...body,
      });
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['whatsapp-settings'] });
      message.success(t("Đã lưu lịch nhắc tự động"));
    },
    onError,
  });
  const state = connection.data?.status ?? 'Unavailable';
  const connected = state === 'Connected';
  const connecting = ['QrReady', 'AwaitingConfirmation', 'Connecting', 'Restoring'].includes(state);
  const busy = connect.isPending || disconnect.isPending;
  const columns = [
    { title: t("Phòng"), dataIndex: 'roomName', key: 'room' },
    { title: t("Kỳ"), dataIndex: 'period', key: 'period' },
    { title: t("Trạng thái"), key: 'status', render: (_: unknown, row: WhatsAppReminder) => <><Tag color={row.status === 'Sent' ? 'success' : row.status === 'Unknown' || row.status === 'Failed' ? 'warning' : 'default'}>{statusLabel(row.status)}</Tag>{row.detail && <div className="zalo-history-detail">{detailLabel(row.detail)}</div>}</> },
    { title: t("Lần xử lý gần nhất"), key: 'time', render: (_: unknown, row: WhatsAppReminder) => new Intl.DateTimeFormat(getLocale(), { dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(row.sentAtMs ?? (row.lastAttemptAtMs || row.queuedAtMs)) },
  ];
  return <>
    <PageTitle title={t("Nhắc hóa đơn qua WhatsApp")} subtitle={t("Gửi thông tin hóa đơn và link thanh toán từ WhatsApp của bạn.")} extra={<Button onClick={() => setGuide(true)}>{t("Hướng dẫn sử dụng")}</Button>} />
    {settings.isError ? <LoadError error={settings.error} onRetry={() => void settings.refetch()} /> : settings.isPending ? <Skeleton active /> : <div className="zalo-settings-grid">
      <SectionCard title={t("Kết nối WhatsApp")}>
        <Space direction="vertical" size="middle" className="zalo-full-width">
          <Alert showIcon type={connected ? 'success' : 'info'} message={connectionLabel(state)} />
          {connection.isError && <LoadError error={connection.error} onRetry={() => void connection.refetch()} />}
          {!settings.data.gatewayEnabled && <Alert type="warning" message={t("Máy chủ chưa bật dịch vụ WhatsApp. Liên hệ quản trị viên để cấu hình.")} />}
          {connection.data?.qrImage && (state === 'QrReady' || state === 'AwaitingConfirmation') && <div className="zalo-qr"><img width={256} height={256} src={connection.data.qrImage} alt={t("QR đăng nhập WhatsApp")} /></div>}
          <Typography.Paragraph>{t("Kết nối xuất hiện trong mục “Thiết bị liên kết” trên điện thoại. Đăng xuất thiết bị đó sẽ làm Roomie ngừng gửi; hãy quét QR lại để kết nối.")}</Typography.Paragraph>
          <Space wrap>
            {!connected && !connecting && <Button type="primary" icon={<QrcodeOutlined />} disabled={!settings.data.gatewayEnabled || busy} loading={connect.isPending} onClick={() => connect.mutate()}>{t("Tạo QR kết nối")}</Button>}
            {(connected || connecting) && <Popconfirm title={t("Ngắt kết nối và dừng lịch tự động?")} onConfirm={() => disconnect.mutate()} okText={t("Ngắt kết nối")} cancelText={t("Hủy")}><Button danger loading={disconnect.isPending} disabled={busy}>{t("Ngắt kết nối")}</Button></Popconfirm>}
          </Space>
          <Typography.Text type="secondary">{t("Người thuê trả lời thì bạn đọc và trả lời trong WhatsApp bình thường.")}</Typography.Text>
        </Space>
      </SectionCard>
      <SectionCard title={t("Lịch nhắc tự động hàng tháng")}>
        <Space direction="vertical" size="middle" className="zalo-full-width">
          <Typography.Paragraph type="secondary">{t("Tự động quét hóa đơn chưa thanh toán và gửi tin nhắn nhắc nhở qua WhatsApp theo lịch định kỳ.")}</Typography.Paragraph>
          <Form layout="vertical">
            <Form.Item label={t("Kích hoạt lịch nhắc tự động")}>
              <Switch checked={scheduleEnabled} onChange={setScheduleEnabled} checkedChildren={t("Bật")} unCheckedChildren={t("Tắt")} />
            </Form.Item>
            <Form.Item label={t("Ngày trong tháng (1 - 28)")}>
              <InputNumber min={1} max={31} value={dayOfMonth} onChange={(val) => val != null && setDayOfMonth(val)} style={{ width: 140 }} />
            </Form.Item>
            <Space wrap size="large">
              <Form.Item label={t("Giờ gửi (7h - 20h)")}>
                <InputNumber min={7} max={20} value={hour} onChange={(val) => val != null && setHour(val)} addonAfter="h" style={{ width: 120 }} />
              </Form.Item>
              <Form.Item label={t("Phút (0 - 59)")}>
                <InputNumber min={0} max={59} value={minute} onChange={(val) => val != null && setMinute(val)} addonAfter={t("Phút")} style={{ width: 130 }} />
              </Form.Item>
            </Space>
            <div>
              <Button type="primary" loading={saveSettings.isPending} onClick={() => saveSettings.mutate({ scheduleEnabled, dayOfMonth, hour, minute })}>{t("Lưu lịch nhắc tự động")}</Button>
            </div>
          </Form>
        </Space>
      </SectionCard>
    </div>}
    <SectionCard title={t("Hàng đợi nhắc hóa đơn")} extra={<Popconfirm title={t("Thêm các hóa đơn còn nợ vào hàng đợi?")} onConfirm={() => queue.mutate()} okText={t("Thêm vào hàng đợi")} cancelText={t("Hủy")}><Button icon={<MessageOutlined />} disabled={!connected} loading={queue.isPending}>{t("Nhắc hóa đơn còn nợ")}</Button></Popconfirm>}>
      <Typography.Paragraph type="secondary">{t("Hóa đơn mới được tự động thêm vào hàng đợi khi phát hành. Nút bên trên dùng để nhắc lại các hóa đơn còn nợ.")}</Typography.Paragraph>
      <Typography.Paragraph type="secondary">{t("Gửi cách nhau ít nhất 5 giây, từ 7h đến trước 21h. Mỗi hóa đơn tối đa một lần gửi trong 20 giờ; trả đủ thì bỏ qua. Chỉ gửi đến người thuê đang ở có số di động Việt Nam hợp lệ.")}</Typography.Paragraph>
      {history.isError ? <LoadError error={history.error} onRetry={() => void history.refetch()} /> : <ResponsiveTable<WhatsAppReminder> rowKey="invoiceId" columns={columns} dataSource={history.data?.items ?? []} loading={history.isPending} locale={{ emptyText: t("Chưa có hóa đơn trong hàng đợi") }} pagination={{ current: page, pageSize: 20, total: history.data?.total ?? 0, onChange: setPage, showSizeChanger: false }} />}
    </SectionCard>
    <Modal title={t("Hướng dẫn nhắc hóa đơn qua WhatsApp")} open={guide} onCancel={() => setGuide(false)} footer={<Button onClick={() => setGuide(false)}>{t("Đóng")}</Button>}>
      <ol className="zalo-guide">
        <li>{t("Tạo QR, mở ứng dụng WhatsApp → Thiết bị liên kết để quét và xác nhận đăng nhập trên điện thoại.")}</li>
        <li>{t("Giữ thiết bị liên kết WhatsApp đăng nhập. Khi phiên bị đăng xuất, quay lại đây và quét QR mới.")}</li>
        <li>{t("Người thuê trả lời thì bạn đọc và trả lời trong WhatsApp bình thường.")}</li>
      </ol>
      <Alert type="warning" showIcon message={t("Kết nối WhatsApp cá nhân sử dụng thư viện không chính thức; có thể bị gián đoạn hoặc bị WhatsApp hạn chế tài khoản.")} />
      <Typography.Paragraph>{t("Ngắt kết nối tại đây xóa phiên lưu trên Roomie và tắt lịch; bạn có thể gỡ thiết bị liên kết trên điện thoại.")}</Typography.Paragraph>
    </Modal>
  </>;
}

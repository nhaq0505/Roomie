import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Modal, Popconfirm, Skeleton, Space, Tag, Typography } from 'antd';
import { MessageOutlined, QrcodeOutlined } from '@ant-design/icons';
import { t } from '../../shared/i18n/runtime';
import { getLocale } from '../../shared/i18n/runtime';
import { LoadError, PageTitle, SectionCard } from '../../shared/ui/Ui';
import ResponsiveTable from '../../shared/ui/ResponsiveTable';
import { zaloApi, type ZaloReminder } from './index';

function connectionLabel(status: string) {
  switch (status) {
    case 'Connected': return t("Đã kết nối Zalo");
    case 'QrReady': return t("Quét QR bằng ứng dụng Zalo");
    case 'AwaitingConfirmation': return t("Xác nhận đăng nhập trên điện thoại");
    case 'Connecting': case 'Restoring': return t("Đang kết nối Zalo…");
    case 'Expired': return t("QR đã hết hạn. Hãy tạo mã mới.");
    case 'AccountInUse': return t("Zalo này đã kết nối với một chủ trọ khác.");
    case 'Unavailable': return t("Dịch vụ Zalo chưa sẵn sàng");
    default: return t("Chưa kết nối Zalo");
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
    case 'DeliveryUnknown': return t("Có thể tin đã gửi. Kiểm tra Zalo trước khi thêm lại sau 20 giờ.");
    case 'RecipientRejected': return t("Không tìm thấy hoặc không thể gửi đến số điện thoại này.");
    case 'ReconnectRequired': return t("Quét QR lại để tiếp tục kết nối.");
    case 'TenantLinkUnavailable': return t("Link người thuê không còn hợp lệ. Kiểm tra hoặc cấp lại link trong trang phòng.");
    case 'InvoiceIneligible': return t("Hóa đơn đã trả đủ, chưa phát hành hoặc người thuê không còn phù hợp.");
    case 'DisconnectedByOwner': return t("Đã dừng theo yêu cầu ngắt kết nối.");
    default: return '';
  }
}

export default function ZaloSettingsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();
  const [page, setPage] = useState(1);
  const [guide, setGuide] = useState(false);
  const settings = useQuery({ queryKey: ['zalo-settings'], queryFn: zaloApi.settings });
  const connection = useQuery({ queryKey: ['zalo-connection'], queryFn: zaloApi.connection, refetchInterval: 3000 });
  const history = useQuery({ queryKey: ['zalo-history', page], queryFn: () => zaloApi.history(page), refetchInterval: 5000 });
  const refresh = () => { void client.invalidateQueries({ queryKey: ['zalo-connection'] }); void client.invalidateQueries({ queryKey: ['zalo-history'] }); };
  const onError = (error: Error) => { void message.error(error.message); };
  const connect = useMutation({ mutationFn: zaloApi.connect, onError, onSuccess: refresh });
  const disconnect = useMutation({ mutationFn: zaloApi.disconnect, onError, onSuccess: () => { refresh(); void client.invalidateQueries({ queryKey: ['zalo-settings'] }); } });
  const queue = useMutation({ mutationFn: zaloApi.queue, onError, onSuccess: result => { void message.success(t("Đã thêm {0} hóa đơn vào hàng đợi.", [result.queued])); setPage(1); refresh(); } });
  const state = connection.data?.status ?? 'Unavailable';
  const connected = state === 'Connected';
  const connecting = ['QrReady', 'AwaitingConfirmation', 'Connecting', 'Restoring'].includes(state);
  const busy = connect.isPending || disconnect.isPending;
  const columns = [
    { title: t("Phòng"), dataIndex: 'roomName', key: 'room' },
    { title: t("Kỳ"), dataIndex: 'period', key: 'period' },
    { title: t("Trạng thái"), key: 'status', render: (_: unknown, row: ZaloReminder) => <><Tag color={row.status === 'Sent' ? 'success' : row.status === 'Unknown' || row.status === 'Failed' ? 'warning' : 'default'}>{statusLabel(row.status)}</Tag>{row.detail && <div className="zalo-history-detail">{detailLabel(row.detail)}</div>}</> },
    { title: t("Lần xử lý gần nhất"), key: 'time', render: (_: unknown, row: ZaloReminder) => new Intl.DateTimeFormat(getLocale(), { dateStyle: 'short', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' }).format(row.sentAtMs ?? (row.lastAttemptAtMs || row.queuedAtMs)) },
  ];
  return <>
    <PageTitle title={t("Nhắc hóa đơn qua Zalo")} subtitle={t("Gửi thông tin hóa đơn và link thanh toán từ Zalo của bạn.")} extra={<Button onClick={() => setGuide(true)}>{t("Hướng dẫn sử dụng")}</Button>} />
    {settings.isError ? <LoadError error={settings.error} onRetry={() => void settings.refetch()} /> : settings.isPending ? <Skeleton active /> : <div className="zalo-settings-grid">
      <SectionCard title={t("Kết nối Zalo")}>
        <Space direction="vertical" size="middle" className="zalo-full-width">
          <Alert showIcon type={connected ? 'success' : 'info'} message={connectionLabel(state)} />
          {connection.isError && <LoadError error={connection.error} onRetry={() => void connection.refetch()} />}
          {!settings.data.gatewayEnabled && <Alert type="warning" message={t("Máy chủ chưa bật dịch vụ Zalo. Liên hệ quản trị viên để cấu hình.")} />}
          {connection.data?.qrImage && state === 'QrReady' && <div className="zalo-qr"><img width={256} height={256} src={connection.data.qrImage} alt={t("QR đăng nhập Zalo")} /></div>}
          <Typography.Paragraph>{t("Kết nối xuất hiện như thiết bị “Zalo Web” trên điện thoại. Đăng xuất thiết bị đó sẽ làm Roomie ngừng gửi; hãy quét QR lại để kết nối.")}</Typography.Paragraph>
          <Space wrap>
            {!connected && !connecting && <Button type="primary" icon={<QrcodeOutlined />} disabled={!settings.data.gatewayEnabled || busy} loading={connect.isPending} onClick={() => connect.mutate()}>{t("Tạo QR kết nối")}</Button>}
            {(connected || connecting) && <Popconfirm title={t("Ngắt kết nối và dừng lịch tự động?")} onConfirm={() => disconnect.mutate()} okText={t("Ngắt kết nối")} cancelText={t("Hủy")}><Button danger loading={disconnect.isPending} disabled={busy}>{t("Ngắt kết nối")}</Button></Popconfirm>}
          </Space>
          <Typography.Text type="secondary">{t("Người thuê trả lời thì bạn đọc và trả lời trong Zalo bình thường.")}</Typography.Text>
        </Space>
      </SectionCard>
    </div>}
    <SectionCard title={t("Hàng đợi nhắc hóa đơn")} extra={<Popconfirm title={t("Thêm các hóa đơn còn nợ vào hàng đợi?")} onConfirm={() => queue.mutate()} okText={t("Thêm vào hàng đợi")} cancelText={t("Hủy")}><Button icon={<MessageOutlined />} disabled={!connected} loading={queue.isPending}>{t("Nhắc hóa đơn còn nợ")}</Button></Popconfirm>}>
      <Typography.Paragraph type="secondary">{t("Gửi cách nhau ít nhất 5 giây, từ 7h đến trước 21h. Mỗi hóa đơn tối đa một lần gửi trong 20 giờ; trả đủ thì bỏ qua. Chỉ gửi đến người thuê đang ở có số di động Việt Nam hợp lệ.")}</Typography.Paragraph>
      {history.isError ? <LoadError error={history.error} onRetry={() => void history.refetch()} /> : <ResponsiveTable<ZaloReminder> rowKey="invoiceId" columns={columns} dataSource={history.data?.items ?? []} loading={history.isPending} locale={{ emptyText: t("Chưa có hóa đơn trong hàng đợi") }} pagination={{ current: page, pageSize: 20, total: history.data?.total ?? 0, onChange: setPage, showSizeChanger: false }} />}
    </SectionCard>
    <Modal title={t("Hướng dẫn nhắc hóa đơn qua Zalo")} open={guide} onCancel={() => setGuide(false)} footer={<Button onClick={() => setGuide(false)}>{t("Đóng")}</Button>}>
      <ol className="zalo-guide">
        <li>{t("Tạo QR, mở ứng dụng Zalo để quét và xác nhận đăng nhập trên điện thoại.")}</li>
        <li>{t("Giữ thiết bị Zalo Web đăng nhập. Khi phiên bị đăng xuất, quay lại đây và quét QR mới.")}</li>
        <li>{t("Người thuê trả lời thì bạn đọc và trả lời trong Zalo bình thường.")}</li>
      </ol>
      <Alert type="warning" showIcon message={t("Kết nối Zalo cá nhân sử dụng thư viện không chính thức; có thể bị gián đoạn hoặc bị Zalo hạn chế tài khoản.")} />
      <Typography.Paragraph>{t("Không mở thêm phiên Zalo Web cho cùng tài khoản khi đang kết nối Roomie. Ngắt kết nối tại đây xóa phiên lưu trên Roomie và tắt lịch; bạn có thể đăng xuất thiết bị Zalo Web trên điện thoại.")}</Typography.Paragraph>
    </Modal>
  </>;
}

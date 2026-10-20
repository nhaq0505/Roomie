import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Image, Skeleton, Space, Typography } from 'antd';
import { formatVnd } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import { LoadError } from '../../shared/ui/Ui';
import { tenantApi } from './api';

export default function ManualPaymentPanel({ token, invoiceId, onRefresh }: { token: string; invoiceId: string; onRefresh: () => void }) {
  const query = useQuery({ queryKey: ['manual-checkout', token, invoiceId], queryFn: () => tenantApi.manualPayment(token, invoiceId === 'all' ? undefined : invoiceId), retry: false, refetchOnMount: 'always' });
  const payment = query.data;
  return <Card className="tenant-checkout-panel" bordered={false}>
    <Typography.Title level={4}>{t("Chuyển khoản VietQR")}</Typography.Title>
    {query.isError ? <LoadError error={query.error} onRetry={() => void query.refetch()} /> : !payment ? <Skeleton active /> : <>
      <Alert showIcon type="info" message={t("Khoản chuyển khoản sẽ được cập nhật sau khi chủ trọ xác nhận đã nhận tiền.")} />
      <div className="tenant-manual-qr"><Image src={payment.qrUrl} alt={t("Mã VietQR chuyển khoản")} width={280} preview={false} referrerPolicy="no-referrer" /></div>
      <div className="invoice-breakdown"><div><span>{t("Số tiền")}</span><strong>{formatVnd(payment.amount)}</strong></div><div><span>{t("Số tài khoản")}</span><Typography.Text copyable>{payment.accountNumber}</Typography.Text></div><div><span>{t("Tên chủ tài khoản")}</span><strong>{payment.accountName}</strong></div><div><span>{t("Mã ngân hàng")}</span><strong>{payment.bankBin}</strong></div><div><span>{t("Nội dung chuyển khoản")}</span><Typography.Text copyable>{payment.description}</Typography.Text></div></div>
      <Typography.Paragraph type="secondary">{t("{0} hóa đơn", [payment.invoices.length])} · {t("Kiểm tra tài khoản người nhận trên ứng dụng ngân hàng trước khi chuyển tiền.")}</Typography.Paragraph>
      <Space wrap><Button onClick={onRefresh}>{t("Làm mới trạng thái hóa đơn")}</Button><Button onClick={() => void query.refetch()} loading={query.isFetching}>{t("Cập nhật mã QR")}</Button></Space>
    </>}
  </Card>;
}

import { CheckCircleFilled, ClockCircleOutlined, CloseCircleFilled, CopyOutlined, ExclamationCircleFilled } from '@ant-design/icons';
import { Alert, Button, Card, QRCode, Typography } from 'antd';
import { formatVnd, periodLabel } from '../../shared/format';
import { getLocale, t } from '../../shared/i18n/runtime';
import { type Invoice } from '../invoices/index';
import { checkoutExpiration, type CheckoutSession } from './checkout';


export default function TenantCheckoutCard({ checkout, invoice, error, checking, reconcileBusy, hostedUrl, expiredByClock, onCheck, onCopy, onRetry, retryBusy }: { checkout: CheckoutSession; invoice: Invoice; error: string; checking: boolean; reconcileBusy: boolean; hostedUrl: string | null; expiredByClock: boolean; onCheck: () => void; onCopy: (text: string, successMessage?: string) => void; onRetry: () => void; retryBusy: boolean }) {
  const status = checkout.status;
  const statusCopy = status === 'paid'
    ? { type: 'success' as const, icon: <CheckCircleFilled />, title: t("Thanh toán thành công"), description: t("Khoản thanh toán đã được xác nhận và hóa đơn đã cập nhật.") }
    : status === 'expired'
      ? { type: 'warning' as const, icon: <ClockCircleOutlined />, title: t("Giao dịch đã hết hạn"), description: t("payOS xác nhận giao dịch đã hết hạn. Hóa đơn vẫn hiển thị số dư hiện tại; bạn có thể thanh toán lại khi sẵn sàng.") }
      : status === 'cancelled'
        ? { type: 'info' as const, icon: <CloseCircleFilled />, title: t("Giao dịch đã bị hủy"), description: t("payOS xác nhận giao dịch đã bị hủy. Hóa đơn vẫn còn số dư.") }
        : status === 'failed'
          ? { type: 'error' as const, icon: <ExclamationCircleFilled />, title: t("Thanh toán chưa hoàn tất"), description: t("payOS chưa hoàn tất giao dịch. Vui lòng thử lại hoặc liên hệ chủ trọ.") }
          : { type: 'info' as const, icon: <ClockCircleOutlined />, title: t("Thanh toán đang chờ xác nhận"), description: t("Khoản thanh toán sẽ được cập nhật sau khi payOS xác nhận giao dịch. Làm mới hóa đơn để kiểm tra trạng thái mới nhất.") };
  const details = [
    checkout.accountName ? [t("Tên người nhận"), checkout.accountName] : null,
    checkout.accountNumber ? [t("Số tài khoản"), checkout.accountNumber] : null,
    checkout.bankBin ? [t("Mã ngân hàng (BIN)"), checkout.bankBin] : null,
  ].filter((entry): entry is [string, string] => !!entry);
  const hasQr = status === 'pending' && !expiredByClock && !checkout.verificationPending && !!checkout.qrCode;
  const invoicePaid = status === 'paid';
  return <Card className="tenant-checkout-panel" bordered={false}>
    <Alert className={`tenant-checkout-status tenant-checkout-status--${status}`} type={statusCopy.type} showIcon icon={statusCopy.icon} message={statusCopy.title} description={statusCopy.description} />
    <div className="tenant-checkout-amount"><span>{invoicePaid ? t("Số tiền đã thanh toán") : t("Số tiền cần chuyển")}</span><div style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 8 }}><strong>{formatVnd(checkout.amount)}</strong>{!invoicePaid && <Button type="text" size="small" icon={<CopyOutlined />} onClick={() => onCopy(String(checkout.amount), t("Đã sao chép số tiền"))}>{t("Sao chép")}</Button>}</div><small>{invoice.id === 'all' ? t("Thanh toán tất cả") : invoice.kind === 'Monthly' ? periodLabel(invoice.period) : invoice.kind === 'Deposit' ? t("Tiền cọc nhận phòng") : t("Hóa đơn bổ sung")} · {t("Hóa đơn")}</small></div>
    {expiredByClock && <Alert className="tenant-checkout-no-qr" type="warning" showIcon message={t("Mã QR đã hết thời hạn hiển thị")} description={t("Mã QR đã hết thời hạn hiển thị. Hệ thống đang xác minh giao dịch với payOS; hãy đợi trạng thái mới trước khi chuyển khoản lại.")} action={<Button size="small" type="primary" onClick={onRetry} loading={retryBusy}>{t("Tạo lại mã QR")}</Button>} />}
    {status === 'pending' && checkout.verificationPending && !expiredByClock && <Alert className="tenant-checkout-no-qr" type="info" showIcon message={t("Đang xác minh thanh toán")} description={t("Khoản thanh toán sẽ được cập nhật sau khi payOS xác nhận giao dịch. Làm mới hóa đơn để kiểm tra trạng thái mới nhất.")} />}
    {hasQr && <div className="tenant-checkout-qr"><QRCode value={checkout.qrCode!} size={224} bordered={false} type="svg" /><Typography.Text>{t("Quét mã bằng ứng dụng ngân hàng để thanh toán đúng số tiền cho hóa đơn.")}</Typography.Text></div>}
    {status === 'pending' && !expiredByClock && !checkout.verificationPending && !checkout.qrCode && <Alert className="tenant-checkout-no-qr" type="warning" showIcon message={t("Mã QR chưa có sẵn cho giao dịch này.")} description={hostedUrl ? t("Bạn có thể mở trang thanh toán payOS an toàn bên dưới để tiếp tục.") : t("Chưa có trang thanh toán payOS an toàn cho giao dịch này. Hãy kiểm tra trạng thái hoặc liên hệ chủ trọ trước khi thanh toán.")} />}
    {details.length > 0 && <div className="tenant-checkout-details">{details.map(([label, value]) => <div key={label}><span>{label}</span><div style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}><strong>{value}</strong>{label === t("Số tài khoản") && <Button type="text" size="small" icon={<CopyOutlined />} onClick={() => onCopy(value, t("Đã sao chép số tài khoản"))}>{t("Sao chép")}</Button>}</div></div>)}</div>}
    {checkout.description && <div className="tenant-checkout-transfer"><div className="tenant-checkout-transfer-label"><span>{t("Nội dung chuyển khoản")}</span><Button type="text" size="small" icon={<CopyOutlined />} onClick={() => onCopy(checkout.description!, t("Đã sao chép nội dung chuyển khoản."))}>{t("Sao chép")}</Button></div><code>{checkout.description}</code></div>}
    {checkout.expiresAt && status === 'pending' && <div className="tenant-checkout-expiry"><ClockCircleOutlined /> <span>{t("Thời hạn thanh toán")}: {checkoutExpiration(checkout.expiresAt)}</span></div>}
    <div className="tenant-checkout-order"><span>{t("Mã đơn thanh toán")}</span><strong>{new Intl.NumberFormat(getLocale()).format(checkout.orderCode)}</strong></div>
    {error && <Alert className="tenant-checkout-error" type="warning" showIcon message={error} />}
    {hostedUrl && status === 'pending' && !expiredByClock && !checkout.verificationPending && <Button className="tenant-checkout-hosted" href={hostedUrl} target="_blank" rel="noopener noreferrer">{t("Mở trang payOS")}</Button>}
    {status === 'pending' && <div className="tenant-checkout-actions">{expiredByClock && <Button type="primary" onClick={onRetry} loading={retryBusy}>{t("Tạo lại mã QR")}</Button>}<Button type={expiredByClock ? 'default' : 'primary'} onClick={onCheck} loading={checking} disabled={reconcileBusy && !checking}>{checking ? t("Đang kiểm tra…") : t("Kiểm tra ngay")}</Button></div>}
    {['expired', 'cancelled', 'failed'].includes(status) && invoice.outstanding > 0 && <Button className="tenant-checkout-retry" type="primary" onClick={onRetry} loading={retryBusy}>{t("Thanh toán")}</Button>}
  </Card>;
}

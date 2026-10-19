import { ArrowLeftOutlined, LockOutlined } from '@ant-design/icons';
import { Alert, Button, Card, Result, Skeleton, Typography } from 'antd';
import { useParams } from 'react-router-dom';
import { formatVnd, periodLabel } from '../../shared/format';
import { errorText } from '../../shared/i18n/errors';
import { t } from '../../shared/i18n/runtime';
import { httpStatus, invoiceIsPaid, isAccessError, isHostedPayOsUrl } from './checkout';
import TenantCheckoutCard from './TenantCheckoutCard';
import { TenantBrand, TenantFooter } from './TenantPage';
import { useTenantCheckout } from './useTenantCheckout';

export default function TenantCheckoutPage() {
  const { token = '', invoiceId = '' } = useParams();
  return <TenantCheckoutContent key={`${token}:${invoiceId}`} token={token} invoiceId={invoiceId} />;
}

function TenantCheckoutContent({ token, invoiceId }: { token: string; invoiceId: string }) {
const state = useTenantCheckout(token, invoiceId);
  const { navigate, location, checkout, createError, checkoutError, tenantAccessError, clockNow, query, invoice, paymentMutation, reconcileMutation, beginCreate, checkNow, copyText } = state;
  const search = window.location.search || location.search;
  const tenantPath = location.pathname.startsWith('/tenant/') ? `/tenant/${encodeURIComponent(token)}` : `/t/${encodeURIComponent(token)}`;
  const pageAccessError = tenantAccessError ?? (query.isError && isAccessError(query.error) ? { status: httpStatus(query.error), message: query.error.message } : null);
  const checkoutExpiredByClock = !!checkout && checkout.status === 'pending' && checkout.expiresAt !== null && clockNow >= checkout.expiresAt * 1000;
  const hostedUrl = checkout && isHostedPayOsUrl(checkout.checkoutUrl) ? checkout.checkoutUrl : null;

  return <div className="tenant-page"><TenantBrand />
    <main className="tenant-main tenant-checkout-page">
      <Button className="tenant-checkout-back" type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate(`${tenantPath}${search}`)}>{t("Hóa đơn của bạn")}</Button>
      <div className="tenant-checkout-heading"><div className="eyebrow">{t("THÔNG TIN PHÒNG")}</div><Typography.Title level={2}>{t("Thanh toán hóa đơn")}</Typography.Title><Typography.Text>{query.data?.room?.name ?? t("Phòng của bạn")}{query.data?.room?.buildingName ? ` · ${query.data.room.buildingName}` : ''}</Typography.Text></div>
      {query.isError && query.data && !isAccessError(query.error) && <Alert className="tenant-payment-error" type="warning" showIcon message={t("Đã có lỗi khi tải dữ liệu.")} description={errorText(query.error.message)} action={<Button size="small" onClick={() => void query.refetch()}>{t("Thử lại")}</Button>} />}
      {pageAccessError ? <div className="tenant-error"><Result status={pageAccessError.status === 404 ? '404' : 'error'} title={pageAccessError.status === 404 ? t("Không tìm thấy liên kết") : t("Không tải được hóa đơn")} subTitle={pageAccessError.status === 404 ? t("Liên kết này không hợp lệ hoặc đã bị thu hồi. Hãy liên hệ chủ trọ để được gửi liên kết mới.") : pageAccessError.message} /></div>
        : query.isPending || query.isFetching && !query.data ? <div className="tenant-loading"><Skeleton active paragraph={{ rows: 8 }} /></div>
          : query.isError && !query.data ? <div className="tenant-error"><Result status="error" title={t("Không tải được hóa đơn")} subTitle={errorText(query.error.message)} extra={<Button type="primary" onClick={() => void query.refetch()}>{t("Thử lại")}</Button>} /></div>
            : !invoice ? <div className="tenant-error"><Result status="404" title={t("Không tìm thấy hóa đơn")} subTitle={t("Hóa đơn này không thuộc liên kết riêng hiện tại hoặc đã không còn khả dụng.")} extra={<Button type="primary" onClick={() => navigate(`${tenantPath}${search}`)}>{t("Hóa đơn của bạn")}</Button>} /></div>
            : invoiceIsPaid(invoice) ? <Card className="tenant-checkout-panel" bordered={false}><Result status="success" title={t("Đã thanh toán")} subTitle={t("Khoản thanh toán đã được xác nhận và hóa đơn đã cập nhật.")} /><div className="tenant-checkout-amount"><span>{t("Số tiền đã thanh toán")}</span><strong>{formatVnd(invoice.paid || invoice.total)}</strong><small>{invoice.kind === 'Monthly' ? periodLabel(invoice.period) : invoice.kind === 'Deposit' ? t("Tiền cọc nhận phòng") : t("Hóa đơn bổ sung")} · {t("Hóa đơn")}</small></div></Card>
              : paymentMutation.isPending && !checkout ? <div className="tenant-loading"><Skeleton active paragraph={{ rows: 8 }} /></div>
                : checkout ? <TenantCheckoutCard checkout={checkout} invoice={invoice} error={checkoutError} checking={reconcileMutation.isPending} reconcileBusy={reconcileMutation.isPending} hostedUrl={hostedUrl} expiredByClock={checkoutExpiredByClock} onCheck={checkNow} onCopy={copyText} onRetry={beginCreate} retryBusy={paymentMutation.isPending} />
                  : createError ? <Card className="tenant-checkout-panel" bordered={false}><Alert type="error" showIcon message={t("Chưa thể bắt đầu thanh toán")} description={errorText(createError)} /><Button className="tenant-checkout-retry" type="primary" onClick={beginCreate} loading={paymentMutation.isPending}>{t("Thử lại")}</Button></Card>
                  : <div className="tenant-loading"><Skeleton active paragraph={{ rows: 8 }} /></div>}
      <div className="tenant-secure-note"><LockOutlined /><span>{t("Thông tin này chỉ dành cho người có liên kết riêng của phòng.")}</span></div>
    </main>
    <TenantFooter />
  </div>;
}

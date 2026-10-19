import { ArrowRightOutlined, CheckCircleFilled, CreditCardOutlined, FileTextOutlined, HomeOutlined, LockOutlined } from '@ant-design/icons';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Alert, App, Button, Card, Collapse, Pagination, Result, Skeleton, Tag, Typography } from 'antd';
import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { formatVnd, periodLabel } from '../../shared/format';
import { errorText } from '../../shared/i18n/errors';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { formatDate, t } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';
import { type Invoice } from '../invoices/index';
import { tenantApi } from './index';

function statusLabel(status: string) {
  if (status === 'Paid') return t("Đã thanh toán");
  if (status === 'Partial') return t("Thanh toán một phần");
  return t("Chưa thanh toán");
}
function statusClass(status: string) {
  if (status === 'Paid') return 'status-paid';
  if (status === 'Partial') return 'status-partial';
  return 'status-unpaid';
}
function httpStatus(error: unknown) {
  return error && typeof error === 'object' && 'status' in error ? Number((error as { status?: number }).status) : 0;
}
function isTenantAccessError(error: unknown) {
  return [401, 403, 404].includes(httpStatus(error));
}
function TenantInvoice({ invoice, onPay, onReconcile, busy, paymentDisabled, reconciling }: { invoice: Invoice; onPay: () => void; onReconcile: () => void; busy: boolean; paymentDisabled: boolean; reconciling: boolean }) {
  const items = invoice.kind === 'Supplemental' ? [{ key: 'supplemental', label: t("Chi tiết khoản thu bổ sung"), children: <div className="tenant-breakdown"><div><span style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{invoice.description}</span><strong>{formatVnd(invoice.total)}</strong></div>{invoice.isFinalMeterReading && <><div><span>{t("Điện cuối kỳ ·")} {invoice.previousReading} → {invoice.currentReading} kWh</span><strong>{formatVnd(invoice.electricityAmount)}</strong></div><div><span>{t("Khoản còn lại")}</span><strong>{formatVnd(invoice.serviceAmount)}</strong></div></>}<div><span>{t("Hạn thanh toán")}</span><strong>{formatDate(invoice.dueDate)}</strong></div></div> }] : invoice.kind === 'Deposit' ? [{ key: 'deposit', label: t("Chi tiết tiền cọc"), children: <div className="tenant-breakdown"><div><span>{t("Cọc yêu cầu")}</span><strong>{formatVnd(invoice.total)}</strong></div><div><span>{t("Đã đóng")}</span><strong>{formatVnd(invoice.paid)}</strong></div><div><span>{t("Chủ trọ đang giữ")}</span><strong>{formatVnd(invoice.depositHeld)}</strong></div>{invoice.depositSettlements.map((entry) => <div key={entry.id}><span>{entry.kind === 'Refund' ? t("Đã hoàn") : t("Khấu trừ")} · {entry.reason}</span><strong>{formatVnd(entry.amount)}</strong></div>)}</div> }] : [{ key: 'details', label: <span className="tenant-detail-toggle">{t("Xem chi tiết hóa đơn")} <ArrowRightOutlined /></span>, children: <div className="tenant-breakdown"><div><span>{t("Tiền thuê")}</span><strong>{formatVnd(invoice.rent)}</strong></div><div><span>{t("Điện")} <small>{invoice.consumption} kWh</small></span><strong>{formatVnd(invoice.electricityAmount)}</strong></div><div><span>{t("Nước")}</span><strong>{formatVnd(invoice.waterAmount)}</strong></div><div><span>{t("Phí dịch vụ")}</span><strong>{formatVnd(invoice.serviceAmount)}</strong></div><div className="tenant-meter"><span>{t("Chỉ số điện")}</span><strong>{invoice.previousReading} → {invoice.currentReading}</strong></div></div> }];
  return <Card className="tenant-invoice-card" bordered={false}><div className="tenant-invoice-top"><div><span className="tenant-invoice-period">{invoice.kind === 'Deposit' ? t("Tiền cọc nhận phòng") : invoice.kind === 'Supplemental' ? t("Hóa đơn bổ sung") : periodLabel(invoice.period)}</span><Tag className={`status-pill ${statusClass(invoice.status)}`}>{statusLabel(invoice.status)}</Tag></div><div className="tenant-total">{formatVnd(invoice.total)}</div></div><Collapse ghost items={items} expandIconPosition="end" /><div className="tenant-payment-line"><div><span>{t("Còn phải thanh toán")}</span><strong className={invoice.outstanding > 0 ? 'balance-due' : 'balance-clear'}>{formatVnd(invoice.outstanding)}</strong></div>{invoice.outstanding > 0 ? <div className="tenant-payment-actions">{(invoice.hasPendingPayment || reconciling) && <Button type="link" size="small" loading={reconciling} onClick={onReconcile}>{t("Kiểm tra giao dịch")}</Button>}<Button type="primary" icon={<CreditCardOutlined />} loading={busy} disabled={paymentDisabled && !busy} onClick={onPay}>{t("Thanh toán")}</Button></div> : <span className="tenant-paid-label"><CheckCircleFilled />  {t("Đã thanh toán")}</span>}</div></Card>;
}

export default function TenantPage() {
  const { token = '' } = useParams();
  return <TenantPageContent key={token} token={token} />;
}

function TenantPageContent({ token }: { token: string }) {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const [page, setPage] = useState(1);
  const [openingInvoiceId, setOpeningInvoiceId] = useState('');
  const query = useQuery({
    queryKey: ['tenant', token, page],
    queryFn: () => tenantApi.overview(token, page),
    enabled: !!token,
    retry: false,
  });
  const reconcileMutation = useMutation({
    mutationFn: (invoiceId: string) => tenantApi.reconcile(token, invoiceId),
    onSuccess: async (result) => {
      await query.refetch();
      if (result.invoice.status.toLowerCase() === 'paid' || result.invoice.outstanding <= 0) {
        message.success(t("payOS đã xác nhận thanh toán."));
      } else if (result.status.trim().toLowerCase() === 'nopayment') {
        message.info(t("Chưa có giao dịch payOS cần đối chiếu cho hóa đơn này."));
      } else if (['cancelled', 'canceled', 'expired'].includes(result.status.trim().toLowerCase())) {
        message.warning(t("Giao dịch trước đã kết thúc. Bạn có thể bắt đầu thanh toán mới."));
      } else {
        message.info(t("payOS chưa xác nhận khoản thanh toán. Hãy thử kiểm tra lại sau."));
      }
    },
    onError: (error: Error) => message.error(error.message),
  });

  if (query.isPending) return <div className="tenant-page"><TenantBrand /><div className="tenant-loading"><Skeleton active paragraph={{ rows: 8 }} /></div></div>;
  const pageAccessError = query.isError && isTenantAccessError(query.error) ? { status: httpStatus(query.error), message: query.error.message } : null;
  if (pageAccessError || query.isError && !query.data) {
    const notFound = pageAccessError?.status === 404;
    return <div className="tenant-page"><TenantBrand /><div className="tenant-error"><Result status={notFound ? '404' : 'error'} title={notFound ? t("Không tìm thấy liên kết") : t("Không tải được hóa đơn")} subTitle={notFound ? t("Liên kết này không hợp lệ hoặc đã bị thu hồi. Hãy liên hệ chủ trọ để được gửi liên kết mới.") : pageAccessError?.message ?? query.error?.message ?? t("Đã có lỗi khi tải dữ liệu.")} /></div><TenantFooter /></div>;
  }
  const overview = query.data;
  const invoices = [...(overview?.invoices ?? [])].sort((a, b) => b.period.localeCompare(a.period));
  const outstanding = overview?.totals.outstanding ?? 0;
  const searchParams = new URLSearchParams(window.location.search || location.search);
  const invoicePath = location.pathname.startsWith('/tenant/') ? '/tenant' : '/t';
  const tenantPath = `${invoicePath}/${encodeURIComponent(token)}`;
  function openCheckout(invoiceId: string) {
    setOpeningInvoiceId(invoiceId);
    navigate(`${tenantPath}/invoices/${encodeURIComponent(invoiceId)}/checkout${searchParams.toString() ? `?${searchParams}` : ''}`);
  }
  return <div className="tenant-page"><TenantBrand />
    <main className="tenant-main"><div className="tenant-welcome"><div className="tenant-house-mark"><HomeOutlined /></div><div><div className="eyebrow">{t("THÔNG TIN PHÒNG")}</div><Typography.Title level={2}>{overview?.room?.name ?? t("Phòng của bạn")}</Typography.Title><Typography.Text>{overview?.room?.buildingName || t("Dãy trọ")}{overview?.room?.occupancy?.tenantName ? ` · ${overview.room.occupancy.tenantName}` : ''}</Typography.Text></div></div>
      <div className="tenant-summary"><div><span>{t("Tổng còn phải thanh toán")}</span><strong className={outstanding > 0 ? 'balance-due' : 'balance-clear'}>{formatVnd(outstanding)}</strong></div><div className="tenant-summary-lock"><LockOutlined />  {t("Liên kết riêng tư")}</div></div>
      {query.isError && query.data && !isTenantAccessError(query.error) && <Alert className="tenant-payment-error" type="warning" showIcon message={t("Đã có lỗi khi tải dữ liệu.")} description={errorText(query.error.message)} action={<Button size="small" onClick={() => void query.refetch()}>{t("Thử lại")}</Button>} />}
      {searchParams.get('payment') === 'returned' && <Alert className="tenant-payment-error" type="info" showIcon message={t("Đang xác minh thanh toán")} description={t("Khoản thanh toán sẽ được cập nhật sau khi payOS xác nhận giao dịch. Làm mới hóa đơn để kiểm tra trạng thái mới nhất.")} action={<Button size="small" onClick={() => void query.refetch()}>{t("Làm mới")}</Button>} />}
      {searchParams.get('payment') === 'cancelled' && <Alert className="tenant-payment-error" type="info" showIcon message={t("Bạn đã quay lại từ trang thanh toán")} description={t("Hóa đơn vẫn hiển thị số dư hiện tại. Bạn có thể thử thanh toán lại khi sẵn sàng.")} />}
      <div className="tenant-section-heading"><div><Typography.Title level={4}>{t("Hóa đơn của bạn")}</Typography.Title><span>{t("Tiền cọc, hóa đơn tháng và khoản thu bổ sung được theo dõi riêng.")}</span></div><span className="tenant-count">{overview?.total ?? 0}  {t("hóa đơn")}</span></div>
      <Pagination current={page} total={overview?.total ?? 0} pageSize={10} onChange={setPage} showSizeChanger={false} hideOnSinglePage />
      {invoices.length ? <div className="tenant-invoice-list">{invoices.map((invoice) => <TenantInvoice key={invoice.id} invoice={invoice} busy={openingInvoiceId === invoice.id} paymentDisabled={false} reconciling={reconcileMutation.isPending && reconcileMutation.variables === invoice.id} onPay={() => openCheckout(invoice.id)} onReconcile={() => reconcileMutation.mutate(invoice.id)} />)}</div> : <Card className="tenant-empty" bordered={false}><FileTextOutlined /><strong>{t("Chưa có hóa đơn")}</strong><span>{t("Hóa đơn mới sẽ xuất hiện ở đây sau khi chủ trọ hoàn tất chốt kỳ.")}</span></Card>}
      <div className="tenant-secure-note"><LockOutlined /><span>{t("Thông tin này chỉ dành cho người có liên kết riêng của phòng.")}</span></div>
    </main>
    <TenantFooter />
  </div>;
}

export function TenantBrand() {
  return <header className="tenant-header"><div className="brand-mark"><RoomieMark /></div><div className="tenant-brand-copy"><strong>Roomie</strong><span>{t("HÓA ĐƠN PHÒNG TRỌ")}</span></div><LanguageSelector /><div className="tenant-header-secure"><LockOutlined />  {t("Liên kết riêng tư")}</div></header>;
}
export function TenantFooter() {
  return <footer className="tenant-footer"><span>{t("Roomie giúp việc quản lý nhà trọ rõ ràng hơn.")}</span><span>{t("Hỗ trợ thanh toán an toàn")}</span></footer>;
}

import { useRef } from 'react';
import { statusLabel, statusClass } from './presentation';
import { CheckCircleFilled, CreditCardOutlined, FileTextOutlined, MessageOutlined, WalletOutlined } from '@ant-design/icons';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Col, Collapse, DatePicker, Drawer, Form, Input, InputNumber, Row, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { formatVnd, periodLabel } from '../../shared/format';
import { dateFormat, formatDate, t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import { LoadError } from '../../shared/ui/Ui';
import DepositLedger from './DepositLedger';
import { invoicesApi, type Invoice, type InvoiceCorrection } from './index';
import type { useInvoicesPage } from './useInvoicesPage';

export default function InvoiceDrawer({ state }: { state: ReturnType<typeof useInvoicesPage> }) {
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const publishKeys = useRef(new Map<string, string>());
  const { adjustmentExpanded, setAdjustmentExpanded, selectedId, receiptForm, correctionForm, detailQuery, reconcileMutation, receiptMutation, invoice, correctionQuery, correctionMutation, confirmReceipt, closeInvoice, confirmCorrection, adjustmentBlockedReason, hasMonthlyOverlap, copyReminderMessage } = state;

  const publishMutation = useMutation({
    mutationFn: async ({ invoice: targetInvoice, requestId }: { invoice: Invoice; requestId: string; key: string }) => {
      return invoicesApi.publishDraft(targetInvoice.id, {
        expectedVersion: targetInvoice.version ?? 0,
        requestId,
        rent: targetInvoice.rent,
        electricityAmount: targetInvoice.electricityAmount,
        waterAmount: targetInvoice.waterAmount,
        serviceAmount: targetInvoice.serviceAmount,
      });
    },
    onSuccess: async (_, variables) => {
      publishKeys.current.delete(variables.key);
      await Promise.all([
        client.invalidateQueries({ queryKey: ['invoices'] }),
        client.invalidateQueries({ queryKey: ['invoice', variables.invoice.id] }),
        client.invalidateQueries({ queryKey: ['readings'] }),
        client.invalidateQueries({ queryKey: ['dashboard'] }),
      ]);
      message.success(t("Đã phát hành hóa đơn"));
    },
    onError: (e: Error) => message.error(e.message),
  });

  const handlePublish = (targetInvoice: Invoice) => {
    const key = `${targetInvoice.id}:${targetInvoice.version ?? 0}`;
    let requestId = publishKeys.current.get(key);
    if (!requestId) {
      requestId = crypto.randomUUID();
      publishKeys.current.set(key, requestId);
    }
    modal.confirm({
      title: t("Phát hành hóa đơn nháp?"),
      content: t("Khách sẽ thấy hóa đơn sau khi phát hành. Kiểm tra đủ tiền thuê, điện, nước và phí dịch vụ trước khi tiếp tục."),
      okText: t("Phát hành hóa đơn"),
      cancelText: t("Kiểm tra lại"),
      onOk: () => publishMutation.mutateAsync({ invoice: targetInvoice, requestId, key }),
    });
  };
  return (
    <Drawer className="invoice-drawer" title={t("Chi tiết hóa đơn")} width={520} open={!!selectedId} onClose={closeInvoice} destroyOnClose>
      {detailQuery.isPending ? <div className="drawer-loading"><div className="loading-placeholder" /></div> : detailQuery.isError ? <LoadError error={detailQuery.error} onRetry={() => void detailQuery.refetch()} /> : invoice && <>
        <div className="invoice-detail-head"><div className="invoice-detail-mark"><FileTextOutlined /></div><div><Typography.Title level={4}>{invoice.roomName}</Typography.Title><Typography.Text type="secondary">{invoice.buildingName || t("Dãy trọ")} · {invoice.tenantName || t("Người thuê")}</Typography.Text></div>{invoice.lifecycleState === 'Draft' ? <Tag className="draft-state-tag">{t("Bản nháp")}</Tag> : <Tag className={`status-pill ${statusClass(invoice.status)}`}>{statusLabel(invoice.status)}</Tag>}</div>
        <div className="invoice-period-box"><span>{t("Kỳ hóa đơn")}</span><strong>{invoice.kind === 'Deposit' ? t("Tiền cọc nhận phòng") : invoice.kind === 'Supplemental' ? t("Hóa đơn bổ sung") : periodLabel(invoice.period)}</strong><span>{t("Phát hành")} {invoice.issuedAt ? dayjs(invoice.issuedAt).format(dateFormat()) : '—'}</span></div>
        {invoice.lifecycleState !== 'Draft' && invoice.tenantToken && (
          <div className="invoice-share-actions" style={{ display: 'flex', flexWrap: 'wrap', gap: 8, margin: '14px 0' }}>
            <Button
              type="primary"
              icon={<MessageOutlined />}
              onClick={() => void copyReminderMessage(invoice)}
            >
              {t("Copy tin nhắn thu tiền")}
            </Button>
          </div>
        )}
        {invoice.kind === 'Supplemental' ? <div className="invoice-breakdown"><div><span>{t("Nội dung")}</span><strong style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{invoice.description}</strong></div>{invoice.isFinalMeterReading && <><div><span>{t("Điện cuối kỳ ·")} {invoice.previousReading} → {invoice.currentReading} kWh</span><strong>{formatVnd(invoice.electricityAmount)}</strong></div><div><span>{t("Khoản còn lại")}</span><strong>{formatVnd(invoice.serviceAmount)}</strong></div></>}<div className="breakdown-total"><span>{t("Số tiền")}</span><strong>{formatVnd(invoice.total)}</strong></div></div> : invoice.kind === 'Deposit' ? <div className="invoice-breakdown"><div className="breakdown-total"><span>{t("Tiền cọc yêu cầu")}</span><strong>{formatVnd(invoice.total)}</strong></div></div> : <div className="invoice-breakdown"><div><span>{t("Tiền thuê phòng")}</span><strong>{formatVnd(invoice.rent)}</strong></div><div><span>{t("Tiền điện")} <small>{invoice.consumption} kWh · {invoice.previousReading} → {invoice.currentReading}</small></span><strong>{formatVnd(invoice.electricityAmount)}</strong></div><div><span>{t("Tiền nước")}{invoice.waterByMeter && invoice.waterConsumption != null ? <> <small>{invoice.waterConsumption} m³ · {invoice.previousWaterReading ?? 0} → {invoice.currentWaterReading ?? 0}</small></> : null}</span><strong>{formatVnd(invoice.waterAmount)}</strong></div><div><span>{t("Phí dịch vụ")}</span><strong>{formatVnd(invoice.serviceAmount)}</strong></div><div className="breakdown-total"><span>{t("Tổng hóa đơn")}</span><strong>{formatVnd(invoice.total)}</strong></div></div>}
        {invoice.lifecycleState === 'Draft' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12, marginTop: 12 }}>
            <Alert type="warning" showIcon message={t("Bản nháp chưa gửi cho khách")} description={t("Bản nháp chưa có trạng thái thanh toán. Mở trang Lập hóa đơn để chỉnh và phát hành.")} />
            <Button type="primary" loading={publishMutation.isPending} onClick={() => handlePublish(invoice)}>{t("Phát hành hóa đơn")}</Button>
          </div>
        ) : <>
          <div className="invoice-balance-box"><div><span>{t("Đã ghi nhận")}</span><strong>{formatVnd(invoice.paid)}</strong></div><div><span>{t("Còn phải thu")}</span><strong className={invoice.outstanding > 0 ? 'balance-due' : 'balance-clear'}>{formatVnd(invoice.outstanding)}</strong></div>{invoice.dueDate && <span className="due-date-note">{t("Hạn thanh toán:")} {dayjs(invoice.dueDate).format(dateFormat())}</span>}</div>
          {invoice.outstanding > 0 && <div className="receipt-form-block"><div className="receipt-form-title"><CreditCardOutlined /><div><strong>{t("Đối soát thanh toán payOS")}</strong><span>{t("Xem trạng thái thanh toán trực tuyến của hóa đơn.")}</span></div></div><Button block loading={reconcileMutation.isPending && reconcileMutation.variables === invoice.id} onClick={() => reconcileMutation.mutate(invoice.id)}>{t("Kiểm tra giao dịch payOS")}</Button></div>}
          {invoice.outstanding > 0 ? <div className="receipt-form-block"><div className="receipt-form-title"><WalletOutlined /><div><strong>{t("Ghi nhận khoản thu")}</strong><span>{invoice.hasPendingPayment ? t("Link payOS đang chờ sẽ được tự động kiểm tra và hủy khi ghi nhận khoản thu ngoài.") : t("Tiền mặt hoặc chuyển khoản đã nhận bên ngoài.")}</span></div></div><Form form={receiptForm} layout="vertical" requiredMark={false} onFinish={confirmReceipt}><Typography.Paragraph><strong>{t("Số tiền nhận")}: {formatVnd(invoice.outstanding)}</strong></Typography.Paragraph><Form.Item label={t("Ghi chú")} name="note"><Input placeholder={t("Ví dụ: Chuyển khoản ngày 12/10")} maxLength={200} /></Form.Item><Button type="primary" block htmlType="submit" loading={receiptMutation.isPending}>{t("Lưu khoản thu")}</Button></Form></div> : <div className="invoice-paid-note"><CheckCircleFilled /> {t("Hóa đơn đã được thanh toán đầy đủ.")}</div>}
        </>}
        {invoice.lifecycleState !== 'Draft' && <section className="invoice-corrections">
          <Collapse
            className="invoice-adjustment-collapse"
            ghost
            activeKey={adjustmentExpanded ? ['adjustment'] : []}
            onChange={(keys) => setAdjustmentExpanded(keys.includes('adjustment'))}
            items={[{
              key: 'adjustment',
              label: invoice.kind === 'Monthly' ? t("Điều chỉnh hóa đơn") : invoice.kind === 'Deposit' ? t("Điều chỉnh tiền cọc") : t("Điều chỉnh hóa đơn bổ sung"),
              forceRender: true,
              children: <>
                <Typography.Paragraph type="secondary">{t("Ghi lại lý do và toàn bộ thay đổi trong lịch sử.")}</Typography.Paragraph>
                {invoice.hasPendingPayment && <Alert type="warning" showIcon message={t("Cần đối soát trước khi điều chỉnh")} description={adjustmentBlockedReason} action={<Button size="small" loading={reconcileMutation.isPending} onClick={() => reconcileMutation.mutate(invoice.id)}>{t("Đối soát payOS")}</Button>} />}
                {!invoice.hasPendingPayment && adjustmentBlockedReason && <Alert type="info" showIcon message={t("Không thể điều chỉnh hóa đơn này")} description={adjustmentBlockedReason} />}
                {!adjustmentBlockedReason && <Form form={correctionForm} layout="vertical" requiredMark={false} onFinish={confirmCorrection}>
            {invoice.kind === 'Monthly' ? <>
              <Row gutter={12}><Col xs={24} sm={12}><Form.Item label={t("Tiền thuê phòng")} name="rent" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Tiền điện")} name="electricityAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Tiền nước")} name="waterAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Phí dịch vụ")} name="serviceAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col></Row>
              {invoice.canCorrectMeter ? <><Form.Item label={t("Chỉ số điện mới")} name="currentReading" rules={[{ required: true, type: 'number', min: invoice.previousReading }]}><InputNumber min={invoice.previousReading} precision={0} step={1} addonAfter="kWh" style={{ width: '100%' }} /></Form.Item>{invoice.waterByMeter && <Form.Item label={t("Chỉ số nước mới")} name="currentWaterReading" rules={[{ required: true, type: 'number', min: invoice.previousWaterReading ?? 0 }]}><InputNumber min={invoice.previousWaterReading ?? 0} precision={0} step={1} addonAfter="m³" style={{ width: '100%' }} /></Form.Item>}</> : <Alert className="meter-correction-policy" type="info" showIcon message={t("Chỉ số chỉ sửa được ở hóa đơn làm mốc mới nhất.")} description={invoice.meterCorrectionBlockedReason || t("Máy chủ chỉ cho điều chỉnh chỉ số ở hóa đơn mới nhất của phòng. Các khoản tiền vẫn có thể điều chỉnh độc lập.")} />}
              <Form.Item label={t("Nội dung hóa đơn")} name="description"><Input maxLength={500} /></Form.Item>
            </> : invoice.kind === 'Deposit' ? <>
              <Form.Item label={t("Số tiền hóa đơn")} name="amount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
              <Form.Item label={t("Nội dung hóa đơn")} name="description"><Input.TextArea maxLength={500} rows={2} /></Form.Item>
            </> : invoice.isFinalMeterReading ? <>
              {hasMonthlyOverlap ? <Alert type="info" showIcon message={t("Kỳ này đã có hóa đơn tháng")} description={t("Tiền thuê và nước được giữ trong hóa đơn tháng. Chỉ sửa điện cuối kỳ và khoản phát sinh bên dưới.")} /> : <Row gutter={12}><Col xs={24} sm={12}><Form.Item label={t("Tiền thuê cuối kỳ")} name="rent" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Tiền nước cuối kỳ")} name="waterAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col></Row>}
              <Row gutter={12}><Col xs={24} sm={12}><Form.Item label={t("Tiền điện cuối kỳ")} name="electricityAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col><Col xs={24} sm={12}><Form.Item label={t("Phí phát sinh cuối kỳ")} name="serviceAmount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item></Col></Row>
              {invoice.canCorrectMeter ? <><Form.Item label={t("Chỉ số điện mới")} name="currentReading" rules={[{ required: true, type: 'number', min: invoice.previousReading }]}><InputNumber min={invoice.previousReading} precision={0} step={1} addonAfter="kWh" style={{ width: '100%' }} /></Form.Item>{invoice.waterByMeter && <Form.Item label={t("Chỉ số nước mới")} name="currentWaterReading" rules={[{ required: true, type: 'number', min: invoice.previousWaterReading ?? 0 }]}><InputNumber min={invoice.previousWaterReading ?? 0} precision={0} step={1} addonAfter="m³" style={{ width: '100%' }} /></Form.Item>}</> : <Alert className="meter-correction-policy" type="info" showIcon message={t("Chỉ số chỉ sửa được ở hóa đơn làm mốc mới nhất.")} description={invoice.meterCorrectionBlockedReason || t("Máy chủ chỉ cho điều chỉnh chỉ số ở hóa đơn mới nhất của phòng. Các khoản tiền vẫn có thể điều chỉnh độc lập.")} />}
              <Form.Item label={t("Nội dung hóa đơn")} name="description"><Input.TextArea maxLength={500} rows={2} /></Form.Item>
            </> : <>
              <Form.Item label={t("Số tiền hóa đơn")} name="amount" rules={[{ required: true, type: 'number', min: 0 }]}><MoneyInput min={0} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} /></Form.Item>
              <Form.Item label={t("Nội dung hóa đơn")} name="description"><Input.TextArea maxLength={500} rows={2} /></Form.Item>
            </>}
            <Form.Item label={t("Hạn thanh toán")} name="dueDate"><DatePicker format={dateFormat()} style={{ width: '100%' }} /></Form.Item>
            <Form.Item label={t("Lý do điều chỉnh")} name="reason" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập lý do điều chỉnh.") }]}><Input.TextArea rows={2} maxLength={500} placeholder={t("Ghi rõ vì sao cần điều chỉnh hóa đơn")}/></Form.Item>
                  <Button type="primary" block htmlType="submit" loading={correctionMutation.isPending}>{t("Lưu điều chỉnh")}</Button>
                </Form>}
              </>,
            }]}
          />
          <div className="invoice-correction-history"><Typography.Title level={5}>{t("Lịch sử điều chỉnh")}</Typography.Title>{correctionQuery.isError ? <LoadError error={correctionQuery.error} onRetry={() => void correctionQuery.refetch()} /> : correctionQuery.isPending ? <div className="loading-placeholder" /> : (correctionQuery.data ?? []).length === 0 ? <Typography.Text type="secondary">{t("Chưa có lần điều chỉnh nào.")}</Typography.Text> : (correctionQuery.data as InvoiceCorrection[]).map((entry) => <CorrectionHistoryItem key={entry.id} entry={entry} kind={invoice.kind} />)}</div>
        </section>}
        {invoice.kind === 'Deposit' && <DepositLedger invoice={invoice} />}
      </>}
    </Drawer>
  );
}

function correctionSnapshot(entry: InvoiceCorrection, side: 'before' | 'after') {
  const parsed = side === 'before' ? entry.before : entry.after;
  const raw = side === 'before' ? entry.beforeJson : entry.afterJson;
  if (parsed && typeof parsed === 'object') return parsed;
  if (raw) { try { return JSON.parse(raw) as Record<string, unknown>; } catch { return {}; } }
  return {};
}

function CorrectionHistoryItem({ entry, kind }: { entry: InvoiceCorrection; kind: Invoice['kind'] }) {
  const before = correctionSnapshot(entry, 'before');
  const after = correctionSnapshot(entry, 'after');
  const fields: Array<[string, string]> = kind === 'Monthly' ? [
    [t("Tiền thuê phòng"), 'rent'], [t("Tiền điện"), 'electricityAmount'], [t("Tiền nước"), 'waterAmount'], [t("Phí dịch vụ"), 'serviceAmount'], [t("Chỉ số điện"), 'currentReading'], [t("Hạn thanh toán"), 'dueDate'], [t("Nội dung hóa đơn"), 'description'], [t("Tổng tiền"), 'total'],
  ] : kind === 'Deposit' ? [[t("Số tiền hóa đơn"), 'total'], [t("Hạn thanh toán"), 'dueDate'], [t("Nội dung hóa đơn"), 'description']] : [
    [t("Tiền thuê phòng"), 'rent'], [t("Tiền điện"), 'electricityAmount'], [t("Tiền nước"), 'waterAmount'], [t("Phí dịch vụ"), 'serviceAmount'], [t("Chỉ số điện"), 'currentReading'], [t("Hạn thanh toán"), 'dueDate'], [t("Nội dung hóa đơn"), 'description'], [t("Tổng tiền"), 'total'],
  ];
  const createdAt = entry.createdAt || entry.recordedAt;
  const value = (key: string, raw: unknown) => {
    if (raw === null || raw === undefined || raw === '') return '—';
    if (key === 'description') return String(raw);
    if (key === 'dueDate') return formatDate(String(raw));
    if (key === 'currentReading') return `${raw} kWh`;
    if (key === 'currentWaterReading') return `${raw} m³`;
    if (typeof raw === 'number') return formatVnd(raw);
    return String(raw);
  };
  return <article className="correction-history-item">
    <div className="correction-history-meta"><strong>{entry.actorName || entry.actorEmail || t("Chủ trọ")}</strong><time dateTime={createdAt}>{createdAt && dayjs(createdAt).format(`${dateFormat()} HH:mm`)}</time></div>
    <p>{entry.reason}</p>
    <div className="correction-history-values">{fields.map(([label, key]) => <div key={key}><span>{label}</span><span>{value(key, before[key])} → {value(key, after[key])}</span></div>)}</div>
  </article>;
}

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Divider, Drawer, Form, Tag, Typography } from 'antd';
import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { formatVnd, periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import { LoadError } from '../../shared/ui/Ui';
import { type Invoice } from '../invoices/index';
import { roomsApi, type MoveoutSummary, type Room } from './index';

interface RefundValues { refundAmount: number; }

function invoiceTitle(invoice: Invoice) {
  if (invoice.kind === 'Monthly') return periodLabel(invoice.period);
  if (invoice.kind === 'Deposit') return t("Tiền cọc nhận phòng");
  return invoice.description?.trim() || t("Hóa đơn bổ sung");
}

function fallbackBlockMessage(data: MoveoutSummary, hasDebt: boolean, hasPending: boolean, hasDraft: boolean) {
  if (hasPending) return t("Có giao dịch thanh toán đang chờ. Hãy mở hóa đơn để đối soát rồi tải lại quyết toán.");
  if (hasDebt) return t("Còn hóa đơn chưa thanh toán. Hãy mở từng hóa đơn để xử lý công nợ rồi tải lại quyết toán.");
  if (hasDraft) return t("Còn bản nháp chưa phát hành. Hãy xử lý bản nháp trên trang hóa đơn rồi tải lại quyết toán.");
  return data.blockedReason || t("Máy chủ chưa cho phép kết thúc lượt thuê. Tải lại quyết toán để kiểm tra trạng thái mới nhất.");
}

export default function MoveoutDrawer({ room, onClose }: { room: Room; onClose: () => void }) {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const client = useQueryClient();
  const query = useQuery({
    queryKey: ['moveout', room.id],
    queryFn: async () => {
      const summary = await roomsApi.moveout(room.id);
      if (summary.occupancyId !== room.occupancy?.id) throw new Error(t("Lượt thuê đã thay đổi. Đóng màn quyết toán và mở lại từ danh sách phòng."));
      return summary;
    },
  });
  const [refundForm] = Form.useForm<RefundValues>();
  const refundValue = Form.useWatch('refundAmount', refundForm);
  const initializedOccupancy = useRef<string | null>(null);
  const completionRequests = useRef(new Map<string, string>());
  const data = query.data;
  const depositInvoice = data?.invoices.find((invoice) => invoice.kind === 'Deposit');
  const held = data?.deposit?.held ?? data?.heldDeposit ?? depositInvoice?.depositHeld ?? 0;
  const depositRequired = data?.deposit?.required ?? depositInvoice?.total ?? 0;
  const depositPaid = data?.deposit?.paid ?? depositInvoice?.paid ?? 0;
  const depositSettled = data?.deposit?.settled ?? Math.max(0, depositPaid - held);
  const depositOutstanding = data?.deposit?.outstanding ?? depositInvoice?.outstanding ?? 0;
  const depositVersion = data?.deposit?.version ?? depositInvoice?.version ?? null;
  const pendingIds = data?.pendingInvoiceIds ?? [];
  const pendingSet = new Set(pendingIds);
  const drafts = data?.invoices.filter((invoice) => invoice.lifecycleState === 'Draft') ?? [];
  const outstandingInvoices = data?.invoices.filter((invoice) => invoice.lifecycleState !== 'Draft' && invoice.outstanding > 0) ?? [];
  const actionableInvoices = data?.invoices.filter((invoice) => invoice.lifecycleState === 'Draft' || invoice.outstanding > 0 || pendingSet.has(invoice.id)) ?? [];
  const totalOutstanding = outstandingInvoices.reduce((total, invoice) => total + invoice.outstanding, 0);
  const hasDebt = totalOutstanding > 0 || depositOutstanding > 0;
  const hasPending = pendingIds.length > 0;
  const hasDraft = drafts.length > 0;
  const canComplete = !!data && data.canComplete && !hasDebt && !hasPending && !hasDraft;
  const refundAmount = Number.isFinite(refundValue) ? Number(refundValue) : held;
  const refundIsValid = Number.isInteger(refundAmount) && refundAmount >= 0 && refundAmount <= held;
  const retainedAmount = refundIsValid ? held - refundAmount : null;

  useEffect(() => {
    if (!data || initializedOccupancy.current === data.occupancyId) return;
    initializedOccupancy.current = data.occupancyId;
    refundForm.setFieldsValue({ refundAmount: data.deposit?.held ?? data.heldDeposit ?? data.invoices.find((invoice) => invoice.kind === 'Deposit')?.depositHeld ?? 0 });
  }, [data, refundForm]);

  const refresh = async () => Promise.all([
    client.invalidateQueries({ queryKey: ['moveout', room.id] }),
    client.invalidateQueries({ queryKey: ['rooms'] }),
    client.invalidateQueries({ queryKey: ['invoices'] }),
    client.invalidateQueries({ queryKey: ['invoice'] }),
    client.invalidateQueries({ queryKey: ['dashboard'] }),
    client.invalidateQueries({ queryKey: ['reports'] }),
  ]);
  const completeMutation = useMutation({
    mutationFn: (body: { occupancyId: string; requestId: string; refundAmount: number; expectedHeld: number; expectedDepositVersion: number | null }) => roomsApi.completeMoveout(room.id, body),
    onSuccess: async (result) => {
      completionRequests.current.clear();
      await refresh();
      message.success(t("Đã ghi nhận hoàn {0} và giữ lại {1}; lượt thuê đã kết thúc.", [formatVnd(result.refundAmount), formatVnd(result.retainedAmount)]));
      onClose();
    },
    onError: (error: Error) => {
      message.error(error.message);
      void query.refetch();
    },
  });

  const openInvoice = (invoiceId?: string) => {
    navigate(invoiceId ? '/invoices?invoice=' + encodeURIComponent(invoiceId) : '/invoices');
  };

  const confirmCompletion = (values: RefundValues) => {
    if (!data || !canComplete) return;
    const amount = values.refundAmount;
    if (!Number.isInteger(amount) || amount < 0 || amount > held) {
      void refundForm.validateFields(['refundAmount']);
      return;
    }
    const requestKey = JSON.stringify({ occupancyId: data.occupancyId, refundAmount: amount, expectedHeld: held, expectedDepositVersion: depositVersion });
    let requestId = completionRequests.current.get(requestKey);
    if (!requestId) {
      requestId = crypto.randomUUID();
      completionRequests.current.set(requestKey, requestId);
    }
    const body = { occupancyId: data.occupancyId, requestId, refundAmount: amount, expectedHeld: held, expectedDepositVersion: depositVersion };
    const retained = held - amount;
    modal.confirm({
      title: t("Xác nhận trả phòng và quyết toán cọc?"),
      content: <div className="moveout-confirm-copy">
        <Typography.Paragraph>{t("Thao tác sẽ ghi nhận bạn đã hoàn {0} cho khách; khoản tiền không được tự chuyển qua ngân hàng.", [formatVnd(amount)])}</Typography.Paragraph>
        <Typography.Paragraph>{t("Phần cọc giữ lại: {0}. Con số này sẽ được lưu thành khoản giữ lại có ghi nhận trong lịch sử.", [formatVnd(retained)])}</Typography.Paragraph>
        <Typography.Paragraph type="secondary">{t("Ngày kết thúc lượt thuê được máy chủ ghi nhận hôm nay. Các hóa đơn đã phát hành được giữ nguyên.")}</Typography.Paragraph>
      </div>,
      okText: t("Ghi nhận đã hoàn & trả phòng"),
      cancelText: t("Kiểm tra lại"),
      onOk: () => completeMutation.mutateAsync(body),
    });
  };

  const showFallbackBlock = !!data && !data.canComplete && !hasDebt && !hasPending && !hasDraft;

  return <Drawer className="moveout-drawer" title={t("Trả phòng · {0}", [room.name])} width={680} open onClose={() => { if (!completeMutation.isPending) onClose(); }} closable={!completeMutation.isPending} maskClosable={!completeMutation.isPending}>
    {query.isError ? <LoadError error={query.error} onRetry={() => void query.refetch()} /> : !data ? <Typography.Paragraph>{t("Đang tải quyết toán…")}</Typography.Paragraph> : <>
      <Typography.Paragraph className="moveout-tenant-line"><strong>{data.tenantName}</strong> · {data.roomName}</Typography.Paragraph>
      <Typography.Paragraph type="secondary">{t("Tiền thuê tháng được tính đủ từ ngày bắt đầu thuê. Ngày kết thúc được máy chủ ghi nhận khi bạn xác nhận trả phòng; hóa đơn đã phát hành được giữ nguyên.")}</Typography.Paragraph>

      <div className="moveout-summary-grid">
        <div><span>{t("Công nợ cần xử lý")}</span><strong>{formatVnd(totalOutstanding)}</strong><small>{t("{0} hóa đơn chưa thanh toán", [outstandingInvoices.length])}</small></div>
        <div><span>{t("Tiền cọc đang giữ")}</span><strong>{formatVnd(held)}</strong><small>{t("Đã nhận {0} · đã quyết toán {1}", [formatVnd(depositPaid), formatVnd(depositSettled)])}</small></div>
        <div><span>{t("Tiền cọc yêu cầu")}</span><strong>{formatVnd(depositRequired)}</strong><small>{t("Còn phải thu {0}", [formatVnd(depositOutstanding)])}</small></div>
      </div>

      {hasPending && <Alert className="moveout-block-alert" type="warning" showIcon message={t("Có giao dịch thanh toán đang chờ")} description={t("Đối soát giao dịch trong hóa đơn tương ứng trước khi kết thúc lượt thuê. Mở hóa đơn bên dưới để kiểm tra.")} />}
      {hasDebt && <Alert className="moveout-block-alert" type="warning" showIcon message={t("Còn hóa đơn chưa thanh toán")} description={t("Hãy xử lý công nợ trong hóa đơn tương ứng trước khi kết thúc lượt thuê. Hệ thống không tự khấu trừ tiền cọc vào công nợ ở màn này.")} />}
      {hasDraft && <Alert className="moveout-block-alert" type="warning" showIcon message={t("Còn bản nháp chưa phát hành")} description={t("Xử lý bản nháp trên trang hóa đơn rồi tải lại quyết toán trước khi kết thúc lượt thuê.")} />}
      {showFallbackBlock && <Alert className="moveout-block-alert" type="warning" showIcon message={t("Chưa thể kết thúc lượt thuê")} description={data.blockedReason || fallbackBlockMessage(data, hasDebt, hasPending, hasDraft)} />}

      <Divider orientation="left">{t("Hóa đơn và giao dịch cần xử lý")}</Divider>
      {actionableInvoices.length === 0 ? <Alert type="success" showIcon message={t("Không còn hóa đơn hoặc giao dịch cần xử lý")} description={t("Có thể tiếp tục quyết toán tiền cọc và kết thúc lượt thuê.")} /> : <div className="moveout-invoice-list">{actionableInvoices.map((invoice) => {
        const isPending = pendingSet.has(invoice.id);
        const isDraft = invoice.lifecycleState === 'Draft';
        return <article className="moveout-invoice-row" key={invoice.id}>
          <div className="moveout-invoice-copy">
            <div className="moveout-invoice-title"><strong>{invoiceTitle(invoice)}</strong><Tag className={'invoice-kind-tag invoice-kind-' + invoice.kind.toLowerCase()}>{invoice.kind === 'Monthly' ? t("Hóa đơn tháng") : invoice.kind === 'Deposit' ? t("Tiền cọc") : t("Bổ sung")}</Tag></div>
            <span>{isDraft ? t("Bản nháp chưa phát hành") : isPending ? t("Đang chờ đối soát") : t("Còn phải thu {0}", [formatVnd(invoice.outstanding)])}</span>
          </div>
          <Button size="small" onClick={() => openInvoice(invoice.id)}>{isPending ? t("Mở để đối soát") : isDraft ? t("Mở bản nháp") : t("Mở hóa đơn")}</Button>
        </article>;
      })}</div>}

      <Divider orientation="left">{t("Quyết toán tiền cọc")}</Divider>
      {held > 0 ? <Form form={refundForm} layout="vertical" requiredMark={false} onFinish={confirmCompletion}>
        <Form.Item label={t("Số tiền ghi nhận đã hoàn cho khách")} name="refundAmount" rules={[
          { required: true, type: 'number', message: t("Nhập số tiền hoàn cọc.") },
          { validator: (_, value: number | undefined) => Number.isInteger(value) && Number(value) >= 0 && Number(value) <= held ? Promise.resolve() : Promise.reject(new Error(t("Số tiền hoàn phải là số nguyên từ 0 đến {0}.", [formatVnd(held)]))) },
        ]} extra={t("Chỉ ghi nhận khoản đã hoàn ngoài hệ thống; thao tác này không chuyển tiền qua ngân hàng.")}>
          <MoneyInput min={0} max={held} precision={0} step={1} addonAfter={t("đ")} style={{ width: '100%' }} />
        </Form.Item>
        <div className="moveout-refund-preview">
          <span>{t("Ghi nhận hoàn")}</span><strong>{refundIsValid ? formatVnd(refundAmount) : '—'}</strong>
          <span>{t("Giữ lại từ tiền cọc")}</span><strong>{retainedAmount === null ? '—' : formatVnd(retainedAmount)}</strong>
        </div>
        <Button type="primary" block htmlType="submit" disabled={!canComplete || !refundIsValid} loading={completeMutation.isPending}>{t("Xác nhận trả phòng")}</Button>
      </Form> : <>
        <Alert type="info" showIcon message={t("Không có tiền cọc đang giữ")} description={t("Khi xác nhận, hệ thống sẽ kết thúc lượt thuê mà không ghi nhận khoản hoàn hoặc giữ lại.")} />
        <Button className="moveout-complete-button" type="primary" block disabled={!canComplete} loading={completeMutation.isPending} onClick={() => confirmCompletion({ refundAmount: 0 })}>{t("Xác nhận trả phòng")}</Button>
      </>}
      <Button className="moveout-refresh-button" block disabled={completeMutation.isPending} loading={query.isFetching} onClick={() => void query.refetch()}>{t("Tải lại quyết toán")}</Button>
    </>}
  </Drawer>;
}

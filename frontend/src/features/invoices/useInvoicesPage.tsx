import type { InvoiceKindFilter, InvoiceStatusFilter, IssuanceFilter } from './types';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Form } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { formatVnd } from '../../shared/format';
import { getLocale, t } from '../../shared/i18n/runtime';
import { invoicesApi, type Invoice } from './index';

export function useInvoicesPage() {
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const [kindFilter, setKindFilter] = useState<InvoiceKindFilter>('All');
  const [issuanceFilter, setIssuanceFilter] = useState<IssuanceFilter>('All');
  const [period, setPeriod] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<InvoiceStatusFilter>('All');
  const [searchText, setSearchText] = useState('');
  const [adjustmentExpanded, setAdjustmentExpanded] = useState(false);
  const selectedId = searchParams.get('invoice');
  const [receiptForm] = Form.useForm<{ note?: string }>();
  const [correctionForm] = Form.useForm<{ amount?: number; rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; currentReading?: number; reason: string; dueDate?: Dayjs; description?: string }>();
  const receiptKeys = useRef(new Map<string, string>());
  const correctionKeys = useRef(new Map<string, string>());
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [kindFilter, issuanceFilter, period, statusFilter, searchText]);
  const listQuery = useQuery({ queryKey: ['invoices', page, kindFilter, issuanceFilter, period, statusFilter, searchText], queryFn: () => invoicesApi.page({page: String(page), pageSize:'10',
    ...(kindFilter === 'All' ? {} : {kind: kindFilter}), ...(issuanceFilter === 'All' ? {} : {lifecycleState: issuanceFilter}),
    ...(period ? {period} : {}), ...(statusFilter === 'All' ? {} : {status:statusFilter}), ...(searchText.trim() ? {search:searchText.trim()} : {})}) });
  const detailQuery = useQuery({ queryKey: ['invoice', selectedId], queryFn: () => invoicesApi.get(selectedId!), enabled: !!selectedId });
  const invoices = listQuery.data?.items ?? [];
  const hasActiveFilters = kindFilter !== 'All' || issuanceFilter !== 'All' || period !== null || statusFilter !== 'All' || !!searchText.trim();
  const draftCount = listQuery.data?.draftCount ?? 0;
  const issuedCount = (listQuery.data?.total ?? 0) - draftCount;
  const totals = listQuery.data?.totals ?? {billed:0, paid:0, outstanding:0};
  const generateMutation = useMutation({
    mutationFn: (selectedPeriod: string) => invoicesApi.generate(selectedPeriod),
    onSuccess: async () => { await Promise.all([client.invalidateQueries({ queryKey: ['invoices'] }), client.invalidateQueries({ queryKey: ['dashboard'] }), client.invalidateQueries({ queryKey: ['rooms'] })]); message.success(t("Đã kiểm tra và tạo các hóa đơn còn thiếu.")); },
    onError: (e: Error) => message.error(e.message),
  });
  const reconcileMutation = useMutation({
    mutationFn: (id: string) => invoicesApi.reconcile(id),
    onSuccess: async (result, id) => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ['invoice', id] }),
        client.invalidateQueries({ queryKey: ['invoices'] }),
        client.invalidateQueries({ queryKey: ['dashboard'] }), client.invalidateQueries({ queryKey: ['rooms'] }),
      ]);
      const status = result.status.toLowerCase();
      if (status === 'paid') message.success(t("payOS xác nhận đã thanh toán. Hóa đơn đã được cập nhật."));
      else if (status === 'nopayment') message.info(t("Chưa có giao dịch payOS cần đối soát cho hóa đơn này."));
      else if (status === 'cancelled' || status === 'expired') message.warning(t("payOS báo giao dịch đã kết thúc và chưa thanh toán."));
      else message.info(t("payOS chưa xác nhận khoản thanh toán. Hãy kiểm tra lại sau."));
    },
    onError: (e: Error) => message.error(e.message),
  });
  const refreshPayments = (id: string) => Promise.all([
    client.invalidateQueries({ queryKey: ['invoice', id] }), client.invalidateQueries({ queryKey: ['invoices'] }),
    client.invalidateQueries({ queryKey: ['invoiceCorrections', id] }), client.invalidateQueries({ queryKey: ['dashboard'] }),
    client.invalidateQueries({ queryKey: ['rooms'] }), client.invalidateQueries({ queryKey: ['reports'] }),
  ]);
  const receiptMutation = useMutation({
    mutationFn: ({ id, values, idempotencyKey, expectedVersion }: { id: string; values: { amount: number; note?: string }; idempotencyKey: string; expectedVersion?: number }) => invoicesApi.addReceipt(id, { amount: values.amount, note: values.note?.trim() || '', idempotencyKey, expectedVersion }),
    onSuccess: async (_, variables) => { await refreshPayments(variables.id); receiptKeys.current.delete(`${variables.id}:${JSON.stringify(variables.values)}`); receiptForm.resetFields(); message.success(t("Đã ghi nhận khoản thu.")); },
    onError: async (e: Error, variables) => { await refreshPayments(variables.id); message.error(e.message); },
  });
  const invoice = detailQuery.data;
  useEffect(() => { setAdjustmentExpanded(false); }, [selectedId]);
  const correctionQuery = useQuery({ queryKey: ['invoiceCorrections', selectedId], queryFn: () => invoicesApi.corrections(selectedId!), enabled: !!selectedId && invoice?.lifecycleState !== 'Draft' });
  const correctionMutation = useMutation({
    mutationFn: ({ id, values, requestId, version }: { id: string; values: { amount?: number; rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; currentReading?: number; reason: string; dueDate?: string; description?: string }; requestId: string; version: number }) => invoicesApi.correct(id, { ...values, expectedVersion: version, requestId }),
    onSuccess: async (_, variables) => { await Promise.all([client.invalidateQueries({ queryKey: ['invoice', variables.id] }), client.invalidateQueries({ queryKey: ['invoices'] }), client.invalidateQueries({ queryKey: ['invoiceCorrections', variables.id] }), client.invalidateQueries({ queryKey: ['dashboard'] })]); correctionKeys.current.delete(variables.id); correctionForm.resetFields(); message.success(t("Đã lưu điều chỉnh hóa đơn.")); },
    onError: (error: Error) => message.error(error.message),
  });
  useEffect(() => { if (invoice) correctionForm.setFieldsValue({ amount: invoice.total, rent: invoice.rent, electricityAmount: invoice.electricityAmount, waterAmount: invoice.waterAmount, serviceAmount: invoice.serviceAmount, currentReading: invoice.currentReading, dueDate: invoice.dueDate ? dayjs(invoice.dueDate) : undefined, description: invoice.description, reason: '' }); }, [invoice, correctionForm]);
  const showInvoice = (id: string) => setSearchParams({ invoice: id });
  const confirmReceipt = (values: { note?: string }) => {
    if (!invoice) return;
    const normalized = { amount: invoice.outstanding, note: values.note?.trim() || '' };
    const snapshot = `${invoice.id}:${JSON.stringify(normalized)}`;
    let idempotencyKey = receiptKeys.current.get(snapshot);
    if (!idempotencyKey) { idempotencyKey = crypto.randomUUID(); receiptKeys.current.set(snapshot, idempotencyKey); }
    modal.confirm({
      title: t("Ghi nhận khoản thu này?"),
      content: <>
        <p>{t("Xác nhận này thanh toán toàn bộ số tiền còn lại của các hóa đơn đã chọn.")}</p>
        <p>{t("Bạn sắp ghi nhận {0}{1}. Khoản thu này sẽ được lưu vào sổ hóa đơn.", [formatVnd(normalized.amount), normalized.note ? ` (${normalized.note})` : ''])}</p>
        {invoice.hasPendingPayment && <p>{t("Hệ thống sẽ kiểm tra và hủy link payOS chưa thanh toán trước khi ghi nhận khoản thu này. Nếu payOS đã nhận tiền, hóa đơn sẽ được đối soát và không ghi thu thêm.")}</p>}
      </>,
      okText: t("Ghi nhận khoản thu"),
      cancelText: t("Kiểm tra lại"),
      onOk: () => receiptMutation.mutateAsync({ id: invoice.id, values: normalized, idempotencyKey, expectedVersion: invoice.version }),
    });
  };
  const closeInvoice = () => setSearchParams({});
  const confirmCorrection = (values: { amount?: number; rent?: number; electricityAmount?: number; waterAmount?: number; serviceAmount?: number; currentReading?: number; reason: string; dueDate?: Dayjs; description?: string }) => {
    if (!invoice) return;
    let requestId = correctionKeys.current.get(invoice.id);
    if (!requestId) { requestId = crypto.randomUUID(); correctionKeys.current.set(invoice.id, requestId); }
    const common = { reason: values.reason.trim(), dueDate: values.dueDate?.format('YYYY-MM-DD'), description: values.description?.trim() };
    const monthlyOverlap = !!invoice.hasMonthlyOverlap;
    const body = invoice.kind === 'Monthly'
      ? { ...common, rent: values.rent, electricityAmount: values.electricityAmount, waterAmount: values.waterAmount, serviceAmount: values.serviceAmount, currentReading: invoice.canCorrectMeter ? values.currentReading : undefined }
      : invoice.kind === 'Deposit'
        ? { ...common, amount: values.amount }
        : invoice.isFinalMeterReading
          ? { ...common, ...(monthlyOverlap ? {} : { rent: values.rent, waterAmount: values.waterAmount }), electricityAmount: values.electricityAmount, serviceAmount: values.serviceAmount, currentReading: invoice.canCorrectMeter ? values.currentReading : undefined }
          : { ...common, amount: values.amount };
    modal.confirm({ title: t("Lưu điều chỉnh hóa đơn?"), content: t("Mức phí mới và lý do sẽ được ghi vào lịch sử hóa đơn."), okText: t("Lưu điều chỉnh"), cancelText: t("Kiểm tra lại"), onOk: () => correctionMutation.mutateAsync({ id: invoice.id, values: body, requestId: requestId!, version: invoice.version ?? 0 }) });
  };
  const adjustmentBlockedReason = invoice?.hasPendingPayment ? t("Hóa đơn có giao dịch payOS đang chờ. Hãy đối soát giao dịch trước khi điều chỉnh.") : invoice?.status === 'Paid' || invoice?.status === 'Partial' || (invoice?.paid ?? 0) > 0 ? t("Chỉ được điều chỉnh hóa đơn chưa thanh toán. Hóa đơn này đã ghi nhận tiền.") : null;
  const hasMonthlyOverlap = !!invoice && !!invoice.hasMonthlyOverlap;
  const getCheckoutUrl = (item: Invoice) => {
    if (!item.tenantToken) return '';
    return `${window.location.origin}/t/${encodeURIComponent(item.tenantToken)}/invoices/${encodeURIComponent(item.id)}/checkout`;
  };

  const formatReminderMessage = (item: Invoice) => {
    const url = getCheckoutUrl(item);
    let periodText = item.period;
    if (item.kind === 'Deposit') {
      periodText = t("Tiền cọc nhận phòng");
    } else if (item.kind === 'Supplemental') {
      periodText = t("Hóa đơn bổ sung");
    } else {
      const [year, month] = item.period.split('-');
      periodText = month && year ? `${month}/${year}` : item.period;
    }
    const amountText = `${new Intl.NumberFormat(getLocale()).format(item.total)} đ`;
    return [
      t("🔔 Thông báo tiền phòng {0} - Kỳ {1}", [item.roomName, periodText]),
      t("💰 Tổng cộng: {0}", [amountText]),
      t("👉 Xem chi tiết chỉ số điện nước và quét mã QR thanh toán tự động tại: {0}", [url]),
    ].join('\n');
  };

  const copyReminderMessage = async (item: Invoice) => {
    const text = formatReminderMessage(item);
    try {
      await navigator.clipboard.writeText(text);
      message.success(t("Đã sao chép tin nhắn thu tiền."));
    } catch {
      message.info(t("Không thể sao chép tự động."));
    }
  };

  return { message, kindFilter, setKindFilter, issuanceFilter, setIssuanceFilter, period, setPeriod, statusFilter, setStatusFilter, searchText, setSearchText, adjustmentExpanded, setAdjustmentExpanded, selectedId, receiptForm, correctionForm, page, setPage, listQuery, detailQuery, invoices, hasActiveFilters, draftCount, issuedCount, totals, generateMutation, reconcileMutation, receiptMutation, invoice, correctionQuery, correctionMutation, showInvoice, confirmReceipt, closeInvoice, confirmCorrection, adjustmentBlockedReason, hasMonthlyOverlap, copyReminderMessage };
}

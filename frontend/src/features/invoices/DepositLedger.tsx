import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { App, Button, Form, Input, Select, Typography } from 'antd';
import { useRef } from 'react';
import { formatVnd, periodLabel } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import MoneyInput from '../../shared/ui/MoneyInput';
import { invoicesApi, type Invoice } from './index';

interface Values { amount: number; kind: 'Refund' | 'Deduction'; reason: string; targetInvoiceId?: string; }

export default function DepositLedger({ invoice }: { invoice: Invoice }) {
  const { message, modal } = App.useApp();
  const client = useQueryClient();
  const [form] = Form.useForm<Values>();
  const kind = Form.useWatch('kind', form);
  const keys = useRef(new Map<string, string>());
  const targetsQuery = useQuery({ queryKey: ['invoices', 'deposit-targets', invoice.occupancyId], queryFn: () => invoicesApi.list(undefined, invoice.occupancyId) });
  const targets = (targetsQuery.data || []).filter(x => x.kind !== 'Deposit' && x.lifecycleState !== 'Draft' && x.occupancyId === invoice.occupancyId && x.outstanding > 0);
  const mutation = useMutation({
    mutationFn: (values: Values) => {
      const key = JSON.stringify(values);
      if (!keys.current.has(key)) keys.current.set(key, crypto.randomUUID());
      return invoicesApi.settleDeposit(invoice.id, { ...values, idempotencyKey: keys.current.get(key)!, expectedVersion: invoice.version, targetExpectedVersion: values.targetInvoiceId ? targets.find(target => target.id === values.targetInvoiceId)?.version : undefined });
    },
    onSuccess: async () => {
      form.resetFields(); keys.current.clear();
      await Promise.all(['invoice', 'invoices', 'rooms', 'dashboard', 'moveout', 'manual-payments', 'reports'].map(key => client.invalidateQueries({ queryKey: [key] })));
      message.success(t("Đã ghi nhận quyết toán cọc."));
    },
    onError: (error: Error) => message.error(error.message),
  });
  const submit = (values: Values) => {
    const payload = { ...values, reason: values.reason.trim(), targetInvoiceId: values.kind === 'Deduction' ? values.targetInvoiceId : undefined };
    modal.confirm({ title: values.kind === 'Refund' ? t("Ghi nhận đã hoàn cọc?") : t("Xác nhận khấu trừ cọc?"),
      content: `${formatVnd(values.amount)} · ${payload.reason}${values.kind === 'Refund' ? t(". Chỉ xác nhận sau khi đã trả tiền cho khách.") : ''}`,
      okText: t("Ghi nhận"), cancelText: t("Hủy"), autoFocusButton: 'cancel', onOk: () => mutation.mutateAsync(payload) });
  };
  return <div className="receipt-form-block">
    <Typography.Title level={5}>{t("Quyết toán tiền cọc")}</Typography.Title>
    <Typography.Paragraph>{t("Cọc đang giữ:")} <strong>{formatVnd(invoice.depositHeld)}</strong></Typography.Paragraph>
    {invoice.depositSettlements.map(entry => <Typography.Paragraph key={entry.id}>
      {entry.kind === 'Refund' ? t("Đã hoàn") : t("Khấu trừ")} {formatVnd(entry.amount)} · {entry.reason}
    </Typography.Paragraph>)}
    {invoice.depositHeld > 0 && <Form name="depositSettlement" form={form} layout="vertical" initialValues={{ kind: 'Refund' }} onFinish={submit}>
      <Form.Item label={t("Cách quyết toán")} name="kind" rules={[{ required: true }]}><Select options={[
        { value: 'Refund', label: t("Ghi nhận đã hoàn cọc cho khách") }, { value: 'Deduction', label: t("Khấu trừ cọc") }]} /></Form.Item>
      <Form.Item label={t("Số tiền")} name="amount" rules={[{ required: true, type: 'number', min: 1, max: invoice.depositHeld, message: t("Vui lòng nhập số tiền.") }]}>
        <MoneyInput min={1} max={invoice.depositHeld} precision={0} addonAfter={t("đ")} style={{ width: '100%' }} />
      </Form.Item>
      {kind === 'Deduction' && <Form.Item label={t("Trừ vào hóa đơn còn nợ (nếu có)")} name="targetInvoiceId" extra={t("Để trống nếu khấu trừ cho hư hỏng hoặc chi phí khác.")}>
        <Select allowClear loading={targetsQuery.isPending} options={targets.map(x => ({ value: x.id, label: t("{0} · Còn nợ {1}", [x.kind === 'Supplemental' ? t("Bổ sung") : periodLabel(x.period), formatVnd(x.outstanding)]) }))} />
      </Form.Item>}
      <Form.Item label={t("Lý do / thông tin hoàn cọc")} name="reason" rules={[{ required: true, whitespace: true, max: 500, message: t("Vui lòng nhập lý do.") }]}>
        <Input.TextArea rows={2} maxLength={500} />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={mutation.isPending}>{t("Ghi nhận quyết toán cọc")}</Button>
    </Form>}
  </div>;
}

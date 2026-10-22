import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Form, Input, Radio, Select, Typography } from 'antd';
import { useEffect } from 'react';
import type { FormInstance } from 'antd';
import { t } from '../../shared/i18n/runtime';
import { LoadError } from '../../shared/ui/Ui';
import { paymentSettingsApi } from './api';
import { VIETQR_BANKS } from './banks';
import type { PaymentPreferences, PaymentSettings } from './types';

export function PaymentModeSettings({ settings, form }: { settings: PaymentSettings; form: FormInstance<PaymentPreferences> }) {
  const client = useQueryClient(); const { message } = App.useApp();
  const mode = Form.useWatch('confirmationMode', form);
  const banks = useQuery({ queryKey: ['payment-banks'], queryFn: paymentSettingsApi.banks, enabled: mode === 'Manual' });
  useEffect(() => { form.setFieldsValue({ confirmationMode: settings.confirmationMode ?? 'Automatic', ...settings.manualBank }); }, [form, settings]);
  const save = useMutation({ mutationFn: paymentSettingsApi.preferences, onSuccess: async next => {
    client.setQueryData(['payment-settings'], next);
    await Promise.all(['rooms', 'invoices', 'room'].map(key => client.invalidateQueries({ queryKey: [key] })));
    message.success(t("Đã lưu chế độ thanh toán."));
  }, onError: (error: Error) => message.error(error.message) });
  return <Card className="settings-card payment-mode-card" bordered={false}>
    <Typography.Title level={4}>{t("Xác nhận thanh toán")}</Typography.Title>
    <Form form={form} layout="vertical" requiredMark={false} initialValues={{ confirmationMode: settings.confirmationMode ?? 'Automatic' }} onFinish={values => save.mutate(values)} disabled={save.isPending}>
      <Form.Item name="confirmationMode" label={t("Chế độ thanh toán")}><Radio.Group className="payment-mode-options">
        <Radio value="Automatic">{t("Tự động xác nhận thanh toán")}</Radio>
        <Radio value="Manual">{t("Xác nhận thanh toán thủ công")}</Radio>
      </Radio.Group></Form.Item>
      <Alert showIcon type="info" message={mode === 'Manual' ? t("Người thuê chuyển khoản bằng VietQR. Bạn kiểm tra tiền thực nhận rồi xác nhận khoản thu.") : t("payOS xác minh giao dịch và tự cập nhật các hóa đơn đã thanh toán.")} />
      {mode === 'Manual' && <div className="payment-manual-fields">
        {banks.isError && <LoadError error={banks.error} onRetry={() => void banks.refetch()} />}
        <Form.Item name="bankBin" label={t("Ngân hàng nhận tiền")} rules={[{ required: true, message: t("Vui lòng chọn ngân hàng thụ hưởng.") }]}><Select showSearch loading={banks.isPending} optionFilterProp="label" options={(banks.data ?? VIETQR_BANKS).map(bank => ({ value: bank.bin, label: `${bank.shortName} — ${bank.name}` }))} /></Form.Item>
        <Form.Item name="accountNumber" label={t("Số tài khoản")} rules={[{ required: true, message: t("Vui lòng nhập số tài khoản.") }, { pattern: /^[A-Za-z0-9]{4,34}$/ }]}><Input autoComplete="off" maxLength={34} /></Form.Item>
        <Form.Item name="accountName" label={t("Tên chủ tài khoản")} rules={[{ required: true, whitespace: true, max: 100, message: t("Vui lòng nhập tên chủ tài khoản.") }]}><Input autoComplete="off" maxLength={100} /></Form.Item>
        <Typography.Paragraph type="secondary">{t("Kiểm tra đúng tài khoản nhận tiền trước khi lưu. Tạo QR không đồng nghĩa với xác nhận đã thanh toán.")}</Typography.Paragraph>
      </div>}
      <Button type="primary" htmlType="submit" loading={save.isPending} style={{ marginTop: 16 }}>{t("Lưu chế độ thanh toán")}</Button>
    </Form>
  </Card>;
}

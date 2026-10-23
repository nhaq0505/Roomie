import { BookOutlined, CheckCircleFilled, CopyOutlined, CreditCardOutlined, ExportOutlined, QuestionCircleOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Drawer, Form, Input, Skeleton, Space, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { errorText } from '../../shared/i18n/errors';
import { t } from '../../shared/i18n/runtime';
import { LoadError, PageTitle } from '../../shared/ui/Ui';
import { paymentSettingsApi, type PaymentSettings, type PaymentPreferences } from './index';

import { PaymentModeSettings } from './PaymentModeSettings';

interface PaymentForm { clientId: string; apiKey: string; checksumKey: string; }

export default function PaymentSettingsPage() {
  const { message } = App.useApp();
  const client = useQueryClient();
  const [guideOpen, setGuideOpen] = useState(false);
  const [form] = Form.useForm<PaymentForm>();
  const [modeForm] = Form.useForm<PaymentPreferences>();
  const selectedMode = Form.useWatch('confirmationMode', modeForm);
  const settingsQuery = useQuery({ queryKey: ['payment-settings'], queryFn: paymentSettingsApi.get });
  useEffect(() => { if (settingsQuery.data) form.setFieldsValue({ clientId: settingsQuery.data.clientId ?? '' }); }, [settingsQuery.data, form]);
  const showVerificationResult = (settings: PaymentSettings) => {
    client.setQueryData(['payment-settings'], settings);
    if (settings.webhookStatus === 'Verified') message.success(t("Đã đăng ký và xác minh webhook với payOS."));
    else message.warning((settings.webhookError ? errorText(settings.webhookError) : null) || t("Cấu hình đã lưu. Webhook chưa được xác minh; hãy thử lại."));
  };
  const verifyMutation = useMutation({
    mutationFn: paymentSettingsApi.verifyWebhook,
    onSuccess: showVerificationResult,
    onError: (error: Error) => message.error(error.message),
  });
  const updateMutation = useMutation({
    mutationFn: paymentSettingsApi.update,
    onSuccess: (settings) => { form.setFieldsValue({ apiKey: '', checksumKey: '' }); showVerificationResult(settings); },
    onError: (error: Error) => message.error(error.message),
  });
  const submit = (values: PaymentForm) => updateMutation.mutate(values);
  const webhookUrl = settingsQuery.data?.webhookUrl || '';
  const isManual = (selectedMode ?? settingsQuery.data?.confirmationMode ?? 'Manual') === 'Manual';
  useEffect(() => { if (isManual) setGuideOpen(false); }, [isManual]);
  const verified = settingsQuery.data?.webhookStatus === 'Verified';
  const busy = updateMutation.isPending || verifyMutation.isPending;
  const copyWebhook = async () => {
    try { await navigator.clipboard.writeText(webhookUrl); message.success(t("Đã sao chép URL webhook.")); }
    catch { message.info(t("Hãy chọn và sao chép URL webhook ở trên.")); }
  };

  return <>
    <PageTitle
      eyebrow={t("XÁC NHẬN THANH TOÁN")}
      title={t("Cấu hình")}
      subtitle={t("Chọn cách xác nhận thanh toán và cấu hình tài khoản nhận tiền.")}
      extra={isManual ? undefined : (
        <Button
          type="primary"
          ghost
          icon={<QuestionCircleOutlined />}
          onClick={() => setGuideOpen(true)}
        >
          {t("Hướng dẫn thiết lập")}
        </Button>
      )}
    />
    {settingsQuery.isError ? <LoadError error={settingsQuery.error} onRetry={() => void settingsQuery.refetch()} /> : settingsQuery.isPending ? <div className="content-card"><Skeleton active paragraph={{ rows: 6 }} /></div> : <><PaymentModeSettings settings={settingsQuery.data} form={modeForm} />{isManual ? null : <div className="settings-grid">
      <div className="settings-main-column">
        <Card className="settings-card" bordered={false}>
          <div className="settings-card-heading"><div className="settings-heading-icon"><CreditCardOutlined /></div><div><Typography.Title level={4}>{t("Cấu hình payOS")}</Typography.Title><Typography.Text type="secondary">{t("Thông tin kết nối được mã hóa và lưu an toàn.")}</Typography.Text></div></div>
          <Alert className="configured-alert" type={verified ? 'success' : settingsQuery.data?.configured ? 'warning' : 'info'} showIcon
            message={verified ? t("Webhook đã được payOS xác minh") : settingsQuery.data?.configured ? t("Đã lưu cấu hình — webhook chưa sẵn sàng") : t("Chưa lưu cấu hình payOS")}
            description={verified ? t("Kênh đã đăng ký webhook. Hãy thử một giao dịch nhỏ để kiểm chứng thanh toán và tự gạch nợ.") : (settingsQuery.data?.webhookError ? errorText(settingsQuery.data.webhookError) : null) || t("Khi lưu cấu hình, hệ thống sẽ tự đăng ký và xác minh webhook với payOS.")} />
          <div className="webhook-setting"><div><strong>URL webhook</strong><span>{t("Hệ thống tự đăng ký URL này với payOS sau khi lưu cấu hình. URL cần truy cập được từ Internet.")}</span></div><Space.Compact block><Input readOnly value={webhookUrl} placeholder={t("Máy chủ chưa có URL HTTPS công khai")} onFocus={(event) => event.currentTarget.select()} /><Button disabled={!webhookUrl} aria-label={t("Sao chép URL webhook")} icon={<CopyOutlined />} onClick={() => void copyWebhook()} /></Space.Compact>
            {settingsQuery.data?.configured && <Button loading={verifyMutation.isPending} disabled={busy && !verifyMutation.isPending} onClick={() => verifyMutation.mutate()}>{t("Xác minh lại webhook")}</Button>}
          </div>
          <Form form={form} layout="vertical" onFinish={submit} requiredMark={false} className="payment-settings-form">
            <Form.Item label="Client ID" name="clientId" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập Client ID.") }]}><Input size="large" placeholder={t("Nhập Client ID từ payOS")} autoComplete="off" /></Form.Item>
            <Form.Item label="API Key" name="apiKey" extra={settingsQuery.data?.configured ? t("Khóa hiện tại được ẩn. Nhập API Key mới để cập nhật.") : undefined} rules={[{ required: !settingsQuery.data?.configured, whitespace: true, message: t("Vui lòng nhập API Key.") }]}><Input.Password size="large" placeholder={settingsQuery.data?.configured ? t("Nhập khóa mới") : t("Nhập API Key")} autoComplete="new-password" /></Form.Item>
            <Form.Item label="Checksum Key" name="checksumKey" extra={settingsQuery.data?.configured ? t("Khóa hiện tại được ẩn. Nhập Checksum Key mới để cập nhật.") : undefined} rules={[{ required: !settingsQuery.data?.configured, whitespace: true, message: t("Vui lòng nhập Checksum Key.") }]}><Input.Password size="large" placeholder={settingsQuery.data?.configured ? t("Nhập khóa mới") : t("Nhập Checksum Key")} autoComplete="new-password" /></Form.Item>
            <div className="settings-form-footer"><span><SafetyCertificateOutlined />  {t("Thông tin nhạy cảm không hiển thị lại sau khi lưu.")}</span><Button type="primary" htmlType="submit" loading={updateMutation.isPending} disabled={verifyMutation.isPending}>{updateMutation.isPending ? t("Đang lưu và xác minh…") : t("Lưu và xác minh payOS")}</Button></div>
          </Form>
        </Card>
        <div className="payment-how-it-works"><div className="eyebrow">{t("CÁCH HOẠT ĐỘNG")}</div><div className="how-steps"><div><span>1</span><p><strong>{t("Người thuê mở liên kết hóa đơn")}</strong><small>{t("Không cần đăng nhập, chỉ dùng liên kết riêng của phòng.")}</small></p></div><div><span>2</span><p><strong>{t("Thanh toán qua payOS")}</strong><small>{t("Người thuê được chuyển đến trang thanh toán an toàn.")}</small></p></div><div><span>3</span><p><strong>{t("Hóa đơn tự cập nhật")}</strong><small>{t("Khoản thu được xác nhận khi webhook đã được xác minh.")}</small></p></div></div></div>
      </div>
      <Card className="settings-side-card" bordered={false}>
        <div className="side-shield"><SafetyCertificateOutlined /></div>
        <Typography.Title level={4}>{t("Bảo mật giao dịch")}</Typography.Title>
        <Typography.Paragraph>{t("Thông tin kết nối được mã hóa ở phía máy chủ. Roomie không lưu số thẻ hoặc thông tin ngân hàng của người thuê.")}</Typography.Paragraph>
        <div className="security-checks">
          <span><CheckCircleFilled />  {t("Mã hóa khóa kết nối")}</span>
          <span><CheckCircleFilled />  {t("Xác minh kết quả thanh toán")}</span>
          <span><CheckCircleFilled />  {t("Tự đối chiếu hóa đơn")}</span>
        </div>
        <div className="settings-state">
          <span className={settingsQuery.data?.configured ? 'connected-dot' : 'disconnected-dot'} />
          {verified ? t("Webhook đã xác minh") : settingsQuery.data?.configured ? t("Webhook chưa sẵn sàng") : t("Chưa lưu cấu hình")}
        </div>
        <Button
          icon={<BookOutlined />}
          block
          onClick={() => setGuideOpen(true)}
          style={{ marginTop: 16 }}
        >
          {t("Hướng dẫn thiết lập payOS")}
        </Button>
      </Card>
    </div>}</>}

    <Drawer
      title={<Space><CreditCardOutlined style={{ color: 'var(--teal)' }} /><span>{t("Hướng dẫn thiết lập payOS")}</span></Space>}
      open={!isManual && guideOpen}
      onClose={() => setGuideOpen(false)}
      width={520}
      extra={
        <Button
          type="link"
          href="https://payos.vn"
          target="_blank"
          rel="noopener noreferrer"
          icon={<ExportOutlined />}
        >
          {t("Mở payOS.vn")}
        </Button>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <Alert
          type="info"
          showIcon
          message={t("payOS là cổng thanh toán tạo mã VietQR động, tiền chuyển thẳng vào tài khoản ngân hàng của bạn ngay khi người thuê quét mã.")}
        />
        <div style={{ padding: '14px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #e7eeec' }}>
          <Typography.Title level={5} style={{ margin: 0, marginBottom: 6, color: '#1677ff' }}>
            {t("Bước 1: Đăng ký tài khoản payOS")}
          </Typography.Title>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 10 }}>
            {t("Truy cập payos.vn, đăng ký tài khoản (chọn loại Cá nhân hoặc Doanh nghiệp) và kích hoạt qua email.")}
          </Typography.Paragraph>
          <Button
            size="small"
            icon={<ExportOutlined />}
            href="https://payos.vn"
            target="_blank"
            rel="noopener noreferrer"
          >
            {t("Mở payOS.vn")}
          </Button>
        </div>
        <div style={{ padding: '14px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #e7eeec' }}>
          <Typography.Title level={5} style={{ margin: 0, marginBottom: 6, color: '#1677ff' }}>
            {t("Bước 2: Liên kết ngân hàng nhận tiền")}
          </Typography.Title>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t("Vào mục Tài khoản ngân hàng trên payOS, thêm số tài khoản ngân hàng bạn muốn nhận tiền phòng.")}
          </Typography.Paragraph>
        </div>
        <div style={{ padding: '14px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #e7eeec' }}>
          <Typography.Title level={5} style={{ margin: 0, marginBottom: 6, color: '#1677ff' }}>
            {t("Bước 3: Lấy bộ khóa kết nối")}
          </Typography.Title>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t("Vào mục Kênh thanh toán, tạo kênh mới rồi vào Cài đặt API để lấy: Client ID, API Key, Checksum Key.")}
          </Typography.Paragraph>
        </div>
        <div style={{ padding: '14px 16px', background: '#f8faf9', borderRadius: 8, border: '1px solid #e7eeec' }}>
          <Typography.Title level={5} style={{ margin: 0, marginBottom: 6, color: '#1677ff' }}>
            {t("Bước 4: Nhập vào Roomie & Xác minh")}
          </Typography.Title>
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {t("Dán 3 khóa vào biểu mẫu và bấm 'Lưu và xác minh payOS'. Hệ thống sẽ tự đăng ký webhook an toàn.")}
          </Typography.Paragraph>
        </div>
        <Alert
          type="warning"
          showIcon
          message={t("Lưu ý quan trọng: Chỉ khi webhook đã được payOS xác minh (chấm xanh), bạn mới được cấp link cho khách thuê.")}
        />
      </div>
    </Drawer>
  </>;
}

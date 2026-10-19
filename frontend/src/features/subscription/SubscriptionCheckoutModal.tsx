import {
CopyOutlined,
ReloadOutlined
} from '@ant-design/icons';
import { Button, Card, Col, Modal, QRCode, Result, Row, Space, Typography } from 'antd';
import { formatVnd } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import type { useSubscriptionDashboard } from './useSubscriptionDashboard';


export default function SubscriptionCheckoutModal({ state }: { state: ReturnType<typeof useSubscriptionDashboard> }) {
  const { checkout, setCheckout, paymentSuccess, setPaymentSuccess, reconcileMutation, copyToClipboard } = state;
  return (
    <Modal
      title={paymentSuccess ? t("Thanh toán thành công") : t("Quét mã QR thanh toán nâng cấp Pro")}
      open={!!checkout}
      onCancel={() => {
        setCheckout(null);
        setPaymentSuccess(false);
      }}
      footer={paymentSuccess ? (
        <Button type="primary" onClick={() => setCheckout(null)}>{t("Hoàn tất")}</Button>
      ) : (
        <Space>
          <Button onClick={() => setCheckout(null)}>{t("Đóng")}</Button>
          {checkout?.checkoutUrl && (
            <Button href={checkout.checkoutUrl} target="_blank" rel="noopener noreferrer">
              {t("Mở trang PayOS")}
            </Button>
          )}
          <Button
            type="primary"
            icon={<ReloadOutlined />}
            loading={reconcileMutation.isPending}
            onClick={() => checkout?.orderCode && reconcileMutation.mutate(checkout.orderCode)}
          >
            {t("Kiểm tra thanh toán")}
          </Button>
        </Space>
      )}
      destroyOnClose
      width={520}
    >
      {paymentSuccess ? (
        <Result
          status="success"
          title={t("Nâng cấp gói Pro thành công!")}
          subTitle={t("Tài khoản của bạn đã được nâng cấp lên gói Pro. Bạn có thể tạo không giới hạn phòng trọ.")}
        />
      ) : checkout ? (
        <div style={{ textAlign: 'center' }}>
          <Typography.Paragraph type="secondary">
            {t("Mở ứng dụng ngân hàng và quét mã VietQR bên dưới để thanh toán tự động.")}
          </Typography.Paragraph>

          <div style={{ display: 'inline-block', padding: 12, background: '#fff', borderRadius: 12, border: '1px solid #f0f0f0', margin: '10px 0' }}>
            <QRCode value={checkout.qrCode || checkout.checkoutUrl} size={220} />
          </div>

          <Card size="small" style={{ textAlign: 'left', marginTop: 12, background: '#fafafa' }}>
            <Row gutter={[8, 8]}>
              <Col span={9}><Typography.Text type="secondary">{t("Số tiền:")}</Typography.Text></Col>
              <Col span={15}><Typography.Text strong style={{ color: '#1890ff', fontSize: 16 }}>{formatVnd(checkout.amount)}</Typography.Text></Col>

              {checkout.accountName && (
                <>
                  <Col span={9}><Typography.Text type="secondary">{t("Chủ tài khoản:")}</Typography.Text></Col>
                  <Col span={15}><Typography.Text strong>{checkout.accountName}</Typography.Text></Col>
                </>
              )}

              {checkout.accountNumber && (
                <>
                  <Col span={9}><Typography.Text type="secondary">{t("Số tài khoản:")}</Typography.Text></Col>
                  <Col span={15}>
                    <Space>
                      <Typography.Text code>{checkout.accountNumber}</Typography.Text>
                      <Button
                        type="text"
                        size="small"
                        icon={<CopyOutlined />}
                        onClick={() => copyToClipboard(checkout.accountNumber!, t("Đã sao chép số tài khoản"))}
                      />
                    </Space>
                  </Col>
                </>
              )}

              {checkout.description && (
                <>
                  <Col span={9}><Typography.Text type="secondary">{t("Nội dung:")}</Typography.Text></Col>
                  <Col span={15}>
                    <Space>
                      <Typography.Text code>{checkout.description}</Typography.Text>
                      <Button
                        type="text"
                        size="small"
                        icon={<CopyOutlined />}
                        onClick={() => copyToClipboard(checkout.description!, t("Đã sao chép nội dung chuyển khoản"))}
                      />
                    </Space>
                  </Col>
                </>
              )}
            </Row>
          </Card>

          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 14 }}>
            {t("Hệ thống sẽ tự động kích hoạt gói Pro trong vòng 1-2 phút sau khi nhận được chuyển khoản.")}
          </Typography.Paragraph>
        </div>
      ) : null}
    </Modal>
  );
}

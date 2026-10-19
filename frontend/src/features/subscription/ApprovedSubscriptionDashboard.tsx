import {
CheckCircleFilled,
CrownOutlined,
SafetyCertificateOutlined
} from '@ant-design/icons';
import { Alert, Button, Card, Col, Progress, Radio, Row, Space, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { formatVnd } from '../../shared/format';
import { t } from '../../shared/i18n/runtime';
import { PageTitle, SectionCard } from '../../shared/ui/Ui';
import { type SubscriptionPackage } from './index';
import SubscriptionCheckoutModal from './SubscriptionCheckoutModal';
import { useSubscriptionDashboard } from './useSubscriptionDashboard';

export default function ApprovedSubscriptionDashboard() {
const state = useSubscriptionDashboard();
  const { selectedPackageId, setSelectedPackageId, upgradeMutation, status, isPro, currentRooms, maxRooms, isLimitReached } = state;
  return <>
    <PageTitle
      eyebrow={t("GÓI CƯỚC & TÀI KHOẢN")}
      title={t("Gói cước dịch vụ & Giới hạn phòng")}
      subtitle={t("Quản lý gói dịch vụ, hạn sử dụng và nâng cấp tài khoản của bạn.")}
    />

    {isLimitReached && (
      <Alert
        style={{ marginBottom: 20 }}
        type="warning"
        showIcon
        message={t("Đã đạt giới hạn phòng của gói Miễn phí")}
        description={t("Bạn đang quản lý {0}/{1} phòng trọ tối đa của gói Miễn phí. Hãy nâng cấp lên gói Pro để tạo phòng không giới hạn.", [currentRooms, maxRooms])}
      />
    )}

    <Row gutter={[20, 20]} style={{ marginBottom: 24 }}>
      <Col xs={24} md={12}>
        <SectionCard title={t("Thông tin gói hiện tại")}>
          <Space direction="vertical" size={14} style={{ width: '100%' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography.Text type="secondary">{t("Gói dịch vụ:")}</Typography.Text>
              {isPro ? (
                <Tag color="gold" icon={<CrownOutlined />} style={{ fontSize: 14, padding: '4px 10px' }}>
                  <strong>PRO</strong>
                </Tag>
              ) : (
                <Tag color="default" style={{ fontSize: 14, padding: '4px 10px' }}>
                  {t("Miễn phí")}
                </Tag>
              )}
            </div>

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography.Text type="secondary">{t("Thời hạn sử dụng:")}</Typography.Text>
              <Typography.Text strong>
                {isPro && status?.expiresAt
                  ? dayjs(status.expiresAt).format('HH:mm DD/MM/YYYY')
                  : t("Không giới hạn thời gian (Gói Miễn phí)")}
              </Typography.Text>
            </div>

            <div style={{ marginTop: 8 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
                <Typography.Text type="secondary">{t("Số phòng đang quản lý:")}</Typography.Text>
                <Typography.Text strong>
                  {isPro ? t("{0} phòng (Không giới hạn)", [currentRooms]) : t("{0} / {1} phòng", [currentRooms, maxRooms])}
                </Typography.Text>
              </div>
              {!isPro && (
                <Progress
                  percent={Math.min(100, Math.round((currentRooms / maxRooms) * 100))}
                  status={isLimitReached ? 'exception' : 'active'}
                  strokeColor={isLimitReached ? '#ff4d4f' : '#1890ff'}
                />
              )}
            </div>
          </Space>
        </SectionCard>
      </Col>

      <Col xs={24} md={12}>
        <SectionCard title={t("Quyền lợi gói Pro")}>
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <Typography.Text>{t("Quản lý không giới hạn số lượng dãy trọ và phòng trọ")}</Typography.Text>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <Typography.Text>{t("Tự động chốt số điện nước & phát hành hóa đơn tự động")}</Typography.Text>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <Typography.Text>{t("Cổng thanh toán trực tuyến payOS & đối soát tự động")}</Typography.Text>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <SafetyCertificateOutlined style={{ color: '#1890ff' }} />
              <Typography.Text>{t("Hỗ trợ kỹ thuật ưu tiên 24/7 từ đội ngũ Roomie")}</Typography.Text>
            </div>
          </Space>
        </SectionCard>
      </Col>
    </Row>

    <SectionCard
      title={isPro ? t("Gia hạn gói Pro") : t("Nâng cấp lên tài khoản Pro")}
      extra={
        <Button
          type="primary"
          icon={<CrownOutlined />}
          size="large"
          loading={upgradeMutation.isPending}
          onClick={() => upgradeMutation.mutate(selectedPackageId)}
        >
          {isPro ? t("Gia hạn ngay") : t("Nâng cấp tài khoản")}
        </Button>
      }
    >
      <Radio.Group
        value={selectedPackageId}
        onChange={(e) => setSelectedPackageId(e.target.value)}
        style={{ width: '100%' }}
      >
        <Row gutter={[16, 16]}>
          {(status?.availablePackages ?? []).map((pkg: SubscriptionPackage) => {
            const isSelected = selectedPackageId === pkg.id;
            return (
              <Col xs={24} sm={8} key={pkg.id}>
                <Card
                  hoverable
                  onClick={() => setSelectedPackageId(pkg.id)}
                  style={{
                    borderColor: isSelected ? '#1890ff' : undefined,
                    boxShadow: isSelected ? '0 0 0 2px rgba(24, 144, 255, 0.2)' : undefined,
                    height: '100%',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                  }}
                >
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                      <Radio value={pkg.id}>
                        <Typography.Text strong style={{ fontSize: 16 }}>{pkg.name}</Typography.Text>
                      </Radio>
                      {pkg.days >= 365 && <Tag color="red">{t("Tiết kiệm nhất")}</Tag>}
                      {pkg.days === 90 && <Tag color="blue">{t("Phổ biến")}</Tag>}
                    </div>
                    <div style={{ margin: '14px 0 10px' }}>
                      <Typography.Title level={3} style={{ margin: 0, color: '#1890ff' }}>
                        {formatVnd(pkg.priceVnd)}
                      </Typography.Title>
                      <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                        {t("Sử dụng trong {0} ngày", [pkg.days])}
                      </Typography.Text>
                    </div>
                    <Typography.Paragraph type="secondary" style={{ fontSize: 13, marginBottom: 0 }}>
                      {pkg.description}
                    </Typography.Paragraph>
                  </div>
                </Card>
              </Col>
            );
          })}
        </Row>
      </Radio.Group>
    </SectionCard>

    {/* QR Payment Modal */}
    <SubscriptionCheckoutModal state={state} />
  </>;
}

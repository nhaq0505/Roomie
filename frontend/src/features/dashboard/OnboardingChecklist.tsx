import {
  ArrowRightOutlined,
  BankOutlined,
  BookOutlined,
  CalculatorOutlined,
  CheckCircleFilled,
  CreditCardOutlined,
  HomeOutlined,
  RocketOutlined,
  UserAddOutlined,
} from '@ant-design/icons';
import { Button, Card, Progress, Space, Tag, Typography } from 'antd';
import { Link, useNavigate } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';

export interface OnboardingChecklistProps {
  hasBuilding: boolean;
  hasRoom: boolean;
  hasPayment: boolean;
  hasTenant: boolean;
  hasInvoice: boolean;
  onDismiss: () => void;
}

export default function OnboardingChecklist({
  hasBuilding,
  hasRoom,
  hasPayment,
  hasTenant,
  hasInvoice,
  onDismiss,
}: OnboardingChecklistProps) {
  const navigate = useNavigate();

  const steps = [
    {
      key: 'building',
      done: hasBuilding,
      icon: <BankOutlined />,
      title: t("Tạo dãy trọ đầu tiên"),
      description: t("Thêm địa điểm cho thuê để gom nhóm các phòng."),
      actionLabel: t("Tạo dãy trọ"),
      to: '/buildings?create=1',
    },
    {
      key: 'room',
      done: hasRoom,
      icon: <HomeOutlined />,
      title: t("Tạo phòng trọ"),
      description: t("Thiết lập giá thuê và phí"),
      actionLabel: t("Tạo phòng trọ"),
      to: '/rooms?create=1',
    },
    {
      key: 'payment',
      done: hasPayment,
      icon: <CreditCardOutlined />,
      title: t("Cài đặt tài khoản nhận tiền VietQR"),
      description: t("Chọn ngân hàng và nhập số tài khoản để tự tạo mã VietQR trên hóa đơn."),
      actionLabel: t("Cài đặt thanh toán"),
      to: '/settings/payment',
    },
    {
      key: 'tenant',
      done: hasTenant,
      icon: <UserAddOutlined />,
      title: t("Thêm người thuê & cấp link phòng"),
      description: t("Quản lý người đang thuê và ngày chuyển vào"),
      actionLabel: t("Thêm người thuê"),
      to: '/rooms',
    },
    {
      key: 'invoice',
      done: hasInvoice,
      icon: <CalculatorOutlined />,
      title: t("Chốt chỉ số điện & phát hành hóa đơn"),
      description: t("Kiểm tra chỉ số rồi phát hành hóa đơn cho người thuê"),
      actionLabel: t("Nhập chỉ số"),
      to: '/readings',
    },
  ];

  const completedCount = steps.filter((s) => s.done).length;
  const percent = Math.round((completedCount / steps.length) * 100);
  const nextStep = steps.find((s) => !s.done);

  return (
    <Card className="onboarding-card" bordered={false} data-testid="onboarding-checklist">
      <div className="onboarding-header">
        <div className="onboarding-header-main">
          <div className="onboarding-badge-row">
            <span className="onboarding-eyebrow">
              <RocketOutlined /> {t("HƯỚNG DẪN BẮT ĐẦU NHANH")}
            </span>
            <Tag color={completedCount === steps.length ? 'success' : 'processing'} className="onboarding-progress-tag">
              {t("Hoàn thành {0}/{1} bước thiết lập", [completedCount, steps.length])}
            </Tag>
          </div>
          <Typography.Title level={4} className="onboarding-title">
            {t("Bắt đầu quản lý nhà trọ với Roomie")}
          </Typography.Title>
          <Typography.Paragraph className="onboarding-subtitle">
            {t("Thực hiện lần lượt 5 bước dưới đây để thiết lập phòng, tạo mã VietQR nhận tiền và gửi hóa đơn cho người thuê.")}
          </Typography.Paragraph>
        </div>
        <Space wrap className="onboarding-header-actions">
          <Link to="/huong-dan" className="onboarding-guide-link">
            <BookOutlined /> {t("Xem cẩm nang hướng dẫn")}
          </Link>
          <Button type="text" size="small" onClick={onDismiss} data-testid="onboarding-dismiss">
            {t("Ẩn hướng dẫn")}
          </Button>
        </Space>
      </div>

      <div className="onboarding-progress-bar">
        <Progress percent={percent} showInfo={false} strokeColor="#0f9d83" trailColor="#e5efec" size="small" />
      </div>

      <div className="onboarding-steps" role="list">
        {steps.map((step, index) => {
          const isCurrent = nextStep?.key === step.key;
          return (
            <div
              key={step.key}
              role="listitem"
              data-testid={`onboarding-step-${step.key}`}
              data-done={step.done ? 'true' : 'false'}
              className={`onboarding-step ${step.done ? 'is-done' : ''} ${isCurrent ? 'is-current' : ''}`}
            >
              <div className="onboarding-step-indicator">
                {step.done ? (
                  <CheckCircleFilled className="onboarding-step-check" />
                ) : (
                  <span className="onboarding-step-number">{index + 1}</span>
                )}
              </div>
              <div className="onboarding-step-body">
                <div className="onboarding-step-title-row">
                  <strong>{step.title}</strong>
                  {step.done ? (
                    <span className="onboarding-step-status is-done">{t("Đã hoàn tất")}</span>
                  ) : isCurrent ? (
                    <span className="onboarding-step-status is-current">{t("Bước tiếp theo")}</span>
                  ) : null}
                </div>
                <span>{step.description}</span>
              </div>
              <div className="onboarding-step-action">
                <Button
                  type={isCurrent ? 'primary' : 'default'}
                  size="small"
                  onClick={() => navigate(step.to)}
                >
                  {step.actionLabel} <ArrowRightOutlined />
                </Button>
              </div>
            </div>
          );
        })}
      </div>
    </Card>
  );
}

import { Fragment, useState, type ComponentType, type ReactNode } from 'react';
import {
LogoutOutlined,
ReloadOutlined
} from '@ant-design/icons';
import { App, Button, Result, Spin } from 'antd';
import { Navigate, useNavigate } from 'react-router-dom';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';
import { useAuth } from '../auth/session';
import ApprovedSubscriptionDashboard from './ApprovedSubscriptionDashboard';

export default function AccountStatusPage({
  layout: Layout = Fragment,
  passwordModal: PasswordModal,
}: {
  layout?: ComponentType<{ children: ReactNode }>;
  passwordModal?: ComponentType<{ open: boolean; onClose: () => void }>;
}) {
  const { message } = App.useApp();
  const { user, ready, refresh, signOut } = useAuth();
  const navigate = useNavigate();
  const [passwordOpen, setPasswordOpen] = useState(false);

  if (!ready) return <div className="screen-center"><Spin size="large" /></div>;
  if (!user) return <Navigate to="/login" replace />;

  if (user.status === 'Approved') {
    if (user.role === 'Admin') return <Navigate to="/admin/owners" replace />;
    return <Layout><ApprovedSubscriptionDashboard /></Layout>;
  }

  const status = user.status ?? 'Pending';
  const config = status === 'Rejected'
    ? { icon: 'error' as const, title: t("Tài khoản chưa được duyệt"), subtitle: t("Thông tin đăng ký chưa thể được chấp nhận. Vui lòng liên hệ quản trị viên để biết thêm chi tiết.") }
    : status === 'Suspended'
      ? { icon: 'warning' as const, title: t("Tài khoản đang tạm khóa"), subtitle: t("Quyền truy cập vào khu vực quản lý đã tạm dừng. Hãy liên hệ quản trị viên để được hỗ trợ.") }
      : { icon: 'info' as const, title: t("Đang chờ duyệt tài khoản"), subtitle: t("Tài khoản của bạn đã được tạo. Quản trị viên sẽ xem xét thông tin trước khi bạn bắt đầu quản lý nhà trọ.") };

  return <div className="status-page">
    <div className="public-language"><LanguageSelector /></div>
    <div className="status-brand"><div className="brand-mark"><RoomieMark /></div><strong>Roomie</strong></div>
    <div className="status-card">
      <Result
        status={config.icon}
        title={config.title}
        subTitle={<>{config.subtitle}<br /><span className="status-email">{user.email}</span></>}
        extra={<div className="status-actions">
          <Button type="primary" icon={<ReloadOutlined />} onClick={() => void refresh()}>{t("Kiểm tra trạng thái")}</Button>
          <Button onClick={() => (PasswordModal ? setPasswordOpen(true) : navigate('/change-password'))}>{t("Đổi mật khẩu")}</Button>
          <Button icon={<LogoutOutlined />} onClick={() => void signOut().then(() => navigate('/login')).catch((error: Error) => message.error(error.message))}>{t("Đăng xuất")}</Button>
        </div>}
      />
    </div>
    <div className="status-foot">{t("Bạn cần trợ giúp? Hãy liên hệ quản trị viên hệ thống.")}</div>
    {PasswordModal && <PasswordModal open={passwordOpen} onClose={() => setPasswordOpen(false)} />}
  </div>;
}

import { ArrowLeftOutlined, ArrowRightOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { Alert, Button, Checkbox, Form, Input, Typography } from 'antd';
import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { errorText } from '../../shared/i18n/errors';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t, translateMessage } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';
import { useAuth } from './AuthProvider';
import { authApi, type User } from './index';

function destination(user: User) {
  if (user.status !== 'Approved') return '/account-status';
  return user.role === 'Admin' ? '/admin/owners' : '/dashboard';
}

export default function LoginPage() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const notice = (location.state as { notice?: string } | null)?.notice;
  const [form] = Form.useForm();

  const submit = async (values: { email: string; password: string; rememberMe?: boolean }) => {
    setBusy(true); setError('');
    try {
      const user = await authApi.login(values.email, values.password, Boolean(values.rememberMe));
      setUser(user);
      navigate(destination(user), { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : t("Không thể đăng nhập. Vui lòng thử lại."));
    } finally { setBusy(false); }
  };

  const fillDemo = (email: string, password: string) => {
    form.setFieldsValue({ email, password });
    setError('');
  };

  return <main className="auth-page"><div className="public-language"><LanguageSelector /></div><div className="auth-decoration auth-decoration-one" /><div className="auth-decoration auth-decoration-two" />
    <div className="auth-aside"><div className="auth-brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div><div className="auth-message"><div className="auth-kicker">{t("NHÀ TRỌ, NHẸ ĐẦU HƠN")}</div><Typography.Title>{t("Gọn gàng từng phòng.")}<br /><span>{t("Rõ ràng từng khoản.")}</span></Typography.Title><Typography.Paragraph>{t("Theo dõi người thuê, chốt chỉ số điện và gửi hóa đơn trong một nơi duy nhất.")}</Typography.Paragraph><div className="auth-feature-list"><div><i>✓</i>  {t("Nắm nhanh tình hình cả dãy trọ")}</div><div><i>✓</i>  {t("Hóa đơn minh bạch, dễ đối chiếu")}</div><div><i>✓</i>  {t("Người thuê tự xem hóa đơn bằng liên kết riêng")}</div></div></div><div className="auth-aside-foot">{t("Đơn giản hóa việc quản lý mỗi ngày")} <span>•</span> 2026</div></div>
    <section className="auth-main"><div className="auth-form-wrap"><div className="auth-small-brand"><div className="brand-mark"><RoomieMark /></div><strong>Roomie</strong></div><Link className="back-link" to="/"><ArrowLeftOutlined aria-hidden="true" /> {t("Về trang chính")}</Link><div className="form-intro"><div className="eyebrow">{t("CHÀO MỪNG TRỞ LẠI")}</div><Typography.Title level={2}>{t("Đăng nhập")}</Typography.Title><Typography.Paragraph>{t("Nhập thông tin tài khoản để tiếp tục quản lý nhà trọ.")}</Typography.Paragraph></div>
      {notice && <Alert className="form-alert" type="success" showIcon message={translateMessage(notice)} />}
      {error && <Alert className="form-alert" type="error" showIcon message={errorText(error)} />}
      <Form form={form} layout="vertical" size="large" initialValues={{ rememberMe: true }} onFinish={submit} requiredMark={false}>
        <Form.Item label={t("Email")} name="email" rules={[{ required: true, message: t("Vui lòng nhập email.") }, { type: 'email', message: t("Email chưa đúng định dạng.") }]}><Input prefix={<MailOutlined />} placeholder="name@example.com" autoComplete="username" /></Form.Item>
        <Form.Item label={t("Mật khẩu")} name="password" rules={[{ required: true, message: t("Vui lòng nhập mật khẩu.") }]}><Input.Password prefix={<LockOutlined />} placeholder={t("Nhập mật khẩu")} autoComplete="current-password" /></Form.Item>
        <div className="auth-options-row"><Form.Item name="rememberMe" valuePropName="checked" noStyle><Checkbox>{t("Ghi nhớ đăng nhập")}</Checkbox></Form.Item><Link className="auth-forgot-link" to="/reset-password">{t("Quên mật khẩu")}</Link></div>
        <Button className="auth-submit" type="primary" htmlType="submit" loading={busy} block>{t("Đăng nhập")} <ArrowRightOutlined /></Button>
      </Form>

      {import.meta.env.DEV && <div className="demo-access"><div className="demo-title">{t("Dùng tài khoản mẫu")}</div><div className="demo-buttons"><Button size="small" onClick={() => fillDemo('owner@phongtro.local', 'Owner123!')}>{t("Chủ trọ")}</Button><Button size="small" onClick={() => fillDemo('admin@phongtro.local', 'Admin123!')}>{t("Quản trị")}</Button></div></div>}
      <div className="auth-switch">{t("Chưa có tài khoản?")} <Link to="/register">{t("Đăng ký làm chủ trọ")}</Link></div>
    </div></section>
  </main>;
}

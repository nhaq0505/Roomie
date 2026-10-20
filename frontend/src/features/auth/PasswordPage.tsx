import { Fragment, type ComponentType, type ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';
import { App, Button, Form, Input, Spin, Typography } from 'antd';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { t } from '../../shared/i18n/runtime';
import { PageTitle, SectionCard } from '../../shared/ui/Ui';
import { useAuth } from './AuthProvider';
import { authApi } from './index';

export default function PasswordPage({ reset = false, layout: Layout = Fragment }: { reset?: boolean; layout?: ComponentType<{ children: ReactNode }> }) {
  const { user, ready, setUser } = useAuth();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [form] = Form.useForm();
  const mutation = useMutation({
    mutationFn: (values: { email: string; token: string; currentPassword: string; newPassword: string; confirmPassword: string }) => reset ? authApi.resetPassword(values) : authApi.changePassword(values),
    onSuccess: () => {
      form.resetFields();
      if (reset) { setUser(null); message.success(t("Đã đổi mật khẩu. Vui lòng đăng nhập lại.")); navigate('/login', { replace: true }); }
      else { message.success(t("Đã đổi mật khẩu. Các phiên đăng nhập khác đã được thu hồi.")); navigate(user?.role === 'Admin' ? '/admin/owners' : '/dashboard', { replace: true }); }
    },
    onError: (error: Error) => message.error(error.message),
  });
  if (!ready) return <div className="screen-center"><Spin /></div>;
  if (!reset && !user) return <Navigate to="/login" replace />;
  const content = <><PageTitle title={reset ? t("Đặt lại mật khẩu") : t("Đổi mật khẩu")} />
    <SectionCard title={t("Mật khẩu mới")}>
      <Typography.Paragraph>{reset ? t("Nhập email và mã đặt lại do quản trị viên cấp. Mã chỉ dùng một lần trong 30 phút.") : t("Phiên đăng nhập hiện tại được giữ lại. Các thiết bị khác sẽ phải đăng nhập lại.")}</Typography.Paragraph>
      <Form form={form} layout="vertical" style={{ maxWidth: 560 }} onFinish={(values) => { if (!mutation.isPending) mutation.mutate(values); }}>
        {reset ? <><Form.Item name="email" label={t("Email")} rules={[{ required: true, type: 'email' }]}><Input autoComplete="username" maxLength={320} /></Form.Item>
          <Form.Item name="token" label={t("Mã đặt lại mật khẩu")} rules={[{ required: true, len: 64, message: t("Nhập mã 64 ký tự do quản trị viên cấp.") }]}><Input.Password autoComplete="off" maxLength={64} /></Form.Item></> :
          <Form.Item name="currentPassword" label={t("Mật khẩu hiện tại")} rules={[{ required: true }]}><Input.Password autoComplete="current-password" maxLength={256} /></Form.Item>}
        <Form.Item name="newPassword" label={t("Mật khẩu mới")} extra={t("Từ 9 đến 256 ký tự, không bắt buộc chữ hoa, số hay ký tự đặc biệt.")} rules={[{ required: true, whitespace: true, min: 9, max: 256, message: t("Mật khẩu mới cần từ 9 đến 256 ký tự.") }]}><Input.Password autoComplete="new-password" maxLength={256} /></Form.Item>
        <Form.Item name="confirmPassword" label={t("Xác nhận mật khẩu mới")} dependencies={['newPassword']} rules={[{ required: true }, ({ getFieldValue }) => ({ validator(_, value) { return value === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error(t("Xác nhận mật khẩu không khớp."))); } })]}><Input.Password autoComplete="new-password" maxLength={256} /></Form.Item>
        <Button type="primary" htmlType="submit" loading={mutation.isPending}>{t("Lưu mật khẩu")}</Button>
      </Form>
      {reset && <Typography.Paragraph style={{ marginTop: 16 }}><Link to="/reset-password">{t("Khôi phục qua email")}</Link> · <Link to="/login">{t("Đăng nhập")}</Link></Typography.Paragraph>}
    </SectionCard></>;
  return reset ? <div style={{ maxWidth: 560, margin: '40px auto', padding: 16 }}>{content}</div> : <Layout>{content}</Layout>;
}

import { ArrowLeftOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { Alert, Button, Form, Input, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { errorText } from '../../shared/i18n/errors';
import { t } from '../../shared/i18n/runtime';
import RoomieMark from '../../shared/ui/RoomieMark';
import { useAuth } from './AuthProvider';
import PasswordPage from './PasswordPage';
import { authApi, type PasswordRecoveryChallenge, type PasswordRecoveryGrant } from './index';

const recoveryChallengeStorage = 'roomie.password_recovery.challenge';
function readStoredRecoveryChallenge(): PasswordRecoveryChallenge | null {
  try {
    const raw = sessionStorage.getItem(recoveryChallengeStorage);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PasswordRecoveryChallenge;
    if (parsed && typeof parsed.challengeToken === 'string' && typeof parsed.email === 'string' &&
        parsed.expiresAt && new Date(parsed.expiresAt).getTime() > Date.now()) {
      return parsed;
    }
    sessionStorage.removeItem(recoveryChallengeStorage);
    return null;
  } catch {
    return null;
  }
}

export default function ForgotPasswordPage() {
  const [params] = useSearchParams();
  useEffect(() => {
    if (params.get('method') === 'admin') {
      try { sessionStorage.removeItem(recoveryChallengeStorage); } catch {}
    }
  }, [params]);
  return params.get('method') === 'admin' ? <PasswordPage reset /> : <EmailPasswordRecovery />;
}

function EmailPasswordRecovery() {
  const [challenge, setChallenge] = useState<PasswordRecoveryChallenge | null>(readStoredRecoveryChallenge);
  const [grant, setGrant] = useState<PasswordRecoveryGrant | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [now, setNow] = useState(Date.now());
  const [emailForm] = Form.useForm();
  const [codeForm] = Form.useForm();
  const [passwordForm] = Form.useForm();
  const mounted = useRef(false);
  const { setUser } = useAuth();
  const navigate = useNavigate();
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    if (!challenge) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [challenge]);
  const wait = challenge ? Math.max(0, Math.ceil((Date.parse(challenge.resendAvailableAt) - now) / 1000)) : 0;
  const expired = !!challenge && Date.parse(grant?.expiresAt ?? challenge.expiresAt) <= now;
  const run = async (action: () => Promise<void>) => {
    if (busy) return;
    setBusy(true); setError('');
    try { await action(); }
    catch (cause) { if (mounted.current) setError(cause instanceof Error ? cause.message : t("Yêu cầu chưa thành công. Vui lòng thử lại.")); }
    finally { if (mounted.current) setBusy(false); }
  };
  const send = (email: string) => run(async () => {
    const next = await authApi.forgotPassword(email);
    if (!mounted.current) return;
    try { sessionStorage.setItem(recoveryChallengeStorage, JSON.stringify(next)); } catch {}
    setChallenge(next); setGrant(null); setNow(Date.now()); codeForm.resetFields(); passwordForm.resetFields();
  });
  const verify = ({ code }: { code: string }) => run(async () => {
    const next = await authApi.verifyPasswordRecovery(challenge!.challengeToken, code);
    if (!mounted.current) return;
    try { sessionStorage.removeItem(recoveryChallengeStorage); } catch {}
    setGrant(next); setNow(Date.now()); codeForm.resetFields();
  });
  const complete = ({ newPassword, confirmPassword }: { newPassword: string; confirmPassword: string }) => run(async () => {
    await authApi.completePasswordRecovery(grant!.resetToken, newPassword, confirmPassword);
    if (!mounted.current) return;
    try { sessionStorage.removeItem(recoveryChallengeStorage); } catch {}
    setUser(null); setGrant(null); passwordForm.resetFields();
    navigate('/login', { replace: true, state: { notice: t("Đã đổi mật khẩu. Vui lòng đăng nhập lại.") } });
  });

  return <main className="auth-page"><div className="public-language"><LanguageSelector /></div>
    <div className="auth-decoration auth-decoration-one" /><div className="auth-decoration auth-decoration-two" />
    <div className="auth-aside"><div className="auth-brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div><div className="auth-message"><Typography.Title>{t("Khôi phục tài khoản của bạn")}</Typography.Title><Typography.Paragraph>{t("Xác minh email để tự đặt lại mật khẩu an toàn.")}</Typography.Paragraph></div></div>
    <section className="auth-main"><div className="auth-form-wrap">
      <div className="auth-small-brand"><div className="brand-mark"><RoomieMark /></div><strong>Roomie</strong></div>
      <Link className="back-link" to="/login"><ArrowLeftOutlined /> {t("Đăng nhập")}</Link>
      <div className="form-intro">
      <Typography.Title level={2}>{grant ? t("Mật khẩu mới") : challenge ? t("Xác minh mã OTP") : t("Quên mật khẩu")}</Typography.Title>
      <Typography.Paragraph type="secondary">{grant ? t("Email đã được xác minh. Hãy đặt mật khẩu mới cho tài khoản.") : challenge ? t("Nếu email có tài khoản, mã khôi phục sẽ được gửi đến {0}. Hãy kiểm tra cả thư rác.", [challenge.email]) : t("Nhập email đã đăng ký để nhận mã đặt lại mật khẩu.")}</Typography.Paragraph>
      </div>
      {error && <Alert className="form-alert" type="error" showIcon message={errorText(error)} />}
      {expired && <Alert type="warning" showIcon message={t("Mã đã hết hạn. Vui lòng yêu cầu mã mới.")} style={{ marginBottom: 16 }} />}
      {!challenge ? <Form form={emailForm} key="recovery-email" name="recovery-email" layout="vertical" size="large" requiredMark={false} onFinish={({ email }) => void send(email)} disabled={busy}>
        <Form.Item name="email" label={t("Email")} rules={[{ required: true, type: 'email', message: t("Email chưa đúng định dạng.") }]}><Input prefix={<MailOutlined />} autoComplete="email" maxLength={320} placeholder="name@example.com" /></Form.Item>
        <Button className="auth-submit" type="primary" htmlType="submit" block loading={busy}>{t("Gửi mã khôi phục")}</Button>
      </Form> : grant ? <Form form={passwordForm} key="recovery-password" name="recovery-password" layout="vertical" size="large" requiredMark={false} onFinish={complete} disabled={busy}>
        <Form.Item name="newPassword" label={t("Mật khẩu mới")} extra={t("Từ 9 đến 256 ký tự, không bắt buộc chữ hoa, số hay ký tự đặc biệt.")} rules={[{ required: true, whitespace: true, min: 9, max: 256, message: t("Mật khẩu mới cần từ 9 đến 256 ký tự.") }]}><Input.Password prefix={<LockOutlined />} autoComplete="new-password" maxLength={256} /></Form.Item>
        <Form.Item name="confirmPassword" label={t("Xác nhận mật khẩu mới")} dependencies={['newPassword']} rules={[{ required: true }, ({ getFieldValue }) => ({ validator(_, value) { return value === getFieldValue('newPassword') ? Promise.resolve() : Promise.reject(new Error(t("Xác nhận mật khẩu không khớp."))); } })]}><Input.Password autoComplete="new-password" maxLength={256} /></Form.Item>
        <Button className="auth-submit" type="primary" htmlType="submit" block loading={busy} disabled={expired}>{t("Lưu mật khẩu")}</Button>
      </Form> : <Form form={codeForm} key="recovery-code" name="recovery-code" layout="vertical" size="large" requiredMark={false} onFinish={verify} disabled={busy}>
        <Form.Item name="code" label={t("Mã xác nhận")} rules={[{ required: true, pattern: /^[0-9]{6}$/, message: t("Nhập đủ 6 chữ số của mã xác nhận.") }]}><Input inputMode="numeric" autoComplete="one-time-code" maxLength={6} placeholder="000000" autoFocus /></Form.Item>
        <Typography.Paragraph type="secondary">{t("Mã có hiệu lực trong 10 phút. Nếu chưa nhận được, hãy kiểm tra thư rác.")}</Typography.Paragraph>
        <Button className="auth-submit" type="primary" htmlType="submit" block loading={busy} disabled={expired}>{t("Xác minh mã OTP")}</Button>
      </Form>}
      {challenge && !grant && <><Button block style={{ marginTop: 12 }} disabled={busy || wait > 0} onClick={() => void send(challenge.email)}>{wait > 0 ? t("Gửi lại mã sau {0} giây", [wait]) : t("Gửi lại mã")}</Button><Button block type="link" disabled={busy} onClick={() => { try { sessionStorage.removeItem(recoveryChallengeStorage); } catch {} setChallenge(null); setGrant(null); setError(''); codeForm.resetFields(); passwordForm.resetFields(); }}>{t("Đổi địa chỉ email")}</Button></>}
      <div className="auth-switch"><Link to="/reset-password?method=admin">{t("Dùng mã do quản trị viên cấp")}</Link></div>
    </div></section>
  </main>;
}

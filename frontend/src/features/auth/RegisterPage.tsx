import { ArrowLeftOutlined, ArrowRightOutlined, GiftOutlined, LockOutlined, MailOutlined, PhoneOutlined, UserOutlined } from '@ant-design/icons';
import { Alert, Button, Form, Input, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { errorText } from '../../shared/i18n/errors';
import LanguageSelector from '../../shared/i18n/LanguageSelector';
import { t } from '../../shared/i18n/runtime';
import { getRegistrationAttributionForApi, trackEvent } from '../../shared/analytics';
import RoomieMark from '../../shared/ui/RoomieMark';
import { useAuth } from './AuthProvider';
import { authApi, type RegistrationVerification } from './index';

const challengeStorage = 'rehouse.registration-verification';
type StoredRegisterChallenge = RegistrationVerification & { name?: string; phone?: string };
function readChallenge(): StoredRegisterChallenge | null {
  try {
    const saved = JSON.parse(sessionStorage.getItem(challengeStorage) ?? 'null');
    return saved?.verificationRequired === true && typeof saved.challengeToken === 'string' && saved.challengeToken.length === 64 &&
      typeof saved.email === 'string' && Number.isFinite(Date.parse(saved.expiresAt)) && Number.isFinite(Date.parse(saved.resendAvailableAt)) ? saved : null;
  } catch { return null; }
}

export default function RegisterPage() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const promoParam = (searchParams.get('promo') ?? '').trim().toUpperCase();
  const [form] = Form.useForm();
  const [otpForm] = Form.useForm();
  const [challenge, setChallenge] = useState<StoredRegisterChallenge | null>(readChallenge);
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    if (promoParam) {
      form.setFieldsValue({ promoCode: promoParam });
    }
  }, [promoParam, form]);
  useEffect(() => {
    if (!challenge) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [challenge]);
  const saveChallenge = (next: StoredRegisterChallenge | null) => {
    setChallenge(next); setNow(Date.now());
    try { if (next) sessionStorage.setItem(challengeStorage, JSON.stringify(next)); else sessionStorage.removeItem(challengeStorage); } catch { /* In-memory flow still works. */ }
  };
  const resendWait = challenge ? Math.max(0, Math.ceil((Date.parse(challenge.resendAvailableAt) - now) / 1000)) : 0;
  const codeExpired = !!challenge && Date.parse(challenge.expiresAt) <= now;
  const { setUser } = useAuth();

  const verify = async ({ code }: { code: string }) => {
    if (!challenge || busy) return;
    setBusy(true); setError('');
    try {
      await authApi.verifyRegistration(challenge.challengeToken, code);
      saveChallenge(null); otpForm.resetFields();
      navigate('/login', { replace: true, state: { notice: t("Email đã được xác nhận. Hãy đăng nhập để bắt đầu sử dụng.") } });
    } catch (err) { setError(err instanceof Error ? err.message : t("Không thể xác nhận email. Vui lòng thử lại.")); }
    finally { setBusy(false); }
  };
  const resend = async () => {
    if (!challenge || busy || resendWait > 0) return;
    setBusy(true); setError('');
    try {
      const next = await authApi.resendRegistration(challenge.challengeToken);
      saveChallenge({ ...next, name: challenge.name, phone: challenge.phone });
      otpForm.resetFields();
    }
    catch (err) { setError(err instanceof Error ? err.message : t("Không thể gửi lại mã. Vui lòng thử lại.")); }
    finally { setBusy(false); }
  };

  const submit = async (values: { name: string; email: string; password: string; phone: string; promoCode?: string }) => {
    if (busy) return;
    setBusy(true); setError('');
    try {
      trackEvent('registration_submit', { eventName: 'Registration Submit', path: '/register' });
    } catch {
      // Non-blocking
    }
    let accountCreated = false;
    try {
      let attribution;
      try {
        attribution = getRegistrationAttributionForApi();
      } catch {
        // Fallback
      }
      const normalizedPromo = (values.promoCode ?? promoParam ?? '').trim().toUpperCase();
      const registration = await authApi.register({
        ...values,
        promoCode: normalizedPromo ? normalizedPromo : undefined,
        attribution,
      });
      if ('verificationRequired' in registration && registration.verificationRequired) {
        form.resetFields(['password']);
        saveChallenge({ ...registration, name: values.name, phone: values.phone });
        return;
      }
      accountCreated = true;
      const user = await authApi.login(values.email, values.password);
      setUser(user);
      navigate('/dashboard', { replace: true });
    } catch (err) {
      if (accountCreated) {
        navigate('/login', { replace: true, state: { notice: t("Tài khoản đã được tạo. Hãy đăng nhập bằng email và mật khẩu vừa đăng ký.") } });
      } else {
        setError(err instanceof Error ? err.message : t("Không thể tạo tài khoản. Vui lòng thử lại."));
      }
    } finally { setBusy(false); }
  };

  return <main className="auth-page auth-page-register"><div className="public-language"><LanguageSelector /></div><div className="auth-decoration auth-decoration-one" /><div className="auth-decoration auth-decoration-two" />
    <div className="auth-aside"><div className="auth-brand"><div className="brand-mark"><RoomieMark /></div><div><strong>Roomie</strong><span>{t("QUẢN LÝ PHÒNG TRỌ")}</span></div></div><div className="auth-message"><div className="auth-kicker">{t("BẮT ĐẦU THẬT DỄ")}</div><Typography.Title>{t("Quản lý phòng trọ")}<br /><span>{t("theo cách của bạn.")}</span></Typography.Title><Typography.Paragraph>{t("Đăng ký, xác nhận email và bắt đầu quản lý nhà trọ với gói Miễn phí.")}</Typography.Paragraph><div className="onboarding-steps"><div><span>01</span><div><strong>{t("Tạo tài khoản")}</strong><small>{t("Điền thông tin liên hệ của bạn")}</small></div></div><div><span>02</span><div><strong>{t("Xác nhận email")}</strong><small>{t("Nhập mã được gửi đến email của bạn")}</small></div></div><div><span>03</span><div><strong>{t("Quản lý nhà trọ")}</strong><small>{t("Thêm phòng và bắt đầu sử dụng")}</small></div></div></div></div><div className="auth-aside-foot">{t("Đơn giản hóa việc quản lý mỗi ngày")} <span>•</span> 2026</div></div>
    <section className="auth-main"><div className="auth-form-wrap"><div className="auth-small-brand"><div className="brand-mark"><RoomieMark /></div><strong>Roomie</strong></div><Link className="back-link" to="/"><ArrowLeftOutlined aria-hidden="true" /> {t("Về trang chính")}</Link><div className="form-intro"><div className="eyebrow">{t("DÀNH CHO CHỦ TRỌ")}</div><Typography.Title level={2}>{challenge ? t("Xác nhận email") : t("Tạo tài khoản")}</Typography.Title><Typography.Paragraph>{challenge ? t("Nhập mã 6 chữ số đã gửi đến {0}.", [challenge.email]) : t("Thông tin này giúp bạn quản lý tài khoản và nhận hỗ trợ khi cần.")}</Typography.Paragraph></div>
      {promoParam && !challenge && (
        <Alert
          className="form-alert"
          type="success"
          showIcon
          icon={<GiftOutlined />}
          message={t("Đã áp dụng mã ưu đãi {0} — Tài khoản sẽ tự động được cộng gói Pro sau khi hoàn tất đăng ký.", [promoParam])}
        />
      )}
      {error && <Alert className="form-alert" type="error" showIcon message={errorText(error)} />}
      {challenge ? <>
        {codeExpired && <Alert className="form-alert" type="warning" showIcon message={t("Mã đã hết hạn. Vui lòng gửi lại mã.")} />}
        <Form form={otpForm} layout="vertical" size="large" onFinish={verify} requiredMark={false} disabled={busy}>
          <Form.Item label={t("Mã xác nhận")} name="code" rules={[{ required: true, pattern: /^[0-9]{6}$/, message: t("Nhập đủ 6 chữ số của mã xác nhận.") }]}>
            <Input inputMode="numeric" autoComplete="one-time-code" maxLength={6} autoFocus placeholder="000000" />
          </Form.Item>
          <Typography.Paragraph type="secondary">{t("Mã có hiệu lực trong 10 phút. Nếu chưa nhận được, hãy kiểm tra thư rác.")}</Typography.Paragraph>
          <Button className="auth-submit" type="primary" htmlType="submit" loading={busy} disabled={codeExpired} block>{t("Xác nhận email")}</Button>
        </Form>
        <Button block style={{ marginTop: 12 }} disabled={busy || resendWait > 0} onClick={() => void resend()}>
          {resendWait > 0 ? t("Gửi lại mã sau {0} giây", [resendWait]) : t("Gửi lại mã")}
        </Button>
        <Button
          type="link"
          block
          disabled={busy}
          style={{ marginTop: 8 }}
          onClick={() => {
            if (challenge) {
              form.setFieldsValue({ name: challenge.name, phone: challenge.phone, email: challenge.email });
            }
            saveChallenge(null);
            setError('');
            otpForm.resetFields();
          }}
        >
          {t("Sửa thông tin đăng ký")}
        </Button>
      </> : <Form form={form} layout="vertical" size="large" onFinish={submit} requiredMark={false} initialValues={{ promoCode: promoParam || undefined }}>
        <Form.Item label={t("Tên của bạn")} name="name" rules={[{ required: true, whitespace: true, message: t("Vui lòng nhập tên.") }]}><Input prefix={<UserOutlined />} placeholder={t("Nguyễn Minh Anh")} autoComplete="name" /></Form.Item>
        <Form.Item label={t("Số điện thoại")} name="phone" rules={[{ required: true, message: t("Vui lòng nhập số điện thoại.") }]}><Input prefix={<PhoneOutlined />} placeholder="09xx xxx xxx" autoComplete="tel" /></Form.Item>
        <Form.Item label={t("Email")} name="email" rules={[{ required: true, message: t("Vui lòng nhập email.") }, { type: 'email', message: t("Email chưa đúng định dạng.") }]}><Input prefix={<MailOutlined />} placeholder="name@example.com" autoComplete="email" /></Form.Item>
        <Form.Item label={t("Mật khẩu")} name="password" rules={[{ required: true, whitespace: true, min: 9, max: 256, message: t("Mật khẩu mới cần từ 9 đến 256 ký tự.") }]}><Input.Password prefix={<LockOutlined />} placeholder={t("Mật khẩu mới")} autoComplete="new-password" /></Form.Item>
        <Form.Item label={t("Mã ưu đãi (tùy chọn)")} name="promoCode"><Input prefix={<GiftOutlined />} placeholder="ROOMIE3M" style={{ textTransform: 'uppercase' }} /></Form.Item>
        <Button className="auth-submit" type="primary" htmlType="submit" loading={busy} block>{t("Gửi mã xác nhận")} <ArrowRightOutlined /></Button>
      </Form>}
      <div className="auth-switch">{t("Đã có tài khoản?")} <Link to="/login">{t("Đăng nhập")}</Link></div><div className="auth-legal">{t("Email cần được xác nhận trước khi đăng nhập.")}</div>
    </div></section>
  </main>;
}

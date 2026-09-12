import { useEffect, useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import {
  ApiError,
  loginWithPassword,
  loginWithSms,
  requestSmsLoginCode,
} from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { FormField } from '../components/FormField';
import { Icon } from '../components/Icon';
import { storeUrl } from '../config';
import { authRoute, getSafeReturnUrl } from '../return-url';

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const phonePattern = /^\+[1-9][0-9]{7,14}$/;
const codePattern = /^[0-9]{6}$/;

type LoginMode = 'password' | 'sms';
type LoginErrors = Partial<Record<'email' | 'password' | 'phoneNumber' | 'code' | 'form', string>>;

function loginErrorMessage(error: unknown, mode: LoginMode): string {
  if (error instanceof ApiError && error.status === 401) {
    return mode === 'password'
      ? '邮箱或密码不正确，或账户暂时不可用。'
      : '手机号或验证码不正确，或账户暂时不可用。';
  }

  return error instanceof Error && error.message
    ? error.message
    : '登录没有完成，请稍后重试。';
}

export default function LoginPage() {
  const { search } = useLocation();
  const returnUrl = getSafeReturnUrl(search);
  const [mode, setMode] = useState<LoginMode>('password');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [code, setCode] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [sendingCode, setSendingCode] = useState(false);
  const [resendAfter, setResendAfter] = useState(0);
  const [smsNotice, setSmsNotice] = useState('');
  const [errors, setErrors] = useState<LoginErrors>({});

  useEffect(() => {
    if (resendAfter <= 0) return;
    const timer = window.setTimeout(
      () => setResendAfter((seconds) => Math.max(0, seconds - 1)),
      1_000,
    );
    return () => window.clearTimeout(timer);
  }, [resendAfter]);

  const switchMode = (nextMode: LoginMode) => {
    setMode(nextMode);
    setErrors({});
    setSmsNotice('');
  };

  const sendCode = async () => {
    const normalizedPhone = phoneNumber.trim();
    if (!phonePattern.test(normalizedPhone)) {
      setErrors({ phoneNumber: '请输入 E.164 格式手机号，例如 +8613800138000。' });
      return;
    }

    setSendingCode(true);
    setErrors({});
    setSmsNotice('');
    try {
      const result = await requestSmsLoginCode(normalizedPhone);
      setResendAfter(result.retryAfterSeconds);
      setSmsNotice(`若该手机号已绑定账户，验证码将在 ${Math.ceil(result.expiresInSeconds / 60)} 分钟内送达。`);
    } catch (error) {
      setErrors({ form: error instanceof Error ? error.message : '验证码发送请求没有完成，请稍后重试。' });
    } finally {
      setSendingCode(false);
    }
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedEmail = email.trim();
    const normalizedPhone = phoneNumber.trim();
    const nextErrors: LoginErrors = {};

    if (mode === 'password') {
      if (!emailPattern.test(normalizedEmail)) nextErrors.email = '请输入有效的邮箱地址。';
      if (!password) nextErrors.password = '请输入密码。';
    } else {
      if (!phonePattern.test(normalizedPhone)) {
        nextErrors.phoneNumber = '请输入 E.164 格式手机号，例如 +8613800138000。';
      }
      if (!codePattern.test(code)) nextErrors.code = '请输入 6 位数字验证码。';
    }

    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      if (mode === 'password') {
        await loginWithPassword({ email: normalizedEmail, password, rememberMe });
      } else {
        await loginWithSms({ phoneNumber: normalizedPhone, code, rememberMe });
      }
      window.location.assign(returnUrl);
    } catch (error) {
      setErrors({ form: loginErrorMessage(error, mode) });
      setSubmitting(false);
    }
  };

  return (
    <AuthLayout>
      <form className="login-form" onSubmit={submit} noValidate>
        <div className="login-form__heading">
          <h1>欢迎回来</h1>
          <p>登录后继续查看订单、管理地址与积分。</p>
        </div>
        <div className="login-mode-tabs" aria-label="选择登录方式">
          <button
            type="button"
            aria-pressed={mode === 'password'}
            onClick={() => switchMode('password')}
          >
            密码登录
          </button>
          <button
            type="button"
            aria-pressed={mode === 'sms'}
            onClick={() => switchMode('sms')}
          >
            短信登录
          </button>
        </div>
        {mode === 'password' ? (
          <>
            <FormField
              id="login-email"
              label="邮箱地址"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              placeholder="name@example.com"
              autoComplete="email"
              maxLength={256}
              required
              leading={<Icon name="mail" />}
              error={errors.email}
            />
            <FormField
              id="login-password"
              label="密码"
              type={passwordVisible ? 'text' : 'password'}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="请输入密码"
              autoComplete="current-password"
              maxLength={128}
              required
              leading={<Icon name="lock" />}
              trailing={(
                <button
                  className="field-icon-button"
                  type="button"
                  onClick={() => setPasswordVisible((visible) => !visible)}
                  aria-label={passwordVisible ? '隐藏密码' : '显示密码'}
                >
                  <Icon name={passwordVisible ? 'eye-off' : 'eye'} />
                </button>
              )}
              error={errors.password}
            />
          </>
        ) : (
          <>
            <FormField
              id="login-phone"
              label="手机号"
              type="tel"
              value={phoneNumber}
              onChange={(event) => setPhoneNumber(event.target.value)}
              placeholder="+8613800138000"
              autoComplete="tel"
              maxLength={16}
              required
              leading={<Icon name="phone" />}
              error={errors.phoneNumber}
            />
            <FormField
              id="login-sms-code"
              label="短信验证码"
              value={code}
              onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
              placeholder="6 位验证码"
              autoComplete="one-time-code"
              inputMode="numeric"
              pattern="[0-9]{6}"
              maxLength={6}
              required
              leading={<Icon name="shield" />}
              trailing={(
                <button
                  className="sms-code-button"
                  type="button"
                  disabled={sendingCode || submitting || resendAfter > 0}
                  onClick={() => void sendCode()}
                >
                  {sendingCode ? '发送中…' : resendAfter > 0 ? `${resendAfter}s 后重发` : '获取验证码'}
                </button>
              )}
              error={errors.code}
            />
            <p className="password-hint">
              仅支持账户中已验证的手机号
              {import.meta.env.DEV ? '；开发环境固定验证码：123456' : ''}
            </p>
          </>
        )}
        <div className="login-form__options">
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={(event) => setRememberMe(event.target.checked)}
            />
            <span><Icon name="check" /></span>
            保持登录
          </label>
          {mode === 'password' ? <a href={`${storeUrl}/forgot-password`}>忘记密码？</a> : <span />}
        </div>
        {smsNotice ? <p className="form-message form-message--success" role="status">{smsNotice}</p> : null}
        {errors.form ? <p className="form-message form-message--error" role="alert">{errors.form}</p> : null}
        <Button className="login-submit" type="submit" loading={submitting}>登录</Button>
        <div className="login-divider"><span>或</span></div>
        <p className="login-register">
          还没有账户？ <Link to={authRoute('/register', returnUrl)}>创建账户</Link>
        </p>
        <p className="login-trust"><Icon name="shield" />安全登录由 MSA Identity 提供</p>
      </form>
    </AuthLayout>
  );
}

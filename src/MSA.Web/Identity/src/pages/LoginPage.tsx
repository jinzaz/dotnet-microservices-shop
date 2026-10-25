import { useEffect, useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import {
  ApiError,
  captchaErrorMessage,
  loginWithPassword,
  loginWithSms,
  requestSmsLoginCode,
} from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { CaptchaField, useCaptcha } from '../components/CaptchaField';
import { CountryCodeSelect, defaultCountryCode, toE164 } from '../components/CountryCodeSelect';
import { FormField } from '../components/FormField';
import { Icon } from '../components/Icon';
import { storeUrl } from '../config';
import { authRoute, getSafeReturnUrl } from '../return-url';

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const codePattern = /^[0-9]{6}$/;

type LoginMode = 'password' | 'sms';
type LoginErrors = Partial<Record<'email' | 'password' | 'phoneNumber' | 'captcha' | 'code' | 'form', string>>;

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
  const [countryCode, setCountryCode] = useState(defaultCountryCode);
  const [phoneNumber, setPhoneNumber] = useState('');
  const [code, setCode] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [sendingCode, setSendingCode] = useState(false);
  const [resendAfter, setResendAfter] = useState(0);
  const [smsNotice, setSmsNotice] = useState('');
  const [errors, setErrors] = useState<LoginErrors>({});
  const captcha = useCaptcha(mode === 'sms');

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
    const normalizedPhone = toE164(countryCode, phoneNumber);
    if (!normalizedPhone) {
      setErrors({ phoneNumber: '请输入有效的手机号。' });
      return;
    }
    if (!captcha.captcha || !captcha.answer) {
      setErrors({ captcha: '请先输入图形验证码的计算结果。' });
      return;
    }

    setSendingCode(true);
    setErrors({});
    setSmsNotice('');
    try {
      const result = await requestSmsLoginCode(normalizedPhone, {
        captchaId: captcha.captcha.captchaId,
        captchaAnswer: captcha.answer,
      });
      setResendAfter(result.retryAfterSeconds);
      setSmsNotice(`验证码已发送，${Math.ceil(result.expiresInSeconds / 60)} 分钟内有效。`);
    } catch (error) {
      const captchaError = captchaErrorMessage(error);
      setErrors(captchaError
        ? { captcha: captchaError }
        : { form: error instanceof Error ? error.message : '验证码发送请求没有完成，请稍后重试。' });
    } finally {
      setSendingCode(false);
      // 每张图形验证码只能用一次，不管成败都换一张。
      void captcha.refresh();
    }
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedEmail = email.trim();
    const normalizedPhone = toE164(countryCode, phoneNumber);
    const nextErrors: LoginErrors = {};

    if (mode === 'password') {
      if (!emailPattern.test(normalizedEmail)) nextErrors.email = '请输入有效的邮箱地址。';
      if (!password) nextErrors.password = '请输入密码。';
    } else {
      if (!normalizedPhone) nextErrors.phoneNumber = '请输入有效的手机号。';
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
        await loginWithSms({ phoneNumber: normalizedPhone!, code, rememberMe });
      }
      window.location.assign(returnUrl === '/' ? storeUrl : returnUrl);
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
          <p>完成身份验证后，将安全返回发起请求的应用。</p>
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
              placeholder="13800138000"
              autoComplete="tel-national"
              maxLength={16}
              required
              leading={<CountryCodeSelect value={countryCode} onChange={setCountryCode} />}
              error={errors.phoneNumber}
            />
            <CaptchaField id="login-captcha" state={captcha} error={errors.captcha} disabled={sendingCode || submitting} />
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
              未注册的手机号验证后将自动创建账户。已有邮箱账户？请先
              <button type="button" onClick={() => switchMode('password')}>用邮箱登录</button>
              ，在个人资料中绑定手机号
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
          {mode === 'password' ? <Link to={`/forgot-password?${new URLSearchParams({ returnUrl })}`}>忘记密码？</Link> : <span />}
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

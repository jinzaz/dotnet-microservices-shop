import { useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { captchaErrorMessage, requestPasswordResetCode, resetPassword } from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { CaptchaField, useCaptcha } from '../components/CaptchaField';
import { CountryCodeSelect, defaultCountryCode, toE164 } from '../components/CountryCodeSelect';
import { FormField } from '../components/FormField';
import { Icon } from '../components/Icon';
import { authRoute, getSafeReturnUrl } from '../return-url';

const codePattern = /^[0-9]{6}$/;
const uppercasePattern = /[A-Z]/;
const lowercasePattern = /[a-z]/;
const digitPattern = /\d/;
const symbolPattern = /[\p{P}\p{S}]/u;

type ResetErrors = Partial<Record<'phoneNumber' | 'captcha' | 'code' | 'password' | 'confirmPassword' | 'form', string>>;

export default function ForgotPasswordPage() {
  const { search } = useLocation();
  const returnUrl = getSafeReturnUrl(search);
  const [countryCode, setCountryCode] = useState(defaultCountryCode);
  const [phoneNumber, setPhoneNumber] = useState('');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [codeRequested, setCodeRequested] = useState(false);
  const [sendingCode, setSendingCode] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [complete, setComplete] = useState(false);
  const [notice, setNotice] = useState('');
  const [errors, setErrors] = useState<ResetErrors>({});
  const captcha = useCaptcha(true);

  const sendCode = async () => {
    const phone = toE164(countryCode, phoneNumber);
    if (!phone) {
      setErrors({ phoneNumber: '请输入有效的手机号。' });
      return;
    }
    if (!captcha.captcha || !captcha.answer) {
      setErrors({ captcha: '请先输入图形验证码的计算结果。' });
      return;
    }

    setSendingCode(true);
    setErrors({});
    try {
      const result = await requestPasswordResetCode(phone, {
        captchaId: captcha.captcha.captchaId,
        captchaAnswer: captcha.answer,
      });
      setCodeRequested(true);
      setNotice(`若该号码已绑定并验证，重置码将在 ${Math.ceil(result.expiresInSeconds / 60)} 分钟内送达。`);
    } catch (error) {
      const captchaError = captchaErrorMessage(error);
      setErrors(captchaError
        ? { captcha: captchaError }
        : { form: error instanceof Error ? error.message : '验证码发送请求没有完成。' });
    } finally {
      setSendingCode(false);
      // 每张图形验证码只能用一次，不管成败都换一张。
      void captcha.refresh();
    }
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const phone = toE164(countryCode, phoneNumber);
    const nextErrors: ResetErrors = {};
    if (!phone) nextErrors.phoneNumber = '请输入有效的手机号。';
    if (!codePattern.test(code)) nextErrors.code = '请输入 6 位数字验证码。';
    if (
      password.length < 10
      || !uppercasePattern.test(password)
      || !lowercasePattern.test(password)
      || !digitPattern.test(password)
      || !symbolPattern.test(password)
    ) nextErrors.password = '密码需至少 10 位，并包含大小写字母、数字和符号。';
    if (confirmPassword !== password) nextErrors.confirmPassword = '两次输入的密码不一致。';
    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      await resetPassword({ phoneNumber: phone!, code, newPassword: password });
      setComplete(true);
    } catch (error) {
      setErrors({ form: error instanceof Error ? error.message : '密码重置没有完成。' });
    } finally {
      setSubmitting(false);
    }
  };

  if (complete) {
    return (
      <AuthLayout>
        <section className="login-form">
          <div className="consent-form__mark"><Icon name="check" /></div>
          <div className="login-form__heading"><h1>密码已更新</h1><p>旧会话已撤销，请使用新密码重新登录。</p></div>
          <Link className="button login-submit" to={authRoute('/login', returnUrl)}>返回登录</Link>
        </section>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout>
      <form className="login-form register-form" onSubmit={submit} noValidate>
        <div className="login-form__heading"><h1>重置密码</h1><p>使用账户中已验证的手机号完成安全校验。</p></div>
        <FormField
          id="reset-phone"
          label="已验证手机号"
          type="tel"
          value={phoneNumber}
          onChange={(event) => setPhoneNumber(event.target.value)}
          placeholder="13800138000"
          autoComplete="tel-national"
          maxLength={16}
          required
          leading={<CountryCodeSelect value={countryCode} onChange={setCountryCode} disabled={sendingCode || submitting} />}
          trailing={<button className="sms-code-button" type="button" disabled={sendingCode || submitting} onClick={() => void sendCode()}>{sendingCode ? '发送中…' : codeRequested ? '重新发送' : '获取重置码'}</button>}
          error={errors.phoneNumber}
        />
        <CaptchaField id="reset-captcha" state={captcha} error={errors.captcha} disabled={sendingCode || submitting} />
        {codeRequested ? <>
          <FormField
            id="reset-code"
            label="短信验证码"
            value={code}
            onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
            autoComplete="one-time-code"
            inputMode="numeric"
            maxLength={6}
            required
            leading={<Icon name="shield" />}
            error={errors.code}
          />
          <FormField id="reset-password" label="新密码" type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" minLength={10} maxLength={128} required leading={<Icon name="lock" />} error={errors.password} />
          <FormField id="reset-confirm-password" label="确认新密码" type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} autoComplete="new-password" minLength={10} maxLength={128} required leading={<Icon name="lock" />} error={errors.confirmPassword} />
        </> : null}
        {notice ? <p className="form-message form-message--success" role="status">{notice}{import.meta.env.DEV ? '开发环境验证码：123456。' : ''}</p> : null}
        {errors.form ? <p className="form-message form-message--error" role="alert">{errors.form}</p> : null}
        {codeRequested ? <Button className="login-submit" type="submit" loading={submitting}>更新密码</Button> : null}
        <p className="login-register"><Link to={authRoute('/login', returnUrl)}>返回登录</Link></p>
      </form>
    </AuthLayout>
  );
}

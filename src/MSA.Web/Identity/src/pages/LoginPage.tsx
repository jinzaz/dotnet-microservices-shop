import { useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ApiError, login } from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { FormField } from '../components/FormField';
import { Icon } from '../components/Icon';
import { storeUrl } from '../config';
import { authRoute, getSafeReturnUrl } from '../return-url';

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type LoginErrors = Partial<Record<'email' | 'password' | 'form', string>>;

function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 401) {
    return '邮箱或密码不正确，或账户暂时不可用。';
  }

  return error instanceof Error && error.message
    ? error.message
    : '登录没有完成，请稍后重试。';
}

export default function LoginPage() {
  const { search } = useLocation();
  const returnUrl = getSafeReturnUrl(search);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<LoginErrors>({});

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedEmail = email.trim();
    const nextErrors: LoginErrors = {};

    if (!emailPattern.test(normalizedEmail)) nextErrors.email = '请输入有效的邮箱地址。';
    if (!password) nextErrors.password = '请输入密码。';
    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      await login({ email: normalizedEmail, password, rememberMe });
      window.location.assign(returnUrl);
    } catch (error) {
      setErrors({ form: loginErrorMessage(error) });
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
        <FormField
          id="login-email"
          label="邮箱地址"
          type="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          placeholder="name@example.com"
          autoComplete="email"
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
          <a href={`${storeUrl}/forgot-password`}>忘记密码？</a>
        </div>
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

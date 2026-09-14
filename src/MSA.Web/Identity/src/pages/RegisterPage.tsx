import { useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ApiError, registerAndLogin } from '../auth-api';
import { AuthLayout } from '../components/AuthLayout';
import { Button } from '../components/Button';
import { FormField } from '../components/FormField';
import { Icon } from '../components/Icon';
import { storeUrl } from '../config';
import { authRoute, getSafeReturnUrl } from '../return-url';

type RegisterErrors = Partial<Record<'displayName' | 'email' | 'password' | 'confirmPassword' | 'form', string>>;

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const uppercasePattern = /[A-Z]/;
const lowercasePattern = /[a-z]/;
const digitPattern = /\d/;
const symbolPattern = /[\p{P}\p{S}]/u;

function registrationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const codes = Object.keys(error.problem?.errors ?? {});
    if (codes.some((code) => code === 'DuplicateEmail' || code === 'DuplicateUserName')) {
      return '该邮箱已经注册，请直接登录或更换邮箱。';
    }
  }

  return error instanceof Error && error.message
    ? error.message
    : '注册没有完成，请稍后重试。';
}

export default function RegisterPage() {
  const { search } = useLocation();
  const returnUrl = getSafeReturnUrl(search);
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<RegisterErrors>({});

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedName = displayName.trim();
    const normalizedEmail = email.trim();
    const nextErrors: RegisterErrors = {};

    if (!normalizedName) nextErrors.displayName = '请输入姓名或昵称。';
    if (!emailPattern.test(normalizedEmail)) nextErrors.email = '请输入有效的邮箱地址。';
    if (
      password.length < 10
      || !uppercasePattern.test(password)
      || !lowercasePattern.test(password)
      || !digitPattern.test(password)
      || !symbolPattern.test(password)
    ) {
      nextErrors.password = '密码需至少 10 位，并包含大小写字母、数字和符号。';
    }
    if (!confirmPassword) nextErrors.confirmPassword = '请再次输入密码。';
    else if (confirmPassword !== password) nextErrors.confirmPassword = '两次输入的密码不一致。';

    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      await registerAndLogin({ displayName: normalizedName, email: normalizedEmail, password });
      window.location.assign(returnUrl === '/' ? storeUrl : returnUrl);
    } catch (error) {
      setErrors({ form: registrationErrorMessage(error) });
      setSubmitting(false);
    }
  };

  const passwordToggle = (
    <button
      className="field-icon-button"
      type="button"
      onClick={() => setPasswordVisible((visible) => !visible)}
      aria-label={passwordVisible ? '隐藏密码' : '显示密码'}
    >
      <Icon name={passwordVisible ? 'eye-off' : 'eye'} />
    </button>
  );

  return (
    <AuthLayout>
      <form className="login-form register-form" onSubmit={submit} noValidate>
        <div className="login-form__heading">
          <h1>创建账户</h1>
          <p>使用一个统一账户安全访问 MSA 应用与服务。</p>
        </div>
        <FormField
          id="register-name"
          label="姓名或昵称"
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          placeholder="请输入姓名或昵称"
          autoComplete="name"
          maxLength={100}
          required
          leading={<Icon name="user" />}
          error={errors.displayName}
        />
        <FormField
          id="register-email"
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
          id="register-password"
          label="密码"
          type={passwordVisible ? 'text' : 'password'}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          placeholder="至少 10 位安全密码"
          autoComplete="new-password"
          minLength={10}
          maxLength={128}
          required
          leading={<Icon name="lock" />}
          trailing={passwordToggle}
          aria-describedby="register-password-hint"
          error={errors.password}
        />
        <p className="password-hint" id="register-password-hint">需包含大写字母、小写字母、数字和符号。</p>
        <FormField
          id="register-confirm-password"
          label="确认密码"
          type={passwordVisible ? 'text' : 'password'}
          value={confirmPassword}
          onChange={(event) => setConfirmPassword(event.target.value)}
          placeholder="再次输入密码"
          autoComplete="new-password"
          minLength={10}
          maxLength={128}
          required
          leading={<Icon name="lock" />}
          error={errors.confirmPassword}
        />
        {errors.form ? <p className="form-message form-message--error" role="alert">{errors.form}</p> : null}
        <Button className="login-submit" type="submit" loading={submitting}>创建并登录</Button>
        <p className="login-register">
          已有账户？ <Link to={authRoute('/login', returnUrl)}>返回登录</Link>
        </p>
        <p className="login-trust"><Icon name="shield" />账户安全由 MSA Identity 提供</p>
      </form>
    </AuthLayout>
  );
}

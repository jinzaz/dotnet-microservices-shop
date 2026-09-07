import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { getErrorMessage } from '../../shared/api/api-error';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { Icon } from '../../shared/ui/Icon';
import type { LoginCredentials } from './types';

type LoginFormProps = {
  onSubmit: (credentials: LoginCredentials) => Promise<void>;
  registerTo?: string;
};

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function LoginForm({ onSubmit, registerTo = '/register' }: LoginFormProps) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<{ email?: string; password?: string; form?: string }>({});

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const nextErrors: typeof errors = {};
    if (!emailPattern.test(email.trim())) nextErrors.email = '请输入有效的邮箱地址。';
    if (!password) nextErrors.password = '请输入密码。';
    if (Object.keys(nextErrors).length) {
      setErrors(nextErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      await onSubmit({ email: email.trim(), password, rememberMe });
    } catch (error) {
      setErrors({ form: getErrorMessage(error, '登录失败，请稍后重试。') });
    } finally {
      setSubmitting(false);
    }
  };

  return (
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
          <input type="checkbox" checked={rememberMe} onChange={(event) => setRememberMe(event.target.checked)} />
          <span><Icon name="check" /></span>
          保持登录
        </label>
        <Link to="/forgot-password">忘记密码？</Link>
      </div>
      {errors.form ? <p className="form-message form-message--error" role="alert">{errors.form}</p> : null}
      <Button className="login-submit" type="submit" loading={submitting}>登录</Button>
      <div className="login-divider"><span>或</span></div>
      <p className="login-register">还没有账户？ <Link to={registerTo}>创建账户</Link></p>
      <p className="login-trust"><Icon name="shield" />安全登录由 MSA Identity 提供</p>
    </form>
  );
}

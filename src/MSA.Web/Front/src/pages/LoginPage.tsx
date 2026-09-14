import { useEffect } from 'react';
import { Navigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { AuthPageLayout } from '../features/auth/AuthPageLayout';

function safeRedirect(value: string | null): string {
  return value?.startsWith('/') && !value.startsWith('//') ? value : '/account';
}

export default function LoginPage() {
  const { isAuthenticated, isLoading, login } = useAuth();
  const [searchParams] = useSearchParams();
  const redirect = safeRedirect(searchParams.get('redirect'));
  const authError = searchParams.get('authError');

  useEffect(() => {
    if (!isLoading && !isAuthenticated && !authError) login(redirect);
  }, [authError, isAuthenticated, isLoading, login, redirect]);

  if (isAuthenticated) return <Navigate to={redirect} replace />;

  return (
    <AuthPageLayout>
      <div className="login-form" role="status">
        <div className="login-form__heading">
          <h1>{authError ? '登录未完成' : '正在安全登录'}</h1>
          <p>{authError === 'access_denied'
            ? '您已取消本次授权，商城会话未建立。'
            : authError
              ? '统一身份服务未能完成登录，请重新尝试。'
              : '即将前往 MSA Identity 完成身份验证。'}</p>
        </div>
        {authError
          ? <button className="button button--primary login-submit" type="button" onClick={() => login(redirect)}>重新登录</button>
          : <div className="route-fallback"><span className="page-spinner" />正在连接统一身份服务…</div>}
      </div>
    </AuthPageLayout>
  );
}

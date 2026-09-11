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

  useEffect(() => {
    if (!isLoading && !isAuthenticated) login(redirect);
  }, [isAuthenticated, isLoading, login, redirect]);

  if (isAuthenticated) return <Navigate to={redirect} replace />;

  return (
    <AuthPageLayout>
      <div className="login-form" role="status">
        <div className="login-form__heading">
          <h1>正在安全登录</h1>
          <p>即将前往 MSA Identity 完成身份验证。</p>
        </div>
        <div className="route-fallback"><span className="page-spinner" />正在连接统一身份服务…</div>
      </div>
    </AuthPageLayout>
  );
}

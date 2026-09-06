import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { LoginForm } from '../features/auth/LoginForm';
import type { LoginCredentials } from '../features/auth/types';
import { MinimalHeader } from '../shared/layout/SiteHeader';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { SiteFooter } from '../shared/layout/SiteFooter';

function safeRedirect(value: string | null): string {
  return value?.startsWith('/') && !value.startsWith('//') ? value : '/account';
}

export default function LoginPage() {
  const { isAuthenticated, login } = useAuth();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  if (isAuthenticated) return <Navigate to="/account" replace />;

  const handleLogin = async (credentials: LoginCredentials) => {
    await login(credentials);
    navigate(safeRedirect(searchParams.get('redirect')), { replace: true });
  };

  return (
    <div className="login-page">
      <MinimalHeader />
      <main className="login-layout">
        <div className="login-media">
          <img
            src="/images/hero-lifestyle.jpg"
            alt="自然光下的手冲壶、陶瓷杯与绿植"
            width="1672"
            height="941"
          />
        </div>
        <div className="login-panel">
          <LoginForm onSubmit={handleLogin} />
        </div>
      </main>
      <ServicePromises />
      <SiteFooter />
    </div>
  );
}

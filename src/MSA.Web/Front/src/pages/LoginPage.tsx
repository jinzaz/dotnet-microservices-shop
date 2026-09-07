import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { AuthPageLayout } from '../features/auth/AuthPageLayout';
import { LoginForm } from '../features/auth/LoginForm';
import type { LoginCredentials } from '../features/auth/types';

function safeRedirect(value: string | null): string {
  return value?.startsWith('/') && !value.startsWith('//') ? value : '/account';
}

export default function LoginPage() {
  const { isAuthenticated, login } = useAuth();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const redirect = safeRedirect(searchParams.get('redirect'));

  if (isAuthenticated) return <Navigate to="/account" replace />;

  const handleLogin = async (credentials: LoginCredentials) => {
    await login(credentials);
    navigate(redirect, { replace: true });
  };

  return (
    <AuthPageLayout>
      <LoginForm
        onSubmit={handleLogin}
        registerTo={`/register?redirect=${encodeURIComponent(redirect)}`}
      />
    </AuthPageLayout>
  );
}

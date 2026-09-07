import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { AuthPageLayout } from '../features/auth/AuthPageLayout';
import { RegisterForm } from '../features/auth/RegisterForm';
import type { RegistrationDetails } from '../features/auth/types';

function safeRedirect(value: string | null): string {
  return value?.startsWith('/') && !value.startsWith('//') ? value : '/account';
}

export default function RegisterPage() {
  const { isAuthenticated, register } = useAuth();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const redirect = safeRedirect(searchParams.get('redirect'));

  if (isAuthenticated) return <Navigate to="/account" replace />;

  const handleRegister = async (details: RegistrationDetails) => {
    await register(details);
    navigate(redirect, { replace: true });
  };

  return (
    <AuthPageLayout>
      <RegisterForm
        onSubmit={handleRegister}
        loginTo={`/login?redirect=${encodeURIComponent(redirect)}`}
      />
    </AuthPageLayout>
  );
}

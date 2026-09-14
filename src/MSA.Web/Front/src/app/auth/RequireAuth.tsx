import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { isDemoMode } from '../../shared/config/api';
import { useAuth } from './AuthProvider';

export function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();

  if (isDemoMode(location.search)) return children;
  if (isLoading) {
    return <div className="route-fallback" role="status"><span className="page-spinner" />正在确认登录状态…</div>;
  }
  if (isAuthenticated) return children;

  const returnSearch = new URLSearchParams(location.search);
  const authError = returnSearch.get('authError');
  returnSearch.delete('authError');
  const returnUrl = `${location.pathname}${returnSearch.size ? `?${returnSearch}` : ''}${location.hash}`;
  const loginSearch = new URLSearchParams({ redirect: returnUrl });
  if (authError) loginSearch.set('authError', authError);

  return <Navigate to={`/login?${loginSearch}`} replace state={{ from: location }} />;
}

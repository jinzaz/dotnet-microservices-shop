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

  const redirect = encodeURIComponent(`${location.pathname}${location.search}${location.hash}`);
  return <Navigate to={`/login?redirect=${redirect}`} replace state={{ from: location }} />;
}

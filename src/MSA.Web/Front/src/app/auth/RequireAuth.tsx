import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { isDemoMode } from '../../shared/config/api';
import { useAuth } from './AuthProvider';

export function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const location = useLocation();

  if (isAuthenticated || isDemoMode(location.search)) return children;

  const redirect = encodeURIComponent(`${location.pathname}${location.search}${location.hash}`);
  return <Navigate to={`/login?redirect=${redirect}`} replace state={{ from: location }} />;
}

import { useQuery } from '@tanstack/react-query';
import {
  createContext,
  useContext,
  useMemo,
  type ReactNode,
} from 'react';
import {
  beginLogin,
  hasBffSession,
  signOut,
} from '../../features/auth/auth-session';

type AuthContextValue = {
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (returnUrl?: string) => void;
  logout: (returnUrl?: string) => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const sessionQuery = useQuery({
    queryKey: ['bff', 'user-session'],
    queryFn: hasBffSession,
    retry: false,
    staleTime: 30_000,
  });
  const isAuthenticated = sessionQuery.data === true;
  const isLoading = sessionQuery.isPending;

  const value = useMemo(
    () => ({ isAuthenticated, isLoading, login: beginLogin, logout: signOut }),
    [isAuthenticated, isLoading],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider.');
  return context;
}

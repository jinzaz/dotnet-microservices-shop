import { useQuery } from '@tanstack/react-query';
import {
  createContext,
  useContext,
  useMemo,
  type ReactNode,
} from 'react';
import {
  beginLogin,
  getBffSession,
  signOut,
  type BffSession,
} from '../../features/auth/auth-session';

type AuthContextValue = {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: BffSession | null;
  login: (returnUrl?: string) => void;
  logout: (returnUrl?: string) => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const sessionQuery = useQuery({
    queryKey: ['bff', 'user-session'],
    queryFn: getBffSession,
    retry: false,
    staleTime: 30_000,
  });
  const user = sessionQuery.data?.isAuthenticated ? sessionQuery.data : null;
  const isLoading = sessionQuery.isPending;

  const value = useMemo(
    () => ({ isAuthenticated: user !== null, isLoading, user, login: beginLogin, logout: signOut }),
    [user, isLoading],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider.');
  return context;
}

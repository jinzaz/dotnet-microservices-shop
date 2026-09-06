import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import {
  clearAuthSession,
  hasAuthSession,
  signIn as establishSession,
  subscribeToAuthSession,
} from '../../features/auth/auth-session';
import type { LoginCredentials } from '../../features/auth/types';

type AuthContextValue = {
  isAuthenticated: boolean;
  login: (credentials: LoginCredentials) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState(hasAuthSession);

  useEffect(
    () => subscribeToAuthSession(() => setIsAuthenticated(hasAuthSession())),
    [],
  );

  const login = useCallback(async (credentials: LoginCredentials) => {
    await establishSession(credentials);
  }, []);

  const logout = useCallback(() => {
    clearAuthSession();
  }, []);

  const value = useMemo(
    () => ({ isAuthenticated, login, logout }),
    [isAuthenticated, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider.');
  return context;
}

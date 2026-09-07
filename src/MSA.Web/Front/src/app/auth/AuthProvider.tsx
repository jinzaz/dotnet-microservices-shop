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
import { registerAccount } from '../../features/auth/auth-api';
import type { LoginCredentials, RegistrationDetails } from '../../features/auth/types';

type AuthContextValue = {
  isAuthenticated: boolean;
  login: (credentials: LoginCredentials) => Promise<void>;
  register: (details: RegistrationDetails) => Promise<void>;
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

  const register = useCallback(async (details: RegistrationDetails) => {
    await registerAccount(details);
    await establishSession({
      email: details.email,
      password: details.password,
      rememberMe: true,
    });
  }, []);

  const logout = useCallback(() => {
    clearAuthSession();
  }, []);

  const value = useMemo(
    () => ({ isAuthenticated, login, register, logout }),
    [isAuthenticated, login, register, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider.');
  return context;
}

import { useQuery } from '@tanstack/react-query';
import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { api, beginLogin, getSession, signOut, type BffSession, type EffectivePermissions } from './api';

type SessionContextValue = {
  session: BffSession;
  permissions: string[];
  can: (code: string) => boolean;
  canAccess: (module: string) => boolean;
  logout: () => Promise<void>;
};

const SessionContext = createContext<SessionContextValue | null>(null);

function Splash({ message }: { message: string }) {
  return (
    <main className="splash">
      <div className="splash__mark"><span /><span /><span /></div>
      <p>{message}</p>
    </main>
  );
}

function LoginGate() {
  const authError = new URLSearchParams(window.location.search).get('authError');
  const loginMessage = authError === 'access_denied'
    ? '您已取消本次授权，未建立后台会话。'
    : authError
      ? '安全登录未完成，请重新发起。'
      : '通过 Authorization Code + PKCE 进入商城管理中枢。业务令牌仅保存在 BFF 服务端。';

  return (
    <main className="login-gate">
      <div className="login-gate__glow" />
      <section>
        <div className="brand-mark" aria-hidden="true"><span /><span /><span /></div>
        <p className="eyebrow">MSA BUSINESS MANAGER</p>
        <h1>让复杂运营，<br />保持清醒。</h1>
        <p role={authError ? 'alert' : undefined}>{loginMessage}</p>
        <button className="button button--accent" type="button" onClick={beginLogin}>{authError ? '重新登录' : '安全登录'} <span>↗</span></button>
      </section>
      <aside aria-label="安全说明">
        <span>PBAC · DENY BY DEFAULT</span>
        <strong>每一次业务请求，都由 BM 权限表决定是否放行。</strong>
      </aside>
    </main>
  );
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const sessionQuery = useQuery({ queryKey: ['bff', 'session'], queryFn: getSession, retry: false });
  const permissionsQuery = useQuery({
    queryKey: ['bff', 'permissions'],
    queryFn: () => api<EffectivePermissions>('/bff/pbac/me/permissions'),
    enabled: Boolean(sessionQuery.data),
    retry: false,
  });

  const permissions = permissionsQuery.data?.permissions ?? [];
  const value = useMemo<SessionContextValue | null>(() => sessionQuery.data ? ({
    session: sessionQuery.data,
    permissions,
    can: (code) => permissions.includes(code),
    canAccess: (module) => permissions.some((code) => code.startsWith(`${module}.`)),
    logout: signOut,
  }) : null, [permissions, sessionQuery.data]);

  if (sessionQuery.isPending) return <Splash message="正在校验安全会话" />;
  if (sessionQuery.isError) return <Splash message={sessionQuery.error.message} />;
  if (!sessionQuery.data) return <LoginGate />;
  if (permissionsQuery.isPending) return <Splash message="正在装载 BM 权限策略" />;
  if (permissionsQuery.isError) return <Splash message={permissionsQuery.error.message} />;
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession() {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession must be used within SessionProvider.');
  return value;
}

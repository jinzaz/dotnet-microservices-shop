import { ApiError } from '../../shared/api/api-error';
import { bffRequest } from '../../shared/api/http';
import { bffUrl } from '../../shared/config/api';

type CsrfResponse = { token?: string };
export type BffSession = {
  isAuthenticated: boolean;
  subject: string | null;
  name: string | null;
  email: string | null;
};

const anonymousSession: BffSession = { isAuthenticated: false, subject: null, name: null, email: null };

let csrfTokenPromise: Promise<string> | null = null;

export async function getBffSession(): Promise<BffSession> {
  try {
    const session = await bffRequest<Partial<BffSession>>('/bff/user');
    return session.isAuthenticated === true
      ? { isAuthenticated: true, subject: session.subject ?? null, name: session.name ?? null, email: session.email ?? null }
      : anonymousSession;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return anonymousSession;
    throw error;
  }
}

export function beginLogin(returnUrl = '/account'): void {
  const query = new URLSearchParams({ returnUrl });
  window.location.assign(bffUrl(`/bff/login?${query}`));
}

async function requestCsrfToken(): Promise<string> {
  const { token } = await bffRequest<CsrfResponse>('/bff/csrf');
  if (!token) throw new Error('安全校验初始化失败，请刷新页面后重试。');
  return token;
}

export function getCsrfToken(): Promise<string> {
  csrfTokenPromise ??= requestCsrfToken().catch((error: unknown) => {
    csrfTokenPromise = null;
    throw error;
  });
  return csrfTokenPromise;
}

export async function signOut(returnUrl = '/'): Promise<void> {
  const token = await getCsrfToken();
  const query = new URLSearchParams({ returnUrl });
  const form = document.createElement('form');
  const field = document.createElement('input');

  form.method = 'post';
  form.action = bffUrl(`/bff/logout?${query}`);
  form.hidden = true;
  field.type = 'hidden';
  field.name = '__RequestVerificationToken';
  field.value = token;
  form.append(field);
  document.body.append(form);
  form.submit();
}

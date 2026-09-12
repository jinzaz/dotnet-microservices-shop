import { toApiError } from '../../shared/api/api-error';
import { bffUrl } from '../../shared/config/api';

type CsrfResponse = { token?: string };
type UserSessionResponse = { isAuthenticated?: boolean };

let csrfTokenPromise: Promise<string> | null = null;

export async function hasBffSession(): Promise<boolean> {
  let response: Response;
  try {
    response = await fetch(bffUrl('/api/bff/user'), {
      credentials: 'include',
      headers: { Accept: 'application/json' },
    });
  } catch {
    throw new Error('暂时无法连接登录服务，请确认 MSA.BFF.Web 已启动。');
  }

  if (response.status === 401) return false;
  if (!response.ok) {
    throw await toApiError(response, '登录状态读取失败，请稍后重试。');
  }

  const session = (await response.json()) as UserSessionResponse;
  return session.isAuthenticated === true;
}

export function beginLogin(returnUrl = '/account'): void {
  const query = new URLSearchParams({ returnUrl });
  window.location.assign(bffUrl(`/api/bff/login?${query}`));
}

async function requestCsrfToken(): Promise<string> {
  let response: Response;
  try {
    response = await fetch(bffUrl('/api/bff/csrf'), {
      credentials: 'include',
      headers: { Accept: 'application/json' },
    });
  } catch {
    throw new Error('暂时无法连接安全会话服务，请确认 MSA.BFF.Web 已启动。');
  }

  if (!response.ok) {
    throw await toApiError(response, '安全校验初始化失败，请稍后重试。');
  }

  const { token } = (await response.json()) as CsrfResponse;
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
  form.action = bffUrl(`/api/bff/logout?${query}`);
  form.hidden = true;
  field.type = 'hidden';
  field.name = '__RequestVerificationToken';
  field.value = token;
  form.append(field);
  document.body.append(form);
  form.submit();
}

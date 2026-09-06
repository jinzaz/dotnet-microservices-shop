import { clearAuthSession, getValidAccessToken } from '../../features/auth/auth-session';
import { toApiError } from './api-error';
import { apiUrl } from '../config/api';

type ApiRequestOptions = Omit<RequestInit, 'body'> & {
  body?: unknown;
  auth?: boolean;
};

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const { body, auth = true, headers: suppliedHeaders, ...requestOptions } = options;
  const headers = new Headers(suppliedHeaders);
  headers.set('Accept', 'application/json');

  if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  const accessToken = auth ? await getValidAccessToken() : null;
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  let response: Response;
  try {
    response = await fetch(apiUrl(path), {
      ...requestOptions,
      credentials: 'include',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new Error('暂时无法连接服务，请确认 API Gateway 已启动。');
  }

  if (!response.ok) {
    if (response.status === 401 && auth) clearAuthSession();
    throw await toApiError(response, '服务请求失败，请稍后重试。');
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

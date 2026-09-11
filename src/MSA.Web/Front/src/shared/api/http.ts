import { getCsrfToken } from '../../features/auth/auth-session';
import { toApiError } from './api-error';
import { apiUrl } from '../config/api';

type ApiRequestOptions = Omit<RequestInit, 'body'> & {
  body?: unknown;
};

function requiresCsrfToken(method: string | undefined): boolean {
  return !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method?.toUpperCase() ?? 'GET');
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const { body, headers: suppliedHeaders, ...requestOptions } = options;
  const headers = new Headers(suppliedHeaders);
  headers.set('Accept', 'application/json');

  if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  if (requiresCsrfToken(requestOptions.method)) {
    headers.set('X-XSRF-TOKEN', await getCsrfToken());
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
    throw new Error('暂时无法连接服务，请确认 MSA.BFF.Web 已启动。');
  }

  if (!response.ok) {
    throw await toApiError(response, '服务请求失败，请稍后重试。');
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

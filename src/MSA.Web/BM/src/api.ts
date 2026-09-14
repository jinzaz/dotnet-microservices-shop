export type BffSession = {
  isAuthenticated: boolean;
  subject: string | null;
  name: string | null;
  email: string | null;
  roles: string[];
};

export type EffectivePermissions = {
  identityUserId: string;
  permissions: string[];
};

type Problem = {
  title?: string;
  detail?: string;
  errors?: Record<string, string[] | string>;
};

type RequestOptions = Omit<RequestInit, 'body'> & { body?: unknown };

const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? '';
const baseUrl = configuredBaseUrl.replace(/\/$/, '');
let csrfPromise: Promise<string> | undefined;

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

function absolute(path: string) {
  return `${baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
}

function firstError(problem?: Problem) {
  if (!problem?.errors) return undefined;
  for (const value of Object.values(problem.errors)) {
    const message = Array.isArray(value) ? value[0] : value;
    if (message) return message;
  }
  return undefined;
}

async function readProblem(response: Response): Promise<Problem | undefined> {
  if (!response.headers.get('content-type')?.includes('json')) return undefined;
  try {
    return await response.json() as Problem;
  } catch {
    return undefined;
  }
}

async function csrfToken() {
  csrfPromise ??= fetch(absolute('/bff/csrf'), {
    credentials: 'include',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  }).then(async (response) => {
    if (!response.ok) throw new ApiError(response.status, '无法建立安全会话，请刷新后重试。');
    const result = await response.json() as { token?: string };
    if (!result.token) throw new Error('BFF 未返回防伪令牌。');
    return result.token;
  }).catch((error: unknown) => {
    csrfPromise = undefined;
    throw error;
  });
  return csrfPromise;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method?.toUpperCase() ?? 'GET';
  const headers = new Headers(options.headers);
  headers.set('Accept', 'application/json');
  if (options.body !== undefined) headers.set('Content-Type', 'application/json');
  if (!['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method)) {
    headers.set('X-XSRF-TOKEN', await csrfToken());
  }

  let response: Response;
  try {
    response = await fetch(absolute(path), {
      ...options,
      method,
      credentials: 'include',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  } catch {
    throw new Error('暂时无法连接 MSA.BFF.BM，请确认服务已启动。');
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      response.status,
      firstError(problem) ?? problem?.detail ?? problem?.title ?? `请求失败（${response.status}）`,
    );
  }
  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

export async function getSession(): Promise<BffSession | null> {
  try {
    return await api<BffSession>('/bff/user');
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null;
    throw error;
  }
}

export function beginLogin() {
  const search = new URLSearchParams(window.location.search);
  search.delete('authError');
  const returnUrl = `${window.location.pathname}${search.size ? `?${search}` : ''}`;
  window.location.assign(absolute(`/bff/login?${new URLSearchParams({ returnUrl })}`));
}

export async function signOut() {
  const token = await csrfToken();
  const form = document.createElement('form');
  const field = document.createElement('input');
  form.method = 'post';
  form.action = absolute(`/bff/logout?${new URLSearchParams({ returnUrl: '/' })}`);
  form.hidden = true;
  field.type = 'hidden';
  field.name = '__RequestVerificationToken';
  field.value = token;
  form.append(field);
  document.body.append(form);
  form.submit();
}

export function idempotencyKey() {
  return crypto.randomUUID();
}

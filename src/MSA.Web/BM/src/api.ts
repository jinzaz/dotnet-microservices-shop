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

export type ObjectUpload = {
  objectKey: string;
  storageUri: string;
  publicUrl: string | null;
  uploadUrl: string;
  expiresOnUtc: string;
  headers: Record<string, string>;
};

export type VerifiedObject = {
  objectKey: string;
  publicUrl: string | null;
  contentType: string;
  contentLength: number;
};

export type Paged<T> = { items: T[]; skip: number; take: number; total: number };

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

const statusMessages: Record<number, string> = {
  401: '登录已过期，请刷新页面重新登录。',
  403: '当前 BM 身份没有执行该操作的权限。',
  404: '请求的资源不存在或已被删除。',
  409: '数据已被他人修改或状态已变化，请刷新后重试。',
  412: '数据版本已变化，请刷新后重试。',
  429: '操作过于频繁，请稍后再试。',
};

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
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new Error('暂时无法连接 MSA.BFF.BM，请确认服务已启动。');
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      response.status,
      firstError(problem) ?? problem?.detail ?? statusMessages[response.status] ?? problem?.title ?? `请求失败（${response.status}）`,
    );
  }
  if (response.status === 204 || response.status === 202 && !response.headers.get('content-type')?.includes('json')) {
    return undefined as T;
  }
  return await response.json() as T;
}

/** Builds a query string, dropping empty values so optional filters are omitted. */
export function query(values: Record<string, string | number | boolean | null | undefined>) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  return params.toString();
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

/** Mirrors the BFF upload policy so bad files are rejected before a signed URL is requested. */
export const catalogMediaPolicy = {
  accept: 'image/jpeg,image/png,image/webp,image/gif,image/avif,video/mp4,video/webm,video/quicktime',
  imageMaxBytes: 20 * 1024 * 1024,
  videoMaxBytes: 512 * 1024 * 1024,
};

export function validateCatalogMedia(file: File): string | null {
  if (!catalogMediaPolicy.accept.split(',').includes(file.type)) return `“${file.name}” 的格式不受支持。`;
  const limit = file.type.startsWith('video/') ? catalogMediaPolicy.videoMaxBytes : catalogMediaPolicy.imageMaxBytes;
  if (file.size === 0) return `“${file.name}” 是空文件。`;
  if (file.size > limit) return `“${file.name}” 超过 ${formatBytes(limit)} 上限。`;
  return null;
}

export function formatBytes(value: number) {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

function putWithProgress(url: string, file: File, headers: Record<string, string>, onProgress?: (ratio: number) => void, signal?: AbortSignal) {
  // fetch() cannot report upload progress, so the direct object-storage PUT uses XMLHttpRequest.
  return new Promise<void>((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('PUT', url);
    for (const [key, value] of Object.entries(headers)) request.setRequestHeader(key, value);
    request.upload.onprogress = (event) => { if (event.lengthComputable) onProgress?.(event.loaded / event.total); };
    request.onload = () => request.status >= 200 && request.status < 300
      ? resolve()
      : reject(new Error(`对象上传失败（${request.status}）`));
    request.onerror = () => reject(new Error('无法上传到对象存储，请检查 RustFS 网络与跨域配置。'));
    request.onabort = () => reject(new DOMException('上传已取消。', 'AbortError'));
    signal?.addEventListener('abort', () => request.abort(), { once: true });
    request.send(file);
  });
}

/** Best-effort cleanup for catalog objects that are no longer (or never were) referenced by Catalog. */
export async function discardCatalogMedia(target: { objectKey?: string; publicUrl?: string }) {
  try {
    await api<void>('/bff/object-storage/catalog-media/discard', { method: 'POST', body: target });
  } catch {
    // An orphaned object is a storage cost, not a user-facing failure.
  }
}

/**
 * Signs, uploads, and verifies one catalog media file. The returned public URL is only handed back after the BFF has
 * confirmed the stored object matches the declared size and type.
 */
export async function uploadCatalogMedia(file: File, onProgress?: (ratio: number) => void, signal?: AbortSignal): Promise<VerifiedObject> {
  const invalid = validateCatalogMedia(file);
  if (invalid) throw new Error(invalid);

  const ticket = await api<ObjectUpload>('/bff/object-storage/catalog-media/upload-url', {
    method: 'POST',
    body: { fileName: file.name, contentType: file.type, contentLength: file.size },
  });
  try {
    await putWithProgress(ticket.uploadUrl, file, ticket.headers, onProgress, signal);
    const verified = await api<VerifiedObject>('/bff/object-storage/catalog-media/verify', {
      method: 'POST',
      body: { objectKey: ticket.objectKey, contentType: file.type, contentLength: file.size },
    });
    if (!verified.publicUrl) throw new Error('对象存储未返回商品媒体访问地址。');
    return verified;
  } catch (error) {
    await discardCatalogMedia({ objectKey: ticket.objectKey });
    throw error;
  }
}

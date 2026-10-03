import { getCsrfToken } from '../../features/auth/auth-session';
import { toApiError } from './api-error';
import { apiUrl, bffUrl } from '../config/api';
import {
  createBodyEncryptionContext,
  decryptResponseBody,
  encryptRequestBody,
  ephemeralKeyHeaderValue,
  ENCRYPTED_CONTENT_TYPE,
  EPHEMERAL_KEY_HEADER,
  type BodyEncryptionContext,
} from './body-crypto';

type ApiRequestOptions = Omit<RequestInit, 'body'> & {
  body?: unknown;
};

function requiresCsrfToken(method: string | undefined): boolean {
  return !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method?.toUpperCase() ?? 'GET');
}

/** 转发到网关/下游服务的接口，路径会被加上 /api 前缀（见 shared/config/api.ts 的 apiUrl）。 */
export function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  return sendRequest<T>(apiUrl(path), options);
}

/** BFF 自己处理的接口（登录、csrf、用户信息...），不走 /api 前缀，直接是 /bff/... 。 */
export function bffRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  return sendRequest<T>(bffUrl(path), options);
}

async function sendRequest<T>(url: string, options: ApiRequestOptions): Promise<T> {
  const { body, headers: suppliedHeaders, ...requestOptions } = options;
  const headers = new Headers(suppliedHeaders);
  headers.set('Accept', 'application/json');

  if (requiresCsrfToken(requestOptions.method)) {
    headers.set('X-XSRF-TOKEN', await getCsrfToken());
  }

  // 每次请求用一把临时密钥：没有 body 的请求（GET 之类）也带这个头，响应才能被加密——
  // 密钥协商不依赖请求体是否存在。
  const cryptoContext = await createBodyEncryptionContext();
  headers.set(EPHEMERAL_KEY_HEADER, ephemeralKeyHeaderValue(cryptoContext));

  let requestBody: BodyInit | undefined;
  if (body !== undefined) {
    const plaintext = new TextEncoder().encode(JSON.stringify(body));
    requestBody = await encryptRequestBody(cryptoContext, plaintext);
    headers.set('Content-Type', ENCRYPTED_CONTENT_TYPE);
  }

  let response: Response;
  try {
    response = await fetch(url, {
      ...requestOptions,
      credentials: 'include',
      headers,
      body: requestBody,
    });
  } catch {
    throw new Error('暂时无法连接服务，请确认 MSA.BFF.Web 已启动。');
  }

  response = await decryptResponseIfNeeded(response, cryptoContext);

  if (!response.ok) {
    throw await toApiError(response, '服务请求失败，请稍后重试。');
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/** BFF 对加密请求的响应也是加密的（包括错误响应），这里统一解密成一个看起来像普通 JSON 响应的 Response，
 * 后面的 toApiError / response.json() 不用关心这一层。没走加密的响应（没带密钥头、或者是被
 * [SkipBodyEncryption] 排除的端点）原样返回。 */
async function decryptResponseIfNeeded(response: Response, context: BodyEncryptionContext): Promise<Response> {
  if (!response.headers.get('Content-Type')?.includes(ENCRYPTED_CONTENT_TYPE)) return response;

  const envelope = await response.arrayBuffer();
  const plaintext = await decryptResponseBody(context, envelope);
  const headers = new Headers(response.headers);
  headers.set('Content-Type', 'application/json');
  return new Response(plaintext, { status: response.status, statusText: response.statusText, headers });
}

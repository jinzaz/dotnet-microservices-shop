import { toApiError } from '../../shared/api/api-error';
import { apiUrl, serviceRoutes } from '../../shared/config/api';
import type { AuthSession, LoginCredentials, TokenResponse } from './types';

const storageKey = 'msa.store.auth.v1';
const refreshLeewayMs = 45_000;
const listeners = new Set<() => void>();
let refreshPromise: Promise<string | null> | null = null;
let sessionEpoch = 0;

function safeRead(storage: Storage): AuthSession | null {
  try {
    const raw = storage.getItem(storageKey);
    return raw ? (JSON.parse(raw) as AuthSession) : null;
  } catch {
    return null;
  }
}

function readSession(): AuthSession | null {
  return safeRead(window.localStorage) ?? safeRead(window.sessionStorage);
}

function notify(): void {
  listeners.forEach((listener) => listener());
}

function persistSession(session: AuthSession): void {
  window.localStorage.removeItem(storageKey);
  window.sessionStorage.removeItem(storageKey);
  const storage = session.rememberMe ? window.localStorage : window.sessionStorage;
  storage.setItem(storageKey, JSON.stringify(session));
  notify();
}

function toSession(tokens: TokenResponse, rememberMe: boolean, previousRefreshToken?: string): AuthSession {
  return {
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token ?? previousRefreshToken,
    tokenType: tokens.token_type || 'Bearer',
    expiresAt: Date.now() + Math.max(tokens.expires_in, 1) * 1000,
    scope: tokens.scope,
    rememberMe,
  };
}

async function requestTokens(parameters: URLSearchParams): Promise<TokenResponse> {
  let response: Response;
  try {
    response = await fetch(apiUrl(`${serviceRoutes.identity}/connect/token`), {
      method: 'POST',
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/x-www-form-urlencoded',
      },
      body: parameters,
    });
  } catch {
    throw new Error('暂时无法连接登录服务，请确认 API Gateway 已启动。');
  }

  if (!response.ok) {
    throw await toApiError(response, '登录服务暂时不可用，请稍后重试。');
  }

  return (await response.json()) as TokenResponse;
}

async function establishIdentityCookie(credentials: LoginCredentials): Promise<void> {
  let response: Response;
  try {
    response = await fetch(apiUrl(`${serviceRoutes.identity}/api/auth/login`), {
      method: 'POST',
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        email: credentials.email,
        password: credentials.password,
        rememberMe: credentials.rememberMe,
      }),
    });
  } catch {
    throw new Error('暂时无法连接登录服务，请确认 API Gateway 已启动。');
  }

  if (!response.ok) {
    throw await toApiError(response, '登录服务暂时不可用，请稍后重试。');
  }
}

export async function signIn(credentials: LoginCredentials): Promise<void> {
  const parameters = new URLSearchParams({
    grant_type: 'password',
    client_id: 'msa-web',
    username: credentials.email,
    password: credentials.password,
    scope: 'openid profile email api offline_access',
  });

  const tokens = await requestTokens(parameters);
  await establishIdentityCookie(credentials);

  persistSession(toSession(tokens, credentials.rememberMe));
}

async function refreshAccessToken(session: AuthSession, epoch: number): Promise<string | null> {
  if (!session.refreshToken) {
    clearAuthSession();
    return null;
  }

  try {
    const tokens = await requestTokens(
      new URLSearchParams({
        grant_type: 'refresh_token',
        client_id: 'msa-web',
        refresh_token: session.refreshToken,
      }),
    );
    if (epoch !== sessionEpoch) return null;
    const refreshed = toSession(tokens, session.rememberMe, session.refreshToken);
    persistSession(refreshed);
    return refreshed.accessToken;
  } catch {
    if (epoch === sessionEpoch) clearAuthSession();
    return null;
  }
}

export async function getValidAccessToken(): Promise<string | null> {
  const session = readSession();
  if (!session) return null;
  if (session.expiresAt - Date.now() > refreshLeewayMs) return session.accessToken;

  const epoch = sessionEpoch;
  refreshPromise ??= refreshAccessToken(session, epoch).finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

export function hasAuthSession(): boolean {
  return readSession() !== null;
}

export function clearAuthSession(): void {
  sessionEpoch += 1;
  window.localStorage.removeItem(storageKey);
  window.sessionStorage.removeItem(storageKey);
  notify();
}

export function subscribeToAuthSession(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

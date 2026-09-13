const fallbackReturnUrl = '/';

export function getSafeReturnUrl(search: string): string {
  const value = new URLSearchParams(search).get('returnUrl');
  if (!value?.startsWith('/') || value.startsWith('//')) return fallbackReturnUrl;

  try {
    return new URL(value, window.location.origin).origin === window.location.origin
      ? value
      : fallbackReturnUrl;
  } catch {
    return fallbackReturnUrl;
  }
}

export function authRoute(path: '/login' | '/register', returnUrl: string): string {
  return `${path}?${new URLSearchParams({ returnUrl })}`;
}

export type ConsentRequest = {
  returnUrl: string;
  clientId: string;
  clientName: string;
  scopes: string[];
};

export function getConsentRequest(search: string): ConsentRequest | null {
  const parameters = new URLSearchParams(search);
  const returnUrl = parameters.get('returnUrl');
  if (!returnUrl?.startsWith('/') || returnUrl.startsWith('//')) return null;

  try {
    const target = new URL(returnUrl, window.location.origin);
    if (target.origin !== window.location.origin || target.pathname !== '/connect/authorize') return null;
  } catch {
    return null;
  }

  const clientId = parameters.get('clientId')?.trim() ?? '';
  return {
    returnUrl,
    clientId,
    clientName: parameters.get('clientName')?.trim() || clientId || '未知应用',
    scopes: [...new Set((parameters.get('scopes') ?? '').split(' ').filter(Boolean))],
  };
}

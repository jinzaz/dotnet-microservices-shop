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
  action: string;
  parameters: Array<[string, string]>;
  clientId: string;
  scopes: string[];
};

export function getConsentRequest(search: string): ConsentRequest | null {
  const parameters = new URLSearchParams(search);
  const returnUrl = parameters.get('returnUrl');
  if (!returnUrl?.startsWith('/') || returnUrl.startsWith('//')) return null;

  try {
    const target = new URL(returnUrl, window.location.origin);
    if (
      target.origin !== window.location.origin
      || target.pathname !== '/connect/authorize'
      || target.hash
    ) return null;

    const clientId = target.searchParams.get('client_id')?.trim() ?? '';
    if (!clientId) return null;

    return {
      action: target.pathname,
      parameters: [...target.searchParams.entries()].filter(([name]) =>
        name.toLowerCase() !== 'consent' && name.toLowerCase() !== '__requestverificationtoken'),
      clientId,
      scopes: [...new Set((target.searchParams.get('scope') ?? '').split(' ').filter(Boolean))],
    };
  } catch {
    return null;
  }
}

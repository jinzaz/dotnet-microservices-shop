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

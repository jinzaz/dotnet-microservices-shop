const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? '';

export const apiBaseUrl = configuredBaseUrl.replace(/\/$/, '');

export const bffUrl = (path: string) =>
  `${apiBaseUrl}${path.startsWith('/') ? path : `/${path}`}`;

export const apiUrl = (path: string) =>
  bffUrl(`/api${path.startsWith('/') ? path : `/${path}`}`);

export const serviceRoutes = {
  identity: '/Auth',
  user: '/user',
  catalog: '/catalog',
  search: '/search',
  basket: '/basket',
  ordering: '/order',
  inventory: '/inventory',
  payment: '/payment',
  notification: '/notification',
  review: '/review',
} as const;

export function isDemoMode(search = window.location.search): boolean {
  const demoEnabled = import.meta.env.VITE_ENABLE_DEMO_MODE !== 'false';
  return import.meta.env.DEV && demoEnabled && new URLSearchParams(search).get('demo') === '1';
}

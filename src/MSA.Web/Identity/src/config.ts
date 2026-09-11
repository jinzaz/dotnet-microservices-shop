const configuredStoreUrl = import.meta.env.VITE_STORE_URL?.trim();

export const storeUrl = (configuredStoreUrl || 'http://localhost:5173').replace(/\/$/, '');

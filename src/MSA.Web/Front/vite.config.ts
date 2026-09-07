import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const gatewayPrefixes = [
  '/identity',
  '/catalog',
  '/basket',
  '/order',
  '/inventory',
  '/payment',
  '/notification',
];

const proxy = Object.fromEntries(
  gatewayPrefixes.map((prefix) => [
    `^${prefix}(?:/|$)`,
    {
      target: 'https://localhost:10000',
      changeOrigin: true,
      secure: false,
    },
  ]),
);

export default defineConfig({
  plugins: [react()],
  resolve: {
    preserveSymlinks: true,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy,
  },
  preview: {
    port: 4173,
    strictPort: true,
  },
});

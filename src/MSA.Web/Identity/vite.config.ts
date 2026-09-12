import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const identityProxy = {
  target: 'https://localhost:10000',
  changeOrigin: true,
  secure: false,
};

const gatewayRoutes = {
  '/api/Auth': identityProxy,
  '/connect': identityProxy,
};

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: gatewayRoutes,
  },
  preview: {
    port: 4174,
    strictPort: true,
    proxy: gatewayRoutes,
  },
});

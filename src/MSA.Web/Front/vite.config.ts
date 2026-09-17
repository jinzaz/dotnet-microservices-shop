import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const bffProxy = {
  target: 'https://localhost:10008',
  changeOrigin: true,
  secure: false,
};

export default defineConfig({
  plugins: [react()],
  resolve: {
    preserveSymlinks: true,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': bffProxy,
      '/bff': bffProxy,
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
    proxy: {
      '/api': bffProxy,
      '/bff': bffProxy,
    },
  },
});

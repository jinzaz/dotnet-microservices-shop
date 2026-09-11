import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const identityProxy = {
  target: 'https://localhost:10002',
  changeOrigin: true,
  secure: false,
};

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': identityProxy,
      '/connect': identityProxy,
    },
  },
  preview: {
    port: 4174,
    strictPort: true,
  },
});

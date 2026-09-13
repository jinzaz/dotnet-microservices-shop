import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const bffProxy = {
  target: 'https://localhost:10009',
  changeOrigin: true,
  secure: false,
};

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5175,
    strictPort: true,
    proxy: { '/api': bffProxy, '/bff': bffProxy },
  },
  preview: {
    port: 4175,
    strictPort: true,
    proxy: { '/api': bffProxy, '/bff': bffProxy },
  },
});

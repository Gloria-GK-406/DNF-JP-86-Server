import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: '/gm/',
  server: { proxy: { '/api/gm': { target: 'http://127.0.0.1:18081', changeOrigin: true } } },
});

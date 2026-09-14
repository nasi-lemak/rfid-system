import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// API calls are proxied to the .NET backend in development; in production set VITE_API_URL.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.API_URL ?? 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: process.env.API_URL ?? 'http://localhost:5080', changeOrigin: true, ws: true },
    },
  },
});

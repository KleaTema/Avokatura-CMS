import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5004',
      // SignalR (task 040): negotiate over HTTP, then the WebSocket upgrade
      '/hubs': { target: 'http://localhost:5004', ws: true },
    },
  },
  test: {
    environment: 'node',
    // unit tests only; e2e/*.spec.ts are Playwright tests (npm run e2e)
    include: ['src/**/*.test.{ts,tsx}'],
  },
})

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
// task 051: the production CSP and headers, built once (also generates docs/deploy/nginx-frontend.conf)
import { buildCsp, otherHeaders } from '../../tools/security/csp.mjs'

const proxy = {
  '/api': 'http://localhost:5004',
  // SignalR (task 040): negotiate over HTTP, then the WebSocket upgrade
  '/hubs': { target: 'http://localhost:5004', ws: true },
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: { proxy },
  // the built app with the production headers (no provider locally: the dev login) — the Playwright CSP check
  // (e2e/csp.config.ts) runs against it; the dev server cannot carry the CSP (its hot reload uses inline scripts)
  preview: {
    proxy,
    headers: { 'Content-Security-Policy': buildCsp([]), ...otherHeaders },
  },
  test: {
    environment: 'node',
    // unit tests only; e2e/*.spec.ts are Playwright tests (npm run e2e)
    include: ['src/**/*.test.{ts,tsx}'],
  },
})

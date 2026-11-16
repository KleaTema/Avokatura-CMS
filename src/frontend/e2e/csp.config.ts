import { defineConfig, devices } from '@playwright/test'

/**
 * Task 051 — the CSP check runs against the BUILT app served by `vite preview` with the production headers
 * (vite.config.ts → tools/security/csp.mjs). The dev server cannot carry the CSP: its hot reload uses inline scripts.
 * `npm run e2e:csp` builds, serves on :4173 (the API on :5004 must be running) and runs e2e/csp.spec.ts.
 */
export default defineConfig({
  testDir: '.',
  testMatch: 'csp.spec.ts',
  timeout: 120_000,
  workers: 1,
  reporter: [['list']],
  globalSetup: './support/global-setup.ts',
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'npm run build && npx vite preview --port 4173 --strictPort',
    cwd: '..',
    url: 'http://localhost:4173/login',
    timeout: 180_000,
    reuseExistingServer: false,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1536, height: 1024 } } }],
})

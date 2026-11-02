import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against the LOCAL API and dev database (see docs/e2e.md). Not part of `npm run build`, `lint` or
 * `test`. One worker: the tests share the dev database and the Kryetar's lists.
 */
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  outputDir: './test-results',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  workers: 1,
  fullyParallel: false,
  reporter: [['list']],
  globalSetup: './support/global-setup.ts',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    locale: 'sq-AL',
    timezoneId: 'Europe/Tirane',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1536, height: 1024 } } }],
})

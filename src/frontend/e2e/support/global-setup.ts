import { API_URL } from './api'

/** Fails fast, with what to start, when the API or the dev server is not running. */
export default async function globalSetup() {
  const baseUrl = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
  const checks: [string, string, string][] = [
    [`${API_URL}/api/identity/me`, 'the API', 'start Host/Ash.Api (dotnet run) on :5004'],
    [baseUrl, 'the frontend', 'run `npm run dev` in src/frontend'],
  ]
  for (const [url, what, how] of checks) {
    try {
      await fetch(url, { headers: { 'X-Dev-User': 'admin@ash.local' } })
    } catch {
      throw new Error(`E2E: ${what} is not reachable at ${url} — ${how}. See docs/e2e.md.`)
    }
  }
}

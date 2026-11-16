/**
 * Task 052 — the frontend's runtime configuration: ONE image for every environment, the values come from
 * /config.json, written by the nginx container's entrypoint from its environment variables at start
 * (deploy/frontend/docker-entrypoint.sh). The identity providers are NOT here: the API is their one source
 * (GET /api/auth/config). Local `npm run dev`: public/config.json (Development defaults).
 *
 * - apiBasePath: where /api and /hubs are ('' = same origin, behind the same proxy — the normal case)
 * - environment: Development | Uat | Production (shown to the user outside Production)
 * - version: the release (footer, support)
 * - cspHosts: the identity hosts the CSP allows (the B2C host given to nginx); null = not checked (local dev)
 */
export interface RuntimeConfig {
  apiBasePath: string
  environment: string
  version: string
  cspHosts: string[] | null
}

const DEFAULTS: RuntimeConfig = {
  apiBasePath: '',
  environment: 'Development',
  version: 'dev',
  cspHosts: null,
}

export let runtimeConfig: RuntimeConfig = DEFAULTS

/** Reads /config.json (never cached by nginx); a missing or broken file keeps the defaults. */
export async function loadRuntimeConfig(): Promise<void> {
  try {
    const response = await fetch('/config.json', { cache: 'no-store' })
    if (!response.ok) return
    const json = (await response.json()) as Partial<RuntimeConfig>
    runtimeConfig = {
      apiBasePath: typeof json.apiBasePath === 'string' ? json.apiBasePath.replace(/\/$/, '') : DEFAULTS.apiBasePath,
      environment: typeof json.environment === 'string' && json.environment ? json.environment : DEFAULTS.environment,
      version: typeof json.version === 'string' && json.version ? json.version : DEFAULTS.version,
      cspHosts: Array.isArray(json.cspHosts) ? json.cspHosts.filter((h): h is string => typeof h === 'string') : null,
    }
  } catch {
    // the defaults
  }
}

/** The hosts an identity provider needs that the CSP does not allow (empty = fine, or not checked). */
export function hostsMissingFromCsp(needed: string[]): string[] {
  const allowed = runtimeConfig.cspHosts
  if (allowed === null) return []
  const set = new Set(allowed.map((h) => h.toLowerCase()))
  return needed.filter((h) => !set.has(h.toLowerCase()))
}

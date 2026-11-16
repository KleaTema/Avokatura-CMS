import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser'
import { hostsMissingFromCsp, loadRuntimeConfig, runtimeConfig } from './runtimeConfig'

/**
 * How the app signs in (task 051), decided by the API (GET /api/auth/config) before the first render:
 * - 'dev': the dev login (X-Dev-User) — the API offers it only in Development;
 * - 'oidc': one or more identity providers (Azure AD B2C, Entra ID), each with its own MSAL PublicClientApplication —
 *   auth code + PKCE, redirect flow, tokens cached in sessionStorage. The provider the user signed in with is
 *   remembered (sessionStorage) for the silent token renewal (API calls, SignalR) and for its logout.
 */
export type AuthMode = 'dev' | 'oidc'

export interface AuthProviderInfo {
  name: string
  kind: string
  displayName: string
  authority: string
  policy: string | null
  knownAuthorities: string[]
  clientId: string
  scopes: string[]
}

const DEV_USER_KEY = 'ash.devUser'
const PROVIDER_KEY = 'ash.provider'

export const devUsers = [
  'admin',
  'protokoll',
  'kryetar',
  'avokat1',
  'avokat2',
  'sekondar',
  'ndihmes',
  'monitor',
  'institucion',
  'jurist',
].map((name) => `${name}@ash.local`)

export const getDevUser = (): string | null => sessionStorage.getItem(DEV_USER_KEY)
export const setDevUser = (email: string) => sessionStorage.setItem(DEV_USER_KEY, email)
export const clearDevUser = () => sessionStorage.removeItem(DEV_USER_KEY)

/** The sign-in mode and providers, filled by initAuth() before the app renders. */
export let authMode: AuthMode = 'oidc'
export let authProviders: AuthProviderInfo[] = []
/** initAuth could not read the configuration (the API is down): the login page says so. */
export let authConfigError = false
/**
 * Task 052: the identity hosts the API names that the frontend's CSP does not allow (the B2C host given to nginx
 * differs from Auth:Providers) — the login would fail silently, so the login page shows an administrator error.
 */
export let cspMismatch: string[] = []

const clients = new Map<string, PublicClientApplication>()

export const rememberedProvider = (): string | null => sessionStorage.getItem(PROVIDER_KEY)
const rememberProvider = (name: string) => sessionStorage.setItem(PROVIDER_KEY, name)
const forgetProvider = () => sessionStorage.removeItem(PROVIDER_KEY)

const providerOf = (name: string | null) => authProviders.find((p) => p.name === name) ?? null

function createClient(provider: AuthProviderInfo) {
  return new PublicClientApplication({
    auth: {
      clientId: provider.clientId,
      authority: provider.authority,
      // B2C: its b2clogin.com host must be trusted explicitly (from the API); harmless for Entra ID
      knownAuthorities: provider.knownAuthorities.length > 0 ? provider.knownAuthorities : [new URL(provider.authority).host],
      redirectUri: `${window.location.origin}/auth/callback`,
      postLogoutRedirectUri: `${window.location.origin}/login`,
      navigateToLoginRequestUrl: false,
    },
    cache: { cacheLocation: 'sessionStorage' },
  })
}

/**
 * Reads the configuration, creates the providers' clients and finishes a redirect that comes back from a provider.
 * Called once before the first render (main.tsx).
 */
export async function initAuth(): Promise<void> {
  await loadRuntimeConfig()
  try {
    const response = await fetch(`${runtimeConfig.apiBasePath}/api/auth/config`)
    if (!response.ok) throw new Error(String(response.status))
    const config = (await response.json()) as { mode: string; providers: AuthProviderInfo[] }
    authMode = config.mode === 'dev' ? 'dev' : 'oidc'
    authProviders = config.providers
  } catch {
    authConfigError = true
    return
  }

  if (authMode !== 'oidc') return
  cspMismatch = hostsMissingFromCsp([...new Set(authProviders.flatMap((p) => p.knownAuthorities))])
  if (cspMismatch.length > 0) return
  for (const provider of authProviders) {
    try {
      const client = createClient(provider)
      await client.initialize()
      clients.set(provider.name, client)
    } catch {
      // a provider with a broken authority is left out; the others still work
    }
  }

  // back from a provider: its client completes the sign-in (only the remembered provider started one)
  const pending = clients.get(rememberedProvider() ?? '')
  if (pending) {
    try {
      const result = await pending.handleRedirectPromise()
      if (result?.account) pending.setActiveAccount(result.account)
    } catch {
      forgetProvider()
    }
  }
}

function accountOf(client: PublicClientApplication): AccountInfo | null {
  return client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null
}

/** Signed in through a provider: its client holds an account. */
export function isOidcSignedIn(): boolean {
  const client = clients.get(rememberedProvider() ?? '')
  return client !== undefined && accountOf(client) !== null
}

/** "Hyr me …": remember the provider and go to its sign-in page (the redirect comes back to /auth/callback). */
export async function loginWith(name: string): Promise<void> {
  const client = clients.get(name)
  const provider = providerOf(name)
  if (!client || !provider) return
  rememberProvider(name)
  await client.loginRedirect({ scopes: provider.scopes, prompt: 'select_account' })
}

/**
 * The access token for the API and the hubs: from the cache, renewed silently by the remembered provider; when the
 * provider needs the user again (session ended, consent), a redirect. '' when not signed in.
 */
export async function accessToken(): Promise<string> {
  const name = rememberedProvider()
  const client = clients.get(name ?? '')
  const provider = providerOf(name)
  const account = client ? accountOf(client) : null
  if (!client || !provider || !account) return ''
  try {
    return (await client.acquireTokenSilent({ scopes: provider.scopes, account })).accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) await client.acquireTokenRedirect({ scopes: provider.scopes, account })
    return ''
  }
}

/** Ends the session at THAT provider too ("Dil", idle logout): its logout page, then back to /login. */
export async function logoutProvider(): Promise<void> {
  const name = rememberedProvider()
  const client = clients.get(name ?? '')
  forgetProvider()
  if (!client) return
  const account = accountOf(client)
  await client.logoutRedirect({ account: account ?? undefined, postLogoutRedirectUri: `${window.location.origin}/login` })
}

/** Forget the local session only (a 401: the user is no longer known), without leaving the app. */
export async function clearOidcSession(): Promise<void> {
  const client = clients.get(rememberedProvider() ?? '')
  forgetProvider()
  if (client) await client.clearCache()
}

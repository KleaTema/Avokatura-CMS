import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

export const authMode: 'dev' | 'oidc' = import.meta.env.VITE_AUTH_MODE === 'oidc' ? 'oidc' : 'dev'

const DEV_USER_KEY = 'ash.devUser'

export const devUsers = [
  'admin',
  'protokoll',
  'kryetar',
  'avokat1',
  'avokat2',
  'ndihmes',
  'monitor',
  'institucion',
  'jurist',
].map((name) => `${name}@ash.local`)

export const getDevUser = (): string | null => sessionStorage.getItem(DEV_USER_KEY)
export const setDevUser = (email: string) => sessionStorage.setItem(DEV_USER_KEY, email)
export const clearDevUser = () => sessionStorage.removeItem(DEV_USER_KEY)

export const oidcConfigured = Boolean(
  import.meta.env.VITE_OIDC_AUTHORITY && import.meta.env.VITE_OIDC_CLIENT_ID,
)

export const userManager: UserManager | null =
  authMode === 'oidc' && oidcConfigured
    ? new UserManager({
        authority: import.meta.env.VITE_OIDC_AUTHORITY ?? '',
        client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? '',
        redirect_uri: `${window.location.origin}/auth/callback`,
        post_logout_redirect_uri: `${window.location.origin}/login`,
        response_type: 'code',
        scope: 'openid profile email',
        userStore: new WebStorageStateStore({ store: window.sessionStorage }),
      })
    : null

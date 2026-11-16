import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { setUnauthorizedHandler } from '../api/client'
import { AuthContext, type AuthState } from './authContext'
import {
  authMode,
  clearDevUser,
  clearOidcSession,
  getDevUser,
  isOidcSignedIn,
  loginWith,
  logoutProvider,
  setDevUser,
} from './authConfig'

/**
 * The session (task 051). Dev mode: the dev user in sessionStorage. Provider mode: the remembered provider's MSAL
 * account (initAuth already finished a redirect before the first render). A 401 forgets the local session (back to the
 * login); "Dil" and the idle logout also end the provider's session (its logout page).
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [isAuthenticated, setAuthenticated] = useState(() =>
    authMode === 'dev' ? getDevUser() !== null : isOidcSignedIn(),
  )

  const clearSession = useCallback(() => {
    clearDevUser()
    if (authMode === 'oidc') void clearOidcSession()
    setAuthenticated(false)
    queryClient.clear()
  }, [queryClient])

  useEffect(() => {
    setUnauthorizedHandler(clearSession)
    return () => setUnauthorizedHandler(null)
  }, [clearSession])

  const value = useMemo<AuthState>(
    () => ({
      isAuthenticated,
      isLoading: false,
      loginDev: (email) => {
        queryClient.clear()
        setDevUser(email)
        setAuthenticated(true)
      },
      loginWith: async (provider) => {
        await loginWith(provider)
      },
      logout: async () => {
        const oidc = authMode === 'oidc'
        clearDevUser()
        setAuthenticated(false)
        queryClient.clear()
        if (oidc) await logoutProvider()
      },
    }),
    [isAuthenticated, queryClient],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { setUnauthorizedHandler } from '../api/client'
import { AuthContext, type AuthState } from './authContext'
import { authMode, clearDevUser, getDevUser, setDevUser, userManager } from './authConfig'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [isAuthenticated, setAuthenticated] = useState(() => authMode === 'dev' && getDevUser() !== null)
  const [isLoading, setLoading] = useState(authMode === 'oidc')

  useEffect(() => {
    if (authMode !== 'oidc') return
    let active = true
    const load = async () => {
      const user = await userManager?.getUser()
      if (!active) return
      setAuthenticated(Boolean(user && !user.expired))
      setLoading(false)
    }
    void load()
    return () => {
      active = false
    }
  }, [])

  const clearSession = useCallback(() => {
    clearDevUser()
    void userManager?.removeUser()
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
      isLoading,
      loginDev: (email) => {
        queryClient.clear()
        setDevUser(email)
        setAuthenticated(true)
      },
      loginOidc: async () => {
        await userManager?.signinRedirect()
      },
      logout: async () => {
        const oidc = authMode === 'oidc' && userManager
        clearSession()
        if (oidc) await userManager?.signoutRedirect()
      },
    }),
    [isAuthenticated, isLoading, queryClient, clearSession],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

import { createContext, useContext } from 'react'

export interface AuthState {
  isAuthenticated: boolean
  isLoading: boolean
  loginDev: (email: string) => void
  /** task 051: sign in through the named provider (Auth:Providers[].Name) */
  loginWith: (provider: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

import type { ReactNode } from 'react'
import { useMe } from '../features/identity/api'
import { ForbiddenPage } from '../pages/ErrorPages'

/** Hides a page unless `me.permissions` has the code. Usability only — the API is the real check. */
export function RequirePermission({ code, children }: { code: string; children: ReactNode }) {
  const me = useMe(true)
  if (!me.data) return null
  return me.data.permissions.includes(code) ? children : <ForbiddenPage />
}

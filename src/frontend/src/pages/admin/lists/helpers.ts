import { useNavigate } from 'react-router'
import { isForbidden } from '../../../lib/problem'

/** Inactive rows stay visible but greyed out. */
export const rowSx = (active: boolean) => (active ? undefined : { '& td': { color: 'text.disabled' } })

/** Returns a handler that sends a 403 to the "no rights" page; true when it did. */
export function useForbiddenRedirect() {
  const navigate = useNavigate()
  return (error: unknown) => {
    if (!isForbidden(error)) return false
    void navigate('/forbidden')
    return true
  }
}

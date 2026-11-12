import { useSearchParams } from 'react-router'

/**
 * The report filters live in the URL (task 045), like the case list's: a refresh or Back keeps them. Any change goes
 * back to page 1. `names` are the parameters this tab owns; "Pastro filtrat" clears them all.
 */
export function useReportParams<N extends string>(names: readonly N[]) {
  const [params, setParams] = useSearchParams()
  const get = (name: N) => params.get(name) ?? ''
  const set = (updates: Partial<Record<N, string>>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [name, value] of Object.entries(updates)) {
          if (typeof value === 'string' && value) next.set(name, value)
          else next.delete(name)
        }
        next.delete('page')
        next.delete('cursor')
        return next
      },
      { replace: true },
    )
  const clear = () => set(Object.fromEntries(names.map((n) => [n, ''])) as Partial<Record<N, string>>)
  return { get, set, clear }
}

/** The width of a field in the filter panel. */
export const PANEL_FIELD = { width: { xs: '100%', md: 240 } }

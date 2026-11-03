import { useSearchParams } from 'react-router'

/**
 * A search text or filter kept in the URL (?q=…, ?court=…), so refresh and back keep it. Other query parameters
 * (e.g. ?list=) are left as they are, except the page: another search or filter starts again at page 1. Typing
 * replaces the history entry.
 */
export function useUrlSearch(param = 'q') {
  const [params, setParams] = useSearchParams()
  const value = params.get(param) ?? ''
  const setValue = (next: string) =>
    setParams(
      (prev) => {
        const updated = new URLSearchParams(prev)
        if (next) updated.set(param, next)
        else updated.delete(param)
        updated.delete('page')
        return updated
      },
      { replace: true },
    )
  return [value, setValue] as const
}

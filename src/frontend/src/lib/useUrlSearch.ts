import { useSearchParams } from 'react-router'

/**
 * The search text kept in the URL (?q=…), so refresh and back keep the search.
 * Other query parameters (e.g. ?list=) are left as they are; typing replaces the history entry.
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
        return updated
      },
      { replace: true },
    )
  return [value, setValue] as const
}

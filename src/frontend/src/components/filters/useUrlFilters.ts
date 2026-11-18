import { useSearchParams } from 'react-router'
import type { FilterDef, FilterValue } from './filterTypes'
import { addedOf, applyAdd, applyClear, applyRemove, applyValue, isUnused, valuesOf } from './urlFilters'

export { ADDED_PARAM } from './urlFilters'

/**
 * Task 053m — a list's filters kept in the URL (the list endpoint reads the same parameters): Back from a row restores
 * them, leaving through the menu (a clean URL) clears them. The added-but-empty ones live in ?af= so they survive too.
 * `unused`: a filter was added and has no value yet (the leave dialog asks before it is lost). The rules: urlFilters.ts.
 */
export function useUrlFilters(filters: readonly FilterDef[], options?: { resetParams?: readonly string[] }) {
  const [params, setParams] = useSearchParams()
  const values = valuesOf(filters, params)
  const added = addedOf(filters, params)

  const update = (change: (next: URLSearchParams) => void) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        change(next)
        // another filter: back to the first page (and whatever else the page resets)
        for (const p of ['page', ...(options?.resetParams ?? [])]) next.delete(p)
        return next
      },
      { replace: true },
    )

  const set = (filter: FilterDef, value: FilterValue) => update((next) => applyValue(filters, next, filter, value))

  return {
    filters,
    values,
    added,
    unused: isUnused(added, values),
    /** a new value; the filters that depend on it are cleared (their options change) */
    set,
    /** the same by key (a "clear the search" link); an unknown key does nothing */
    setKey: (key: string, value: FilterValue) => {
      const def = filters.find((f) => f.key === key)
      if (def) set(def, value)
    },
    add: (key: string) => update((next) => applyAdd(filters, next, key)),
    remove: (filter: FilterDef) => update((next) => applyRemove(filters, next, filter)),
    /** every filter and every added one gone (the defaults stay, empty) */
    clear: () => update((next) => applyClear(filters, next)),
  }
}

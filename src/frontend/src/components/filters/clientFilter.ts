import { tiraneDateOf } from '../../lib/dates'
import { matches } from '../../lib/search'
import { FilterKind, hasValue, type FilterDef, type FilterValues } from './filterTypes'

/**
 * Task 053m — the filters of a table the browser holds whole (lists, offices, judges …): the same metadata as a
 * server table, applied to the rows here. `get` reads a row's value for a filter key (the server names the keys after
 * the row's fields): text → contained (accent- and case-insensitive), select → equal, yes / no → equal, ranges → within.
 */
export function matchesFilters<T>(
  row: T,
  filters: readonly FilterDef[],
  values: FilterValues,
  get: (row: T, key: string) => unknown,
): boolean {
  for (const f of filters) {
    const wanted = values[f.key]
    if (!hasValue(wanted)) continue
    const actual = get(row, f.key)
    switch (f.kind) {
      case FilterKind.Text: {
        // a search over several fields: `get` returns them all, any one may contain the text
        const fields = Array.isArray(actual) ? actual : [actual]
        if (typeof wanted !== 'string' || !matches(wanted, ...fields.map((x) => (x == null ? null : String(x))))) return false
        break
      }
      case FilterKind.YesNo:
        if (Boolean(actual) !== wanted) return false
        break
      case FilterKind.DateRange: {
        if (typeof wanted !== 'object') break
        // a date ("yyyy-MM-dd") as it is; an instant (UTC) by its Tirane day
        const day = typeof actual !== 'string' ? '' : actual.length > 10 ? (tiraneDateOf(actual) ?? '') : actual
        if (!day || (wanted.from && day < wanted.from) || (wanted.to && day > wanted.to)) return false
        break
      }
      case FilterKind.NumberRange: {
        if (typeof wanted !== 'object') break
        const n = Number(actual)
        if (actual == null || Number.isNaN(n) || (wanted.min && n < Number(wanted.min)) || (wanted.max && n > Number(wanted.max))) return false
        break
      }
      default:
        if (String(actual ?? '') !== wanted) return false
    }
  }

  return true
}

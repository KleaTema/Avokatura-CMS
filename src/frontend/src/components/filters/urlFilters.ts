import { descendantsOf, FilterKind, hasValue, type FilterDef, type FilterValue, type FilterValues } from './filterTypes'

/**
 * Task 053m — the URL side of a list's filters, as pure functions (useUrlFilters applies them; the unit tests read
 * them): each filter's value lives in its own parameters (`params`, else its key), the ones added with "+ Shto filtër"
 * in ?af= (comma-separated keys) so an added-but-empty filter survives Back too.
 */

/** The URL parameter that keeps the filters added with "+ Shto filtër". */
export const ADDED_PARAM = 'af'

const paramsOf = (f: FilterDef) => (f.params && f.params.length > 0 ? f.params : [f.key])

/** One filter's value as its URL parameters hold it (a range: two parameters; yes / no: "true" / "false"). */
function read(f: FilterDef, params: URLSearchParams): FilterValue | undefined {
  const [first, second] = paramsOf(f)
  if (f.kind === FilterKind.DateRange || f.kind === FilterKind.NumberRange) {
    const from = params.get(first) ?? ''
    const to = second ? (params.get(second) ?? '') : ''
    if (!from && !to) return undefined
    return f.kind === FilterKind.DateRange ? { from, to } : { min: from, max: to }
  }

  const raw = params.get(first)
  if (raw === null || raw === '') return undefined
  if (f.kind === FilterKind.YesNo) return raw === 'true'
  return raw
}

function write(f: FilterDef, value: FilterValue | undefined, params: URLSearchParams) {
  const [first, second] = paramsOf(f)
  const put = (name: string | undefined, v: string | undefined) => {
    if (!name) return
    if (v) params.set(name, v)
    else params.delete(name)
  }

  if (value === undefined || value === '') {
    paramsOf(f).forEach((p) => params.delete(p))
  } else if (typeof value === 'boolean') {
    put(first, value ? 'true' : 'false')
  } else if (typeof value === 'string') {
    put(first, value)
  } else if (f.kind === FilterKind.DateRange) {
    put(first, value.from)
    put(second, value.to)
  } else {
    put(first, value.min)
    put(second, value.max)
  }
}

/** Every filter's value in the URL. */
export function valuesOf(filters: readonly FilterDef[], params: URLSearchParams): FilterValues {
  const values: FilterValues = {}
  for (const f of filters) {
    const v = read(f, params)
    if (v !== undefined) values[f.key] = v
  }
  return values
}

/** The keys added with "+ Shto filtër" (only the table's own). */
export function addedOf(filters: readonly FilterDef[], params: URLSearchParams): string[] {
  return (params.get(ADDED_PARAM) ?? '').split(',').filter((k) => k !== '' && filters.some((f) => f.key === k))
}

/** A filter was added and has no value yet: the leave dialog asks before it is lost. */
export const isUnused = (added: readonly string[], values: FilterValues) => added.some((k) => !hasValue(values[k]))

function setAdded(next: URLSearchParams, keys: readonly string[]) {
  if (keys.length > 0) next.set(ADDED_PARAM, keys.join(','))
  else next.delete(ADDED_PARAM)
}

function clearChildren(filters: readonly FilterDef[], key: string, next: URLSearchParams) {
  for (const child of descendantsOf(filters, key)) {
    const def = filters.find((f) => f.key === child)
    if (def) write(def, undefined, next)
  }
}

/** A new value; the filters that depend on it are cleared (their options change). Changes `next`. */
export function applyValue(filters: readonly FilterDef[], next: URLSearchParams, filter: FilterDef, value: FilterValue) {
  write(filter, value, next)
  clearChildren(filters, filter.key, next)
}

/** "+ Shto filtër": the key joins ?af= (last, so its field takes the focus). Changes `next`. */
export function applyAdd(filters: readonly FilterDef[], next: URLSearchParams, key: string) {
  const added = addedOf(filters, next)
  setAdded(next, [...added.filter((k) => k !== key), key])
}

/** ×: the filter's value, its children's and its place in ?af= go. Changes `next`. */
export function applyRemove(filters: readonly FilterDef[], next: URLSearchParams, filter: FilterDef) {
  const added = addedOf(filters, next)
  write(filter, undefined, next)
  clearChildren(filters, filter.key, next)
  setAdded(next, added.filter((k) => k !== filter.key))
}

/** "Pastro filtrat": every filter and every added one gone (the defaults stay, empty). Changes `next`. */
export function applyClear(filters: readonly FilterDef[], next: URLSearchParams) {
  for (const f of filters) write(f, undefined, next)
  next.delete(ADDED_PARAM)
}

/** The leave dialog: only while a filter is unused, and only when the page itself changes (not its query). */
export const blocksLeaving = (unused: boolean, from: string, to: string) => unused && from !== to

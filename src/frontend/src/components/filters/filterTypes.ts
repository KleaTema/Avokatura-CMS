/**
 * Task 053m — the ONE filter model of every table: the server's metadata (GET /api/filters/{table}, or the report
 * builder's ReportFilterMeta — the same shape) and the values the FilterBar edits.
 */

/** The filter kinds (the server's FilterKinds / ReportFilterKinds). */
export const FilterKind = {
  Select: 'select',
  Text: 'text',
  DateRange: 'dateRange',
  NumberRange: 'numberRange',
  YesNo: 'yesNo',
} as const

/** One choice; parent = the parent filter's value it belongs to (a court → its level). */
export interface FilterOptionDef {
  value: string
  label: string
  parent?: string | null
}

/** One filter as the FilterBar draws it. `params`: the query parameters it writes (URL-backed tables). */
export interface FilterDef {
  key: string
  label: string
  kind: string
  parent?: string | null
  isDefault: boolean
  required?: boolean
  locked?: boolean
  options?: readonly FilterOptionDef[] | null
  params?: readonly string[]
}

/** A range value: dates yyyy-MM-dd, numbers as typed. */
export type RangeValue = { from?: string; to?: string; min?: string; max?: string }
export type FilterValue = string | boolean | RangeValue
export type FilterValues = Record<string, FilterValue>

/** Whether a value filters anything (an empty text, an empty range or "—" does not). */
export function hasValue(value: FilterValue | undefined): boolean {
  if (value === undefined) return false
  if (typeof value === 'string') return value.trim() !== ''
  if (typeof value === 'boolean') return true
  return Object.values(value).some((v) => typeof v === 'string' && v.trim() !== '')
}

/** A select / text filter's value ('' when empty) — what a list endpoint's parameter takes. */
export function textOf(values: FilterValues, key: string): string {
  const v = values[key]
  return typeof v === 'string' ? v : ''
}

/** A yes / no filter as a parameter: '' | 'true' | 'false'. */
export function yesNoOf(values: FilterValues, key: string): string {
  const v = values[key]
  return v === true ? 'true' : v === false ? 'false' : ''
}

/** One side of a range filter ('' when empty). */
export function rangeOf(values: FilterValues, key: string, side: keyof RangeValue): string {
  const v = values[key]
  return typeof v === 'object' ? (v[side] ?? '') : ''
}

/** The filters under `key` (its children, their children…): cleared when `key` changes. */
export function descendantsOf(filters: readonly FilterDef[], key: string): string[] {
  const children = filters.filter((f) => f.parent === key).map((f) => f.key)
  return children.flatMap((child) => [child, ...descendantsOf(filters, child)])
}

/** The options of a filter under its parent's current choice (all of them while the parent has none). */
export function optionsUnder(filter: FilterDef, values: FilterValues): readonly FilterOptionDef[] {
  const options = filter.options ?? []
  const parentValue = filter.parent ? values[filter.parent] : undefined
  if (typeof parentValue !== 'string' || parentValue === '') return options
  return options.filter((o) => o.parent == null || o.parent === parentValue)
}

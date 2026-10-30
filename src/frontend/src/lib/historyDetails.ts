import { sq } from '../i18n/sq'
import { formatDate, formatDateTime } from './dates'

const h = sq.cases.page.history

export interface DetailRow {
  key: string
  label: string
  before: string
  after: string
}

const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** Public ids are for links, not for reading. */
const hidden = (key: string) => /publicid$/i.test(key)

/** Nested objects become dotted keys: { head: { name } } → "head.name". */
function flatten(value: unknown, prefix = '', into = new Map<string, unknown>()): Map<string, unknown> {
  if (!isPlainObject(value)) return into
  for (const [key, inner] of Object.entries(value)) {
    if (hidden(key)) continue
    const path = prefix ? `${prefix}.${key}` : key
    if (isPlainObject(inner)) flatten(inner, path, into)
    else into.set(path, inner)
  }
  return into
}

const ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$/
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/

/** One value as text: codes and booleans in Albanian, dates in Tirane format, lists joined. */
export function displayValue(key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return sq.cases.page.none
  if (typeof value === 'boolean') return value ? sq.cases.page.yes : sq.cases.page.no
  if (Array.isArray(value)) return value.map((item) => displayValue(key, item)).join(', ')
  if (isPlainObject(value)) {
    return [...flatten(value)].map(([k, v]) => `${labelOf(k)}: ${displayValue(k, v)}`).join('; ')
  }
  const text = String(value)
  const last = key.split('.').pop() ?? key
  if (last === 'kind' || last === 'partyKind') return sq.partyEditor.kinds[text] ?? text
  if (last === 'mode') return sq.cases.page.data.modes[text] ?? text
  if (last === 'priority') return sq.cases.priority[text] ?? text
  if (ISO_DATE_TIME.test(text)) return formatDateTime(text)
  if (ISO_DATE.test(text)) return formatDate(text)
  return text
}

export function labelOf(key: string): string {
  const last = key.split('.').pop() ?? key
  return h.fields[key] ?? h.fields[last] ?? key
}

/** The changed fields of a history row (old/new from the API) as readable rows; empty when there is nothing to show. */
export function detailRows(before: unknown, after: unknown): DetailRow[] {
  const old = flatten(before)
  const next = flatten(after)
  const keys = [...new Set([...old.keys(), ...next.keys()])]
  return keys.map((key) => ({
    key,
    label: labelOf(key),
    before: old.has(key) ? displayValue(key, old.get(key)) : sq.cases.page.none,
    after: next.has(key) ? displayValue(key, next.get(key)) : sq.cases.page.none,
  }))
}

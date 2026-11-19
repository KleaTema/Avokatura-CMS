import { formatDate } from '../../lib/dates'
import { FilterKind, hasValue, type FilterDef, type FilterValues, type RangeValue } from './filterTypes'

/** The words of a range: "nga 01.01.2026 deri 31.03.2026", "nga 01.01.2026", "deri 31.03.2026". */
function rangeText(value: RangeValue, dates: boolean, from: string, to: string): string {
  const a = dates ? (value.from ? formatDate(value.from) : '') : (value.min ?? '')
  const b = dates ? (value.to ? formatDate(value.to) : '') : (value.max ?? '')
  return [a && `${from} ${a}`, b && `${to} ${b}`].filter(Boolean).join(' ')
}

/**
 * Task 053o — the filters in words for a printout's header: one "Label: value" per filter with a value, in the bar's
 * order; a select shows its option's label, yes / no "Po" / "Jo", a range its ends.
 */
export function describeFilters(
  filters: readonly FilterDef[],
  values: FilterValues,
  words: { yes: string; no: string; from: string; to: string },
): string[] {
  return filters
    .filter((f) => hasValue(values[f.key]))
    .map((f) => {
      const value = values[f.key]
      if (typeof value === 'boolean') return `${f.label}: ${value ? words.yes : words.no}`
      if (typeof value === 'string') {
        const option = f.options?.find((o) => o.value === value)
        return `${f.label}: ${option?.label ?? value}`
      }

      return `${f.label}: ${rangeText(value!, f.kind === FilterKind.DateRange, words.from.toLowerCase(), words.to.toLowerCase())}`
    })
}

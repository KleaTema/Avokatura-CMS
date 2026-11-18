import { describe, expect, it } from 'vitest'
import { matchesFilters } from './clientFilter'
import { FilterKind, optionsUnder, type FilterDef } from './filterTypes'
import { addedOf, applyAdd, applyClear, applyRemove, applyValue, blocksLeaving, isUnused, valuesOf } from './urlFilters'

/** The case list's shape: search, status (defaults), level → court → judge, a yes / no and a date range. */
const FILTERS: FilterDef[] = [
  { key: 'q', label: 'Kërko', kind: FilterKind.Text, isDefault: true, params: ['q'] },
  { key: 'status', label: 'Statusi', kind: FilterKind.Select, isDefault: true, params: ['status'] },
  {
    key: 'level',
    label: 'Shkalla',
    kind: FilterKind.Select,
    isDefault: false,
    params: ['level'],
    options: [
      { value: 'FIRST', label: 'E para' },
      { value: 'APPEAL', label: 'Apeli' },
    ],
  },
  {
    key: 'court',
    label: 'Gjykata',
    kind: FilterKind.Select,
    parent: 'level',
    isDefault: true,
    params: ['court'],
    options: [
      { value: '1', label: 'Tiranë', parent: 'FIRST' },
      { value: '2', label: 'Apeli Tiranë', parent: 'APPEAL' },
    ],
  },
  { key: 'judge', label: 'Gjyqtari', kind: FilterKind.Select, parent: 'court', isDefault: false, params: ['judge'] },
  { key: 'suspended', label: 'Pezulluar', kind: FilterKind.YesNo, isDefault: false, params: ['suspended'] },
  { key: 'hearing', label: 'Seanca', kind: FilterKind.DateRange, isDefault: false, params: ['from', 'to'] },
]
const def = (key: string) => FILTERS.find((f) => f.key === key)!

describe('filters in the URL (task 053m)', () => {
  it('reads each kind from its own parameters', () => {
    const values = valuesOf(FILTERS, new URLSearchParams('q=640&court=1&suspended=false&from=2026-01-01&tab=all'))
    expect(values).toEqual({ q: '640', court: '1', suspended: false, hearing: { from: '2026-01-01', to: '' } })
  })

  it('adds a filter (kept in ?af= while empty: unused) and removes it with its value', () => {
    const params = new URLSearchParams('tab=all')
    applyAdd(FILTERS, params, 'hearing')
    expect(addedOf(FILTERS, params)).toEqual(['hearing'])
    expect(isUnused(addedOf(FILTERS, params), valuesOf(FILTERS, params))).toBe(true)

    applyValue(FILTERS, params, def('hearing'), { from: '2026-03-01', to: '2026-03-31' })
    expect(params.get('from')).toBe('2026-03-01')
    expect(params.get('to')).toBe('2026-03-31')
    expect(isUnused(addedOf(FILTERS, params), valuesOf(FILTERS, params))).toBe(false)

    applyRemove(FILTERS, params, def('hearing'))
    expect(params.toString()).toBe('tab=all')
  })

  it('ignores ?af= keys of another table', () => {
    expect(addedOf(FILTERS, new URLSearchParams('af=impQ,judge'))).toEqual(['judge'])
  })

  it('clears the children of a changed parent (level → court → judge)', () => {
    const params = new URLSearchParams('level=FIRST&court=1&judge=7&q=x')
    applyValue(FILTERS, params, def('level'), 'APPEAL')
    expect(params.toString()).toBe('level=APPEAL&q=x')
  })

  it('offers a child only the options under its parent’s choice', () => {
    expect(optionsUnder(def('court'), {}).map((o) => o.value)).toEqual(['1', '2'])
    expect(optionsUnder(def('court'), { level: 'APPEAL' }).map((o) => o.value)).toEqual(['2'])
  })

  it('clears every filter and the added ones, keeping the page’s other parameters', () => {
    const params = new URLSearchParams('tab=office&q=x&status=OPEN&af=judge&judge=3&from=2026-01-01')
    applyClear(FILTERS, params)
    expect(params.toString()).toBe('tab=office')
  })

  it('asks before leaving only for an unused filter and only when the page changes', () => {
    expect(blocksLeaving(true, '/cases', '/agenda')).toBe(true)
    expect(blocksLeaving(true, '/cases', '/cases')).toBe(false) // the page's own query (tab, page) changes
    expect(blocksLeaving(false, '/cases', '/agenda')).toBe(false)
  })
})

describe('filters applied in the browser (task 053m)', () => {
  type Row = { name: string; city: string | null; level: string; active: boolean; created: string; size: number }
  const rows: Row[] = [
    { name: 'Gjykata e Tiranës', city: 'Tiranë', level: 'FIRST', active: true, created: '2026-03-10T23:30:00Z', size: 5 },
    { name: 'Gjykata e Apelit Durrës', city: 'Durrës', level: 'APPEAL', active: false, created: '2026-01-02T08:00:00Z', size: 50 },
  ]
  const filters: FilterDef[] = [
    { key: 'q', label: 'Kërko', kind: FilterKind.Text, isDefault: true },
    { key: 'level', label: 'Shkalla', kind: FilterKind.Select, isDefault: true },
    { key: 'active', label: 'Aktive', kind: FilterKind.YesNo, isDefault: true },
    { key: 'created', label: 'Krijuar', kind: FilterKind.DateRange, isDefault: false },
    { key: 'size', label: 'MB', kind: FilterKind.NumberRange, isDefault: false },
  ]
  const field = (r: Row, key: string): unknown => {
    const fields: Record<string, unknown> = { q: [r.name, r.city], level: r.level, active: r.active, created: r.created, size: r.size }
    return fields[key]
  }
  const names = (values: Parameters<typeof matchesFilters>[2]) =>
    rows.filter((r) => matchesFilters(r, filters, values, field)).map((r) => r.city)

  it('matches a search in any of the fields, accent- and case-insensitive', () => {
    expect(names({ q: 'durres' })).toEqual(['Durrës'])
    expect(names({ q: 'GJYKATA' })).toEqual(['Tiranë', 'Durrës'])
  })

  it('matches a select, a yes / no and a number range', () => {
    expect(names({ level: 'APPEAL' })).toEqual(['Durrës'])
    expect(names({ active: true })).toEqual(['Tiranë'])
    expect(names({ active: false })).toEqual(['Durrës'])
    expect(names({ size: { min: '10' } })).toEqual(['Durrës'])
  })

  it('matches an instant by its Tirane day', () => {
    // 23:30 UTC on 10 March is 11 March in Tirana
    expect(names({ created: { from: '2026-03-11', to: '2026-03-11' } })).toEqual(['Tiranë'])
    expect(names({ created: { to: '2026-03-10' } })).toEqual(['Durrës'])
  })
})

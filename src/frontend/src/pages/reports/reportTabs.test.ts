import { describe, expect, it } from 'vitest'
import { describeFilters } from '../../components/filters/describeFilters'
import { FilterKind, type FilterDef } from '../../components/filters/filterTypes'
import { mainOf, TABLE_OF, tabFromParam, tabOfTable } from './reportTabs'

describe('Raporte & Statistika tabs (task 053o)', () => {
  it('maps the sub-tabs to the main tabs and the API tables', () => {
    expect(mainOf('aktive')).toBe('raportet')
    expect(mainOf('mbyllura')).toBe('raportet')
    expect(mainOf('mine')).toBe('raportet')
    expect(mainOf('statistika')).toBe('statistikat')
    expect(mainOf('institucionale')).toBe('statistikat')
    expect(TABLE_OF).toEqual({ aktive: 1, mbyllura: 3, statistika: 5, institucionale: 6 })
  })

  it('opens old links (?tab=tabela1/3/5/6) on the same table, anything else on Tabela 1', () => {
    expect(tabFromParam('tabela1')).toBe('aktive')
    expect(tabFromParam('tabela3')).toBe('mbyllura')
    expect(tabFromParam('tabela5')).toBe('statistika')
    expect(tabFromParam('tabela6')).toBe('institucionale')
    expect(tabFromParam('mine')).toBe('mine')
    expect(tabFromParam('nope')).toBe('aktive')
    expect(tabFromParam(null)).toBe('aktive')
  })

  it('opens a saved report of an API table on its tab', () => {
    expect([1, 3, 5, 6].map(tabOfTable)).toEqual(['aktive', 'mbyllura', 'statistika', 'institucionale'])
  })
})

describe('the filters in words (the printout header)', () => {
  const filters: FilterDef[] = [
    { key: 'court', label: 'Gjykata', kind: FilterKind.Select, isDefault: true, options: [{ value: '1', label: 'Tiranë' }] },
    { key: 'closed', label: 'Data e mbylljes', kind: FilterKind.DateRange, isDefault: false },
    { key: 'suspended', label: 'Pezulluar', kind: FilterKind.YesNo, isDefault: false },
    { key: 'q', label: 'Kërko', kind: FilterKind.Text, isDefault: true },
  ]
  const words = { yes: 'Po', no: 'Jo', from: 'Nga', to: 'Deri' }

  it('names each filter with a value, in the bar order, with option labels and range ends', () => {
    expect(
      describeFilters(filters, { court: '1', closed: { from: '2026-01-01', to: '2026-03-31' }, suspended: false }, words),
    ).toEqual(['Gjykata: Tiranë', 'Data e mbylljes: nga 01.01.2026 deri 31.03.2026', 'Pezulluar: Jo'])
    expect(describeFilters(filters, { closed: { from: '2026-01-01' }, q: 'abc' }, words)).toEqual([
      'Data e mbylljes: nga 01.01.2026',
      'Kërko: abc',
    ])
  })

  it('says nothing for empty filters', () => {
    expect(describeFilters(filters, { q: '  ', closed: {} }, words)).toEqual([])
  })
})

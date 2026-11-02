import { describe, expect, it } from 'vitest'
import { mondayOf, rangeOf, shift, titleOf, validDate } from './agendaRange'

describe('agenda ranges (task 031): what GET /api/agenda is asked for', () => {
  it('weeks start on Monday', () => {
    expect(mondayOf('2026-11-09')).toBe('2026-11-09') // Monday
    expect(mondayOf('2026-11-15')).toBe('2026-11-09') // Sunday
    expect(mondayOf('2026-11-01')).toBe('2026-10-26')
  })

  it('day, week and list ranges', () => {
    expect(rangeOf('dita', '2026-11-12')).toEqual({ from: '2026-11-12', to: '2026-11-12' })
    expect(rangeOf('java', '2026-11-12')).toEqual({ from: '2026-11-09', to: '2026-11-15' })
    expect(rangeOf('lista', '2026-11-12')).toEqual({ from: '2026-11-09', to: '2026-11-15' })
  })

  it('the month shows six whole weeks from the Monday on or before the 1st (42 days, within the 62-day limit)', () => {
    expect(rangeOf('muaji', '2026-11-20')).toEqual({ from: '2026-10-26', to: '2026-12-06' })
    expect(rangeOf('muaji', '2026-06-15')).toEqual({ from: '2026-06-01', to: '2026-07-12' }) // the 1st is a Monday
  })

  it('steps back and forward', () => {
    expect(shift('dita', '2026-10-31', 1)).toBe('2026-11-01')
    expect(shift('java', '2026-11-12', -1)).toBe('2026-11-05')
    expect(shift('muaji', '2026-01-31', 1)).toBe('2026-02-01')
    expect(shift('muaji', '2026-01-15', -1)).toBe('2025-12-01')
  })

  it('accepts only real calendar dates from the URL', () => {
    expect(validDate('2026-02-29')).toBeNull()
    expect(validDate('2026-11-9')).toBeNull()
    expect(validDate('2028-02-29')).toBe('2028-02-29')
  })

  it('titles in Albanian', () => {
    expect(titleOf('java', '2026-11-12')).toBe('9 – 15 Nëntor 2026')
    expect(titleOf('java', '2026-12-03')).toBe('30 Nëntor – 6 Dhjetor 2026')
    expect(titleOf('java', '2026-12-30')).toBe('28 Dhjetor 2026 – 3 Janar 2027')
    expect(titleOf('muaji', '2026-11-12')).toBe('Nëntor 2026')
    expect(titleOf('dita', '2026-11-09')).toBe('E hënë, 9 Nëntor 2026')
  })
})

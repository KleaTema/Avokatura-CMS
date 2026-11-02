import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import { isoDateOf, pickerValuesOfUtc, TIME_REFERENCE, timeOf, utcIsoOf } from './pickerValues'

describe('UTC hearing time → picker values (editing a hearing, task 030)', () => {
  it('shows the Tirane wall clock, in winter and in summer', () => {
    const winter = pickerValuesOfUtc('2026-11-12T08:30:00Z')
    expect(isoDateOf(winter.date)).toBe('2026-11-12')
    expect(timeOf(winter.time)).toBe('09:30')
    const summer = pickerValuesOfUtc('2026-07-01T22:15:00Z') // after midnight in Tirane: the next day
    expect(isoDateOf(summer.date)).toBe('2026-07-02')
    expect(timeOf(summer.time)).toBe('00:15')
  })

  it('round-trips through utcIsoOf', () => {
    for (const utc of ['2026-11-12T08:30:00Z', '2027-03-28T07:00:00Z', '2026-10-25T00:30:00Z']) {
      const { date, time } = pickerValuesOfUtc(utc)
      expect(utcIsoOf(date, time)).toBe(utc)
    }
  })

  it('is empty for nothing', () => {
    expect(pickerValuesOfUtc(null)).toEqual({ date: null, time: null })
  })
})

// what the pickers hand to the form: a local date at midnight, and a time on the reference day
const date = (iso: string) => dayjs(iso)
const time = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number)
  return TIME_REFERENCE.hour(h).minute(m).second(0)
}

describe('picker values → UTC hearing time', () => {
  it('winter and summer time', () => {
    expect(utcIsoOf(date('2026-01-15'), time('10:00'))).toBe('2026-01-15T09:00:00Z')
    expect(utcIsoOf(date('2026-07-15'), time('10:00'))).toBe('2026-07-15T08:00:00Z')
  })

  it('29.03.2026: 02:30 does not exist, 03:00 is summer time', () => {
    expect(utcIsoOf(date('2026-03-29'), time('02:30'))).toBeNull()
    expect(utcIsoOf(date('2026-03-29'), time('03:00'))).toBe('2026-03-29T01:00:00Z')
  })

  it('25.10.2026: 02:30 happens twice → the first, 03:00 is winter time', () => {
    expect(utcIsoOf(date('2026-10-25'), time('02:30'))).toBe('2026-10-25T00:30:00Z')
    expect(utcIsoOf(date('2026-10-25'), time('03:00'))).toBe('2026-10-25T02:00:00Z')
  })

  it('empty or incomplete pickers give null', () => {
    expect(utcIsoOf(null, time('10:00'))).toBeNull()
    expect(utcIsoOf(date('2026-01-15'), null)).toBeNull()
    expect(utcIsoOf(dayjs('not a date'), time('10:00'))).toBeNull()
  })

  it('reads the calendar date and the 24h time as picked', () => {
    expect(isoDateOf(date('2026-11-05'))).toBe('2026-11-05')
    expect(timeOf(time('14:05'))).toBe('14:05')
  })
})

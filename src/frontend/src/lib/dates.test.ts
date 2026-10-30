import { describe, expect, it } from 'vitest'
import {
  addDays,
  formatDate,
  formatDateTime,
  formatTiraneDate,
  parseDisplayDate,
  tiraneDateOf,
  tiraneLocalToUtcIso,
  todayInTirane,
} from './dates'

describe('formatDate (DateOnly, no time zone)', () => {
  it('rewrites yyyy-MM-dd as dd.MM.yyyy', () => {
    expect(formatDate('2026-11-05')).toBe('05.11.2026')
    expect(formatDate('2026-01-01')).toBe('01.01.2026')
  })

  it('never shifts the day, whatever the time zone', () => {
    // the dates where a Date-based conversion would move to the previous or next day
    expect(formatDate('2026-03-29')).toBe('29.03.2026')
    expect(formatDate('2026-10-25')).toBe('25.10.2026')
    expect(formatDate('2026-12-31')).toBe('31.12.2026')
  })

  it('returns empty for empty or non-date input', () => {
    expect(formatDate(null)).toBe('')
    expect(formatDate(undefined)).toBe('')
    expect(formatDate('2026-11-05T10:00:00Z')).toBe('')
    expect(formatDate('05.11.2026')).toBe('')
  })
})

describe('calendar arithmetic and UTC instants', () => {
  it('adds days across month and year ends', () => {
    expect(addDays('2026-01-31', 1)).toBe('2026-02-01')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2026-03-29', 1)).toBe('2026-03-30')
  })

  it('takes the Tirane date of a UTC instant', () => {
    expect(formatTiraneDate('2026-07-14T23:30:00Z')).toBe('15.07.2026')
    expect(tiraneDateOf('2026-07-14T23:30:00Z')).toBe('2026-07-15')
    expect(tiraneDateOf(null)).toBeNull()
  })
})

describe('tiraneLocalToUtcIso', () => {
  it('converts a winter time (CET, UTC+1)', () => {
    expect(tiraneLocalToUtcIso('2026-01-15', '10:00')).toBe('2026-01-15T09:00:00Z')
  })

  it('converts a summer time (CEST, UTC+2)', () => {
    expect(tiraneLocalToUtcIso('2026-07-15', '10:00')).toBe('2026-07-15T08:00:00Z')
  })

  it('converts a time just after midnight back into the previous UTC day', () => {
    expect(tiraneLocalToUtcIso('2026-07-15', '00:30')).toBe('2026-07-14T22:30:00Z')
  })

  describe('29.03.2026 — clocks go forward 02:00 → 03:00', () => {
    it('01:59 is still winter time', () => {
      expect(tiraneLocalToUtcIso('2026-03-29', '01:59')).toBe('2026-03-29T00:59:00Z')
    })

    it.each(['02:00', '02:30', '02:59'])('%s does not exist → null', (time) => {
      expect(tiraneLocalToUtcIso('2026-03-29', time)).toBeNull()
    })

    it('03:00 is summer time', () => {
      expect(tiraneLocalToUtcIso('2026-03-29', '03:00')).toBe('2026-03-29T01:00:00Z')
    })

    it('04:00 is summer time', () => {
      expect(tiraneLocalToUtcIso('2026-03-29', '04:00')).toBe('2026-03-29T02:00:00Z')
    })
  })

  describe('25.10.2026 — clocks go back 03:00 → 02:00', () => {
    it('01:59 is still summer time', () => {
      expect(tiraneLocalToUtcIso('2026-10-25', '01:59')).toBe('2026-10-24T23:59:00Z')
    })

    it('02:00 happens twice → the first (summer time)', () => {
      expect(tiraneLocalToUtcIso('2026-10-25', '02:00')).toBe('2026-10-25T00:00:00Z')
    })

    it('02:30 happens twice → the first (summer time)', () => {
      expect(tiraneLocalToUtcIso('2026-10-25', '02:30')).toBe('2026-10-25T00:30:00Z')
    })

    it('03:00 is winter time', () => {
      expect(tiraneLocalToUtcIso('2026-10-25', '03:00')).toBe('2026-10-25T02:00:00Z')
    })

    it('04:00 is winter time', () => {
      expect(tiraneLocalToUtcIso('2026-10-25', '04:00')).toBe('2026-10-25T03:00:00Z')
    })
  })

  it.each([
    ['2026-02-30', '10:00'],
    ['2026-13-01', '10:00'],
    ['2026-01-15', '24:00'],
    ['2026-01-15', '9:30'],
    ['15.01.2026', '10:00'],
  ])('rejects %s %s', (date, time) => {
    expect(tiraneLocalToUtcIso(date, time)).toBeNull()
  })

  it('round-trips with formatDateTime', () => {
    expect(formatDateTime(tiraneLocalToUtcIso('2026-10-25', '03:00'))).toBe('25.10.2026 03:00')
    expect(formatDateTime(tiraneLocalToUtcIso('2026-03-29', '03:00'))).toBe('29.03.2026 03:00')
  })
})

describe('calendar dates', () => {
  it('parses dd.MM.yyyy and d.M.yyyy', () => {
    expect(parseDisplayDate('05.11.2026')).toBe('2026-11-05')
    expect(parseDisplayDate(' 5.1.2026 ')).toBe('2026-01-05')
  })

  it('rejects dates that do not exist', () => {
    expect(parseDisplayDate('29.02.2026')).toBeNull()
    expect(parseDisplayDate('2026-11-05')).toBeNull()
    expect(parseDisplayDate('')).toBeNull()
  })

  it('takes today in Tirane, not in UTC', () => {
    // 23:30 UTC on 14 July is already 15 July in Tirane (UTC+2)
    expect(todayInTirane(new Date('2026-07-14T23:30:00Z'))).toBe('2026-07-15')
    expect(todayInTirane(new Date('2026-01-14T22:30:00Z'))).toBe('2026-01-14')
  })
})

describe('formatDateTime', () => {
  it('converts winter UTC to Tirane (UTC+1)', () => {
    expect(formatDateTime('2025-01-15T10:30:00Z')).toBe('15.01.2025 11:30')
  })

  it('converts summer UTC to Tirane (UTC+2)', () => {
    expect(formatDateTime('2025-07-15T10:30:00Z')).toBe('15.07.2025 12:30')
  })

  it('rolls over midnight', () => {
    expect(formatDateTime('2025-01-15T23:30:00Z')).toBe('16.01.2025 00:30')
  })

  it('returns empty string for empty or invalid input', () => {
    expect(formatDateTime(null)).toBe('')
    expect(formatDateTime('not a date')).toBe('')
  })
})

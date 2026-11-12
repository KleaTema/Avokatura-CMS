import { describe, expect, it } from 'vitest'
import { fileNameFrom, formatMegabytes } from './download'
import { formatCount } from './numbers'

describe('formatCount (report numbers)', () => {
  it('groups thousands with a dot, four digits too', () => {
    expect(formatCount(0)).toBe('0')
    expect(formatCount(1234)).toBe('1.234')
    expect(formatCount(200000)).toBe('200.000')
  })
})

describe('fileNameFrom (Content-Disposition, task 045)', () => {
  it("prefers filename* (RFC 5987) and decodes it", () => {
    expect(fileNameFrom(`attachment; filename=raporti.xlsx; filename*=UTF-8''raporti-%C3%A7%C3%ABshtjet-2026.xlsx`)).toBe(
      'raporti-çështjet-2026.xlsx',
    )
  })

  it('reads a quoted or plain filename', () => {
    expect(fileNameFrom('attachment; filename="raporti-ceshtjet-aktive-2026-11-12.xlsx"')).toBe(
      'raporti-ceshtjet-aktive-2026-11-12.xlsx',
    )
    expect(fileNameFrom('attachment; filename=statistikat.xlsx')).toBe('statistikat.xlsx')
  })

  it('handles the ASP.NET Core form (both, filename first) and missing or broken values', () => {
    expect(fileNameFrom(`attachment; filename=a.xlsx; filename*=UTF-8''a.xlsx`)).toBe('a.xlsx')
    expect(fileNameFrom(`attachment; filename=b.xlsx; filename*=UTF-8''%E0%A4%A`)).toBe('b.xlsx')
    expect(fileNameFrom('attachment')).toBeNull()
    expect(fileNameFrom(null)).toBeNull()
  })
})

describe('formatMegabytes (download progress)', () => {
  it('shows megabytes with one decimal and the Albanian comma', () => {
    expect(formatMegabytes(0)).toBe('0,0 MB')
    expect(formatMegabytes(1024 * 1024 * 3.45)).toBe('3,5 MB')
    expect(formatMegabytes(1024 * 1024 * 1234)).toBe('1.234,0 MB')
  })
})

import { describe, expect, it } from 'vitest'
import { formatDateTime } from './dates'

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

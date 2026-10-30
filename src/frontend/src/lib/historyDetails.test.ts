import { describe, expect, it } from 'vitest'
import { detailRows, displayValue } from './historyDetails'

describe('history details as readable rows', () => {
  it('shows a transfer as name before/after, without the public ids', () => {
    const rows = detailRows(
      { name: 'Ilir Mëparshmi', userPublicId: '0b5a…' },
      { name: 'Avokat1 Dev', userPublicId: '1c6b…', reason: 'Ngarkesë pune' },
    )
    expect(rows).toEqual([
      { key: 'name', label: 'Emri', before: 'Ilir Mëparshmi', after: 'Avokat1 Dev' },
      { key: 'reason', label: 'Arsyeja', before: '—', after: 'Ngarkesë pune' },
    ])
  })

  it('a creation has only "after" values; codes, booleans and dates are translated', () => {
    const rows = detailRows(null, {
      kind: 'LEGAL',
      possibleDuplicateConfirmed: false,
      hearingAt: '2026-11-12T08:30:00Z',
    })
    expect(rows.map((r) => [r.label, r.before, r.after])).toEqual([
      ['Lloji', '—', 'Person juridik'],
      ['Regjistruar edhe pse e ngjashme', '—', 'Jo'],
      ['Seanca', '—', '12.11.2026 09:30'],
    ])
  })

  it('flattens nested objects and keeps unknown keys readable', () => {
    expect(detailRows(null, { head: { name: 'Kryetar Dev' }, customField: 7 })).toEqual([
      { key: 'head.name', label: 'Emri', before: '—', after: 'Kryetar Dev' },
      { key: 'customField', label: 'customField', before: '—', after: '7' },
    ])
  })

  it('handles values that are not objects', () => {
    expect(detailRows('text', 42)).toEqual([])
    expect(displayValue('mode', 'DELEGATED')).toBe('Delegim')
    expect(displayValue('items', ['a', 'b'])).toBe('a, b')
    expect(displayValue('receivedDate', '2026-03-29')).toBe('29.03.2026')
  })
})

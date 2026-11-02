import { describe, expect, it } from 'vitest'
import { detailRows, displayValue, historyLine } from './historyDetails'

describe('history line (task 028)', () => {
  it('reads a judge change from the stored names', () => {
    expect(historyLine('JUDGE_CHANGED', { name: 'Ilir Gjyqtari' }, { name: 'Klodiana Çela' }, 'x')).toBe(
      'Gjyqtari u ndryshua: Ilir Gjyqtari → Klodiana Çela',
    )
  })

  it('says "u caktua" when there was no judge, and falls back to the summary without names', () => {
    expect(historyLine('JUDGE_CHANGED', null, { name: 'Klodiana Çela' }, 'x')).toBe('Gjyqtari u caktua: Klodiana Çela')
    expect(historyLine('JUDGE_CHANGED', null, null, 'Gjyqtari u ndryshua.')).toBe('Gjyqtari u ndryshua.')
  })

  it('keeps the object change short (the texts are in the expandable view)', () => {
    expect(historyLine('OBJECT_CHANGED', { objectText: 'a' }, { objectText: 'b' }, 'Objekti u ndryshua.')).toBe(
      'Objekti u ndryshua',
    )
  })

  it('uses the summary for every other event', () => {
    expect(historyLine('PARTY_ADDED', null, { displayName: 'A' }, 'Pala u shtua: A.')).toBe('Pala u shtua: A.')
  })
})

describe('money in the history (task 025)', () => {
  it('shows claim amounts in Albanian notation, exactly', () => {
    expect(displayValue('claimAmount', '9999999999999999.99')).toBe('9.999.999.999.999.999,99')
  })
})

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

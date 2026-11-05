import { describe, expect, it } from 'vitest'
import { highlightParts } from './highlight'

const marked = (text: string, q: string) =>
  highlightParts(text, q)
    .filter((p) => p.match)
    .map((p) => p.text)

describe('highlightParts (header search, task 047)', () => {
  it('marks a part of a code', () => {
    expect(marked('2026-686640/1', '640')).toEqual(['640'])
  })

  it('ignores accents and case both ways, keeping the original characters', () => {
    expect(marked('Kjo çështje civile', 'ceshtje')).toEqual(['çështje'])
    expect(marked('Petrit HOXHA', 'hoxha')).toEqual(['HOXHA'])
    expect(marked('ceshtje', 'Çështje')).toEqual(['ceshtje'])
  })

  it('marks every word and every occurrence; the parts join back to the text', () => {
    const text = 'Pala: Petrit Hoxha dhe Hoxha Ana'
    expect(marked(text, 'Hoxha Petrit')).toEqual(['Petrit', 'Hoxha', 'Hoxha'])
    expect(
      highlightParts(text, 'Hoxha')
        .map((p) => p.text)
        .join(''),
    ).toBe(text)
  })

  it('leaves words under two letters and empty input alone', () => {
    expect(marked('A b c', 'a')).toEqual([])
    expect(highlightParts('', 'abc')).toEqual([])
    expect(highlightParts('abc', '')).toEqual([{ text: 'abc', match: false }])
  })
})

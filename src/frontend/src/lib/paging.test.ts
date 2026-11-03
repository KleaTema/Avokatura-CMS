import { describe, expect, it } from 'vitest'
import { pushCursor, resolvePage, storageKey, withoutPage } from './paging'

describe('keyset paging (cursor stack)', () => {
  it('shows the page of the URL only when its cursor is known', () => {
    expect(resolvePage([], null)).toBe(1)
    expect(resolvePage([], '3')).toBe(1) // a shared link: no cursors in this browser tab
    expect(resolvePage(['c2', 'c3'], '3')).toBe(3)
    expect(resolvePage(['c2'], '2')).toBe(2)
    expect(resolvePage(['c2'], 'x')).toBe(1)
    expect(resolvePage(['c2'], '-4')).toBe(1)
  })

  it('keeps the cursors up to the page and adds the next one', () => {
    expect(pushCursor([], 1, 'c2')).toEqual(['c2'])
    expect(pushCursor(['c2'], 2, 'c3')).toEqual(['c2', 'c3'])
    // back on page 1 after visiting page 3: going forward again replaces the later cursors
    expect(pushCursor(['c2', 'c3'], 1, 'n2')).toEqual(['n2'])
  })

  it('keys the cursors by path and filters, not by any page parameter or their order', () => {
    const a = storageKey('/cases', new URLSearchParams('tab=all&q=640&page=3&docspage=2'))
    const b = storageKey('/cases', new URLSearchParams('q=640&tab=all'))
    expect(a).toBe(b)
    expect(storageKey('/cases', new URLSearchParams('q=641&tab=all'))).not.toBe(b)
    expect(storageKey('/cases', new URLSearchParams('q=640&tab=all&size=25'))).not.toBe(b)
  })

  it('drops the page of its prefix only', () => {
    expect(withoutPage(new URLSearchParams('q=x&page=3&docspage=2&size=25')).toString()).toBe('q=x&docspage=2&size=25')
    expect(withoutPage(new URLSearchParams('page=3&docspage=2'), 'docs').toString()).toBe('page=3')
  })
})

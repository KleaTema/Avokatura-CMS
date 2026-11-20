import { QueryClient, QueryObserver } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { liveListQuery } from './liveQuery'
import { matches, normalize } from './search'
import { serverSearchText } from './useLiveSearch'

/** A fake list endpoint: the rows containing q, answered after `delayMs` (aborted → rejects, as fetch does). */
function fakeEndpoint(delays: Record<string, number>, aborted: string[]) {
  const rows = ['2026-0005', '2026-0056', '2026-0156', '2026-0700']
  return (q: string, signal: AbortSignal) =>
    new Promise<string[]>((resolve, reject) => {
      const timer = setTimeout(() => resolve(rows.filter((r) => r.includes(q))), delays[q] ?? 0)
      signal.addEventListener('abort', () => {
        clearTimeout(timer)
        aborted.push(q)
        reject(new DOMException('Aborted', 'AbortError'))
      })
    })
}

describe('live search (lib/liveQuery: the case list hook uses it)', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('typing "5" then "56" with a slow first answer shows only the "56" rows', async () => {
    vi.useFakeTimers()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const aborted: string[] = []
    // "5" answers late (1 s), "56" fast (50 ms): the late "5" answer must never replace the "56" rows
    const endpoint = fakeEndpoint({ '5': 1000, '56': 50 }, aborted)
    const options = (q: string) => liveListQuery(['cases', { q }] as const, (signal) => endpoint(q, signal))

    const observer = new QueryObserver(client, options('5'))
    const shown: string[][] = []
    const unsubscribe = observer.subscribe((result) => {
      if (result.data) shown.push(result.data)
    })

    await vi.advanceTimersByTimeAsync(10) // "5" is in flight
    observer.setOptions(options('56')) // the user typed "6"
    await vi.advanceTimersByTimeAsync(60) // "56" answered
    expect(observer.getCurrentResult().data).toEqual(['2026-0056', '2026-0156'])

    await vi.advanceTimersByTimeAsync(2000) // long after "5" would have answered
    expect(observer.getCurrentResult().data).toEqual(['2026-0056', '2026-0156'])
    // the "5" request was aborted when "56" replaced it, and its rows were never shown
    expect(aborted).toEqual(['5'])
    expect(shown.every((rows) => rows.every((r) => r.includes('56')))).toBe(true)

    unsubscribe()
    client.clear()
  })

  it('keeps the previous rows (placeholder) while the next text loads, then shows the new ones', async () => {
    vi.useFakeTimers()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const endpoint = fakeEndpoint({ '5': 10, '56': 200 }, [])
    const options = (q: string) => liveListQuery(['cases', { q }] as const, (signal) => endpoint(q, signal))

    const observer = new QueryObserver(client, options('5'))
    const unsubscribe = observer.subscribe(() => undefined)
    await vi.advanceTimersByTimeAsync(20)
    expect(observer.getCurrentResult().data).toHaveLength(3)

    observer.setOptions(options('56'))
    await vi.advanceTimersByTimeAsync(10)
    // no empty table: the "5" rows stay, marked as placeholder, while "56" loads
    expect(observer.getCurrentResult().isPlaceholderData).toBe(true)
    expect(observer.getCurrentResult().data).toHaveLength(3)

    await vi.advanceTimersByTimeAsync(250)
    expect(observer.getCurrentResult().isPlaceholderData).toBe(false)
    expect(observer.getCurrentResult().data).toEqual(['2026-0056', '2026-0156'])

    unsubscribe()
    client.clear()
  })
})

describe('matches / normalize (lists filtered in the browser: the server rule)', () => {
  it('is accent- and case-insensitive for Albanian', () => {
    expect(normalize('Përgjigje Çela')).toBe('pergjigje cela')
    expect(matches('pergjigje', 'Përgjigje e paditurit')).toBe(true)
    expect(matches('c', 'Çela')).toBe(true)
    expect(matches('Ë', 'Shëndetësisë')).toBe(true)
  })

  it('matches a substring from 1 character, every term somewhere', () => {
    expect(matches('5', '2026-0056')).toBe(true)
    expect(matches('56', '2026-0005')).toBe(false)
    expect(matches('2026-12', 'TR-2026-1234/1')).toBe(true)
    expect(matches('hoxha tir', 'Arben Hoxha', 'Gjykata Tiranë')).toBe(true)
    expect(matches('hoxha durr', 'Arben Hoxha', 'Gjykata Tiranë')).toBe(false)
  })

  it('sends the server only terms of 2 or more characters (1 character keeps the whole list)', () => {
    expect(serverSearchText('5')).toBe('')
    expect(serverSearchText(' a b ')).toBe('')
    expect(serverSearchText('56')).toBe('56')
    expect(serverSearchText(' e  Hoxha 5 ')).toBe('Hoxha')
  })

  it('treats % and _ as ordinary characters, and an empty text as no filter', () => {
    expect(matches('%', 'abc')).toBe(false)
    expect(matches('_', 'a_b')).toBe(true)
    expect(matches('   ', 'abc')).toBe(true)
  })
})

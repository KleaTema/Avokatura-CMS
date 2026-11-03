import { useLocation, useSearchParams } from 'react-router'

/** Rows per page: 10 by default; the footer offers these. */
export const PAGE_SIZES = [10, 25, 50] as const
export const DEFAULT_PAGE_SIZE = 10

const pageKey = (prefix: string) => `${prefix}page`
const sizeKey = (prefix: string) => `${prefix}size`

/** Every page parameter (`page`, `docspage`, …): changing one table's page must not reset another's cursors. */
const isPageParam = (name: string) => /page$/i.test(name)

function readSize(params: URLSearchParams, prefix: string): number {
  const size = Number(params.get(sizeKey(prefix)))
  return (PAGE_SIZES as readonly number[]).includes(size) ? size : DEFAULT_PAGE_SIZE
}

/** The same list with other filters: drop the page of `prefix` (the page size stays). */
export function withoutPage(params: URLSearchParams, prefix = ''): URLSearchParams {
  const next = new URLSearchParams(params)
  next.delete(pageKey(prefix))
  return next
}

/** The sessionStorage key of a list's cursors: the path and its filters (sorted), without any page parameter. */
export function storageKey(pathname: string, params: URLSearchParams): string {
  const filters: [string, string][] = []
  params.forEach((value, name) => {
    if (!isPageParam(name)) filters.push([name, value])
  })
  filters.sort(([a], [b]) => a.localeCompare(b))
  return `ash.pager:${pathname}?${new URLSearchParams(filters).toString()}`
}

function readStack(key: string): string[] {
  try {
    const raw = sessionStorage.getItem(key)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((x): x is string => typeof x === 'string') : []
  } catch {
    return []
  }
}

function writeStack(key: string, stack: string[]) {
  try {
    sessionStorage.setItem(key, JSON.stringify(stack))
  } catch {
    // private mode / full storage: paging still works, only Back may land on page 1
  }
}

export interface KeysetPaging {
  /** 1-based */
  page: number
  size: number
  /** the cursor of this page (`after`); undefined on page 1 */
  cursor: string | undefined
  next: (nextCursor: string) => void
  previous: () => void
  setSize: (size: number) => void
}

/**
 * Keyset paging for a server list (rule 10: no OFFSET). The page number and size live in the URL (`page`, `size`,
 * with an optional prefix for a second table on the same page); the cursors of the pages visited are kept in
 * sessionStorage under the URL without its page, so Back / refresh return to the same page. A page whose cursor is
 * unknown (a shared link, another browser tab) starts again at page 1.
 */
/**
 * The page to show: the one in the URL when its cursor is known, else page 1. stack[i] = the cursor of page i + 2
 * (page 1 has none).
 */
export function resolvePage(stack: string[], pageParam: string | null): number {
  const requested = Math.max(1, Math.floor(Number(pageParam) || 1))
  return requested > 1 && stack[requested - 2] === undefined ? 1 : requested
}

/** The cursors after going from `page` to the next one: later pages are dropped, the new cursor kept. */
export function pushCursor(stack: string[], page: number, nextCursor: string): string[] {
  const updated = stack.slice(0, page - 1)
  updated[page - 1] = nextCursor
  return updated
}

export function useKeysetPaging(prefix = ''): KeysetPaging {
  const [params, setParams] = useSearchParams()
  const { pathname } = useLocation()
  const key = storageKey(pathname, params)
  const stack = readStack(key)
  const page = resolvePage(stack, params.get(pageKey(prefix)))
  const size = readSize(params, prefix)

  const goTo = (target: number, change?: (p: URLSearchParams) => void) =>
    setParams(
      (prev) => {
        const nextParams = new URLSearchParams(prev)
        if (target > 1) nextParams.set(pageKey(prefix), String(target))
        else nextParams.delete(pageKey(prefix))
        change?.(nextParams)
        return nextParams
      },
      { replace: true },
    )

  return {
    page,
    size,
    cursor: page > 1 ? stack[page - 2] : undefined,
    next: (nextCursor) => {
      writeStack(key, pushCursor(stack, page, nextCursor))
      goTo(page + 1)
    },
    previous: () => goTo(Math.max(1, page - 1)),
    setSize: (value) =>
      goTo(1, (p) => {
        if (value === DEFAULT_PAGE_SIZE) p.delete(sizeKey(prefix))
        else p.set(sizeKey(prefix), String(value))
      }),
  }
}

export interface ClientPaging<T> {
  items: T[]
  page: number
  size: number
  total: number
  setPage: (page: number) => void
  setSize: (size: number) => void
}

/** Numbered paging of a list loaded at once (lookups, offices, file types): page and size in the URL. */
export function useClientPaging<T>(all: T[], prefix = ''): ClientPaging<T> {
  const [params, setParams] = useSearchParams()
  const size = readSize(params, prefix)
  const pages = Math.max(1, Math.ceil(all.length / size))
  const page = Math.min(pages, Math.max(1, Math.floor(Number(params.get(pageKey(prefix))) || 1)))
  const update = (change: (p: URLSearchParams) => void) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        change(next)
        return next
      },
      { replace: true },
    )

  return {
    items: all.slice((page - 1) * size, page * size),
    page,
    size,
    total: all.length,
    setPage: (target) =>
      update((p) => {
        if (target > 1) p.set(pageKey(prefix), String(target))
        else p.delete(pageKey(prefix))
      }),
    setSize: (value) =>
      update((p) => {
        p.delete(pageKey(prefix))
        if (value === DEFAULT_PAGE_SIZE) p.delete(sizeKey(prefix))
        else p.set(sizeKey(prefix), String(value))
      }),
  }
}

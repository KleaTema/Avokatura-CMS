import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type SearchResult = components['schemas']['SearchResultItem']

/** The order of the groups = the ranking of GET /api/search. */
export const MATCH_ORDER = ['CODE', 'ACT', 'PARTY', 'OBJECT', 'JUDGE', 'INSTITUTION'] as const

export const SEARCH_LIMIT = 8

/**
 * GET /api/search (task 047). The text is the key: a newer text starts a new request and TanStack aborts the
 * previous one (its AbortSignal reaches fetch), so an older answer can never replace a newer one. Shorter than
 * 2 characters: no request. The previous results stay on screen while the next ones load (no layout jump).
 */
export const useGlobalSearch = (q: string) =>
  useQuery({
    queryKey: ['global-search', q],
    enabled: q.length >= 2,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    queryFn: ({ signal }) => api.GET('/api/search', { params: { query: { q, limit: SEARCH_LIMIT } }, signal }).then(unwrap),
  })

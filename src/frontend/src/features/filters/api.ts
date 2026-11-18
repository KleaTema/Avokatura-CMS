import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

/** One filter of a table (GET /api/filters/{table}) — the FilterBar's FilterDef. */
export type TableFilter = components['schemas']['TableFilter']

/**
 * Task 053m — a table's filters: its ONE metadata source on the server (the options limited to the user's scope).
 * `context`: the page's query the options depend on (the case list's tab).
 */
export const useTableFilters = (table: string, context: Record<string, string> = {}) =>
  useQuery({
    queryKey: ['filters', table, context] as const,
    staleTime: 5 * 60_000,
    placeholderData: keepPreviousData,
    queryFn: () =>
      api
        // the context is free-form (the endpoint reads the raw query): serialized as is
        .GET('/api/filters/{table}', { params: { path: { table } }, querySerializer: () => new URLSearchParams(context).toString() })
        .then(unwrap),
  })

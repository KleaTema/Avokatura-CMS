import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { serverQuery } from '../../lib/search'

/** Public reference lists (pickers). Keys follow the API path: ['reference', 'hearing-types'], ['reference', 'courts'], … */
export const referenceKeys = {
  all: ['reference'] as const,
  list: (name: string) => ['reference', name] as const,
}

const LIST_STALE_MS = 5 * 60_000

export type Court = components['schemas']['CourtResponse']
export type Judge = components['schemas']['JudgeResponse']
export type PartyRole = components['schemas']['PartyRoleResponse']
export type HearingType = components['schemas']['HearingTypeResponse']
export type SummonsType = components['schemas']['SummonsTypeResponse']

export const useCourts = () =>
  useQuery({
    queryKey: referenceKeys.list('courts'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/courts').then(unwrap),
  })

export const usePartyRoles = () =>
  useQuery({
    queryKey: referenceKeys.list('party-roles'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/party-roles').then(unwrap),
  })

export const useSummonsTypes = () =>
  useQuery({
    queryKey: referenceKeys.list('summons-types'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/summons-types').then(unwrap),
  })

export const useHearingTypes = () =>
  useQuery({
    queryKey: referenceKeys.list('hearing-types'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/hearing-types').then(unwrap),
  })

/**
 * Active judges (server search, at most 50): of one court (q optional), or of all courts (q of 2+ characters).
 * Disabled when neither a court nor a long enough q is given, or when `enabled` is false.
 */
export const useJudgeSearch = (courtId: number | null, q: string, enabled = true) => {
  const query = serverQuery(q)
  return useQuery({
    queryKey: ['reference', 'judges', courtId ?? 'all', query] as const,
    enabled: enabled && (courtId !== null || query !== ''),
    staleTime: 60_000,
    queryFn: () =>
      api
        .GET('/api/reference/judges', { params: { query: { courtId: courtId ?? undefined, q: query || undefined } } })
        .then(unwrap),
  })
}

export const useCourtLevels = () =>
  useQuery({
    queryKey: referenceKeys.list('court-levels'),
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/reference/court-levels').then(unwrap),
  })

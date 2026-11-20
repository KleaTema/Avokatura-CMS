import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

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

export type DecisionResult = components['schemas']['DecisionResultResponse']

/** The active decision results (Rezultati i çështjes), in the list's order. */
export const useDecisionResults = () =>
  useQuery({
    queryKey: referenceKeys.list('decision-results'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/decision-results').then(unwrap),
  })

/**
 * Every active judge (task 054a), loaded once and cached; an admin change of judges or courts invalidates it
 * (invalidateLookup → ['reference', 'judges']). JudgePicker filters it in the browser.
 */
export const useActiveJudges = (enabled = true) =>
  useQuery({
    queryKey: ['reference', 'judges', 'active'] as const,
    enabled,
    staleTime: Infinity,
    queryFn: ({ signal }) => api.GET('/api/reference/judges/active', { signal }).then(unwrap),
  })

export type DocumentType = components['schemas']['DocumentTypeResponse']
export type AllowedFileType = components['schemas']['AllowedFileTypeResponse']

export const useDocumentTypes = () =>
  useQuery({
    queryKey: referenceKeys.list('document-types'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/document-types').then(unwrap),
  })

/** Extensions and size limits (task 035: the upload's quick check before sending — the server checks again). */
export const useAllowedFileTypes = () =>
  useQuery({
    queryKey: referenceKeys.list('allowed-file-types'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/allowed-file-types').then(unwrap),
  })

export const useCaseCategories = () =>
  useQuery({
    queryKey: referenceKeys.list('case-categories'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/case-categories').then(unwrap),
  })

/** Subcategories with their category's code (only NDJEKJE has any). */
export const useCaseSubcategories = () =>
  useQuery({
    queryKey: referenceKeys.list('case-subcategories'),
    staleTime: LIST_STALE_MS,
    queryFn: () => api.GET('/api/reference/case-subcategories').then(unwrap),
  })

export const useCourtLevels = () =>
  useQuery({
    queryKey: referenceKeys.list('court-levels'),
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/reference/court-levels').then(unwrap),
  })

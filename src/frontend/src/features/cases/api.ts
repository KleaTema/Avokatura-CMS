import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, ApiError, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { MIN_SERVER_QUERY, normalize } from '../../lib/search'

export type RegisterCaseRequest = components['schemas']['RegisterCaseRequest']
export type RegisterCaseResponse = components['schemas']['RegisterCaseResponse']
export type PartyRequest = components['schemas']['PartyRequest']

/** POST /api/cases/register. Nothing to invalidate yet: the case list (task 022) will add its key here. */
export const useRegisterCase = () =>
  useMutation({
    mutationFn: (body: RegisterCaseRequest) => api.POST('/api/cases/register', { body }).then(unwrap),
  })

export type RegistrationSearchItem = components['schemas']['RegistrationSearchItem']
export type NextLevelPreview = components['schemas']['NextLevelPreviewResponse']
export type NotifyNotClosedRequest = components['schemas']['NotifyNotClosedRequest']

export type RegisterNextLevelRequest = components['schemas']['RegisterNextLevelRequest']

/** The next level sends the summons only: the object text and the parties are copied from the previous level. */
export const toNextLevelRequest = (r: RegisterCaseRequest): RegisterNextLevelRequest => ({
  summonsNumber: r.summonsNumber,
  summonsTypeCode: r.summonsTypeCode,
  actNumber: r.actNumber,
  actDate: r.actDate,
  receivedDate: r.receivedDate,
  courtLevelCode: r.courtLevelCode,
  courtId: r.courtId,
  judgeId: r.judgeId,
  firstHearing: r.firstHearing,
  confirmPossibleDuplicate: r.confirmPossibleDuplicate,
})

const SEARCH_PAGE_SIZE = 20

/** Closed (or not yet closed) dossiers by code, act number, party name or NIPT — Protokoll's registration lookup. */
export const useRegistrationSearch = (q: string, courtId: string) =>
  useInfiniteQuery({
    queryKey: ['registration-search', q, courtId] as const,
    enabled: q.length >= MIN_SERVER_QUERY,
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/cases/registration-search', {
          params: {
            query: { q, courtId: courtId ? Number(courtId) : undefined, after: pageParam, limit: SEARCH_PAGE_SIZE },
          },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

/** What continuing will copy; a 409 (already continued, not closed, archived) comes back as an ApiError. */
export const useNextLevelPreview = (previousCasePublicId: string | null) =>
  useQuery({
    queryKey: ['next-level-preview', previousCasePublicId] as const,
    enabled: previousCasePublicId !== null,
    retry: false,
    queryFn: () =>
      api
        .GET('/api/cases/{previousCasePublicId}/next-level/preview', {
          params: { path: { previousCasePublicId: previousCasePublicId ?? '' } },
        })
        .then(unwrap),
  })

export const useRegisterNextLevel = (previousCasePublicId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: RegisterCaseRequest) =>
      api
        .POST('/api/cases/{previousCasePublicId}/next-level', {
          params: { path: { previousCasePublicId } },
          body: toNextLevelRequest(body),
        })
        .then(unwrap),
    // the continued dossier now shows its new level in the search
    onSuccess: () => qc.invalidateQueries({ queryKey: ['registration-search'] }),
  })
}

export const useNotifyNotClosed = (previousCasePublicId: string) =>
  useMutation({
    mutationFn: (body: NotifyNotClosedRequest) =>
      api
        .POST('/api/cases/{previousCasePublicId}/notify-not-closed', { params: { path: { previousCasePublicId } }, body })
        .then(unwrap),
  })

const possibleDuplicateSchema = z.object({
  matches: z.array(
    z.object({ caseCode: z.string(), status: z.string(), courtName: z.string(), registeredAt: z.string() }),
  ),
  hiddenCount: z.number(),
})

export type PossibleDuplicates = z.infer<typeof possibleDuplicateSchema>

/** The extensions of a 409 case.possible_duplicate (cases the user may see, plus how many are hidden). */
export function possibleDuplicates(error: unknown): PossibleDuplicates | null {
  if (!(error instanceof ApiError) || error.problem?.code !== 'case.possible_duplicate') return null
  const parsed = possibleDuplicateSchema.safeParse(error.problem)
  return parsed.success ? parsed.data : { matches: [], hiddenCount: 0 }
}

/**
 * Whether a case with exactly this act number exists that Protokoll could continue at the next level
 * (registration search, needs case.link_previous). The 409 above does not say which match had the same act number.
 */
export async function hasCaseWithActNumber(actNumber: string): Promise<boolean> {
  const wanted = normalize(actNumber.trim())
  try {
    const page = await api
      .GET('/api/cases/registration-search', { params: { query: { q: actNumber.trim(), limit: 50 } } })
      .then(unwrap)
    return page.items.some((item) => normalize(item.actNumber) === wanted)
  } catch {
    return false // the offer is a convenience; without it the user can still cancel or register anyway
  }
}

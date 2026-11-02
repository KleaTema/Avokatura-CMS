import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, ApiError, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { MIN_SERVER_QUERY, normalize } from '../../lib/search'

export type RegisterCaseRequest = components['schemas']['RegisterCaseRequest']
export type RegisterCaseResponse = components['schemas']['RegisterCaseResponse']
export type PartyRequest = components['schemas']['PartyRequest']

// A new case or level shows in the lists and the tab counts.
const invalidateCaseLists = (qc: QueryClient) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: caseListKeys.all }),
    qc.invalidateQueries({ queryKey: caseListKeys.tabs }),
  ])

/** POST /api/cases/register. */
export const useRegisterCase = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: RegisterCaseRequest) => api.POST('/api/cases/register', { body }).then(unwrap),
    onSuccess: () => invalidateCaseLists(qc),
  })
}

export type CaseListItem = components['schemas']['CaseListItem']

/** The list's filters as the URL holds them (all optional; the tab comes from the API's default when missing). */
export interface CaseListFilters {
  tab: string
  q: string
  /** '' = open cases (REGISTERED + ACTIVE), 'CLOSED' = closed ones */
  status: string
  courtId: string
  categoryCode: string
  priority: string
  /** '' | 'true' | 'false' */
  suspended: string
  /** Tirane calendar dates, yyyy-MM-dd */
  hearingFrom: string
  hearingTo: string
  /** public id of a lawyer (current primary or secondary) */
  lawyerId: string
}

const LIST_PAGE_SIZE = 50

export const caseListKeys = {
  all: ['cases', 'list'] as const,
  list: (f: CaseListFilters) => ['cases', 'list', f] as const,
  tabs: ['cases', 'tabs'] as const,
}

/** The user's tabs with open-case counts and the tab to open first. */
export const useCaseTabs = () =>
  useQuery({
    queryKey: caseListKeys.tabs,
    staleTime: 60_000,
    queryFn: () => api.GET('/api/cases/tabs').then(unwrap),
  })

/**
 * One tab of the case list, keyset-paged. The loaded pages stay in the cache for a minute, so Back from a case
 * shows the same list at the same length without reloading it from the first page.
 */
export const useCaseList = (f: CaseListFilters, enabled: boolean) =>
  useInfiniteQuery({
    queryKey: caseListKeys.list(f),
    enabled,
    staleTime: 60_000,
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/cases', {
          params: {
            query: {
              tab: f.tab || undefined,
              q: f.q || undefined,
              status: f.status || undefined,
              courtId: f.courtId ? Number(f.courtId) : undefined,
              categoryCode: f.categoryCode || undefined,
              priority: f.priority || undefined,
              suspended: f.suspended === '' ? undefined : f.suspended === 'true',
              hearingFrom: f.hearingFrom || undefined,
              hearingTo: f.hearingTo || undefined,
              lawyerId: f.lawyerId || undefined,
              after: pageParam,
              limit: LIST_PAGE_SIZE,
            },
          },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

// ---------------------------------------------------------------- case page (task 022)

export type CaseDetail = components['schemas']['GetCaseResponse']
export type CaseHistoryItem = components['schemas']['CaseHistoryItem']
export type PartyResponse = components['schemas']['PartyResponse']
export type RemovePartyResponse = components['schemas']['RemovePartyResponse']

export const caseKeys = {
  detail: (id: string) => ['case', id] as const,
  parties: (id: string) => ['case-parties', id] as const,
  history: (id: string) => ['case-history', id] as const,
}

/** GET /api/cases/{id}; 404 (no access or no case — never told apart) is not retried. */
export const useCase = (id: string) =>
  useQuery({
    queryKey: caseKeys.detail(id),
    retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    queryFn: () => api.GET('/api/cases/{id}', { params: { path: { id } } }).then(unwrap),
  })

const HISTORY_PAGE_SIZE = 30

/** The dossier's history (all levels the user can view), newest first. */
export const useCaseHistory = (id: string, enabled: boolean) =>
  useInfiniteQuery({
    queryKey: caseKeys.history(id),
    enabled,
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/cases/{id}/history', {
          params: { path: { id }, query: { after: pageParam, limit: HISTORY_PAGE_SIZE } },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

// A party change shows in the case (parties, institutions), the party list and the history.
const invalidateCaseParties = (qc: QueryClient, id: string) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: caseKeys.detail(id) }),
    qc.invalidateQueries({ queryKey: caseKeys.parties(id) }),
    qc.invalidateQueries({ queryKey: caseKeys.history(id) }),
  ])

export const useAddParty = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: PartyRequest) =>
      api.POST('/api/cases/{caseId}/parties', { params: { path: { caseId } }, body }).then(unwrap),
    onSuccess: () => invalidateCaseParties(qc, caseId),
  })
}

export const useUpdateParty = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ partyId, body }: { partyId: string; body: PartyRequest }) =>
      api
        .PUT('/api/cases/{caseId}/parties/{partyId}', { params: { path: { caseId, partyId } }, body })
        .then(unwrap),
    onSuccess: () => invalidateCaseParties(qc, caseId),
  })
}

/** endInstitutionLink: the user's answer, asked BEFORE the call when it is the institution's last party (task 017). */
export const useRemoveParty = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ partyId, endInstitutionLink }: { partyId: string; endInstitutionLink?: boolean }) =>
      api
        .DELETE('/api/cases/{caseId}/parties/{partyId}', {
          params: { path: { caseId, partyId }, query: { endInstitutionLink } },
        })
        .then(unwrap),
    onSuccess: () => invalidateCaseParties(qc, caseId),
  })
}

// ---------------------------------------------------------------- assigning lawyers (task 023)

export type AssignableLawyer = components['schemas']['AssignableLawyer']
export type OfficeLawyer = components['schemas']['OfficeLawyer']
export type AssignLawyersRequest = components['schemas']['AssignLawyersRequest']

/** The lawyers the Kryetar may assign in this case, least loaded first (the Kryetar himself with isSelf). */
export const useAssignableLawyers = (caseId: string) =>
  useQuery({
    queryKey: ['assignable-lawyers', caseId] as const,
    retry: false,
    queryFn: () => api.GET('/api/cases/{id}/assignable-lawyers', { params: { path: { id: caseId } } }).then(unwrap),
  })

/** The lawyers of the user's office, for the lawyer filter of the case list (case.view_office). */
export const useOfficeLawyers = (enabled: boolean) =>
  useQuery({
    queryKey: ['office-lawyers'] as const,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/cases/office-lawyers').then(unwrap),
  })

/** POST assignment: the case becomes ACTIVE, moves between the tabs and changes its page and history. */
export const useAssignLawyers = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: AssignLawyersRequest) =>
      api.POST('/api/cases/{id}/assignment', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: () =>
      Promise.all([
        invalidateCaseLists(qc),
        qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
        qc.invalidateQueries({ queryKey: caseKeys.history(caseId) }),
        qc.invalidateQueries({ queryKey: ['assignable-lawyers'] }),
      ]),
  })
}

export type TransferLawyerRequest = components['schemas']['TransferLawyerRequest']
export type CaseTeamMember = components['schemas']['CaseTeamMember']

/** POST transfer (task 024): the team (part of the case), its history, the lists ("mine" tabs) and the workloads change. */
export const useTransferLawyer = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: TransferLawyerRequest) =>
      api.POST('/api/cases/{id}/transfer', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: () =>
      Promise.all([
        invalidateCaseLists(qc),
        qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
        qc.invalidateQueries({ queryKey: caseKeys.history(caseId) }),
        qc.invalidateQueries({ queryKey: ['assignable-lawyers'] }),
      ]),
  })
}

// ---------------------------------------------------------------- case data (task 025)

export type UpdateClaimRequest = components['schemas']['UpdateClaimRequest']
export type SetCategoryRequest = components['schemas']['SetCategoryRequest']
export type SetSuspensionRequest = components['schemas']['SetSuspensionRequest']
export type SetSecurityMeasureRequest = components['schemas']['SetSecurityMeasureRequest']
export type SetPriorityRequest = components['schemas']['SetPriorityRequest']

/**
 * One save of the case's data. The response's rowVersion (and changed fields) go straight into the case in the query
 * cache: the case page then shows them, and the next save sends that rowVersion — consecutive saves of the same user
 * never conflict with each other. The history and the lists (priority, suspension) reload.
 */
function useCaseDataSave<TBody, TResult extends { rowVersion: number | string }>(
  caseId: string,
  send: (body: TBody) => Promise<TResult>,
  apply: (c: CaseDetail, result: TResult) => CaseDetail,
) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: send,
    onSuccess: (result) => {
      qc.setQueryData<CaseDetail>(caseKeys.detail(caseId), (c) =>
        c ? apply({ ...c, header: { ...c.header, rowVersion: result.rowVersion } }, result) : c,
      )
      return Promise.all([qc.invalidateQueries({ queryKey: caseKeys.history(caseId) }), invalidateCaseLists(qc)])
    },
  })
}

const path = (id: string) => ({ params: { path: { id } } })

export const useUpdateClaim = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: UpdateClaimRequest) => api.PATCH('/api/cases/{id}/claim', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({
      ...c,
      data: {
        ...c.data,
        claim:
          r.claimAmount === null && r.claimAreaM2 === null
            ? null
            : { amount: r.claimAmount, currency: r.claimCurrency, areaM2: r.claimAreaM2 },
      },
    }),
  )

export const useSetCategory = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: SetCategoryRequest) => api.PATCH('/api/cases/{id}/category', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, data: { ...c.data, category: r.category, subcategory: r.subcategory } }),
  )

export const useSetSuspension = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: SetSuspensionRequest) => api.PATCH('/api/cases/{id}/suspension', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({
      ...c,
      header: { ...c.header, isSuspended: r.isSuspended },
      data: { ...c.data, suspendedAt: r.suspendedAt },
    }),
  )

export const useSetSecurityMeasure = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: SetSecurityMeasureRequest) =>
      api.PUT('/api/cases/{id}/security-measure', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, data: { ...c.data, hasSecurityMeasure: r.hasSecurityMeasure, securityMeasure: r.measure } }),
  )

export const useSetPriority = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: SetPriorityRequest) => api.PATCH('/api/cases/{id}/priority', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, header: { ...c.header, priority: r.priority } }),
  )

// ---------------------------------------------------------------- legal assistants (task 026)

export type AssignableAssistant = components['schemas']['AssignableAssistant']

/** Legal assistants of the case's office not yet on the case, least loaded first. */
export const useAssignableAssistants = (caseId: string, enabled: boolean) =>
  useQuery({
    queryKey: ['assignable-assistants', caseId] as const,
    enabled,
    retry: false,
    queryFn: () =>
      api.GET('/api/cases/{id}/assignable-assistants', { params: { path: { id: caseId } } }).then(unwrap),
  })

/**
 * After adding or removing an assistant: the new rowVersion goes into the cached case at once (the next save uses
 * it), then the case (team), its history, the lists and the picker reload.
 */
const afterAssistantChange = (qc: QueryClient, caseId: string, rowVersion: number | string) => {
  qc.setQueryData<CaseDetail>(caseKeys.detail(caseId), (c) => (c ? { ...c, header: { ...c.header, rowVersion } } : c))
  return Promise.all([
    qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.history(caseId) }),
    qc.invalidateQueries({ queryKey: ['assignable-assistants', caseId] }),
    invalidateCaseLists(qc),
  ])
}

export const useAddAssistant = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { userPublicId: string; rowVersion: number }) =>
      api.POST('/api/cases/{id}/assistants', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterAssistantChange(qc, caseId, r.rowVersion),
  })
}

export const useRemoveAssistant = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ userPublicId, rowVersion }: { userPublicId: string; rowVersion: number }) =>
      api
        .DELETE('/api/cases/{id}/assistants/{userPublicId}', {
          params: { path: { id: caseId, userPublicId }, query: { rowVersion } },
        })
        .then(unwrap),
    onSuccess: (r) => afterAssistantChange(qc, caseId, r.rowVersion),
  })
}

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
    // the continued dossier now shows its new level in the search, and the new level in the case lists
    onSuccess: () =>
      Promise.all([qc.invalidateQueries({ queryKey: ['registration-search'] }), invalidateCaseLists(qc)]),
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

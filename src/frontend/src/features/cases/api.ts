import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { api, ApiError, unwrap } from '../../api/client'
import type { components, paths } from '../../api/schema'
import { liveListQuery } from '../../lib/liveQuery'
import { normalize } from '../../lib/search'

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
  /**
   * '' = not chosen: open cases (REGISTERED + ACTIVE) — or every status when q is given (the API decides);
   * 'OPEN' | 'CLOSED' | 'ALL' chosen explicitly
   */
  status: string
  courtLevel: string
  courtId: string
  /** a judge id */
  judgeId: string
  categoryCode: string
  priority: string
  /** '' | 'true' | 'false' */
  suspended: string
  /** Tirane calendar dates, yyyy-MM-dd */
  hearingFrom: string
  hearingTo: string
  registeredFrom: string
  registeredTo: string
  /** public id of a lawyer (current primary or secondary) */
  lawyerId: string
  /** public id of an office (the Monitor, task 031) */
  officeId: string
  /** public id of an institution that is a party */
  institutionId: string
  /** a dashboard filter code (task 046): missingData30, hearingOverdue, decisionPending, … ('' = none) */
  filter: string
  /** task 053m: a part of the case code / of the act number ('' = none) */
  code?: string
  actNumber?: string
}

/** One page of a keyset list: the cursor of the page (none = page 1) and its size. */
export interface PageRequest {
  cursor: string | undefined
  size: number
}

export const caseListKeys = {
  all: ['cases', 'list'] as const,
  list: (f: CaseListFilters, p: PageRequest) => ['cases', 'list', f, p.cursor ?? null, p.size] as const,
  tabs: ['cases', 'tabs'] as const,
}

/** The user's tabs with open-case counts and the tab to open first. */
export const useCaseTabs = (enabled = true) =>
  useQuery({
    queryKey: caseListKeys.tabs,
    enabled,
    staleTime: 60_000,
    queryFn: () => api.GET('/api/cases/tabs').then(unwrap),
  })

/** Widest first: a search should cover every case the user may see, not only the default tab's ("Pa avokat"). */
const SEARCH_TAB_ORDER = ['all', 'office', 'institution', 'mine', 'registered', 'unassigned']

/** The tab the header search opens: the user's widest tab (undefined → the list's default). */
export const searchTabOf = (tabs: { tab: string }[]) =>
  SEARCH_TAB_ORDER.find((code) => tabs.some((x) => x.tab === code))

/**
 * One page of a tab of the case list (keyset, with the total of the filtered list). Pages stay in the cache for a
 * minute, so Back from a case shows the same page without loading; the previous page stays visible while the next
 * one loads.
 */
export const useCaseList = (f: CaseListFilters, p: PageRequest, enabled: boolean) =>
  useQuery({
    enabled,
    staleTime: 60_000,
    // live search (lib/liveQuery): keyed by the text, a newer one aborts this request, the old rows stay meanwhile
    ...liveListQuery(caseListKeys.list(f, p), (signal) =>
      api
        .GET('/api/cases', {
          signal,
          params: {
            query: {
              tab: f.tab || undefined,
              q: f.q || undefined,
              status: f.status || undefined,
              courtLevel: f.courtLevel || undefined,
              courtId: f.courtId ? Number(f.courtId) : undefined,
              judgeId: f.judgeId ? Number(f.judgeId) : undefined,
              categoryCode: f.categoryCode || undefined,
              priority: f.priority || undefined,
              suspended: f.suspended === '' ? undefined : f.suspended === 'true',
              hearingFrom: f.hearingFrom || undefined,
              hearingTo: f.hearingTo || undefined,
              registeredFrom: f.registeredFrom || undefined,
              registeredTo: f.registeredTo || undefined,
              lawyerId: f.lawyerId || undefined,
              officeId: f.officeId || undefined,
              institutionId: f.institutionId || undefined,
              filter: f.filter || undefined,
              code: f.code || undefined,
              actNumber: f.actNumber || undefined,
              after: p.cursor,
              limit: p.size,
            },
          },
        })
        .then(unwrap),
    ),
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
  /** every picker of a case (lawyers, assistants, jurists): one prefix, so one invalidation covers them all */
  pickers: (id: string) => ['case-pickers', id] as const,
  picker: (id: string, kind: 'lawyers' | 'assistants' | 'jurists') => ['case-pickers', id, kind] as const,
  hearings: (id: string) => ['case-hearings', id] as const,
  hearingDefaults: (id: string) => ['case-hearing-defaults', id] as const,
  /** the dossier timeline seen from this case (every filter combination under one prefix) */
  documents: (id: string) => ['case-documents', id] as const,
}

/** The agenda (task 031) shows hearings: every hearing change reloads it, whatever its later keys look like. */
export const agendaKeys = { all: ['agenda'] as const }

/**
 * The one thing every case change of tasks 023–028 does on success (task 029): the response's rowVersion — and,
 * with `apply`, the changed fields — go into the cached case at once, so the next save of the same user sends the
 * latest rowVersion (one source, never a copy in a screen); then the case (with its team), its history, the case
 * lists with their tab counts and the case's pickers reload. Since task 030 also its hearings (with the defaults for
 * a new one) and the agenda: the next hearing is part of the case header and the lists. Since task 035 the documents.
 */
export function afterCaseChange(
  qc: QueryClient,
  caseId: string,
  rowVersion?: number | string,
  apply?: (c: CaseDetail) => CaseDetail,
) {
  qc.setQueryData<CaseDetail>(caseKeys.detail(caseId), (c) => {
    if (!c) return c
    const next = apply ? apply(c) : c
    return rowVersion === undefined ? next : { ...next, header: { ...next.header, rowVersion } }
  })
  return Promise.all([
    qc.invalidateQueries({ queryKey: caseKeys.detail(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.history(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.pickers(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.hearings(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.hearingDefaults(caseId) }),
    qc.invalidateQueries({ queryKey: caseKeys.documents(caseId) }),
    qc.invalidateQueries({ queryKey: agendaKeys.all }),
    invalidateCaseLists(qc),
  ])
}

/** GET /api/cases/{id}; 404 (no access or no case — never told apart) is not retried. */
export const useCase = (id: string) =>
  useQuery({
    queryKey: caseKeys.detail(id),
    retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
    queryFn: () => api.GET('/api/cases/{id}', { params: { path: { id } } }).then(unwrap),
  })

/**
 * One page of the dossier's history (all levels the user can view), newest first, with the total. `q`: live search
 * over the rows' text (from 2 characters); a newer text aborts this request.
 */
export const useCaseHistory = (id: string, p: PageRequest, enabled: boolean, q = '') =>
  useQuery({
    queryKey: [...caseKeys.history(id), q, p.cursor ?? null, p.size] as const,
    enabled,
    placeholderData: keepPreviousData,
    queryFn: ({ signal }) =>
      api
        .GET('/api/cases/{id}/history', {
          signal,
          params: { path: { id }, query: { after: p.cursor, limit: p.size, q: q || undefined } },
        })
        .then(unwrap),
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
export type AssignLawyersRequest = components['schemas']['AssignLawyersRequest']
export type FilterOptions = components['schemas']['FilterOptionsResponse']
export type FilterLawyer = components['schemas']['FilterLawyer']

/** The lawyers the Kryetar may assign in this case, least loaded first (the Kryetar himself with isSelf). */
export const useAssignableLawyers = (caseId: string) =>
  useQuery({
    queryKey: caseKeys.picker(caseId, 'lawyers'),
    retry: false,
    queryFn: () => api.GET('/api/cases/{id}/assignable-lawyers', { params: { path: { id: caseId } } }).then(unwrap),
  })

/**
 * The office and lawyer filters of the case list and the agenda (task 031): every office and lawyer for
 * case.view_all, the own office and its lawyers for case.view_office, empty lists for anyone else.
 */
export const useFilterOptions = (enabled: boolean) =>
  useQuery({
    queryKey: ['case-filter-options'] as const,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/cases/filter-options').then(unwrap),
  })

/** The lawyers of the chosen office (all of them when none is chosen), as picker options. */
export const lawyerOptionsOf = (lawyers: FilterLawyer[], officeId: string) =>
  lawyers
    .filter((l) => !officeId || l.officePublicId === officeId)
    .map((l) => ({ value: l.publicId, label: l.fullName }))

/** POST assignment: the case becomes ACTIVE, moves between the tabs and changes its page and history. */
export const useAssignLawyers = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: AssignLawyersRequest) =>
      api.POST('/api/cases/{id}/assignment', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
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
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
  })
}

// ---------------------------------------------------------------- case data (task 025)

export type UpdateClaimRequest = components['schemas']['UpdateClaimRequest']
export type SetCategoryRequest = components['schemas']['SetCategoryRequest']
export type SetSuspensionRequest = components['schemas']['SetSuspensionRequest']
export type SetSecurityMeasureRequest = components['schemas']['SetSecurityMeasureRequest']
export type SetPriorityRequest = components['schemas']['SetPriorityRequest']

/**
 * One save of the case's data: the changed fields show at once (`apply`), then the shared afterCaseChange (rowVersion
 * into the cache, case / history / lists / pickers reload).
 */
function useCaseDataSave<TBody, TResult extends { rowVersion: number | string }>(
  caseId: string,
  send: (body: TBody) => Promise<TResult>,
  apply: (c: CaseDetail, result: TResult) => CaseDetail,
) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: send,
    onSuccess: (result) => afterCaseChange(qc, caseId, result.rowVersion, (c) => apply(c, result)),
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

export type ChangeJudgeRequest = components['schemas']['ChangeJudgeRequest']

/** PATCH judge (task 028); the response's warnings (judge.court_mismatch) are for the user, the save went through. */
export const useChangeJudge = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: ChangeJudgeRequest) => api.PATCH('/api/cases/{id}/judge', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, data: { ...c.data, judgeName: r.judge.name } }),
  )

export const useChangeObject = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: { objectText: string; rowVersion: number }) =>
      api.PATCH('/api/cases/{id}/object', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, data: { ...c.data, objectText: r.objectText } }),
  )

export const useSetPriority = (caseId: string) =>
  useCaseDataSave(
    caseId,
    (body: SetPriorityRequest) => api.PATCH('/api/cases/{id}/priority', { ...path(caseId), body }).then(unwrap),
    (c, r) => ({ ...c, header: { ...c.header, priority: r.priority } }),
  )

// ---------------------------------------------------------------- decision, closing, archiving (tasks 036–039)

export type DecisionRequest = components['schemas']['DecisionRequest']
export type CaseDecision = components['schemas']['CaseDecisionSummary']
export type RecordDecisionResponse = components['schemas']['RecordDecisionResponse']
export type UpdateDecisionResponse = components['schemas']['UpdateDecisionResponse']

/** What the API suggests after a decision (DecisionSuggestions). */
export const SUGGESTIONS = { retrial: 'RETRIAL_EXPECTED', archive: 'ARCHIVE_SUGGESTED' } as const

/**
 * POST /decision: records the decision and closes the level. Afterwards everything the closing touches reloads through
 * the one helper: the case (header, decision, permissions), its hearings (cancelled ones), documents, history, the
 * lists with their counts and the agenda.
 */
export const useRecordDecision = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: DecisionRequest) => api.POST('/api/cases/{id}/decision', { ...path(caseId), body }).then(unwrap),
    onSuccess: (result) => afterCaseChange(qc, caseId, result.caseRowVersion),
  })
}

/** PUT /decision (decision rowVersion): completes the decision of a CLOSED level; the case row is not changed. */
export const useUpdateDecision = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: DecisionRequest) => api.PUT('/api/cases/{id}/decision', { ...path(caseId), body }).then(unwrap),
    onSuccess: () => afterCaseChange(qc, caseId),
  })
}

/**
 * POST /api/dossiers/{id}/archive (rowVersion of the latest case). The whole dossier becomes read-only: besides this
 * case, every level's page reloads (their permissions shrink to case.view).
 */
export const useArchiveDossier = (caseId: string, dossierPublicId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (rowVersion: number) =>
      api
        .POST('/api/dossiers/{dossierPublicId}/archive', { params: { path: { dossierPublicId } }, body: { rowVersion } })
        .then(unwrap),
    onSuccess: (result) =>
      Promise.all([
        afterCaseChange(qc, caseId, result.caseRowVersion),
        qc.invalidateQueries({ queryKey: ['case'] }),
      ]),
  })
}

// ---------------------------------------------------------------- legal assistants per case (task 053q)

export type AssignableAssistant = components['schemas']['AssignableAssistant']

/** Legal assistants of the case's office not yet on the case, least loaded first (the primary lawyer picks). */
export const useAssignableAssistants = (caseId: string, enabled: boolean) =>
  useQuery({
    queryKey: caseKeys.picker(caseId, 'assistants'),
    enabled,
    retry: false,
    queryFn: ({ signal }) =>
      api.GET('/api/cases/{id}/assignable-assistants', { params: { path: { id: caseId } }, signal }).then(unwrap),
  })

export const useAddAssistant = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { userPublicId: string; rowVersion: number }) =>
      api.POST('/api/cases/{id}/assistants', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
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
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
  })
}

// ---------------------------------------------------------------- institution jurist and delegation (task 027)

export type AssignableJurist = components['schemas']['AssignableJurist']
export type CaseInstitutionView = components['schemas']['CaseInstitutionView']

/** The jurists of the user's own institution (institution.assign_jurist), the current one marked. */
export const useAssignableJurists = (caseId: string, enabled: boolean) =>
  useQuery({
    queryKey: caseKeys.picker(caseId, 'jurists'),
    enabled,
    retry: false,
    queryFn: () => api.GET('/api/cases/{id}/assignable-jurists', { params: { path: { id: caseId } } }).then(unwrap),
  })

export const useAssignJurist = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { userPublicId: string; rowVersion: number }) =>
      api.POST('/api/cases/{id}/jurist', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
  })
}

export const useDelegateCase = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { institutionPublicId: string; mode: string; rowVersion: number }) =>
      api.POST('/api/cases/{id}/delegate', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
  })
}

export const useUndelegateCase = (caseId: string) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { reason: string | null; rowVersion: number }) =>
      api.POST('/api/cases/{id}/undelegate', { params: { path: { id: caseId } }, body }).then(unwrap),
    onSuccess: (r) => afterCaseChange(qc, caseId, r.rowVersion),
  })
}

// ---------------------------------------------------------------- hearings (task 030)

export type HearingView = components['schemas']['HearingView']
export type HearingWarning = components['schemas']['RegistrationWarning']
export type AddHearingRequest = components['schemas']['AddHearingRequest']
export type UpdateHearingRequest = components['schemas']['UpdateHearingRequest']
export type CompleteHearingRequest = components['schemas']['CompleteHearingRequest']

/** "Përgatitore", or "Tjetër: <text>". */
export const hearingTypeName = (h: Pick<HearingView, 'type' | 'otherTypeText'>) =>
  h.otherTypeText ? `${h.type.name}: ${h.otherTypeText}` : h.type.name

/** The level's hearings, newest first (as the API sends them). */
export const useHearings = (caseId: string) =>
  useQuery({
    queryKey: caseKeys.hearings(caseId),
    queryFn: () => api.GET('/api/cases/{id}/hearings', path(caseId)).then(unwrap),
  })

/** The type and description to start a new hearing with (from the last one). */
export const useHearingDefaults = (caseId: string, enabled: boolean) =>
  useQuery({
    queryKey: caseKeys.hearingDefaults(caseId),
    enabled,
    queryFn: () => api.GET('/api/cases/{id}/hearings/new-defaults', path(caseId)).then(unwrap),
  })

/**
 * A hearing save: the saved hearing(s) replace their copy in the cache at once (so the next edit sends the latest
 * rowVersion), then the shared afterCaseChange. A hearing never changes the case's rowVersion.
 */
function useHearingSave<TBody, TResult>(
  caseId: string,
  send: (body: TBody) => Promise<TResult>,
  saved: (result: TResult) => HearingView[],
) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: send,
    onSuccess: (result) => {
      const changed = saved(result)
      qc.setQueryData<HearingView[]>(caseKeys.hearings(caseId), (list) =>
        list?.map((h) => changed.find((c) => c.publicId === h.publicId) ?? h),
      )
      return afterCaseChange(qc, caseId)
    },
  })
}

export const useAddHearing = (caseId: string) =>
  useHearingSave(
    caseId,
    (body: AddHearingRequest) => api.POST('/api/cases/{id}/hearings', { ...path(caseId), body }).then(unwrap),
    (r) => [r.hearing],
  )

export const useUpdateHearing = (caseId: string) =>
  useHearingSave(
    caseId,
    ({ hearingId, body }: { hearingId: string; body: UpdateHearingRequest }) =>
      api
        .PUT('/api/cases/{id}/hearings/{hearingId}', { params: { path: { id: caseId, hearingId } }, body })
        .then(unwrap),
    (r) => [r.hearing],
  )

/** Completes a hearing and, with `next`, schedules the next one — one request, one transaction. */
export const useCompleteHearing = (caseId: string) =>
  useHearingSave(
    caseId,
    ({ hearingId, body }: { hearingId: string; body: CompleteHearingRequest }) =>
      api
        .POST('/api/cases/{id}/hearings/{hearingId}/complete', { params: { path: { id: caseId, hearingId } }, body })
        .then(unwrap),
    (r) => (r.next ? [r.hearing, r.next] : [r.hearing]),
  )

export type RegistrationSearchItem = components['schemas']['RegistrationSearchItem']
export type NextLevelPreview = components['schemas']['NextLevelPreviewResponse']
export type NotifyNotClosedRequest = components['schemas']['NotifyNotClosedRequest']
export type NotifyRecipient = components['schemas']['NotifyRecipient']

export type RegisterNextLevelRequest = components['schemas']['RegisterNextLevelRequest']

/**
 * The next level sends the summons, the office and the parties ADDED in the form (task 053j): the object text and the
 * plaintiff / defendant are copied from the previous level.
 */
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
  parties: r.parties,
  officePublicId: r.officePublicId,
})

/** "Zyra që e ndjek" (task 053j): the offices, and the one covering the court (null = none: the choice is required). */
export const useRegistrationOffices = (courtId: number | null) =>
  useQuery({
    queryKey: ['registration-offices', courtId] as const,
    staleTime: 60_000,
    queryFn: () =>
      api
        .GET('/api/cases/registration/offices', { params: { query: { courtId: courtId ?? undefined } } })
        .then(unwrap),
  })

export type RegistrationFilter = components['schemas']['RegistrationFilterResponse']
type RegistrationSearchQuery = NonNullable<paths['/api/cases/registration-search']['get']['parameters']['query']>
/** The filter parameters (code, actNumber, courtId, …) — generated from the server's filter registry (task 053b). */
export type RegistrationFilterName = Exclude<keyof RegistrationSearchQuery, 'after' | 'limit' | 'status' | 'more'>
export type RegistrationFilterValues = Partial<Record<RegistrationFilterName, string>>

/** The status choice of "Vazhdo çështje ekzistuese" (task 053j); CONTINUABLE = "Mund të vazhdohen", the default. */
export const REGISTRATION_STATUSES = ['CONTINUABLE', 'ALL', 'OPEN', 'CLOSED'] as const
export type RegistrationStatus = (typeof REGISTRATION_STATUSES)[number]

/** One "+ Shto filtër" filter: the report builder's registry (task 053h) over the country's latest levels. */
export type MoreFilter = components['schemas']['CaseFilterField']

/**
 * The "+ Shto filtër" filters with their options under the values already chosen (`more`: the report builder's
 * JSON object, as the search receives it).
 */
export const useMoreFilters = (more: string) =>
  useQuery({
    queryKey: ['registration-search', 'more-filters', more] as const,
    staleTime: 60_000,
    placeholderData: keepPreviousData,
    queryFn: () =>
      api.GET('/api/cases/registration-search/more-filters', { params: { query: { more: more || undefined } } }).then(unwrap),
  })

/** The fields of the search form, in order, with their control (GET …/registration-search/filters). */
export const useRegistrationFilters = () =>
  useQuery({
    queryKey: ['registration-search', 'filters'] as const,
    staleTime: Infinity,
    queryFn: () => api.GET('/api/cases/registration-search/filters').then(unwrap),
  })

/** Court and judge are ids; every other filter is text or a code. */
const NUMERIC_INPUTS = new Set(['Court', 'Judge'])

/**
 * "Vazhdo çështje ekzistuese" (task 053j): every dossier of the country by its latest level, one page with the total.
 * Each filter must match (on any level of the dossier); values '' are not sent; `more` = the "+ Shto filtër" values
 * (the report builder's JSON object, '' = none); status default CONTINUABLE ("Mund të vazhdohen").
 */
export const useRegistrationSearch = (
  filters: RegistrationFilter[],
  values: RegistrationFilterValues,
  status: RegistrationStatus,
  more: string,
  p: PageRequest,
) => {
  const entries = filters
    .map((f) => [f.name, values[f.name as RegistrationFilterName] ?? '', f.input] as const)
    .filter(([, value]) => value !== '')
  const query = Object.fromEntries(
    entries.map(([name, value, input]) => [name, NUMERIC_INPUTS.has(input) ? Number(value) : value]),
  ) as Partial<RegistrationSearchQuery>
  return useQuery({
    queryKey: ['registration-search', query, status, more, p.cursor ?? null, p.size] as const,
    enabled: filters.length > 0,
    placeholderData: keepPreviousData,
    // live search: a newer text aborts this request
    queryFn: ({ signal }) =>
      api
        .GET('/api/cases/registration-search', {
          signal,
          params: { query: { ...query, status, more: more || undefined, after: p.cursor, limit: p.size } },
        })
        .then(unwrap),
  })
}

/** Who "Njofto…" reaches (GET …/notify-not-closed/recipients): the primary lawyer if any, and the Kryetar. */
export const useNotifyRecipients = (caseId: string) =>
  useQuery({
    queryKey: ['notify-recipients', caseId] as const,
    retry: false,
    queryFn: () =>
      api
        .GET('/api/cases/{previousCasePublicId}/notify-not-closed/recipients', {
          params: { path: { previousCasePublicId: caseId } },
        })
        .then(unwrap),
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
      Promise.all([
        qc.invalidateQueries({ queryKey: ['registration-search'] }),
        invalidateCaseLists(qc),
      ]),
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
      // every status (task 053j: the default would hide what cannot be continued yet)
      .GET('/api/cases/registration-search', { params: { query: { actNumber: actNumber.trim(), status: 'ALL', limit: 50 } } })
      .then(unwrap)
    return page.items.some((item) => normalize(item.actNumber) === wanted)
  } catch {
    return false // the offer is a convenience; without it the user can still cancel or register anyway
  }
}

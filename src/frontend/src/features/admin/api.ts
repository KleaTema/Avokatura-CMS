import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { MIN_SERVER_QUERY } from '../../lib/search'
import { referenceKeys } from '../reference/api'

export type AdminUser = components['schemas']['UserResponse']
export type AdminRole = components['schemas']['RoleResponse']
export type CreateUserRequest = components['schemas']['CreateUserRequest']

const PAGE_SIZE = 50

export const adminKeys = {
  users: ['admin-users'] as const,
  userList: (q: string) => ['admin-users', 'list', q] as const,
  roles: ['admin-roles'] as const,
  offices: ['admin-offices'] as const,
  institutions: ['admin-institutions'] as const,
  institutionList: (q: string) => ['admin-institutions', 'list', q] as const,
  institutionSearch: (q: string) => ['institution-search', q] as const,
  // under 'admin-users': any user change (role, status, office) can change who may be head
  headCandidates: (officeId: string) => ['admin-users', 'head-candidates', officeId] as const,
  lookup: (list: string) => ['admin-lookups', list] as const,
  judgeList: (q: string, active: boolean | undefined) => ['admin-lookups', 'judges', q, active ?? 'all'] as const,
}

export const useAdminUsers = (q: string) =>
  useInfiniteQuery({
    queryKey: adminKeys.userList(q),
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/identity/admin/users', {
          params: { query: { q: q || undefined, after: pageParam, limit: PAGE_SIZE } },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

export const useAdminRoles = () =>
  useQuery({
    queryKey: adminKeys.roles,
    staleTime: Infinity,
    queryFn: () => api.GET('/api/identity/admin/roles').then(unwrap),
  })

export const useAdminOffices = (enabled = true) =>
  useQuery({
    queryKey: adminKeys.offices,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/identity/admin/offices').then(unwrap),
  })

export const MIN_INSTITUTION_QUERY = MIN_SERVER_QUERY

/** Type-ahead over active institutions (public names only); runs from 2 characters. */
export const useInstitutionSearch = (q: string, enabled = true) =>
  useQuery({
    queryKey: adminKeys.institutionSearch(q),
    enabled: enabled && q.length >= MIN_INSTITUTION_QUERY,
    staleTime: 60_000,
    queryFn: () => api.GET('/api/identity/institutions', { params: { query: { q } } }).then(unwrap),
  })

// A user change shows in the user lists and in the active-user counts of offices and institutions.
const invalidateUsers = (qc: QueryClient) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: adminKeys.users }),
    qc.invalidateQueries({ queryKey: adminKeys.offices }),
    qc.invalidateQueries({ queryKey: adminKeys.institutions }),
  ])

export const useCreateUser = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CreateUserRequest) => api.POST('/api/identity/admin/users', { body }).then(unwrap),
    onSuccess: () => invalidateUsers(qc),
  })
}

export const useGrantRole = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, role }: { id: string; role: string }) =>
      api.POST('/api/identity/admin/users/{id}/roles', { params: { path: { id } }, body: { role } }).then(unwrap),
    onSuccess: () => invalidateUsers(qc),
  })
}

export const useRevokeRole = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, role }: { id: string; role: string }) =>
      api.DELETE('/api/identity/admin/users/{id}/roles/{role}', { params: { path: { id, role } } }).then(unwrap),
    onSuccess: () => invalidateUsers(qc),
  })
}

export const useDeactivateUser = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) =>
      api.POST('/api/identity/admin/users/{id}/deactivate', { params: { path: { id } } }).then(unwrap),
    onSuccess: () => invalidateUsers(qc),
  })
}

export const useActivateUser = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) =>
      api.POST('/api/identity/admin/users/{id}/activate', { params: { path: { id } } }).then(unwrap),
    onSuccess: () => invalidateUsers(qc),
  })
}

// ---- Lookup lists (task 014) ----

export type SimpleListKey = 'hearing-types' | 'party-roles' | 'summons-types' | 'document-types' | 'decision-results'

/** One row of any simple list; fields a list does not have are null. */
export interface SimpleListItem {
  code: string
  name: string
  sortOrder: number | null
  isOther: boolean | null
  reportGroup: string | null
  outcomeGroup: string | null
  sendsBackForRetrial: boolean | null
  isActive: boolean
}

/** Form values for any simple list; each list sends only the fields it has. */
export interface SimpleItemInput {
  code: string
  name: string
  sortOrder: number
  isOther: boolean
  group: string
  sendsBackForRetrial: boolean
}

interface SimpleRowFields {
  code: string
  name: string
  isActive: boolean
  sortOrder?: number | string
  isOther?: boolean
  reportGroup?: string
  outcomeGroup?: string
  sendsBackForRetrial?: boolean
}

const toItem = (r: SimpleRowFields): SimpleListItem => ({
  code: r.code,
  name: r.name,
  sortOrder: r.sortOrder === undefined ? null : Number(r.sortOrder),
  isOther: r.isOther ?? null,
  reportGroup: r.reportGroup ?? null,
  outcomeGroup: r.outcomeGroup ?? null,
  sendsBackForRetrial: r.sendsBackForRetrial ?? null,
  isActive: r.isActive,
})

interface SimpleListApi {
  list: () => Promise<SimpleListItem[]>
  create: (v: SimpleItemInput) => Promise<unknown>
  update: (code: string, v: SimpleItemInput) => Promise<unknown>
  setActive: (code: string, active: boolean) => Promise<unknown>
}

const simpleListApi: Record<SimpleListKey, SimpleListApi> = {
  'hearing-types': {
    list: () => api.GET('/api/reference/admin/hearing-types').then(unwrap).then((rows) => rows.map(toItem)),
    create: (v) =>
      api
        .POST('/api/reference/admin/hearing-types', {
          body: { code: v.code, name: v.name, sortOrder: v.sortOrder, isOther: v.isOther },
        })
        .then(unwrap),
    update: (code, v) =>
      api
        .PUT('/api/reference/admin/hearing-types/{code}', {
          params: { path: { code } },
          body: { name: v.name, sortOrder: v.sortOrder, isOther: v.isOther },
        })
        .then(unwrap),
    setActive: (code, active) =>
      active
        ? api.POST('/api/reference/admin/hearing-types/{code}/activate', { params: { path: { code } } }).then(unwrap)
        : api.POST('/api/reference/admin/hearing-types/{code}/deactivate', { params: { path: { code } } }).then(unwrap),
  },
  'party-roles': {
    list: () => api.GET('/api/reference/admin/party-roles').then(unwrap).then((rows) => rows.map(toItem)),
    create: (v) =>
      api
        .POST('/api/reference/admin/party-roles', {
          body: { code: v.code, name: v.name, reportGroup: v.group, sortOrder: v.sortOrder },
        })
        .then(unwrap),
    update: (code, v) =>
      api
        .PUT('/api/reference/admin/party-roles/{code}', {
          params: { path: { code } },
          body: { name: v.name, sortOrder: v.sortOrder },
        })
        .then(unwrap),
    setActive: (code, active) =>
      active
        ? api.POST('/api/reference/admin/party-roles/{code}/activate', { params: { path: { code } } }).then(unwrap)
        : api.POST('/api/reference/admin/party-roles/{code}/deactivate', { params: { path: { code } } }).then(unwrap),
  },
  'summons-types': {
    list: () => api.GET('/api/reference/admin/summons-types').then(unwrap).then((rows) => rows.map(toItem)),
    create: (v) => api.POST('/api/reference/admin/summons-types', { body: { code: v.code, name: v.name } }).then(unwrap),
    update: (code, v) =>
      api
        .PUT('/api/reference/admin/summons-types/{code}', { params: { path: { code } }, body: { name: v.name } })
        .then(unwrap),
    setActive: (code, active) =>
      active
        ? api.POST('/api/reference/admin/summons-types/{code}/activate', { params: { path: { code } } }).then(unwrap)
        : api.POST('/api/reference/admin/summons-types/{code}/deactivate', { params: { path: { code } } }).then(unwrap),
  },
  'document-types': {
    list: () => api.GET('/api/reference/admin/document-types').then(unwrap).then((rows) => rows.map(toItem)),
    create: (v) =>
      api
        .POST('/api/reference/admin/document-types', {
          body: { code: v.code, name: v.name, sortOrder: v.sortOrder, isOther: v.isOther },
        })
        .then(unwrap),
    update: (code, v) =>
      api
        .PUT('/api/reference/admin/document-types/{code}', {
          params: { path: { code } },
          body: { name: v.name, sortOrder: v.sortOrder, isOther: v.isOther },
        })
        .then(unwrap),
    setActive: (code, active) =>
      active
        ? api.POST('/api/reference/admin/document-types/{code}/activate', { params: { path: { code } } }).then(unwrap)
        : api.POST('/api/reference/admin/document-types/{code}/deactivate', { params: { path: { code } } }).then(unwrap),
  },
  'decision-results': {
    list: () => api.GET('/api/reference/admin/decision-results').then(unwrap).then((rows) => rows.map(toItem)),
    create: (v) =>
      api
        .POST('/api/reference/admin/decision-results', {
          body: {
            code: v.code,
            name: v.name,
            outcomeGroup: v.group,
            sendsBackForRetrial: v.sendsBackForRetrial,
            sortOrder: v.sortOrder,
            isOther: v.isOther,
          },
        })
        .then(unwrap),
    update: (code, v) =>
      api
        .PUT('/api/reference/admin/decision-results/{code}', {
          params: { path: { code } },
          body: { name: v.name, sortOrder: v.sortOrder, isOther: v.isOther },
        })
        .then(unwrap),
    setActive: (code, active) =>
      active
        ? api.POST('/api/reference/admin/decision-results/{code}/activate', { params: { path: { code } } }).then(unwrap)
        : api.POST('/api/reference/admin/decision-results/{code}/deactivate', { params: { path: { code } } }).then(unwrap),
  },
}

// A lookup change shows up in the admin table and in every picker that uses the public list.
const invalidateLookup = (qc: QueryClient, list: string, alsoAdmin: string[] = []) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: adminKeys.lookup(list) }),
    qc.invalidateQueries({ queryKey: referenceKeys.list(list) }),
    ...alsoAdmin.map((other) => qc.invalidateQueries({ queryKey: adminKeys.lookup(other) })),
  ])

export const useSimpleList = (list: SimpleListKey) =>
  useQuery({ queryKey: adminKeys.lookup(list), queryFn: simpleListApi[list].list })

export const useSaveSimpleItem = (list: SimpleListKey) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ code, input }: { code: string | null; input: SimpleItemInput }) =>
      code === null ? simpleListApi[list].create(input) : simpleListApi[list].update(code, input),
    onSuccess: () => invalidateLookup(qc, list),
  })
}

export const useSetSimpleItemActive = (list: SimpleListKey) => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ code, active }: { code: string; active: boolean }) => simpleListApi[list].setActive(code, active),
    onSuccess: () => invalidateLookup(qc, list),
  })
}

export type AdminCourt = components['schemas']['CourtAdminResponse']
export type CourtInput = components['schemas']['UpdateCourtRequest']
export type AdminJudge = components['schemas']['JudgeAdminResponse']
export type JudgeInput = components['schemas']['UpdateJudgeRequest']

export const useAdminCourts = (enabled = true) =>
  useQuery({
    queryKey: adminKeys.lookup('courts'),
    enabled,
    queryFn: () => api.GET('/api/reference/admin/courts').then(unwrap),
  })

// Judges show their court's name, so a court change refreshes the judge list too.
export const useSaveCourt = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number | null; body: CourtInput }) =>
      id === null
        ? api.POST('/api/reference/admin/courts', { body }).then(unwrap)
        : api.PUT('/api/reference/admin/courts/{id}', { params: { path: { id } }, body }).then(unwrap),
    onSuccess: () => invalidateLookup(qc, 'courts', ['judges']),
  })
}

export const useSetCourtActive = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, active }: { id: number; active: boolean }) =>
      active
        ? api.POST('/api/reference/admin/courts/{id}/activate', { params: { path: { id } } }).then(unwrap)
        : api.POST('/api/reference/admin/courts/{id}/deactivate', { params: { path: { id } } }).then(unwrap),
    onSuccess: () => invalidateLookup(qc, 'courts', ['judges']),
  })
}

export const useAdminJudges = (q: string, active: boolean | undefined) =>
  useInfiniteQuery({
    queryKey: adminKeys.judgeList(q, active),
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/reference/admin/judges', {
          params: { query: { q: q || undefined, active, after: pageParam, limit: PAGE_SIZE } },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

export const useSaveJudge = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number | null; body: JudgeInput }) =>
      id === null
        ? api.POST('/api/reference/admin/judges', { body }).then(unwrap)
        : api.PUT('/api/reference/admin/judges/{id}', { params: { path: { id } }, body }).then(unwrap),
    onSuccess: () => invalidateLookup(qc, 'judges'),
  })
}

export type AdminFileType = components['schemas']['FileTypeAdminResponse']
export type FileTypeInput = components['schemas']['UpdateFileTypeRequest']
export type AdminCodeFormat = components['schemas']['DossierCodeFormatAdminResponse']
export type CodeFormatInput = components['schemas']['CreateDossierCodeFormatRequest']

export const useAdminFileTypes = () =>
  useQuery({
    queryKey: adminKeys.lookup('file-types'),
    queryFn: () => api.GET('/api/reference/admin/file-types').then(unwrap),
  })

// The public list behind upload checks is /api/reference/allowed-file-types.
export const useSaveFileType = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number | null; body: FileTypeInput }) =>
      id === null
        ? api.POST('/api/reference/admin/file-types', { body }).then(unwrap)
        : api.PUT('/api/reference/admin/file-types/{id}', { params: { path: { id } }, body }).then(unwrap),
    onSuccess: () =>
      Promise.all([
        qc.invalidateQueries({ queryKey: adminKeys.lookup('file-types') }),
        qc.invalidateQueries({ queryKey: referenceKeys.list('allowed-file-types') }),
      ]),
  })
}

export const useAdminCodeFormats = () =>
  useQuery({
    queryKey: adminKeys.lookup('dossier-code-formats'),
    queryFn: () => api.GET('/api/reference/admin/dossier-code-formats').then(unwrap),
  })

export const useCreateCodeFormat = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CodeFormatInput) => api.POST('/api/reference/admin/dossier-code-formats', { body }).then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: adminKeys.lookup('dossier-code-formats') }),
  })
}

export const useActivateCodeFormat = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) =>
      api
        .POST('/api/reference/admin/dossier-code-formats/{id}/activate', { params: { path: { id } } })
        .then(unwrap),
    onSuccess: () => qc.invalidateQueries({ queryKey: adminKeys.lookup('dossier-code-formats') }),
  })
}

export const useSetJudgeActive = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, active }: { id: number; active: boolean }) =>
      active
        ? api.POST('/api/reference/admin/judges/{id}/activate', { params: { path: { id } } }).then(unwrap)
        : api.POST('/api/reference/admin/judges/{id}/deactivate', { params: { path: { id } } }).then(unwrap),
    onSuccess: () => invalidateLookup(qc, 'judges'),
  })
}

// ---- Offices and institutions (task 015) ----

export type AdminOffice = components['schemas']['OfficeAdminResponse']
export type OfficeInput = components['schemas']['CreateOfficeRequest']
export type AdminInstitution = components['schemas']['InstitutionAdminResponse']
export type InstitutionInput = components['schemas']['CreateInstitutionRequest']
export type InstitutionSearchResult = components['schemas']['InstitutionSearchResult']

// Offices also feed the office picker of the Users page and the office names in the user list.
const invalidateOffices = (qc: QueryClient) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: adminKeys.offices }),
    qc.invalidateQueries({ queryKey: adminKeys.users }),
  ])

const invalidateInstitutions = (qc: QueryClient) =>
  Promise.all([
    qc.invalidateQueries({ queryKey: adminKeys.institutions }),
    qc.invalidateQueries({ queryKey: ['institution-search'] }),
    qc.invalidateQueries({ queryKey: adminKeys.users }),
  ])

export const useSaveOffice = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string | null; body: OfficeInput }) =>
      publicId === null
        ? api.POST('/api/identity/admin/offices', { body }).then(unwrap)
        : api
            .PUT('/api/identity/admin/offices/{publicId}', {
              params: { path: { publicId } },
              body: { name: body.name, officeType: body.officeType, parentOfficeId: body.parentOfficeId },
            })
            .then(unwrap),
    onSuccess: () => invalidateOffices(qc),
  })
}

export const useSetOfficeActive = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? api.POST('/api/identity/admin/offices/{publicId}/activate', { params: { path: { publicId } } }).then(unwrap)
        : api.POST('/api/identity/admin/offices/{publicId}/deactivate', { params: { path: { publicId } } }).then(unwrap),
    onSuccess: () => invalidateOffices(qc),
  })
}

export const useSetOfficeHead = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, userPublicId }: { publicId: string; userPublicId: string | null }) =>
      api
        .PUT('/api/identity/admin/offices/{publicId}/head', { params: { path: { publicId } }, body: { userPublicId } })
        .then(unwrap),
    onSuccess: () => invalidateOffices(qc),
  })
}

/** Active users of the office with role OFFICE_HEAD — the only valid heads. */
export const useOfficeHeadCandidates = (officePublicId: string) =>
  useQuery({
    queryKey: adminKeys.headCandidates(officePublicId),
    queryFn: () =>
      api
        .GET('/api/identity/admin/users', {
          params: { query: { officeId: officePublicId, role: 'OFFICE_HEAD', active: true, limit: 200 } },
        })
        .then(unwrap)
        .then((page) => page.items),
  })

export const useAdminInstitutionList = (q: string) =>
  useInfiniteQuery({
    queryKey: adminKeys.institutionList(q),
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) =>
      api
        .GET('/api/identity/admin/institutions', {
          params: { query: { q: q || undefined, after: pageParam, limit: PAGE_SIZE } },
        })
        .then(unwrap),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
  })

export const useSaveInstitution = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, body }: { publicId: string | null; body: InstitutionInput }) =>
      publicId === null
        ? api.POST('/api/identity/admin/institutions', { body }).then(unwrap)
        : api
            .PUT('/api/identity/admin/institutions/{publicId}', { params: { path: { publicId } }, body })
            .then(unwrap),
    onSuccess: () => invalidateInstitutions(qc),
  })
}

export const useSetInstitutionActive = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ publicId, active }: { publicId: string; active: boolean }) =>
      active
        ? api
            .POST('/api/identity/admin/institutions/{publicId}/activate', { params: { path: { publicId } } })
            .then(unwrap)
        : api
            .POST('/api/identity/admin/institutions/{publicId}/deactivate', { params: { path: { publicId } } })
            .then(unwrap),
    onSuccess: () => invalidateInstitutions(qc),
  })
}

import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
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

export const useAdminInstitutions = (enabled = true) =>
  useQuery({
    queryKey: adminKeys.institutions,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: () => api.GET('/api/identity/admin/institutions').then(unwrap),
  })

// Every user mutation changes one user; the user lists (for every search term) are the only cached views of it.
const invalidateUsers = (qc: QueryClient) => qc.invalidateQueries({ queryKey: adminKeys.users })

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

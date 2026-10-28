import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

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

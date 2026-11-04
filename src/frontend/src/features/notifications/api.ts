import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import type { PageRequest } from '../cases/api'

export type NotificationItem = components['schemas']['NotificationItem']

/** Every notification query under one prefix: a push or a read refreshes them all. */
export const notificationKeys = {
  all: ['notifications'] as const,
  count: ['notifications', 'unread-count'] as const,
  /** the bell's popover: the newest 10 */
  latest: ['notifications', 'latest'] as const,
  list: (unreadOnly: boolean, p: PageRequest) => ['notifications', 'list', unreadOnly, p.cursor ?? null, p.size] as const,
}

/** The safety net while there is no SignalR backplane (task 040): the count is fetched again every 60 s. */
const COUNT_POLL_MS = 60_000

/** The unread count; polled every 60 s while the browser tab is visible (TanStack pauses it in a hidden tab). */
export const useUnreadCount = (enabled: boolean) =>
  useQuery({
    queryKey: notificationKeys.count,
    enabled,
    refetchInterval: COUNT_POLL_MS,
    refetchIntervalInBackground: false,
    queryFn: () =>
      api
        .GET('/api/notifications/unread-count')
        .then(unwrap)
        .then((r) => Number(r.unread)),
  })

/** The newest 10, read or not (the bell's popover). */
export const useLatestNotifications = (enabled: boolean) =>
  useQuery({
    queryKey: notificationKeys.latest,
    enabled,
    queryFn: () =>
      api
        .GET('/api/notifications', { params: { query: { limit: 10 } } })
        .then(unwrap)
        .then((page) => page.items),
  })

/** One page of the notifications page, newest first, with the total. */
export const useNotifications = (unreadOnly: boolean, p: PageRequest) =>
  useQuery({
    queryKey: notificationKeys.list(unreadOnly, p),
    placeholderData: keepPreviousData,
    queryFn: () =>
      api
        .GET('/api/notifications', { params: { query: { unreadOnly: unreadOnly || undefined, after: p.cursor, limit: p.size } } })
        .then(unwrap),
  })

/** The count from a response goes into the cache at once; the lists reload. */
function afterRead(qc: QueryClient, unread: number | string) {
  qc.setQueryData(notificationKeys.count, Number(unread))
  void qc.invalidateQueries({ queryKey: notificationKeys.all, predicate: (q) => q.queryKey[1] !== 'unread-count' })
}

export function useMarkRead() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.POST('/api/notifications/{id}/read', { params: { path: { id } } }).then(unwrap),
    onSuccess: (r) => afterRead(qc, r.unread),
  })
}

export function useMarkAllRead() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.POST('/api/notifications/read-all').then(unwrap),
    onSuccess: (r) => afterRead(qc, r.unread),
  })
}

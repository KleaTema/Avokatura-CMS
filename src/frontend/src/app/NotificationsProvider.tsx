import { HubConnectionState } from '@microsoft/signalr'
import { Alert, Button, Snackbar } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { z } from 'zod'
import { dashboardKeys } from '../features/dashboard/api'
import { notificationKeys, useMarkRead, useUnreadCount } from '../features/notifications/api'
import { sq } from '../i18n/sq'
import { buildHub, startWithRetry } from './hub'
import { isAppPath, NotificationsContext, type LiveNotification, type NotificationsState } from './notificationsContext'

const t = sq.notifications

const HUB_PATH = '/hubs/notifications'

const newSchema = z.object({
  publicId: z.string(),
  title: z.string(),
  linkPath: z.string().nullish(),
  createdAt: z.string(),
})
const countSchema = z.object({ unread: z.union([z.number(), z.string()]) })

/**
 * Task 040: ONE SignalR connection per logged-in session (mounted once in the Layout), with automatic reconnect.
 * "notification.new" → toast + lists reload; "notification.count" → the badge; "document.status" → the documents of
 * every open case reload (no polling). After a reconnect the count, the newest page and the documents are fetched
 * once, since pushes during the gap are lost. The count is also polled every 60 s (useUnreadCount) while the tab is
 * visible: with several API instances and no backplane yet a push can miss (docs/client-questions.md).
 */
export function NotificationsProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const count = useUnreadCount(true)
  const markRead = useMarkRead()
  const [latestLive, setLatestLive] = useState<LiveNotification | null>(null)
  const [toast, setToast] = useState<LiveNotification | null>(null)

  const refreshAfterGap = useCallback(() => {
    void qc.invalidateQueries({ queryKey: notificationKeys.all })
    void qc.invalidateQueries({ queryKey: ['case-documents'] })
  }, [qc])

  useEffect(() => {
    let disposed = false
    const connection = buildHub(HUB_PATH)

    connection.on('notification.new', (payload: unknown) => {
      const parsed = newSchema.safeParse(payload)
      if (!parsed.success) return
      const live: LiveNotification = { ...parsed.data, linkPath: parsed.data.linkPath ?? null }
      setLatestLive(live)
      setToast(live)
      void qc.invalidateQueries({ queryKey: notificationKeys.all, predicate: (q) => q.queryKey[1] !== 'unread-count' })
      // what the notification is about usually changes a dashboard number (task 046)
      void qc.invalidateQueries({ queryKey: dashboardKeys.all })
    })
    connection.on('notification.count', (payload: unknown) => {
      const parsed = countSchema.safeParse(payload)
      if (parsed.success) qc.setQueryData(notificationKeys.count, Number(parsed.data.unread))
    })
    connection.on('document.status', () => {
      void qc.invalidateQueries({ queryKey: ['case-documents'] })
    })
    connection.onreconnected(refreshAfterGap)

    const cancelRetry = startWithRetry(connection, () => disposed, (attempt) => {
      if (attempt > 0) refreshAfterGap()
    })

    return () => {
      disposed = true
      cancelRetry()
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [qc, refreshAfterGap])

  // the browser tab's title adds the chat's unread count to this one: ChatProvider sets it (task 050)
  const unread = count.data

  const { mutate: markAsRead } = markRead
  const open = useCallback<NotificationsState['open']>(
    (n) => {
      if (!n.readAt) markAsRead(n.publicId)
      if (isAppPath(n.linkPath)) navigate(n.linkPath)
    },
    [markAsRead, navigate],
  )

  const value = useMemo<NotificationsState>(() => ({ unread, latestLive, open }), [unread, latestLive, open])

  return (
    <NotificationsContext.Provider value={value}>
      {children}
      <Snackbar
        open={toast !== null}
        autoHideDuration={8000}
        onClose={(_, reason) => reason !== 'clickaway' && setToast(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      >
        {/* announced once by the bell's live region, not again here */}
        <Alert
          severity="info"
          role="presentation"
          onClose={() => setToast(null)}
          sx={{ width: '100%', boxShadow: 3, alignItems: 'center' }}
          action={
            toast && isAppPath(toast.linkPath) ? (
              <Button
                size="small"
                onClick={() => {
                  open({ publicId: toast.publicId, linkPath: toast.linkPath })
                  setToast(null)
                }}
              >
                {t.open}
              </Button>
            ) : undefined
          }
        >
          {toast?.title}
        </Alert>
      </Snackbar>
    </NotificationsContext.Provider>
  )
}

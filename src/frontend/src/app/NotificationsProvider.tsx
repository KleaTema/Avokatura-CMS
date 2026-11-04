import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { Alert, Button, Snackbar } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { z } from 'zod'
import { dashboardKeys } from '../features/dashboard/api'
import { notificationKeys, useMarkRead, useUnreadCount } from '../features/notifications/api'
import { sq } from '../i18n/sq'
import { authMode, getDevUser, userManager } from './authConfig'
import { isAppPath, NotificationsContext, type LiveNotification, type NotificationsState } from './notificationsContext'

const t = sq.notifications

const HUB_PATH = '/hubs/notifications'

/** First connection attempts when the server is not reachable yet (later drops: withAutomaticReconnect). */
const START_RETRY_MS = [2_000, 5_000, 10_000, 30_000]

const newSchema = z.object({
  publicId: z.string(),
  title: z.string(),
  linkPath: z.string().nullish(),
  createdAt: z.string(),
})
const countSchema = z.object({ unread: z.union([z.number(), z.string()]) })

/**
 * The hub URL: in Development the dev user travels in the query string (a browser WebSocket cannot send the
 * X-Dev-User header; the API accepts `dev_user` on /hubs/* only and never logs it). With OIDC the token comes from
 * accessTokenFactory instead (sent as a header, or `access_token` for the WebSocket handshake).
 */
function hubUrl(): string {
  if (authMode !== 'dev') return HUB_PATH
  const email = getDevUser()
  return email ? `${HUB_PATH}?dev_user=${encodeURIComponent(email)}` : HUB_PATH
}

async function accessToken(): Promise<string> {
  if (authMode === 'dev') return ''
  const user = await userManager?.getUser()
  return user && !user.expired ? user.access_token : ''
}

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
    let retry: ReturnType<typeof setTimeout> | undefined
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl(), { accessTokenFactory: accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build()

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

    const start = async (attempt: number) => {
      try {
        await connection.start()
        if (attempt > 0) refreshAfterGap()
      } catch {
        // not reachable yet, or stopped while starting (unmount): try again unless disposed
        if (disposed) return
        retry = setTimeout(() => void start(attempt + 1), START_RETRY_MS[Math.min(attempt, START_RETRY_MS.length - 1)])
      }
    }
    void start(0)

    return () => {
      disposed = true
      clearTimeout(retry)
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [qc, refreshAfterGap])

  const unread = count.data
  useEffect(() => {
    document.title = unread ? t.tabTitleWithCount(unread) : t.tabTitle
  }, [unread])
  useEffect(() => () => void (document.title = t.tabTitle), [])

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

import { createContext, useContext } from 'react'

/** A notification pushed while the page was open ("notification.new"). */
export interface LiveNotification {
  publicId: string
  title: string
  linkPath: string | null
  createdAt: string
}

export interface NotificationsState {
  /** the unread count (undefined until loaded) */
  unread: number | undefined
  /** the last notification that arrived live — never one of the initial load */
  latestLive: LiveNotification | null
  /** marks it read (when unread) and opens its case page; a link that is not an app route is ignored */
  open: (n: { publicId: string; linkPath?: string | null; readAt?: string | null }) => void
}

export const NotificationsContext = createContext<NotificationsState | null>(null)

export function useNotificationsState(): NotificationsState {
  const ctx = useContext(NotificationsContext)
  if (!ctx) throw new Error('useNotificationsState must be used inside NotificationsProvider')
  return ctx
}

/** Only an app route ("/cases/…"): never an absolute URL or "//host" (a stored link must not lead off the app). */
export const isAppPath = (path: string | null | undefined): path is string =>
  typeof path === 'string' && /^\/(?![/\\])/.test(path)

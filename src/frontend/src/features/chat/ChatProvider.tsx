import { HubConnectionState } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react'
import { z } from 'zod'
import { buildHub, startWithRetry } from '../../app/hub'
import { useNotificationsState } from '../../app/notificationsContext'
import { sq } from '../../i18n/sq'
import { chatKeys, useChatUnread } from './api'
import { ChatContext, type ChatEvent, type ChatState } from './chatContext'
import type { ThreadMessage } from './model'

const HUB_PATH = '/hubs/chat'

const messageSchema = z.object({
  id: z.string(),
  type: z.string(),
  sender: z.object({ id: z.string(), name: z.string() }).nullish(),
  body: z.string(),
  isDeleted: z.boolean(),
  replyToMessageId: z.string().nullish(),
  createdAt: z.string(),
  editedAt: z.string().nullish(),
})
const messagePushedSchema = z.object({ conversationId: z.string(), message: messageSchema })
const readSchema = z.object({ conversationId: z.string(), userId: z.string(), lastReadMessageId: z.string() })
const participantsSchema = z.object({ conversationId: z.string() })

/**
 * Task 050: ONE chat hub connection per session (mounted in the Layout, inside NotificationsProvider), the same
 * auth and reconnect pattern as task 040. Threads subscribe to its events; the badge and the conversation list reload
 * on every message. After a reconnect every thread fetches what is newer than it shows, and the badges reload. Chat
 * messages never create bell notifications. Also sets the browser tab title: notifications + chat unread, "(5) ASH".
 */
export function ChatProvider({ meId, children }: { meId: string | undefined; children: ReactNode }) {
  const qc = useQueryClient()
  const unread = useChatUnread(true)
  const listeners = useRef(new Set<(event: ChatEvent) => void>())
  const { unread: notificationsUnread } = useNotificationsState()
  // read inside the hub handlers without reconnecting when it arrives
  const meRef = useRef(meId)
  useEffect(() => {
    meRef.current = meId
  }, [meId])

  const emit = useCallback((event: ChatEvent) => {
    for (const listener of listeners.current) listener(event)
  }, [])

  const refreshAfterGap = useCallback(() => {
    void qc.invalidateQueries({ queryKey: chatKeys.all })
    emit({ kind: 'reconnected' })
  }, [qc, emit])

  useEffect(() => {
    let disposed = false
    const connection = buildHub(HUB_PATH)
    const onMessage = (kind: 'message.new' | 'message.edited' | 'message.deleted') => (payload: unknown) => {
      const parsed = messagePushedSchema.safeParse(payload)
      if (!parsed.success) return
      const message: ThreadMessage = parsed.data.message
      emit({ kind, conversationId: parsed.data.conversationId, message })
      void qc.invalidateQueries({ queryKey: chatKeys.unread })
      void qc.invalidateQueries({ queryKey: chatKeys.conversationsAll })
    }
    connection.on('message.new', onMessage('message.new'))
    connection.on('message.edited', onMessage('message.edited'))
    connection.on('message.deleted', onMessage('message.deleted'))
    connection.on('conversation.read', (payload: unknown) => {
      const parsed = readSchema.safeParse(payload)
      if (!parsed.success) return
      emit({ kind: 'conversation.read', ...parsed.data })
      // my own read in another tab: my badges follow
      if (parsed.data.userId === meRef.current) {
        void qc.invalidateQueries({ queryKey: chatKeys.unread })
        void qc.invalidateQueries({ queryKey: chatKeys.conversationsAll })
      }
    })
    connection.on('participants.changed', (payload: unknown) => {
      const parsed = participantsSchema.safeParse(payload)
      if (!parsed.success) return
      void qc.invalidateQueries({ queryKey: chatKeys.participants(parsed.data.conversationId) })
      void qc.invalidateQueries({ queryKey: chatKeys.conversationsAll })
      void qc.invalidateQueries({ queryKey: chatKeys.unread })
      emit({ kind: 'participants.changed', conversationId: parsed.data.conversationId })
    })
    connection.onreconnected(refreshAfterGap)
    // also at the FIRST start: the badge was loaded before the connection was up, a message in between came by no push
    const cancelRetry = startWithRetry(connection, () => disposed, (attempt) => {
      if (attempt > 0) refreshAfterGap()
      else void qc.invalidateQueries({ queryKey: chatKeys.unread })
    })

    return () => {
      disposed = true
      cancelRetry()
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [qc, emit, refreshAfterGap])

  const chatUnread = unread.data?.unread
  const total = (notificationsUnread ?? 0) + (chatUnread ?? 0)
  useEffect(() => {
    document.title = total ? sq.notifications.tabTitleWithCount(total) : sq.notifications.tabTitle
  }, [total])
  useEffect(() => () => void (document.title = sq.notifications.tabTitle), [])

  const subscribe = useCallback<ChatState['subscribe']>((listener) => {
    listeners.current.add(listener)
    return () => void listeners.current.delete(listener)
  }, [])

  const value = useMemo<ChatState>(
    () => ({ meId, unread: chatUnread, unreadCapped: unread.data?.capped ?? false, subscribe }),
    [meId, chatUnread, unread.data?.capped, subscribe],
  )
  return <ChatContext.Provider value={value}>{children}</ChatContext.Provider>
}

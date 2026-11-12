import { Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material'
import { ArrowDown } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/client'
import { useMe } from '../identity/api'
import { sq } from '../../i18n/sq'
import { problemMessage } from '../../lib/problem'
import { deleteMessage, editMessage, fetchMessages, sendMessage, useMarkChatRead, type ChatMessage } from './api'
import { useChat } from './chatContext'
import { Composer } from './Composer'
import { MessageItem } from './MessageItem'
import {
  canChange,
  confirmSent,
  MAX_IN_VIEW,
  newTempId,
  prependMessages,
  SYSTEM,
  threadItems,
  upsertMessage,
  type ThreadMessage,
} from './model'

const t = sq.chat

const PAGE = 50
/** Closer than this to the bottom counts as "at the bottom" (new messages scroll into view). */
const BOTTOM_SLACK = 48
/** Closer than this to the top loads the older page. */
const TOP_LOAD_AT = 160
const READ_DELAY_MS = 1000

const fromApi = (m: ChatMessage): ThreadMessage => ({ ...m, sender: m.sender ?? null })

type Gone = 'removed' | 'not-allowed' | null

/**
 * One conversation (task 050): the chat page and the case's Chat tab use it alike.
 * - Newest page first, older pages when scrolling up, with the scroll position kept (no jump).
 * - At most ~300 messages in the DOM: the oldest are dropped (at a page boundary) when the user is at the bottom, and
 *   come back by scrolling up (each page remembers the cursor of the page before it).
 * - A new message scrolls into view only when the user is at the bottom; otherwise a "n mesazhe të reja ↓" pill.
 * - Optimistic send (a temp id, replaced by the server's message; the hub's echo is not shown twice).
 * - Read up to the newest message once it is visible AND the window has focus (1 s debounce, never backwards).
 */
export function ChatThread({
  conversationId,
  readOnly,
  height,
}: {
  conversationId: string
  readOnly: boolean
  /** CSS height of the whole thread (list + composer) */
  height: string | Record<string, string>
}) {
  const { meId, subscribe } = useChat()
  const me = useMe(true)
  const markRead = useMarkChatRead()

  const [messages, setMessages] = useState<ThreadMessage[]>([])
  /** id of the oldest message of each loaded page → the cursor for the page before it (null = nothing older) */
  const [boundaries, setBoundaries] = useState<Record<string, string | null>>({})
  const [loading, setLoading] = useState(true)
  const [loadingOlder, setLoadingOlder] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [gone, setGone] = useState<Gone>(null)
  const [newCount, setNewCount] = useState(0)
  const [replyTo, setReplyTo] = useState<ThreadMessage | null>(null)
  const [editing, setEditing] = useState<ThreadMessage | null>(null)
  const [deleting, setDeleting] = useState<ThreadMessage | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [failedTexts, setFailedTexts] = useState<Record<string, string>>({})
  const [now, setNow] = useState(() => Date.now())

  const scroller = useRef<HTMLDivElement>(null)
  const bottomSentinel = useRef<HTMLDivElement>(null)
  const atBottom = useRef(true)
  /** set before a prepend: distance from the bottom to keep */
  const anchor = useRef<number | null>(null)
  const stickToBottom = useRef(true)
  const lastMarked = useRef<string | null>(null)
  const messagesRef = useRef(messages)
  useEffect(() => {
    messagesRef.current = messages
  }, [messages])

  const oldest = messages[0]
  const olderCursor = oldest ? boundaries[oldest.id] : null

  const handleGone = useCallback((error: unknown) => {
    if (error instanceof ApiError && error.status === 403) setGone('removed')
    else if (error instanceof ApiError && error.status === 404) setGone('not-allowed')
    else return false
    return true
  }, [])

  // ------------------------------------------------------------------ loading

  /** After a gap (reconnect) or a participant change: what is newer than shown joins in (nothing is dropped); a 403 = removed. */
  const loadNewest = useCallback(async () => {
    try {
      const page = await fetchMessages(conversationId, undefined, PAGE)
      const items = page.items.map(fromApi)
      setMessages((list) => items.reduce((acc, m) => upsertMessage(acc, m, meId), list))
      setLoadError(null)
    } catch (error) {
      if (!handleGone(error)) setLoadError(problemMessage(error))
    }
  }, [conversationId, handleGone, meId])

  // the first page; one thread per conversation (the parents key it by the conversation id: fresh state for another)
  useEffect(() => {
    let current = true
    fetchMessages(conversationId, undefined, PAGE)
      .then((page) => {
        if (!current) return
        const items = page.items.map(fromApi)
        stickToBottom.current = true
        setMessages([...items].reverse())
        setBoundaries(items.length ? { [items[items.length - 1].id]: page.nextCursor ?? null } : {})
      })
      .catch((error: unknown) => {
        if (current && !handleGone(error)) setLoadError(problemMessage(error))
      })
      .finally(() => {
        if (current) setLoading(false)
      })
    return () => {
      current = false
    }
  }, [conversationId, handleGone])

  const loadOlder = useCallback(async () => {
    if (loadingOlder || !olderCursor) return
    setLoadingOlder(true)
    try {
      const page = await fetchMessages(conversationId, olderCursor, PAGE)
      const items = page.items.map(fromApi)
      const el = scroller.current
      if (el) anchor.current = el.scrollHeight - el.scrollTop
      setMessages((list) => prependMessages(list, [...items].reverse()))
      if (items.length) setBoundaries((b) => ({ ...b, [items[items.length - 1].id]: page.nextCursor ?? null }))
    } catch (error) {
      if (!handleGone(error)) setLoadError(problemMessage(error))
    } finally {
      setLoadingOlder(false)
    }
  }, [conversationId, olderCursor, loadingOlder, handleGone])

  // keep the view where it was after older messages were put in front, or at the bottom when it should stick there
  useLayoutEffect(() => {
    const el = scroller.current
    if (!el) return
    if (anchor.current !== null) {
      el.scrollTop = el.scrollHeight - anchor.current
      anchor.current = null
    } else if (stickToBottom.current) {
      el.scrollTop = el.scrollHeight
      stickToBottom.current = false
    }
  }, [messages])

  // the DOM cap: at the bottom, drop the oldest pages beyond ~300 (cut only where a page starts, to load it back).
  // Applied where messages are added while the user is at the bottom (a new message, scrolling down).
  const boundariesRef = useRef(boundaries)
  useEffect(() => {
    boundariesRef.current = boundaries
  }, [boundaries])
  const capAtBottom = useCallback((list: ThreadMessage[]): ThreadMessage[] => {
    if (!atBottom.current || list.length <= MAX_IN_VIEW) return list
    for (let i = 1; i < list.length; i++) {
      if (list.length - i <= MAX_IN_VIEW && list[i].id in boundariesRef.current) {
        stickToBottom.current = true
        return list.slice(i)
      }
    }
    return list
  }, [])

  // ------------------------------------------------------------------ live

  useEffect(
    () =>
      subscribe((event) => {
        if (event.kind === 'reconnected') {
          void loadNewest()
          return
        }
        if (event.conversationId !== conversationId) return
        if (event.kind === 'participants.changed') {
          // was I the one removed? the messages answer 403 then
          void loadNewest()
          return
        }
        if (event.kind === 'conversation.read') return
        const message = event.message
        const known = messagesRef.current.some((m) => m.id === message.id)
        setMessages((list) => capAtBottom(upsertMessage(list, message, meId)))
        if (event.kind === 'message.new' && !known && message.sender?.id !== meId) {
          if (atBottom.current) stickToBottom.current = true
          else setNewCount((n) => n + 1)
        }
      }),
    [subscribe, conversationId, meId, loadNewest, capAtBottom],
  )

  // the 15-minute edit window closes by itself
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30_000)
    return () => clearInterval(timer)
  }, [])

  // ------------------------------------------------------------------ read state

  const newestId = [...messages].reverse().find((m) => !m.tempId)?.id
  const { mutate: markAsRead } = markRead
  const markIfSeen = useCallback(() => {
    const newest = [...messagesRef.current].reverse().find((m) => !m.tempId)
    const el = bottomSentinel.current
    if (!newest || !el || !document.hasFocus() || document.visibilityState !== 'visible') return
    const box = el.getBoundingClientRect()
    const view = scroller.current?.getBoundingClientRect()
    if (view && (box.top > view.bottom || box.bottom < view.top)) return
    // never backwards: only a message newer than the last one marked
    if (lastMarked.current && lastMarked.current >= `${newest.createdAt}|${newest.id}`) return
    lastMarked.current = `${newest.createdAt}|${newest.id}`
    markAsRead({ conversationId, lastMessageId: newest.id })
  }, [conversationId, markAsRead])

  useEffect(() => {
    if (!newestId || gone) return
    const el = bottomSentinel.current
    if (!el) return
    let timer: ReturnType<typeof setTimeout> | undefined
    const schedule = () => {
      clearTimeout(timer)
      timer = setTimeout(markIfSeen, READ_DELAY_MS)
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) schedule()
    }, { root: scroller.current })
    observer.observe(el)
    window.addEventListener('focus', schedule)
    return () => {
      observer.disconnect()
      window.removeEventListener('focus', schedule)
      clearTimeout(timer)
    }
  }, [newestId, gone, markIfSeen])

  // ------------------------------------------------------------------ actions

  const myName = me.data ? `${me.data.firstName} ${me.data.lastName}` : t.you

  const send = useCallback(
    async (body: string, replyToId: string | undefined, existingTemp?: ThreadMessage) => {
      const temp: ThreadMessage = existingTemp
        ? { ...existingTemp, status: 'sending' }
        : {
            id: newTempId(),
            type: 'TEXT',
            sender: meId ? { id: meId, name: myName } : null,
            body,
            isDeleted: false,
            replyToMessageId: replyToId ?? null,
            createdAt: new Date().toISOString(),
          }
      const tempId = existingTemp?.tempId ?? temp.id
      const pending = { ...temp, tempId, status: 'sending' as const }
      setMessages((list) => [...list.filter((m) => m.tempId !== tempId), pending])
      stickToBottom.current = true
      setNewCount(0)
      try {
        const saved = await sendMessage(conversationId, body, replyToId)
        setMessages((list) => confirmSent(list, tempId, fromApi(saved)))
      } catch (error) {
        if (handleGone(error)) return
        const text = error instanceof ApiError && error.status === 429 ? t.rateLimited : undefined
        setFailedTexts((f) => ({ ...f, [tempId]: text ? `${text} ${t.failed}` : t.failed }))
        setMessages((list) => list.map((m) => (m.tempId === tempId ? { ...m, status: 'failed' } : m)))
      }
    },
    [conversationId, meId, myName, handleGone],
  )

  const onSend = (body: string) => {
    void send(body, replyTo?.id)
    setReplyTo(null)
  }
  const onRetry = useCallback((m: ThreadMessage) => void send(m.body, m.replyToMessageId ?? undefined, m), [send])
  const onReply = useCallback((m: ThreadMessage) => {
    setEditing(null)
    setReplyTo(m)
  }, [])
  const onEdit = useCallback((m: ThreadMessage) => {
    setReplyTo(null)
    setEditing(m)
  }, [])
  const onDeleteAsk = useCallback((m: ThreadMessage) => setDeleting(m), [])

  const saveEdit = async (id: string, body: string) => {
    try {
      const saved = await editMessage(id, body)
      setMessages((list) => upsertMessage(list, fromApi(saved)))
      setEditing(null)
      setActionError(null)
    } catch (error) {
      if (!handleGone(error)) setActionError(problemMessage(error))
    }
  }

  const confirmDelete = async () => {
    if (!deleting) return
    try {
      const saved = await deleteMessage(deleting.id)
      setMessages((list) => upsertMessage(list, fromApi(saved)))
      setActionError(null)
    } catch (error) {
      if (!handleGone(error)) setActionError(problemMessage(error))
    } finally {
      setDeleting(null)
    }
  }

  const onScroll = () => {
    const el = scroller.current
    if (!el) return
    atBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < BOTTOM_SLACK
    if (atBottom.current) {
      setNewCount(0)
      // back down after scrolling up: older pages loaded on the way are dropped again (the DOM cap)
      setMessages((list) => {
        const capped = capAtBottom(list)
        return capped.length === list.length ? list : capped
      })
    }
    if (el.scrollTop < TOP_LOAD_AT && olderCursor && !loadingOlder) void loadOlder()
  }

  const toBottom = () => {
    const el = scroller.current
    if (el) el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' })
    setNewCount(0)
  }

  // ------------------------------------------------------------------ render

  const byId = new Map(messages.map((m) => [m.id, m]))
  const items = threadItems(messages)
  const banner = gone === 'removed' ? t.removed : gone === 'not-allowed' ? t.notAllowed : readOnly ? t.readOnly : undefined

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height, minHeight: 0, position: 'relative' }}>
      {gone && (
        <Alert severity="warning" sx={{ m: 2 }}>
          {gone === 'removed' ? t.removed : t.notAllowed}
        </Alert>
      )}
      {loadError && !gone && (
        <Alert severity="error" sx={{ m: 2 }}>
          {loadError}
        </Alert>
      )}
      {actionError && (
        <Alert severity="error" sx={{ mx: 2, mt: 1 }} onClose={() => setActionError(null)}>
          {actionError}
        </Alert>
      )}
      <Box
        ref={scroller}
        onScroll={onScroll}
        role="log"
        aria-live="polite"
        aria-label={t.messages}
        tabIndex={0}
        sx={{ flex: 1, minHeight: 0, overflowY: 'auto', px: 2, py: 1, display: gone ? 'none' : 'block' }}
      >
        {loading && <CircularProgress size={24} sx={{ display: 'block', mx: 'auto', my: 3 }} aria-label={sq.errors.loading} />}
        {loadingOlder && (
          <Typography variant="caption" color="text.secondary" component="p" sx={{ textAlign: 'center', py: 1 }}>
            {t.loadingOlder}
          </Typography>
        )}
        {!loading && messages.length === 0 && !loadError && (
          <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', my: 4 }}>
            {t.noMessages}
          </Typography>
        )}
        {items.map((item) =>
          item.kind === 'day' ? (
            <Box key={item.key} role="separator" sx={{ display: 'flex', justifyContent: 'center', my: 1.5 }}>
              <Chip size="small" label={item.label} variant="outlined" />
            </Box>
          ) : (
            <MessageItem
              key={item.key}
              message={item.message}
              mine={!!meId && item.message.sender?.id === meId}
              firstOfGroup={item.firstOfGroup}
              replied={item.message.replyToMessageId ? byId.get(item.message.replyToMessageId) : undefined}
              canReply={!readOnly && !gone && !item.message.tempId && !item.message.isDeleted && item.message.type !== SYSTEM}
              canChange={!readOnly && !gone && canChange(item.message, meId, now)}
              onReply={onReply}
              onEdit={onEdit}
              onDelete={onDeleteAsk}
              onRetry={onRetry}
              failedText={item.message.tempId ? failedTexts[item.message.tempId] : undefined}
            />
          ),
        )}
        {/* '1px', not 1: MUI reads a number ≤ 1 as a fraction (100%) */}
        <Box ref={bottomSentinel} sx={{ height: '1px' }} />
      </Box>

      {newCount > 0 && (
        <Button
          variant="contained"
          size="small"
          onClick={toBottom}
          endIcon={<ArrowDown size={16} aria-hidden />}
          sx={{ position: 'absolute', left: '50%', transform: 'translateX(-50%)', bottom: 96, borderRadius: 999, boxShadow: 3 }}
        >
          {t.newMessages(newCount)}
        </Button>
      )}

      <Composer
        conversationId={conversationId}
        disabled={loading}
        banner={banner}
        replyTo={replyTo ? { name: replyTo.sender?.name ?? '', excerpt: replyTo.body } : undefined}
        onCancelReply={() => setReplyTo(null)}
        editing={editing ? { id: editing.id, body: editing.body } : undefined}
        onCancelEdit={() => setEditing(null)}
        onSend={onSend}
        onSaveEdit={(id, body) => void saveEdit(id, body)}
      />

      <Dialog open={deleting !== null} onClose={() => setDeleting(null)} aria-labelledby="chat-delete-title">
        <DialogTitle id="chat-delete-title">{t.deleteTitle}</DialogTitle>
        <DialogContent>
          <Typography>{t.deleteQuestion}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleting(null)}>{t.cancel}</Button>
          <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
            {t.delete}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}

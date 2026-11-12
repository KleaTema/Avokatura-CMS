import { addDays, formatTiraneDate, tiraneDateOf, todayInTirane } from '../../lib/dates'
import { sq } from '../../i18n/sq'

/**
 * Task 050 — the chat thread's pure logic (no React): day separators, grouping by sender, safe links, and merging the
 * optimistic messages with what the server and the hub send.
 */

export interface ThreadUser {
  id: string
  name: string
}

/** A message as the thread keeps it: the API's ChatMessage, plus the local state of one being sent. */
export interface ThreadMessage {
  id: string
  type: string
  sender?: ThreadUser | null
  body: string
  isDeleted: boolean
  replyToMessageId?: string | null
  createdAt: string
  editedAt?: string | null
  /** set while it exists only here (the optimistic copy); id = tempId until the server answers */
  tempId?: string
  status?: 'sending' | 'failed'
}

export const SYSTEM = 'SYSTEM'

/** "Sot", "Dje" or dd.MM.yyyy — the Tirane calendar day of a UTC instant. */
export function dayLabel(utcIso: string, now: Date = new Date()): string {
  const day = tiraneDateOf(utcIso)
  const today = todayInTirane(now)
  if (day === today) return sq.chat.today
  if (day === addDays(today, -1)) return sq.chat.yesterday
  return formatTiraneDate(utcIso)
}

export type ThreadItem =
  | { kind: 'day'; key: string; label: string }
  | { kind: 'message'; key: string; message: ThreadMessage; firstOfGroup: boolean }

/** Messages of one sender within this many minutes share a header (name shown once). */
const GROUP_GAP_MS = 5 * 60_000

/** Oldest first → a day separator before each new Tirane day; a group starts on a new sender, day, gap or system message. */
export function threadItems(messages: readonly ThreadMessage[], now: Date = new Date()): ThreadItem[] {
  const items: ThreadItem[] = []
  let lastDay: string | null = null
  let previous: ThreadMessage | null = null
  for (const message of messages) {
    const day = tiraneDateOf(message.createdAt) ?? ''
    if (day !== lastDay) {
      items.push({ kind: 'day', key: `day-${day}`, label: dayLabel(message.createdAt, now) })
      lastDay = day
      previous = null
    }
    const firstOfGroup =
      previous === null ||
      message.type === SYSTEM ||
      previous.type === SYSTEM ||
      previous.sender?.id !== message.sender?.id ||
      Date.parse(message.createdAt) - Date.parse(previous.createdAt) > GROUP_GAP_MS
    items.push({ kind: 'message', key: message.tempId ?? message.id, message, firstOfGroup })
    previous = message
  }
  return items
}

export interface TextPart {
  text: string
  /** only http(s) URLs; the text is shown as text (React escapes it), never as HTML */
  href?: string
}

const URL_PATTERN = /\bhttps?:\/\/[^\s<>"']+/gi
const TRAILING = /[.,;:!?)\]}]+$/

/** Splits plain text into text and http/https links. Anything else (javascript:, data:, www.…) stays text. */
export function linkify(text: string): TextPart[] {
  const parts: TextPart[] = []
  let last = 0
  for (const match of text.matchAll(URL_PATTERN)) {
    const start = match.index ?? 0
    const raw = match[0]
    // a sentence's full stop or a closing bracket is not part of the link
    const url = raw.replace(TRAILING, '')
    if (start > last) parts.push({ text: text.slice(last, start) })
    let href: string | undefined
    try {
      const parsed = new URL(url)
      href = parsed.protocol === 'http:' || parsed.protocol === 'https:' ? parsed.href : undefined
    } catch {
      href = undefined
    }
    parts.push(href ? { text: url, href } : { text: url })
    last = start + url.length
  }
  if (last < text.length) parts.push({ text: text.slice(last) })
  return parts.length ? parts : [{ text }]
}

const byTime = (a: ThreadMessage, b: ThreadMessage) =>
  a.createdAt === b.createdAt ? 0 : a.createdAt < b.createdAt ? -1 : 1

/**
 * Adds or replaces one server message (from a page, a send response or the hub). The hub's echo of MY message may come
 * before the send's response: then it takes the place of the oldest pending copy with the same text, so it is never
 * shown twice. Kept oldest first.
 */
export function upsertMessage(list: readonly ThreadMessage[], message: ThreadMessage, meId?: string): ThreadMessage[] {
  const existing = list.findIndex((m) => m.id === message.id && !m.tempId)
  if (existing >= 0) {
    const next = [...list]
    next[existing] = message
    return next
  }
  if (meId && message.sender?.id === meId) {
    const pending = list.findIndex((m) => m.tempId && m.status === 'sending' && m.body === message.body)
    if (pending >= 0) {
      const next = [...list]
      next[pending] = { ...message, tempId: undefined }
      return next.sort(byTime)
    }
  }
  return [...list, message].sort(byTime)
}

/** The send answered: its optimistic copy becomes the server message (or disappears if the hub echo already did that). */
export function confirmSent(list: readonly ThreadMessage[], tempId: string, message: ThreadMessage): ThreadMessage[] {
  const withoutTemp = list.filter((m) => m.tempId !== tempId)
  return upsertMessage(withoutTemp, message)
}

/** Older messages (a page) in front; duplicates dropped. */
export function prependMessages(list: readonly ThreadMessage[], older: readonly ThreadMessage[]): ThreadMessage[] {
  const known = new Set(list.map((m) => m.id))
  return [...older.filter((m) => !known.has(m.id)), ...list].sort(byTime)
}

/** The DOM keeps at most `max` messages: the oldest are dropped from view (scrolling up loads them again). */
export const MAX_IN_VIEW = 300

export function capOldest(list: readonly ThreadMessage[], max = MAX_IN_VIEW): ThreadMessage[] {
  return list.length > max ? list.slice(list.length - max) : [...list]
}

export const newTempId = () => `temp-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`

/** Edit and delete are offered for 15 minutes after sending (the API's rule; client question chat-edit-window). */
export const EDIT_WINDOW_MS = 15 * 60_000

export const canChange = (message: ThreadMessage, meId: string | undefined, now: number = Date.now()) =>
  !!meId &&
  !message.tempId &&
  !message.isDeleted &&
  message.type !== SYSTEM &&
  message.sender?.id === meId &&
  now - Date.parse(message.createdAt) < EDIT_WINDOW_MS

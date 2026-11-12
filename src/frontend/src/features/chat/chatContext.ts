import { createContext, useContext } from 'react'
import type { ThreadMessage } from './model'

/** What the chat hub said (task 050). Message events carry the conversation and the message. */
export type ChatEvent =
  | { kind: 'message.new' | 'message.edited' | 'message.deleted'; conversationId: string; message: ThreadMessage }
  | { kind: 'conversation.read'; conversationId: string; userId: string; lastReadMessageId: string }
  | { kind: 'participants.changed'; conversationId: string }
  /** the connection came back: pushes during the gap are lost, a thread fetches what is newer than it shows */
  | { kind: 'reconnected' }

export interface ChatState {
  /** my public id (the hub's user): my own messages, my echo */
  meId: string | undefined
  /** the Chat menu badge */
  unread: number | undefined
  unreadCapped: boolean
  subscribe: (listener: (event: ChatEvent) => void) => () => void
}

export const ChatContext = createContext<ChatState | null>(null)

export function useChat(): ChatState {
  const ctx = useContext(ChatContext)
  if (!ctx) throw new Error('useChat must be used inside ChatProvider')
  return ctx
}

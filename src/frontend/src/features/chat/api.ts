import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'

export type ChatMessage = components['schemas']['ChatMessage']
export type ChatConversation = components['schemas']['ChatConversation']
export type ChatParticipant = components['schemas']['ChatParticipant']
export type MessagePage = components['schemas']['MessagePage']

/** Every chat query under one prefix. The thread's messages are NOT here: the thread keeps them itself (model.ts). */
export const chatKeys = {
  all: ['chat'] as const,
  unread: ['chat', 'unread-total'] as const,
  conversations: (cursor: string | undefined) => ['chat', 'conversations', cursor ?? null] as const,
  conversationsAll: ['chat', 'conversations'] as const,
  caseConversation: (casePublicId: string) => ['chat', 'case', casePublicId] as const,
  participants: (conversationId: string) => ['chat', 'participants', conversationId] as const,
}

/** The Chat menu badge: unread over my conversations (the server caps it at 99). Live via ChatProvider; polled as a safety net. */
export const useChatUnread = (enabled: boolean) =>
  useQuery({
    queryKey: chatKeys.unread,
    enabled,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    queryFn: () =>
      api
        .GET('/api/chat/unread-total')
        .then(unwrap)
        .then((r) => ({ unread: Number(r.unread), capped: r.capped })),
  })

const conversationsQuery = (cursor: string | undefined) => ({
  queryKey: chatKeys.conversations(cursor),
  queryFn: () => api.GET('/api/chat/conversations', { params: { query: { after: cursor, limit: 20 } } }).then(unwrap),
})

/**
 * My conversations (most recent activity first): the first page and every page after it the user asked for with
 * "Shfaq më shumë" (keyset cursors). A list beside the thread, not a table: it grows instead of paging.
 */
export const useConversationPages = (cursors: readonly string[]) =>
  useQueries({
    queries: [undefined, ...cursors].map((cursor) => ({ ...conversationsQuery(cursor), placeholderData: keepPreviousData })),
  })

/** The case's chat, created on first use (POST is idempotent; it also brings the participants up to date). */
export const useCaseConversation = (casePublicId: string, enabled: boolean) =>
  useQuery({
    queryKey: chatKeys.caseConversation(casePublicId),
    enabled,
    retry: false,
    queryFn: () =>
      api.POST('/api/chat/cases/{caseId}/conversation', { params: { path: { caseId: casePublicId } } }).then(unwrap),
  })

export const useParticipants = (conversationId: string, enabled: boolean) =>
  useQuery({
    queryKey: chatKeys.participants(conversationId),
    enabled,
    queryFn: () =>
      api.GET('/api/chat/conversations/{id}/participants', { params: { path: { id: conversationId } } }).then(unwrap),
  })

export const fetchMessages = (conversationId: string, before: string | undefined, limit = 50) =>
  api
    .GET('/api/chat/conversations/{id}/messages', { params: { path: { id: conversationId }, query: { before, limit } } })
    .then(unwrap)

export const sendMessage = (conversationId: string, body: string, replyToMessageId: string | undefined) =>
  api
    .POST('/api/chat/conversations/{id}/messages', {
      params: { path: { id: conversationId } },
      body: { body, replyToMessageId: replyToMessageId ?? null },
    })
    .then(unwrap)

export const editMessage = (messageId: string, body: string) =>
  api.PUT('/api/chat/messages/{id}', { params: { path: { id: messageId } }, body: { body } }).then(unwrap)

export const deleteMessage = (messageId: string) =>
  api.DELETE('/api/chat/messages/{id}', { params: { path: { id: messageId } } }).then(unwrap)

/** Marks read up to the message; the server never moves the pointer back. The badges reload. */
export const useMarkChatRead = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ conversationId, lastMessageId }: { conversationId: string; lastMessageId: string }) =>
      api
        .POST('/api/chat/conversations/{id}/read', { params: { path: { id: conversationId } }, body: { lastMessageId } })
        .then((r) => {
          if (r.error) throw r.error
        }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: chatKeys.unread })
      void qc.invalidateQueries({ queryKey: chatKeys.conversationsAll })
    },
  })
}

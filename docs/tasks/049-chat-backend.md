# 049 — Chat backend

**Phase:** 9 · Chat  
**Depends on:** [040](040-notifications.md)

## Goal
Conversations and messages with keyset paging, read pointers and SignalR delivery. Who-can-chat-with-whom is a pluggable policy.

## Context
- Schema `ash_chat`: conversations (DIRECT|CASE|GROUP, case_id, direct_pair_key, last_message_at), conversation_participants (last_read_message_id), messages (partitioned by month, PK (id, created_at)), message_attachments.
- Connection `ConnectionStrings:Chat` (account `ash_chat_app`).
- Paging: `WHERE conversation_id=? AND id < :cursor ORDER BY id DESC LIMIT 50`. Unread = messages with id > last_read_message_id (cap 99+).

## Files to create or change
- Ash.Modules.Chat/*
- Ash.Modules.Chat/Application/Policy/IChatPolicy.cs (`CanStartDirect(a,b)`, `CaseParticipants(caseId)`)
- Ash.Modules.Chat/Infrastructure/ChatHub.cs

## Steps
1. Endpoints: list my conversations, open direct (idempotent via direct_pair_key), open case chat (participants = case team + institution users from `v_case_access`), send message (max 4000 chars), list messages, mark read.
2. Case chat participants sync on assignment/transfer events (left_at for removed people).
3. Implement `OpenChatPolicy` (everyone) and `CaseOnlyChatPolicy`; select via config `Chat:Policy`.

## Acceptance criteria
- [ ] Two users exchange messages live; unread badge correct after reload.
- [ ] Paging 10,000 messages stays fast (EXPLAIN shows partition pruning).

## Out of scope
- Attachments (reuse task 033 pipeline, later).

## Open questions (use the default, mark TODO in code)
- Who may chat with whom (open to all vs restricted)? Pending decision.

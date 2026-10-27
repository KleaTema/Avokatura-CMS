# 050 — Chat UI

**Phase:** 9 · Chat  
**Depends on:** [011](011-frontend-skeleton.md), [049](049-chat-backend.md)

## Goal
Chat page and case-chat panel.

## Context
- Endpoints and hub from task 049.

## Files to create or change
- src/frontend/src/pages/Chat.tsx
- src/frontend/src/components/CaseChatPanel.tsx

## Steps
1. Conversation list sorted by last_message_at, unread badges, infinite scroll upward for history, typing not required.

## Acceptance criteria
- [ ] Message appears for the other user without refresh.

## Out of scope
- Anything not listed above.

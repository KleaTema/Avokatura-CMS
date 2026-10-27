# 040 — Notifications (Njoftimet) with real-time push

**Phase:** 6 · Notifications & background jobs  
**Depends on:** [023](023-assign-lawyers.md)

## Goal
Store notifications, show the unread count, list and mark them read, and push new ones live with SignalR.

## Context
- `notifications(user_id, notification_type, case_id, title, body, link_path, created_at, read_at)`.
- Producers: CaseRegistered → office head; LawyerAssigned/Transferred → lawyers; InstitutionLinked/Delegated → institution users; reminders (task 041); infected upload.

## Files to create or change
- Ash.Modules.Notifications/*
- Ash.Modules.Notifications.Contracts/INotificationSender.cs
- Ash.Modules.Notifications/Infrastructure/NotificationsHub.cs (SignalR, user-targeted)
- src/frontend/src/components/NotificationBell.tsx, pages/Notifications.tsx

## Steps
1. Event handlers for each Cases event insert notifications in the same transaction; after commit push via hub.
2. `GET /api/notifications?after=`, `GET /api/notifications/unread-count`, `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all`.
3. `link_path` uses public ids (e.g. `/cases/{publicId}`).

## Acceptance criteria
- [ ] Assigning a lawyer makes the bell count increase within 2 seconds without refresh.

## Out of scope
- E-mail notifications.

## Open questions (use the default, mark TODO in code)
- Should notifications also be sent by e-mail?

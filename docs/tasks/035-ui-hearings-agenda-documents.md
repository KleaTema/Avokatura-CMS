# 035 — UI: hearings, agenda and documents

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [022](022-ui-case-list-detail.md), [030](030-hearings.md), [031](031-agenda.md), [034](034-document-list-download-delete.md)

## Goal
Hearings tab, agenda calendar page and documents tab with drag-and-drop upload.

## Context
- Endpoints from tasks 030, 031, 033, 034.

## Files to create or change
- src/frontend/src/pages/cases/tabs/HearingsTab.tsx, DocumentsTab.tsx
- src/frontend/src/pages/Agenda.tsx

## Steps
1. Agenda: week view default, month view, list view; click opens the case.
2. Upload: progress bar, shows PENDING → CLEAN status (poll or SignalR from task 040).
3. Hearing form pre-fills previous description (workflow).

## Acceptance criteria
- [ ] A lawyer can add a hearing and upload a PDF without leaving the case page.

## Out of scope
- Anything not listed above.

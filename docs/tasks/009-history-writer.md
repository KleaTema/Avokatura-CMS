# 009 — Case history writer

**Phase:** 0 · Foundation  
**Depends on:** [004](004-api-host-pipeline.md)

## Goal
Give all case features one way to write the business history shown in the case 'Historiku' tab.

## Context
- Table `case_history_events(dossier_id, case_id, event_type, actor_user_id, occurred_at, summary, details JSON)` — append-only (triggers reject UPDATE/DELETE).
- Event types list is in the comment of 004_core_documents_history.sql.
- Summary text is Albanian and shown as-is, e.g. "Avokati primar u ndryshua: Drita Avokatja → Ermal Avokati".

## Files to create or change
- Ash.Modules.Cases.Contracts/IHistoryWriter.cs, HistoryEventType.cs (enum mirroring the list)
- Ash.Modules.Cases/Infrastructure/History/HistoryWriter.cs

## Steps
1. `Add(dossierId, caseId?, type, summary, old?, new?)` → adds the row to the current UoW (same transaction).
2. `details` JSON shape: `{"old":{...},"new":{...}}`, only changed fields.

## Acceptance criteria
- [ ] A handler that adds history and then fails leaves no history row.
- [ ] Unit test for details JSON.

## Out of scope
- Anything not listed above.

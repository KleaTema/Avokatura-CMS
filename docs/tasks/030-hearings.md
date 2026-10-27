# 030 — Hearings (Seancat)

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [020](020-case-detail.md)

## Goal
Add, edit and describe hearings; keep 'next hearing' and 'last hearing description' correct on the case.

## Context
- `case_hearings(case_id, hearing_type_id, other_type_text, scheduled_at, status SCHEDULED|HELD|POSTPONED|CANCELLED, description, row_version)`.
- Workflow: after a hearing the user writes its description and may schedule the next one. New hearing form pre-fills the previous description.
- Permissions `hearing.add`, `hearing.edit` (primary, secondary, assistant, both institution profiles).
- Maintain on `cases`: `next_hearing_at` = earliest future SCHEDULED; `last_hearing_id` = latest HELD (used by reports).

## Files to create or change
- Ash.Modules.Cases/Application/Hearings/*
- Ash.Modules.Cases/Endpoints/HearingEndpoints.cs

## Steps
1. `GET/POST /api/cases/{id}/hearings`, `PUT /api/cases/{id}/hearings/{hearingId}` (rowVersion).
2. Recalculate next/last hearing in the same transaction after every change.
3. History HEARING_ADDED / HEARING_UPDATED. Set `first_data_entry_at` if null.

## Acceptance criteria
- [ ] Marking a hearing HELD with description updates `last_hearing_id`; the active cases report shows that description.
- [ ] Type 'Tjetër' without text → 400.

## Out of scope
- Anything not listed above.

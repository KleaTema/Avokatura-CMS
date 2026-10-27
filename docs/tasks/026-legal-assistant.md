# 026 — Primary lawyer adds or removes the legal assistant

**Phase:** 3 · Assignment & case data  
**Depends on:** [023](023-assign-lawyers.md)

## Goal
The primary lawyer assigns one or more legal assistants to the case and can remove them.

## Context
- Role LEGAL_ASSISTANT in `case_assignments`; removal = `ended_at` + `end_reason = 'REMOVED'`.
- Permission `assistant.manage` (primary only).

## Files to create or change
- Ash.Modules.Cases/Application/Assistants/*
- Ash.Modules.Cases/Endpoints/AssignmentEndpoints.cs (extend)

## Steps
1. `POST /api/cases/{id}/assistants` { userPublicId }, `DELETE /api/cases/{id}/assistants/{userPublicId}`.
2. Picker: users with role LEGAL_ASSISTANT in the same office. History ASSISTANT_ADDED / ASSISTANT_REMOVED; notify the assistant.

## Acceptance criteria
- [ ] Assistant sees the case in 'Çështjet e mia'; after removal gets 404.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 5: one assistant per case or several? (default: several)

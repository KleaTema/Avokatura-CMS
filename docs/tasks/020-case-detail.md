# 020 — Case detail (quick view and tabs)

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [017](017-case-parties.md), [019](019-case-list.md)

## Goal
Return everything the case page shows: data, parties, team, hearings, documents count, decision, status and history.

## Context
- Workflow 'Shikim i shpejtë': të dhënat, palët, seancat, dokumentet, hapat, statuset, rezultati, historiku.
- Also return the levels of the dossier (X/1…X/n) so the UI can switch between them, and the caller's case permissions (so the UI hides buttons).

## Files to create or change
- Ash.Modules.Cases/Application/GetCase/*
- Ash.Modules.Cases/Application/GetHistory/*
- Ash.Modules.Cases/Endpoints/CaseDetailEndpoints.cs

## Steps
1. `GET /api/cases/{id}` → header + data + team + institutions + decision + `permissions[]` + `levels[]` + rowVersion.
2. `GET /api/cases/{id}/history?after=` keyset, newest first, includes all levels of the dossier with level label.
3. Write audit CASE_VIEW.

## Acceptance criteria
- [ ] A secondary lawyer gets `permissions` without `decision.record`.
- [ ] History shows previous primary lawyer after a transfer.

## Out of scope
- Hearings/documents endpoints (own tasks).

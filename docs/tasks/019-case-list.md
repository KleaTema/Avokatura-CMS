# 019 — Case list with scopes and filters

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [007](007-case-access-service.md)

## Goal
One list endpoint that each role uses with its own scope: my cases, office cases, unassigned, institution cases, all.

## Context
- Scope via `ICaseAccessService.ApplyScope`. Tabs: 'Çështjet e mia' (current assignment), 'Pa avokat' (REGISTERED, Kryetar), 'Të zyrës' (Kryetar), 'Të institucionit' (institution users), 'Të gjitha' (Monitor/Admin).
- Index-friendly filters: status, court, category, priority, suspended, next hearing range, lawyer, text (FULLTEXT on object_text; party names via case_parties FULLTEXT).

## Files to create or change
- Ash.Modules.Cases/Application/ListCases/*
- Ash.Modules.Cases/Endpoints/CaseListEndpoints.cs

## Steps
1. `GET /api/cases?tab=&status=&courtId=&…&after=&limit=` → KeysetPage of {publicId, caseCode, actNumber, court, primaryLawyer, priority, status, nextHearingAt, isSuspended}.
2. Sort: next_hearing_at asc for active, registered_at desc for REGISTERED. Cursor includes the sort key + id.
3. Check the query plan uses the indexes (EXPLAIN in a test or a note).

## Acceptance criteria
- [ ] Lawyer sees only own cases; Kryetar sees office cases; institution user sees linked cases.
- [ ] Paging through 5,000 seeded cases returns each case once.

## Out of scope
- Anything not listed above.

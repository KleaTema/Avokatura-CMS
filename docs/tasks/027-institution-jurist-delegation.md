# 027 — Institution: assign jurist; lawyer delegates the case

**Phase:** 3 · Assignment & case data  
**Depends on:** [017](017-case-parties.md), [023](023-assign-lawyers.md)

## Goal
Institution user assigns their jurist; the primary lawyer can delegate (or relate) the case to the institution, which then manages it.

## Context
- INSTITUTION_USER (permission `institution.assign_jurist`) assigns an INSTITUTION_JURIST of the same institution → `case_assignments` role INSTITUTION_JURIST.
- Delegation allowed only when `court_levels.allows_delegation = 1` (first and second instance). Permission `case.delegate` (primary).
- Delegate = set category DELEGIM (or RELATIM) + end the MONITORING `case_institutions` row + insert DELEGATED row, one transaction. The institution's profile becomes INSTITUTION_DELEGATED (can record the decision, change judge…).

## Files to create or change
- Ash.Modules.Cases/Application/AssignJurist/*, DelegateCase/*
- Ash.Modules.Cases/Endpoints/InstitutionEndpoints.cs

## Steps
1. `POST /api/cases/{id}/jurist` { userPublicId } (institution side).
2. `POST /api/cases/{id}/delegate` { institutionPublicId, mode: DELEGIM|RELATIM, rowVersion }.
3. History JURIST_ASSIGNED, DELEGATED; notify institution users.

## Acceptance criteria
- [ ] Delegating at Gjykata e Lartë → 400.
- [ ] After delegation the institution jurist can record the decision.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Does RELATIM give the institution the same rights as DELEGIM?

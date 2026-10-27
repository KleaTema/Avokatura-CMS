# 007 — Case access service (the single gate to case data)

**Phase:** 0 · Foundation  
**Depends on:** [006](006-identity-me-endpoint.md)  
**Also attach:** `docs/architecture.md`

## Goal
Implement `ICaseAccessService` so every case query and action is checked in one place.

## Context
- Rule 4 in copilot-instructions; docs/architecture.md → Access model.
- Views: `v_case_access(user_id, case_id, access_profile)`, `v_case_permissions(user_id, case_id, access_profile, permission_code)`.
- System permissions: `case.view_all` (Monitor, Admin), `case.view_office` (Kryetar).

## Files to create or change
- Ash.Modules.Cases.Contracts/ICaseAccessService.cs
- Ash.Modules.Cases/Infrastructure/Access/CaseAccessService.cs
- Ash.Modules.Cases/Infrastructure/Access/CaseScopeQuery.cs (IQueryable filter for lists)

## Steps
1. Methods: `CanViewAsync(caseId)`, `HasAsync(caseId, permissionCode)`, `RequireAsync(caseId, permissionCode)` (throws NotFound when the user cannot even view, Forbidden when they can view but lack the permission), `ApplyScope(IQueryable<Case>)` for lists.
2. CanView rule exactly as docs/architecture.md says, including the Protokoll who registered a still-REGISTERED case.
3. Cache per request (a user checking 10 things on one case = 1 query).

## Acceptance criteria
- [ ] Integration tests with the scenario data: primary lawyer can `decision.record`; secondary cannot; institution user of a MONITORING link can `hearing.add` but not `case.change_judge`; lawyer of another office gets NotFound; Kryetar of the office can view; Monitor can view all.
- [ ] No case query in the codebase bypasses the service (code review checklist item).

## Out of scope
- Endpoints using it (later tasks).

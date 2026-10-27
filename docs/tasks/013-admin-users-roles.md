# 013 — Admin: users and roles

**Phase:** 1 · Reference data & administration  
**Depends on:** [006](006-identity-me-endpoint.md), [011](011-frontend-skeleton.md)

## Goal
Let the Administrator create users, link them to an office or institution, grant roles and deactivate them.

## Context
- Tables: users, user_roles (granted_by), roles. A user belongs to an office OR an institution, never both (CHECK).
- Roles: ADMIN, PROTOCOL, OFFICE_HEAD, STATE_LAWYER, LEGAL_ASSISTANT, MONITOR, INSTITUTION_USER, INSTITUTION_JURIST.
- Permission `admin.users`. Every change → audit ADMIN_CHANGE.

## Files to create or change
- Ash.Modules.Identity/Application/Users/* (Create, Update, Deactivate, GrantRole, RevokeRole, List)
- Ash.Modules.Identity/Endpoints/AdminUserEndpoints.cs
- src/frontend/src/pages/admin/Users*.tsx

## Steps
1. Create user with email + names + office/institution; `external_subject` filled on first login (match by email) — document this rule.
2. Deactivate = `is_active = 0` (never delete). A deactivated lawyer still appears in history.
3. Validation: institution roles only for institution users; ASH roles only for office users.

## Acceptance criteria
- [ ] Admin can create a lawyer, grant STATE_LAWYER, log in as them (dev) and see the lawyer menu.
- [ ] Granting INSTITUTION_USER to an office user → 400 with Albanian message.

## Out of scope
- Bulk import from the old system.

## Open questions (use the default, mark TODO in code)
- Is a user created by the admin before first login, or auto-created on first login from the identity provider?

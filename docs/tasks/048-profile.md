# 048 — Profile page

**Phase:** 8 · Dashboard, search & profile  
**Depends on:** [006](006-identity-me-endpoint.md)

## Goal
Show the user's own data and preferences.

## Context
- Pending client answer (question 3). Default: read-only name, email, office/institution, roles; editable phone and notification preferences.

## Files to create or change
- Ash.Modules.Identity/Application/Profile/*
- src/frontend/src/pages/Profile.tsx

## Steps
1. `GET /api/identity/profile`, `PUT /api/identity/profile` (phone only for now).

## Acceptance criteria
- [ ] User can change their phone; other fields read-only.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 3: which profile fields per role, and which are editable?

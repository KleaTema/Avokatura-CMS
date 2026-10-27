# 015 — Admin: offices and institutions

**Phase:** 1 · Reference data & administration  
**Depends on:** [013](013-admin-users-roles.md)

## Goal
Manage ASH offices (with their Kryetar) and state institutions (with parent ministry).

## Context
- `offices(code, name, office_type CENTRAL|LOCAL|REPRESENTATION, parent_office_id, head_user_id, is_active)`.
- `institutions(name, short_name, institution_type, parent_institution_id, nipt, is_active)` — parent used by statistics 'per ministry'.
- New cases are routed to `offices.head_user_id` of the registering office.

## Files to create or change
- Ash.Modules.Identity/Application/Offices/*, Institutions/*
- Ash.Modules.Identity/Endpoints/AdminOrgEndpoints.cs
- src/frontend/src/pages/admin/Offices.tsx, Institutions.tsx

## Steps
1. CRUD + deactivate. Head of office must be an active user of that office with role OFFICE_HEAD.
2. Institution search endpoint `GET /api/identity/institutions?q=` (used by party registration).

## Acceptance criteria
- [ ] Setting a head who is not OFFICE_HEAD → 400.
- [ ] Institution search finds 'Ministria e Financave' by 'financ'.

## Out of scope
- Importing the official institution list.

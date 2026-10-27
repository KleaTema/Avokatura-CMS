# 006 — Identity module: current user and menus

**Phase:** 0 · Foundation  
**Depends on:** [005](005-authentication.md)

## Goal
Give the frontend one call that returns who the user is, their roles, their permissions and which menu items to show.

## Context
- Menus come from system permissions `menu.*` (seeded in 007_seed_reference_data.sql), e.g. Protokoll has no chat/agenda/reports.
- Tables: `users`, `user_roles`, `roles`, `role_permissions`, `permissions` (scope SYSTEM).

## Files to create or change
- Ash.Modules.Identity/Infrastructure/IdentityDbContext.cs + configurations
- Ash.Modules.Identity/Application/GetMe/*
- Ash.Modules.Identity/Endpoints/MeEndpoints.cs
- Ash.Modules.Identity.Contracts/IPermissionService.cs, IUserDirectory.cs (GetUser(publicId), FindUsers(query, role))

## Steps
1. Map Identity tables (read-only for now).
2. `GET /api/identity/me` → { publicId, firstName, lastName, email, office {publicId,name}?, institution {publicId,name}?, roles[], permissions[], menus[] }.
3. `.RequirePermission(code)` endpoint filter using IPermissionService (403 when missing).
4. IUserDirectory used later by assignment screens (list lawyers of an office).

## Acceptance criteria
- [ ] Each seeded role returns the menus from the workflow: Protokoll → dashboard, cases, notifications, profile; Kryetar → + chat, agenda, reports.
- [ ] Integration test per role checks the `menus` array.

## Out of scope
- Editing users (task 013).

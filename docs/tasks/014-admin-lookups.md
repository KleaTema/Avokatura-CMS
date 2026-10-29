# 014 — Admin: manage lookup lists and settings

**Phase:** 1 · Reference data & administration  
**Depends on:** [012](012-reference-data-api.md), [013](013-admin-users-roles.md)

## Goal
Let the Administrator add, rename and deactivate lookup values, allowed file types and the unique-code format, without a release.

## Context
- Lists from task 012. Codes are stable identifiers: name editable, code immutable after creation.
- `allowed_file_types(extension, content_type, magic_hex, max_size_bytes, applies_to, is_active)`.
- `dossier_code_formats(template, random_digits, is_active)` — exactly one active; switching = deactivate old + activate new in one transaction; old dossier codes never change.

## Files to create or change
- Ash.Modules.Reference/Application/Admin/*
- Ash.Modules.Reference/Endpoints/AdminReferenceEndpoints.cs
- src/frontend/src/pages/admin/Lists.tsx, FileTypes.tsx, CodeFormat.tsx

## Steps
1. Generic CRUD for simple lists (code, name, sort_order, is_active, is_other where present).
2. Courts: name, level, city, is_foreign. Judges: names, court, external_ref.
3. Never delete a row used by data; only deactivate.
4. Invalidate the reference cache on change. Audit ADMIN_CHANGE.

## Acceptance criteria
- [ ] Deactivated hearing type disappears from pickers but old hearings still show its name.
- [ ] Switching code format to `ASH-{YYYY}-{RAND}` makes the next dossier use it.

## Out of scope
- Anything not listed above.

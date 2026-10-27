# 021 — UI: registration form and party editor

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [011](011-frontend-skeleton.md), [016](016-register-new-case.md), [017](017-case-parties.md), [018](018-register-next-level.md)

## Goal
Protokoll screens: new case form, party editor with institution suggestion, and 'continue closed case' flow.

## Context
- Endpoints from tasks 012, 016, 017, 018. Required fields marked *. Court picker filtered by court level, judge picker by court (searchable).

## Files to create or change
- src/frontend/src/pages/cases/RegisterCase.tsx
- src/frontend/src/pages/cases/ContinueCase.tsx
- src/frontend/src/components/PartyEditor.tsx

## Steps
1. Form with react-hook-form + zod; server errors mapped to fields.
2. Party editor: type (Paditës/I paditur/Person i tretë…), kind; when kind = Institucion, an autocomplete over institutions.
3. After save show the generated code big and a 'Regjistro tjetër' button.

## Acceptance criteria
- [ ] Protokoll can register a case with 2 parties end-to-end in under 1 minute.
- [ ] Keyboard-only usage works (tab order).

## Out of scope
- Anything not listed above.

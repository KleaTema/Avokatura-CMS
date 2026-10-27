# 022 — UI: case list and case page

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [011](011-frontend-skeleton.md), [019](019-case-list.md), [020](020-case-detail.md)

## Goal
Case list with tabs/filters and the case page with tabs (Të dhënat, Palët, Seancat, Dokumentet, Vendimi, Historiku).

## Context
- Endpoints from tasks 019, 020. Buttons visible only when the action's permission is in `permissions`.

## Files to create or change
- src/frontend/src/pages/cases/CaseList.tsx
- src/frontend/src/pages/cases/CasePage.tsx + tabs/*
- src/frontend/src/components/LevelSwitcher.tsx

## Steps
1. Infinite scroll with the keyset cursor; filters in the URL query string (shareable).
2. Level switcher (X/1, X/2…) on the case page.
3. History tab: timeline with actor, date, summary; expandable old/new details.

## Acceptance criteria
- [ ] Back/forward browser buttons keep filters.
- [ ] No action button is shown that the API would reject.

## Out of scope
- Anything not listed above.

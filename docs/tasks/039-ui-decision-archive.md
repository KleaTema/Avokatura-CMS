# 039 — UI: decision, closing and archiving

**Phase:** 5 · Decision, closing & archiving  
**Depends on:** [022](022-ui-case-list-detail.md), [036](036-record-decision-close.md), [037](037-update-decision-later.md), [038](038-archive-dossier.md)

## Goal
Decision tab: close form, later completion, archive confirmation.

## Context
- Endpoints from tasks 036–038. Checkbox label depends on the level (Ankimuar / Rekursuar).

## Files to create or change
- src/frontend/src/pages/cases/tabs/DecisionTab.tsx
- src/frontend/src/components/CloseCaseDialog.tsx

## Steps
1. Result dropdown from reference data; 'Tjetër' shows a textbox.
2. Money and m² fields side by side for each value type.
3. After a final decision show the archive confirmation dialog.

## Acceptance criteria
- [ ] The whole close flow works without leaving the case page.

## Out of scope
- Anything not listed above.

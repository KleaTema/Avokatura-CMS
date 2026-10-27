# 029 — UI: assignment, transfer and case data

**Phase:** 3 · Assignment & case data  
**Depends on:** [022](022-ui-case-list-detail.md), [023](023-assign-lawyers.md), [024](024-transfer-primary-lawyer.md), [025](025-edit-case-data.md), [026](026-legal-assistant.md), [027](027-institution-jurist-delegation.md), [028](028-change-judge-object.md)

## Goal
Screens for the Kryetar (assign/transfer) and for the team (case data, status, assistant, jurist, delegation, judge/object).

## Context
- Endpoints from tasks 023–028. Hide every action the user lacks permission for.

## Files to create or change
- src/frontend/src/pages/cases/tabs/DataTab.tsx
- src/frontend/src/components/AssignLawyerDialog.tsx, TransferDialog.tsx, DelegateDialog.tsx

## Steps
1. Handle 409 (row version) with a 'Të dhënat u ndryshuan nga një përdorues tjetër — Rifresko' message.
2. Money inputs: Albanian number format; m² separate field.

## Acceptance criteria
- [ ] Kryetar assigns from the 'Pa avokat' tab in two clicks.
- [ ] Concurrent edit shows the reload message.

## Out of scope
- Anything not listed above.

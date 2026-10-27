# 037 — Complete the decision later

**Phase:** 5 · Decision, closing & archiving  
**Depends on:** [036](036-record-decision-close.md)

## Goal
When the reasoned decision arrives later, the user completes the decision data and attaches the document.

## Context
- Only fields of the decision change; the case stays CLOSED. `decision_received_at` is set when switching to received.
- Permission `decision.record`. row_version on case_decisions.

## Files to create or change
- Ash.Modules.Cases/Application/UpdateDecision/*

## Steps
1. `PUT /api/cases/{id}/decision` (rowVersion). History DECISION_UPDATED with changed fields.
2. Allow setting `is_appealed`/`is_recursed` later (appeal filed after closing).

## Acceptance criteria
- [ ] Marking received + attaching the PDF moves the case into 'mbartur ardhur vendim' in statistics.

## Out of scope
- Anything not listed above.

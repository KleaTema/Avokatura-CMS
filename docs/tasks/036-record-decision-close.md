# 036 — Record the decision and close the case

**Phase:** 5 · Decision, closing & archiving  
**Depends on:** [030](030-hearings.md), [034](034-document-list-download-delete.md)

## Goal
Users with `decision.record` close a case level with its result and decision data; the case counts as closed for statistics even if the reasoned decision has not arrived.

## Context
- `case_decisions` columns: result_id, result_other_text, decision_number, decision_date, dispositive, currency, won/lost amount + m², prevented/caused damage amount + m², court_costs_amount, decision_received, decision_document_id, is_appealed, is_recursed, is_final, row_version.
- Checkbox shown by `court_levels.appeal_kind`: ANKIM → 'Ankimuar' (first instance), REKURS → 'Rekursuar' (appeal); 'Formë e prerë' on every level. Final and appealed together is rejected by the DB.
- Close: `cases.lifecycle_status = CLOSED`, `closed_at`, `closed_by`, `next_hearing_at = NULL`; end current assignments? NO — keep them (lawyer still sees the closed case).

## Files to create or change
- Ash.Modules.Cases/Application/RecordDecision/*
- Ash.Modules.Cases/Endpoints/DecisionEndpoints.cs

## Steps
1. `POST /api/cases/{id}/decision` → creates decision + closes the case in one transaction. History DECISION_RECORDED, CASE_CLOSED.
2. If `decisionReceived = true` require the decision document (uploaded via task 033, type VENDIM) and link it.
3. Result with `sends_back_for_retrial = 1` → response suggests 'Vazhdo si rigjykim' (task 018).

## Acceptance criteria
- [ ] Closing without the reasoned decision leaves `decision_received = 0`; the case appears in 'mbartur në pritje të vendimit të arsyetuar' next year.
- [ ] Final + appealed → 400 with Albanian message.

## Out of scope
- Anything not listed above.

# 025 — Edit case data, status and classification

**Phase:** 3 · Assignment & case data  
**Depends on:** [023](023-assign-lawyers.md)

## Goal
Lawyers edit the case's working data: claim value, damage type, category/subcategory, suspension, security measure, priority.

## Context
- Columns on `cases`: claim_amount + claim_currency, claim_area_m2, category_id, subcategory_id (FK pair guarantees the subcategory belongs to the category), priority NONE|LOW|MEDIUM|HIGH, is_suspended (+suspended_at), has_security_measure.
- `case_security_measures(case_id, decision_number, decision_date, dispositive, document_id)` when has_security_measure = 1.
- Permissions: `case.edit_data` (primary, secondary, assistant, delegated institution), `case.set_status`, `case.set_priority`, `case.set_category` (primary).
- First data entry by the team sets `first_data_entry_at` (stops the 30-day reminder).

## Files to create or change
- Ash.Modules.Cases/Application/UpdateCaseData/*, SetSuspension/*, SetSecurityMeasure/*, SetPriority/*, SetCategory/*
- Ash.Modules.Cases/Endpoints/CaseDataEndpoints.cs

## Steps
1. Separate small endpoints (PATCH) per action, each with its own permission and history event (CATEGORY_CHANGED, SUSPENSION_CHANGED, SECURITY_MEASURE_SET, PRIORITY_CHANGED).
2. All require `rowVersion`; increment it on change; mismatch → 409.
3. Category DELEGIM/RELATIM is NOT set here — it goes through task 027 (delegation).

## Acceptance criteria
- [ ] Two users editing the same case: the second save gets 409 and the UI can reload.
- [ ] Subcategory KOD_A_AVSH with category DELEGIM → 400.

## Out of scope
- Anything not listed above.

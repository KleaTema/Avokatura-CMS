# 016 — Register a new case from a summons (X/1)

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [007](007-case-access-service.md), [009](009-history-writer.md), [012](012-reference-data-api.md)

## Goal
Protokoll registers a fletëthirrje: creates the dossier with its unique code and case X/1, the first hearing, and sends it to the Kryetar.

## Context
- Procedure `CALL create_dossier(office_id, user_id, @dossier_id, @code)` issues the random unique code (format from `dossier_code_formats`). Call it inside the UoW transaction.
- Case fields (required*): summons_number*, summons_type_id, act_number*, act_date, received_date*, court_level_id*, court_id*, judge_id*, object_text*, first hearing date+time*.
- case_code = `<unique_code>/1`, level_seq = 1, lifecycle_status = REGISTERED, office_id = user's office.
- Permission: system `case.create`.

## Files to create or change
- Ash.Modules.Cases/Domain/Dossier.cs, Case.cs, CaseHearing.cs
- Ash.Modules.Cases/Infrastructure/CasesDbContext.cs + configurations
- Ash.Modules.Cases/Application/RegisterCase/*
- Ash.Modules.Cases/Endpoints/RegistrationEndpoints.cs
- Ash.Modules.Cases.Contracts/Events/CaseRegistered.cs

## Steps
1. `POST /api/cases/register` → 201 { casePublicId, caseCode }.
2. Validate the court belongs to the chosen court level and the judge is active. If the judge's court differs from the chosen court, return a warning, not an error (the KLGJ list may be outdated).
3. Insert first `case_hearings` row (type from request, default PERGATITORE), set `cases.next_hearing_at`.
4. History: DOSSIER_CREATED, CASE_REGISTERED, SENT_TO_HEAD. Publish `CaseRegistered` (Notifications will notify the office head).
5. Parties are added in the same screen but saved by task 017's endpoint after the case exists (or include them in this request — choose one and document).

## Acceptance criteria
- [ ] Registering returns a code like 2026-483915/1; the case has status REGISTERED, the Protokoll's office, and one SCHEDULED hearing.
- [ ] Missing required field → 400 with field errors in Albanian.
- [ ] Integration test incl. history rows.

## Out of scope
- Linking to a closed case (task 018).

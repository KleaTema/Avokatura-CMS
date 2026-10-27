# 018 — Continue a closed case at the next level (X/n+1)

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [016](016-register-new-case.md), [017](017-case-parties.md)

## Goal
When a summons concerns an existing dispute (appeal, recourse, retrial), Protokoll finds the closed case and creates X/n+1 from it.

## Context
- X/n stays unchanged. New case: same dossier, level_seq = max+1, previous_case_id = X/n, copies object_text, parties (not deleted) and active institution links.
- Protokoll fills: summons_number*, act_number*, court_level_id*, court_id*, judge_id*, received_date*, first hearing date+time*.
- Search for the closed case: by unique code, act number of any level, party name/NIPT, court. Answer to client question 1 may refine this.

## Files to create or change
- Ash.Modules.Cases/Application/SearchClosedCases/*
- Ash.Modules.Cases/Application/RegisterNextLevel/*
- Ash.Modules.Cases/Endpoints/RegistrationEndpoints.cs (extend)

## Steps
1. `GET /api/cases/closed-search?q=&court=&after=` (keyset, max 50) returning code, act number, court, parties, closed date.
2. `POST /api/cases/{closedCaseId}/next-level` → 201 { casePublicId, caseCode }. Reject if the case is not CLOSED, the dossier is archived, or a newer level already exists.
3. History on the NEW case: CASE_CREATED_FROM_PREVIOUS (with previous code), SENT_TO_HEAD.

## Acceptance criteria
- [ ] From 2026-483915/1 (closed) Protokoll creates 2026-483915/2; parties and institution link are copied; /1 is unchanged.
- [ ] Trying again on /1 → 409 'Kjo çështje ka vazhduar tashmë në /2'.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 1: how should Protokoll identify the existing case (which search fields)?

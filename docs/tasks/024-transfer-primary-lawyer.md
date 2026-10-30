# 024 — Transfer the primary lawyer

**Phase:** 3 · Assignment & case data  
**Depends on:** [023](023-assign-lawyers.md)

## Goal
Kryetar replaces the primary lawyer of an active case; the old one stays in history and the new one sees everything done before.

## Context
- Transfer = end current PRIMARY row (`ended_at`, `ended_by`, `end_reason = 'TRANSFERRED'`) + insert new PRIMARY row, one transaction.
- Statistic 'Transferuar si çështje' counts these rows. Old lawyer loses access immediately (access view uses current rows).

## Files to create or change
- Ash.Modules.Cases/Application/TransferLawyer/*
- Ash.Modules.Cases/Endpoints/AssignmentEndpoints.cs (extend)

## Steps
1. `POST /api/cases/{id}/transfer` { newPrimaryUserPublicId, reason?, rowVersion }. Also allow changing/removing the secondary here.
2. History LAWYER_TRANSFERRED with old → new names. Notify old and new lawyer.
   History details shape fixed by task 020 test (`CaseDetailTests`, transfer seeded via SQL):
   `{"old": {"name": "First Last", "userPublicId": "<guid>"}, "new": {"name": "First Last", "userPublicId": "<guid>", "reason": "…"}}`
   — names are a snapshot at that moment, the public id is for linking, never internal ids; `reason` only when given.
3. Ongoing 30-day timer: decide per client question 6 (default: restarts for the new lawyer).

## Acceptance criteria
- [ ] After transfer, old lawyer gets 404 on the case; new lawyer sees full history.
- [ ] Integration test.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 6: does the 30-day reminder restart after a transfer?

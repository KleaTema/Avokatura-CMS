# 031 — Agenda (Agjenda)

**Phase:** 4 · Hearings, agenda & documents  
**Depends on:** [030](030-hearings.md)

## Goal
Show each user the hearings of their cases by day/week/month; Kryetar sees the office, Monitor sees all.

## Context
- View `v_user_agenda(user_id, hearing_id, case_id, case_code, scheduled_at, hearing_type, status, court)` for case-level users.
- Kryetar: hearings of office cases (filter by lawyer). Monitor: all (filter by office/lawyer).

## Files to create or change
- Ash.Modules.Cases/Application/Agenda/*
- Ash.Modules.Cases/Endpoints/AgendaEndpoints.cs

## Steps
1. `GET /api/agenda?from=&to=&lawyerId=&officeId=` (max 62-day range) → list of hearings with case code, court, type, time.
2. Use index `ix_ch_agenda(scheduled_at, status)`.

## Acceptance criteria
- [ ] A lawyer with 3 cases sees exactly their hearings in the chosen week.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 4: what else belongs in the agenda (deadlines, reminders, personal events)?

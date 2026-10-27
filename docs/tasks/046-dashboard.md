# 046 — Dashboard per role

**Phase:** 8 · Dashboard, search & profile  
**Depends on:** [019](019-case-list.md), [031](031-agenda.md), [040](040-notifications.md)

## Goal
Landing page with the numbers and lists each role needs first thing in the morning.

## Context
- Suggested widgets (confirm with client): Protokoll — cases registered today; Kryetar — cases without lawyer, cases per lawyer, overdue data reminders; Lawyer — my active cases, hearings this week, reminders; Institution — linked cases, upcoming hearings; Monitor — counts by office, overdue 6-month cases.

## Files to create or change
- Ash.Modules.Cases/Application/Dashboard/*
- src/frontend/src/pages/Dashboard.tsx

## Steps
1. One endpoint `GET /api/dashboard` returning only the widgets for the caller's roles; each widget a cheap indexed query.
2. Widgets link to the filtered case list.

## Acceptance criteria
- [ ] Dashboard loads in < 500 ms with 50,000 cases.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Which widgets does each role want on the dashboard?

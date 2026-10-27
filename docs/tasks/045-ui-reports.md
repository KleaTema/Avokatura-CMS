# 045 — UI: reports and statistics

**Phase:** 7 · Reports & statistics  
**Depends on:** [011](011-frontend-skeleton.md), [042](042-report-active-cases.md), [043](043-report-closed-cases.md), [044](044-statistics.md)

## Goal
Reports page with the two reports and the statistics table, filters and export buttons.

## Context
- Endpoints from tasks 042–044. Statistics: a wide table with sticky first column (court) and a TOTAL row.

## Files to create or change
- src/frontend/src/pages/reports/ActiveCases.tsx, ClosedCases.tsx, Statistics.tsx

## Steps
1. Filters shown according to role (lawyer has no office filter).
2. Export downloads the xlsx with a progress indicator.

## Acceptance criteria
- [ ] Kryetar can export the office's active cases in two clicks.

## Out of scope
- Anything not listed above.

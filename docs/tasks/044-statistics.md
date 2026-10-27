# 044 — Statistics (Tabela 5 and 6)

**Phase:** 7 · Reports & statistics  
**Depends on:** [042](042-report-active-cases.md)  
**Also attach:** `docs/sql/example_statistics.sql`

## Goal
Implement the statistics per court with the memo's definitions, filterable by lawyer, office, ministry, institution and year.

## Context
- Reference implementation: `docs/sql/example_statistics.sql` (each memo definition = one 0/1 flag, then summed per court WITH ROLLUP).
- Definitions: mbartur aktive, ardhur gjatë vitit, total, deleguar, relatuar, aktive, përfunduar (ASH vs deleguar/relatuar), transferuar, në dorë, pezulluar, rezultati sipas llojit, dëmi parandaluar/shkaktuar (lekë + m²), ankimuar/rekursuar, mbartur në pritje / ardhur vendim.
- Tabela 6 'Çështje institucionale' = same measures filtered by institution (and its parent ministry).

## Files to create or change
- Ash.Modules.Reports/Application/Statistics/*
- Ash.Modules.Reports/Infrastructure/Sql/statistics_*.sql (embedded)

## Steps
1. Parameterised SQL only (no string concatenation). `GET /api/reports/statistics?year=&officeId=&lawyerId=&institutionId=&ministryId=&kind=active|closed` + export.
2. Integration tests: port the scenario from the ash-db tests (2026 vs 2027 expected numbers).

## Acceptance criteria
- [ ] For the test scenario, year 2027 shows mbartur_aktive = 1 and mbartur_pritje_vendimi_arsyetuar = 1.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 6: which statistics each role may see.

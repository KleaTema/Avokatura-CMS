# 043 — Report: closed cases (Tabela 3)

**Phase:** 7 · Reports & statistics  
**Depends on:** [042](042-report-active-cases.md)

## Goal
Closed cases report: one row per closed level, with result, decision and values.

## Context
- View `v_report_closed_cases` (per level: shkalla, gjykata, gjyqtari, avokati at closing, rezultati, decision data, won/lost, damages, costs, is_appealed/is_recursed/is_final, decision_received, closed_at).

## Files to create or change
- Ash.Modules.Reports/Application/ClosedCases/*

## Steps
1. Same pattern as task 042; filters: closed date range, result, court, lawyer, office.
2. Group rows of the same dossier together (order by dossier, level).

## Acceptance criteria
- [ ] A dossier closed at /1 and /2 shows two rows with their own results.

## Out of scope
- Anything not listed above.

# 042 — Report: active cases (Tabela 1)

**Phase:** 7 · Reports & statistics  
**Depends on:** [019](019-case-list.md)

## Goal
Generate the active cases report with filters and Excel export, scoped to what the user may see.

## Context
- View `v_report_active_cases` (kodi_unik, numri_aktit, paditesi, i_padituri_person_i_trete, objekti, gjykata, gjyqtari, seanca_e_radhes, avokati, pershkrimi_seances_se_fundit, masa_sigurimit, claim values, kategoria, lloji…).
- Scope: lawyer/assistant → own cases; Kryetar → office; Monitor/Admin → all; institution → linked cases.
- Reports run on the read replica connection `ConnectionStrings:Reporting` (falls back to App locally).

## Files to create or change
- Ash.Modules.Reports/Application/ActiveCases/*
- Ash.Modules.Reports/Infrastructure/ReportingDb.cs (Dapper, read-only)
- Ash.Modules.Reports/Export/ExcelExporter.cs (ClosedXML)

## Steps
1. `GET /api/reports/active-cases?officeId=&lawyerId=&courtId=&categoryId=&after=` (JSON, paged) and `…/export` (xlsx, streamed).
2. Column headers in Albanian as in the memo. Audit REPORT_EXPORT with filters.

## Acceptance criteria
- [ ] Export of 10,000 rows finishes in < 10 s and uses streaming.
- [ ] A lawyer's export contains only their cases.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Client question 6: exact report access per role.

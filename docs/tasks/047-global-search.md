# 047 — Global search

**Phase:** 8 · Dashboard, search & profile  
**Depends on:** [019](019-case-list.md)

## Goal
One search box (dashboard + reports) that finds cases by code, act number, party, object, judge.

## Context
- FULLTEXT indexes: cases.object_text, case_parties.display_name, judges, institutions. Collation makes 'ceshtje' match 'çështje'.
- Results limited by `ICaseAccessService.ApplyScope`.

## Files to create or change
- Ash.Modules.Cases/Application/Search/*

## Steps
1. `GET /api/search?q=` → top 20 cases. Exact match on unique code / act number first, then FULLTEXT (NATURAL LANGUAGE MODE).

## Acceptance criteria
- [ ] Searching 'Hoxha' finds cases where Petrit Hoxha is a party, only within the user's scope.

## Out of scope
- Full-text inside PDFs (later, OpenSearch).

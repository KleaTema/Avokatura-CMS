# 012 — Reference data API with caching

**Phase:** 1 · Reference data & administration  
**Depends on:** [006](006-identity-me-endpoint.md)

## Goal
Serve all lookup lists to the frontend and to other modules from one cached service.

## Context
- Tables: court_levels, courts, judges, party_roles, summons_types, hearing_types, document_types, decision_results, case_categories, case_subcategories, allowed_file_types.
- Only `is_active = 1` rows in pickers; inactive rows still resolve by id (old cases).
- `is_other = 1` → UI shows the 'Tjetër' textbox.

## Files to create or change
- Ash.Modules.Reference/Infrastructure/ReferenceDbContext.cs
- Ash.Modules.Reference.Contracts/IReferenceData.cs
- Ash.Modules.Reference/Endpoints/ReferenceEndpoints.cs

## Steps
1. `GET /api/reference/{list}` for each list; `GET /api/reference/judges?courtId=&q=` (searchable, judges list can be long).
2. IMemoryCache, 10 min, invalidated by the admin edit endpoints (task 014).
3. IReferenceData methods by code: `GetResultByCode("KTHIM_RIGJYKIM")`, `GetCourtLevel(id)` incl. `AllowsDelegation`, `AppealKind`.

## Acceptance criteria
- [ ] All lists return seeded values; judges search by partial name works with and without diacritics (collation).
- [ ] Second call within 10 min hits no SQL (verify with logging).

## Out of scope
- Anything not listed above.

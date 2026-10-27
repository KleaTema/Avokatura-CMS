# 017 — Parties: add, edit, delete

**Phase:** 2 · Registration (Protokoll)  
**Depends on:** [016](016-register-new-case.md)

## Goal
Manage the parties of a case; choosing a state institution links the case to that institution.

## Context
- `case_parties(case_id, party_role_id, party_kind NATURAL|LEGAL|INSTITUTION, institution_id, display_name, identifier, deleted_at)`; INSTITUTION requires institution_id (CHECK).
- Picking an institution (search from task 015) creates `case_institutions` with link_mode MONITORING if no active link exists, and publishes `InstitutionLinked` (institution users get notified).
- Rights: Protokoll while REGISTERED; then case permissions `party.add`, `party.edit`, `party.delete` (primary, secondary, assistant, delegated institution).

## Files to create or change
- Ash.Modules.Cases/Application/Parties/*
- Ash.Modules.Cases/Endpoints/PartyEndpoints.cs

## Steps
1. `GET/POST /api/cases/{id}/parties`, `PUT/DELETE /api/cases/{id}/parties/{partyId}`.
2. Delete = soft (`deleted_at`, `deleted_by`). Deleting the last institution party does NOT end the institution link automatically (ask the user in UI).
3. History: PARTY_ADDED / PARTY_REMOVED / INSTITUTION_LINKED with names.

## Acceptance criteria
- [ ] Adding 'Ministria e Financave' as I paditur creates the MONITORING link and a notification for that institution's INSTITUTION_USER.
- [ ] Secondary lawyer can delete a party; Monitor gets 404/403 as per access rules.

## Out of scope
- Anything not listed above.

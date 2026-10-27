# 038 — Archive the dossier (final decision)

**Phase:** 5 · Decision, closing & archiving  
**Depends on:** [036](036-record-decision-close.md)

## Goal
When a level closes with a final decision (formë e prerë), the user archives the dossier; archived dossiers are read-only.

## Context
- Permission `decision.close_final`. `dossiers.archived_at`, `archived_by`.
- Workflow: after saving a final decision the system asks 'Arkivo çështjen?' and the user confirms.

## Files to create or change
- Ash.Modules.Cases/Application/ArchiveDossier/*

## Steps
1. `POST /api/dossiers/{id}/archive` — only if the latest level is CLOSED with `is_final = 1`. History DOSSIER_ARCHIVED.
2. All write endpoints reject changes on archived dossiers (one check in the access service: write permissions false when archived).
3. Case chat of an archived dossier becomes read-only (Chat module listens to `DossierArchived`).

## Acceptance criteria
- [ ] After archiving, adding a hearing → 409 'Dosja është e arkivuar'.

## Out of scope
- Anything not listed above.

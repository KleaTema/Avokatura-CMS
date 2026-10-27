# 028 — Change judge and object with history

**Phase:** 3 · Assignment & case data  
**Depends on:** [020](020-case-detail.md)

## Goal
Allowed users change the judge or the object of the case; the previous value is kept in history.

## Context
- `case.change_judge` (primary, delegated institution); `case.change_object` (primary, secondary, assistant, delegated institution).
- Old value goes to `case_history_events.details.old`.

## Files to create or change
- Ash.Modules.Cases/Application/ChangeJudge/*, ChangeObject/*
- Ash.Modules.Cases/Endpoints/CaseDataEndpoints.cs (extend)

## Steps
1. PATCH endpoints with rowVersion. History JUDGE_CHANGED / OBJECT_CHANGED with old and new.

## Acceptance criteria
- [ ] History shows 'Gjyqtari u ndryshua: Ilir Gjyqtari → Klodiana Gjyqtarja'.

## Out of scope
- Anything not listed above.

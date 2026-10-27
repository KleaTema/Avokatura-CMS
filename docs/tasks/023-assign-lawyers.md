# 023 — Kryetar assigns primary and secondary lawyer

**Phase:** 3 · Assignment & case data  
**Depends on:** [020](020-case-detail.md)

## Goal
Kryetar opens a REGISTERED case, picks the primary lawyer (optionally a secondary), optionally sets priority; the case becomes ACTIVE.

## Context
- `case_assignments(case_id, user_id, case_role, assigned_by, assigned_at, ended_at, end_reason)`; unique keys guarantee max 1 current PRIMARY and 1 current SECONDARY.
- Kryetar may assign himself. Only the Kryetar adds the secondary lawyer.
- Permission: system `case.assign_lawyer` + case in the Kryetar's office.
- On assign: `cases.lifecycle_status = ACTIVE`, `assigned_at = now` (start of 30-day reminder).

## Files to create or change
- Ash.Modules.Cases/Application/AssignLawyers/*
- Ash.Modules.Cases/Endpoints/AssignmentEndpoints.cs
- Ash.Modules.Cases.Contracts/Events/LawyerAssigned.cs

## Steps
1. `POST /api/cases/{id}/assignment` { primaryUserPublicId, secondaryUserPublicId?, priority?, rowVersion }.
2. Lawyer list for the picker: `GET /api/identity/users?role=STATE_LAWYER&officeId=` (+ the Kryetar himself).
3. Unique-key violation → 409 with clear message. History LAWYER_ASSIGNED / SECONDARY_ASSIGNED / PRIORITY_CHANGED. Publish LawyerAssigned (primary + secondary notified; linked institutions notified).

## Acceptance criteria
- [ ] Case moves from 'Pa avokat' to the lawyer's 'Çështjet e mia'; lawyer gets a notification.
- [ ] Assigning a second primary → 409.
- [ ] Integration test.

## Out of scope
- Transfer (task 024).

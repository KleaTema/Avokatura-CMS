# 051 — Security hardening

**Phase:** 10 · Hardening & release  
**Depends on:** [034](034-document-list-download-delete.md), [042](042-report-active-cases.md), [049](049-chat-backend.md)

## Goal
Close the common gaps before the first deployment.

## Context
- Government system with legal records; audit and least privilege already in place.

## Files to create or change
- Ash.Api/Security/*
- docs/security-checklist.md

## Steps
1. Security headers (CSP, HSTS, X-Content-Type-Options, frame-ancestors none), CORS only for the frontend origin.
2. Rate limiting on login-adjacent and upload endpoints; request size limits.
3. Check every endpoint has an authorization rule (architecture/integration test that enumerates endpoints).
4. Dependency vulnerability scan in CI (`dotnet list package --vulnerable`, `npm audit`).

## Acceptance criteria
- [ ] Test enumerating endpoints finds none without auth (except /health).
- [ ] OWASP ZAP baseline scan shows no high findings.

## Out of scope
- Anything not listed above.

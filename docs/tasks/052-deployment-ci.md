# 052 — CI pipeline and deployment package

**Phase:** 10 · Hardening & release  
**Depends on:** [002](002-database-project-dbup.md), [051](051-security-hardening.md)

## Goal
Build, test and package backend, frontend and database scripts for AKSHI deployment.

## Context
- Production DB users and passwords come from AKSHI secrets; scripts 001 host masks `10.%` to be adjusted to the real subnet.

## Files to create or change
- CI pipeline file (GitHub Actions or Azure DevOps — ask which AKSHI/company uses)
- deploy/README.md
- Dockerfiles for API and frontend (nginx)

## Steps
1. Pipeline: restore, build, unit + integration tests (Testcontainers), frontend lint/build, publish artifacts.
2. Deploy order: run Ash.Database (migrator account) → deploy API → deploy frontend.
3. Config via environment variables only.

## Acceptance criteria
- [ ] A tagged commit produces versioned artifacts.
- [ ] Deploy README lets someone else deploy without asking.

## Out of scope
- Anything not listed above.

## Open questions (use the default, mark TODO in code)
- Which CI system and hosting (VMs, Kubernetes) AKSHI provides.

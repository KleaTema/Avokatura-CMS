# 005 — Authentication: dev login and production OIDC

**Phase:** 0 · Foundation  
**Depends on:** [004](004-api-host-pipeline.md)

## Goal
Authenticate users locally without the government identity provider, and be ready to switch to it in production by configuration only.

## Context
- Production identity: government-employee identity provider (OIDC). Its details are not known yet — keep it configuration-driven.
- `users.external_subject` stores the provider's subject claim; we never store passwords.
- Locally we need to log in as any seeded user to test roles.

## Files to create or change
- Ash.Api/Auth/AuthExtensions.cs
- Ash.Api/Auth/DevAuthHandler.cs (Development only)
- Ash.Api/Auth/CurrentUserMiddleware.cs (fills ICurrentUser)
- Ash.Modules.Identity/Application/ResolveUser/*

## Steps
1. Production: JWT bearer validated against `Auth:Authority` / `Auth:Audience`; the frontend does the OIDC login (PKCE).
2. Development: header `X-Dev-User: <email>` authenticates as that user (refuse to register this handler outside Development; add a startup assertion).
3. On each request resolve `users` by `external_subject` (prod) or `email` (dev); inactive or unknown user → 401.
4. Update `users.last_login_at` at most once per hour per user (avoid a write on every request).
5. Load roles and system permissions once per request (cache 60 s in memory keyed by user id).

## Acceptance criteria
- [ ] In Development, calls with `X-Dev-User` of a seeded user succeed; without it → 401.
- [ ] Running with `ASPNETCORE_ENVIRONMENT=Production` and a dev header → 401.
- [ ] Integration test covers both.

## Out of scope
- Login UI (task 011).
- Account provisioning (task 013).

## Open questions (use the default, mark TODO in code)
- Which identity provider and claims AKSHI uses for government employees (email? NID?).

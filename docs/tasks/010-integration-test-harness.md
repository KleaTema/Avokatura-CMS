# 010 — Integration test harness

**Phase:** 0 · Foundation  
**Depends on:** [002](002-database-project-dbup.md), [004](004-api-host-pipeline.md)

## Goal
Make writing an integration test a few lines: real MySQL in a container, schema from DbUp, seed helpers, authenticated HTTP client.

## Context
- Testcontainers.MySql with image `mysql:8.0`, same server flags as docker-compose (UTC, event scheduler, utf8mb4_0900_ai_ci).

## Files to create or change
- Ash.Tests.Integration/Infrastructure/AshApiFactory.cs (WebApplicationFactory)
- Ash.Tests.Integration/Infrastructure/MySqlFixture.cs (one container per test run, DbUp applied once)
- Ash.Tests.Integration/Infrastructure/Seed.cs (Office, Institution, User with roles, Court, Judge, RegisteredCase…)
- Ash.Tests.Integration/Infrastructure/HttpClientExtensions.cs (`AsUser(email)` sets X-Dev-User)

## Steps
1. Reset data between tests by truncating business tables (keep lookup seed) — use a list of tables in delete order.
2. Port the life-cycle scenario from `docs/sql/tests/test_scenario.sql` into a C# Seed builder, and the 11 negative checks from `run_tests.sh` into xUnit tests.

## Acceptance criteria
- [ ] A sample test registers nothing, calls `/api/identity/me` as a seeded Kryetar and gets 200.
- [ ] Full test suite runs in under 2 minutes on a laptop.

## Out of scope
- Anything not listed above.

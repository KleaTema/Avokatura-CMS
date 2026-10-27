# 002 — Database project with DbUp

**Phase:** 0 · Foundation  
**Depends on:** [001](001-solution-skeleton.md)

## Goal
Move the existing SQL scripts into a DbUp console project that creates/upgrades the database on any environment.

## Context
- Existing scripts from the `ash-db` package: 01–08 (schema, seed, views). `09_mysql_server.cnf` is server config, `10_example_statistics.sql` is reference SQL, `local/99_local_dev_user.sql` is local-only.
- DbUp runs scripts in name order and records them in a journal table, so each runs exactly once.
- Scripts 01 and 06 create users/events; they need a DBA connection (`ash_migrator` in prod, root locally).

## Files to create or change
- src/backend/Database/Ash.Database/Ash.Database.csproj (console, net10.0)
- src/backend/Database/Ash.Database/Scripts/001_databases_and_users.sql … 008_views_and_grants.sql (renamed from 01–08, embedded resources)
- src/backend/Database/Ash.Database/LocalScripts/900_local_dev_user.sql (only applied when `--local`)
- src/backend/Database/Ash.Database/Program.cs
- docs/sql/example_statistics.sql, docs/sql/mysql_server.cnf, docs/sql/tests/ (test_scenario.sql, run_tests.sh)
- docker-compose.yml at repo root (moved from ash-db; mysql init mounts removed)

## Steps
1. Rename scripts to 3-digit prefixes; content unchanged.
2. Before DbUp runs, execute `CREATE DATABASE IF NOT EXISTS ash_ops` (the journal table lives there and must exist before script 001).
3. Scripts 004 and 006 contain `DELIMITER $$` blocks (triggers, procedures, events). Verify DbUp's MySQL splitter handles them; if not, move each routine into its own script without DELIMITER.
4. Program: read connection string from `ConnectionStrings:Migrator` (env var / user-secrets), run `DeployChanges.To.MySqlDatabase(...).WithScriptsEmbeddedInAssembly(...)`, journal table `ash_ops.schema_versions`, transaction per script, log to console, exit code 1 on failure.
5. Flag `--local` also applies LocalScripts.
6. Scripts 001 creates databases, so connect to the server without a default database (`Database=` empty) and use `USE` inside scripts as they already do.
7. docker-compose: MySQL no longer runs init scripts itself; README explains `docker compose up -d` then `dotnet run --project src/backend/Database/Ash.Database -- --local`.

## Acceptance criteria
- [ ] On an empty MySQL, running the project creates 4 schemas and 38 tables, prints each script name, exits 0.
- [ ] Running it a second time applies nothing and exits 0.
- [ ] A broken new script stops the run with exit code 1 and a clear message.

## Out of scope
- Changing any table definition.

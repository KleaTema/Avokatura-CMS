---
name: ash-sql-migration
description: How to change the ASH MySQL database schema with a new DbUp script — naming, rules, MySQL 8 syntax, partitioned tables, routines, testing. Use whenever a task needs a new table, column, index, view, seed row, procedure or grant.
---

# Changing the database schema (ASH)

The schema is owned by DbUp scripts in `src/backend/Database/Ash.Database/Scripts/`. EF Core only maps tables; never generate EF migrations.

## Rules
1. NEVER edit a script that already exists in `Scripts/`. Add a new one: next free 3-digit number + short snake-case name, e.g. `012_add_hearing_location.sql`. Mark it as Embedded Resource.
2. Start every script with `USE ash_core;` (or the schema it changes).
3. One concern per script. Keep it small and reviewable.
4. Data that is a legal record is never deleted: add `deleted_at`/`ended_at` columns instead of delete flows.
5. New columns on big tables: add as `NULL` or with a `DEFAULT`, so the change is instant on MySQL 8 (`ALGORITHM=INSTANT` where possible).
6. Every new column and table gets a `COMMENT` with the Albanian term.
7. Times are `DATETIME(3)` in UTC. Money `DECIMAL(18,2)`, area `DECIMAL(14,2)` in separate columns. Public ids `BINARY(16) DEFAULT (UUID_TO_BIN(UUID(), 1))`.
8. Closed sets the code branches on → `VARCHAR` + `CHECK`. Lists the business may extend → lookup table + FK.
9. Every FK column gets an index; every list query in a task gets a matching composite index.
10. Partitioned tables (`ash_chat.messages`, `ash_audit.audit_log`): no foreign keys, every unique key must contain the partition column.
11. Grants: if a new table must be read by `ash_chat_app` or `ash_reporting`, add the GRANT in the same script.
12. Routines/triggers use `DELIMITER $$ … $$ DELIMITER ;` exactly like scripts 004 and 006.
13. Adding a NOT NULL column whose default is non-deterministic (UUID(), NOW()) to a table that may have rows: ADD it NULL → UPDATE existing rows → MODIFY to NOT NULL DEFAULT (...). A one-step ALTER fails with error 1674 when binary logging is on (MySQL 8 default).

## After writing the script
- Run `dotnet run --project src/backend/Database/Ash.Database -- --local` against the local Docker MySQL; it must apply only the new script and exit 0.
- Run it again: nothing applied, exit 0.
- Update the EF entity + `IEntityTypeConfiguration` of the module that owns the table (see `docs/architecture.md` ownership table).
- Add or update an integration test that touches the new column/table.

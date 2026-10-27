# ASH Case Management — database (MySQL 8.0.30+)

## Local setup (Docker)

```
docker compose up -d          # first time: builds the database + buckets (1–2 min)
docker compose ps             # mysql and minio should be "healthy"
docker compose down           # stop, data is kept
docker compose down -v        # stop AND delete all local data (next "up" rebuilds from scratch)
```

- MySQL: `localhost:3306`, user `ash_dev` / `ash_dev_local` (local only, `local/99_local_dev_user.sql`)
- MinIO S3 API: `http://localhost:9000`, console `http://localhost:9001`, `ash_minio` / `ash_minio_local_pw`
- Scripts 01–08 run automatically ONLY when the MySQL volume is empty. A new script added later
  runs through DbUp, or rebuild locally with `down -v` + `up -d`.

## Scripts

Run in order as a DBA account (or via DbUp/Flyway as `ash_migrator`):

| File | What it creates |
|---|---|
| 01_databases_and_users.sql | schemas `ash_core`, `ash_chat`, `ash_audit`, `ash_ops` + least-privilege accounts |
| 02_core_reference.sql | offices, institutions, users, roles/permissions, courts, judges, lookup lists |
| 03_core_cases.sql | dossiers, cases (X/n), assignments, institution links, parties, hearings, decisions |
| 04_core_documents_history.sql | document metadata, case history (append-only), notifications, reminders |
| 05_chat.sql | conversations, participants, messages (monthly partitions), attachments |
| 06_audit_and_ops.sql | audit log (monthly partitions, append-only), daily digests, partition rotation event |
| 07_seed_reference_data.sql | roles, permission matrix, lookup values |
| 08_views_and_grants.sql | access views, report views (Tabela 1 & 3), agenda view, table grants |
| 09_mysql_server.cnf | server settings the design relies on (UTC, utf8mb4, TLS, binlog, events) |
| 10_example_statistics.sql | reference queries for the statistics (Tabela 5) |
| tests/ | life-cycle scenario + 11 negative constraint tests (`tests/run_tests.sh`) |

Unique code: `CALL create_dossier(office_id, user_id, @dossier_id, @code)` — format comes from the active row of `dossier_code_formats`.

Before production: replace the `__SET_FROM_SECRETS__` passwords and the `'10.%'` host masks.
Files themselves are NOT stored in MySQL — see the design document (object storage).

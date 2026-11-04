# ASH Case Management

## Local database setup

```
docker compose up -d
dotnet run --project src/backend/Database/Ash.Database -- --local
```

- `docker compose up -d` starts MySQL 8.0 (empty) and MinIO. MySQL no longer runs any init scripts.
- The DbUp project creates/upgrades the schemas and applies only scripts not yet recorded in `ash_ops.schema_versions`. Run it again after pulling new scripts.
- `--local` also applies `LocalScripts/` (the `ash_dev` account). Without it, only `Scripts/` run.
- Connection string: `ConnectionStrings:Migrator` (env var `ConnectionStrings__Migrator` or user-secrets). With `--local` it defaults to `root` on `localhost:3306`. Use a DBA account (`ash_migrator` in prod).
- Exit code is 0 on success, 1 on any failure.
- Rebuild from zero (DELETES local data): `docker compose down -v`, `docker compose up -d`, run the project again.

Local endpoints:
- MySQL: `localhost:3306`, user `ash_dev` / `ash_dev_local`
- MinIO S3 API: `http://localhost:9000`, console `http://localhost:9001`, `ash_minio` / `ash_minio_local_pw`

## Backend tests

```
cd src/backend
dotnet test Tests/Ash.Tests.Unit
dotnet test Tests/Ash.Tests.Architecture
dotnet test Tests/Ash.Tests.Integration --blame-hang-timeout 5m --blame-hang-dump-type none
```

- The integration tests start their own MySQL (and MinIO) containers with Testcontainers: Docker must run.
- `--blame-hang-timeout 5m` (built into the .NET SDK): if no test finishes for 5 minutes the run stops and names the
  test that was running (`Sequence_*.xml` under `TestResults/`), instead of hanging silently.
- While the API runs from `Host/Ash.Api` its `bin` is locked; add `--artifacts-path <some temp folder>` to build the
  tests elsewhere.

## Database reference

Scripts live in `src/backend/Database/Ash.Database/Scripts/` (never edit one that already ran). Reference SQL, server config and constraint tests are in `docs/sql/`.

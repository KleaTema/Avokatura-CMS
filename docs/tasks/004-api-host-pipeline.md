# 004 — API host, unit of work and error handling

**Phase:** 0 · Foundation  
**Depends on:** [002](002-database-project-dbup.md), [003](003-shared-kernel.md)

## Goal
Wire the host so every request gets one MySQL transaction shared by all module DbContexts, consistent errors, logging and OpenAPI.

## Context
- docs/architecture.md → Request pipeline.
- One transaction per write request; read-only GET requests may skip the transaction.

## Files to create or change
- Ash.Api/Program.cs
- Ash.Infrastructure/Persistence/IUnitOfWork.cs, MySqlUnitOfWork.cs (opens MySqlConnection + transaction, `EnlistAsync(DbContext)`, `CommitAsync`)
- Ash.Infrastructure/Persistence/ModuleDbContextBase.cs (uses UoW connection/transaction)
- Ash.Infrastructure/Events/IEventPublisher.cs + InProcessEventPublisher (resolves `IEventHandler<T>` and runs them in the same scope)
- Ash.Api/Errors/ProblemDetailsMapping.cs (Error.Type → 400/404/409/403, RowVersionMismatch → 409)
- appsettings.json (no secrets), appsettings.Development.json

## Steps
1. Program: Serilog (console + rolling file), ProblemDetails, OpenAPI (`/openapi/v1.json`) + Scalar UI in Development, health checks `/health` (MySQL ping), call every `Add<M>Module` / `Map<M>Endpoints`.
2. Endpoint filter or middleware that, for POST/PUT/PATCH/DELETE, begins the UoW, and commits after a successful handler; rolls back on exception.
3. Connection string name `ConnectionStrings:App` (user `ash_app` in prod, `ash_dev` locally).
4. Set `default time zone` handling: EF reads DATETIME as `DateTimeKind.Utc` (value converter or provider option).

## Acceptance criteria
- [ ] `GET /health` returns Healthy with MySQL running and Unhealthy when it is stopped.
- [ ] A test endpoint that writes two rows through two different DbContexts and then throws leaves NO rows (rollback works); remove the test endpoint after.
- [ ] OpenAPI document is served in Development.

## Out of scope
- Authentication (task 005).

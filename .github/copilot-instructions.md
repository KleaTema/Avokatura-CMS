# Copilot instructions — ASH Case Management (Avokatura e Shtetit)

Electronic case management for the State Advocacy Office of Albania. Modular monolith: .NET backend + React frontend in one repo.
Full module map: `docs/architecture.md`. Work comes as task files in `docs/tasks/` — implement ONLY what the task file asks.
Repo skills in `.github/skills/`: ash-backend-feature, ash-sql-migration, ash-frontend-feature, ash-task-done-check (always run this last).

## Stack
- Backend: .NET 10, C# latest, nullable enabled, Minimal APIs, EF Core 10 (MySQL provider chosen in task 001), Serilog, SignalR. Validation is plain static helpers (no FluentValidation).
- Database: MySQL 8.0. Schema is owned by **DbUp SQL scripts** in `src/backend/Database/Ash.Database/Scripts/`. NEVER use EF migrations; EF only maps existing tables.
- Files: S3-compatible object storage (MinIO locally, AKSHI storage in production) via `IFileStorage`.
- Frontend: React + TypeScript + Vite, React Router, TanStack Query, MUI, typed API client generated from OpenAPI.
- Tests: xUnit, Shouldly, Testcontainers (MySQL), NetArchTest.
- Do NOT add MediatR, AutoMapper or FluentAssertions (commercial licences). Use plain handler classes and manual mapping.

## Repo layout
```
src/backend/Host/Ash.Api                      composition root, endpoints mapping, auth
src/backend/BuildingBlocks/Ash.SharedKernel   Result, errors, IClock, TiraneTime, ICurrentUser, paging, event abstractions
src/backend/BuildingBlocks/Ash.Infrastructure MySQL unit of work, in-process event publisher, S3 storage, background jobs
src/backend/BuildingBlocks/Ash.AspNetCore     RequirePermission, Error.ToProblem (no module knowledge)
src/backend/Modules/<M>/Ash.Modules.<M>            module internals (Application/, Infrastructure/ = DbContext + entities, Endpoints/)
src/backend/Modules/<M>/Ash.Modules.<M>.Contracts  the ONLY thing other modules may reference (references Ash.SharedKernel only)
src/backend/Database/Ash.Database             DbUp runner + Scripts/
src/backend/Tests/*                           integration + architecture tests
src/frontend                                  React app
```
Modules: Identity, Reference, Cases, Documents, Notifications, Audit, Reports, Chat.

## Hard rules (never break)
1. A module never references another module's internals or tables — only its `.Contracts` project.
2. All `DateTime` values are UTC. Convert to Europe/Tirane only in the frontend.
3. The API exposes `public_id` (Guid) only. Internal BIGINT ids never leave the server. Exception: reference data (courts, judges) may expose their numeric id; lookup lists use code.
4. Every read or write of a case goes through `ICaseAccessService` (Cases.Contracts). No access → return **404**, not 403.
5. Every change to a case writes a `case_history_events` row in the SAME transaction (`IHistoryWriter`).
6. Never hard-delete legal records: set `deleted_at` / `ended_at` / `archived_at`.
7. `cases`, `case_hearings`, `case_decisions` use `row_version`: the client sends it back, mismatch → **409 Conflict**.
8. Lookup values (courts, results, types, categories…) are read from tables by `code`, never hardcoded lists.
9. Schema change = NEW script `NNN_description.sql`. Never edit a script that already ran.
10. Lists use keyset paging (`?after=<cursor>&limit=50`), never OFFSET.
11. Errors are RFC 7807 ProblemDetails. User-facing text is Albanian; code, identifiers and comments are English.
12. No secrets in code or appsettings committed to git; use user-secrets locally.
13. Entities use `DateTime` (UTC) for DATETIME columns, never `DateTimeOffset`.
14. Edit files ONLY with the file edit tool, never with PowerShell (Set-Content, .Replace, here-strings). Never pipe test or build output through Select-Object; show it directly.
## Conventions
- Endpoint per feature: `Endpoints/<Feature>Endpoints.cs` with `Map<Feature>(this IEndpointRouteBuilder)`; routes `/api/<module>/...`.
- Handler per use case: `Application/<UseCase>/<UseCase>Handler.cs` + `<UseCase>Request` + `<UseCase>Validator` (static helpers returning `Error?`; field errors via `Error.ValidationFields(message, fields)` with camelCase keys; validate before trimming).
- Endpoints return `result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem()` (`Ash.AspNetCore.Http`).
- Permission check: `.RequirePermission("case.assign_lawyer")` from `Ash.AspNetCore.Authorization` (system) or `await ICaseAccessService.RequireAsync(caseId, "hearing.add", ct)` (case level).
- One `DbContext` per module in `Infrastructure/`, mapped in `OnModelCreating`, with its entity classes next to it (no `Domain/` folder); all contexts share the request's connection + transaction via `IUnitOfWork`.
- Raw SQL / stored procedures only through `IUnitOfWork.CreateCommand(sql)` (bound to the request transaction). Work that must wait for the commit (cache invalidation) goes in `IUnitOfWork.OnCommitted(...)`.
- Events: records implement `IEvent` (`Ash.SharedKernel.Events`) and live in the publisher's `.Contracts`; `IEventPublisher.PublishAsync` runs handlers in the same transaction.
- Tests: every handler that changes data gets an integration test (real MySQL via Testcontainers).

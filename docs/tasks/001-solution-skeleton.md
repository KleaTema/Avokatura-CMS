# 001 — Solution skeleton and project references

**Phase:** 0 · Foundation  
**Depends on:** none

## Goal
Create the backend solution with the modular-monolith project layout, empty but compiling, with architecture rules enforced by a test.

## Context
- Layout and module list: `.github/copilot-instructions.md` (Repo layout).
- Modules: Identity, Reference, Cases, Documents, Notifications, Audit, Reports, Chat. Each has `Ash.Modules.<M>` and `Ash.Modules.<M>.Contracts`.
- Rule: a module may reference SharedKernel, Infrastructure and other modules' `.Contracts` only.

## Files to create or change
- src/backend/Ash.sln
- src/backend/Directory.Build.props (net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors)
- src/backend/Directory.Packages.props (central package versions)
- src/backend/Host/Ash.Api
- src/backend/BuildingBlocks/Ash.SharedKernel
- src/backend/BuildingBlocks/Ash.Infrastructure
- src/backend/Modules/<M>/Ash.Modules.<M> and .Contracts (8 modules)
- src/backend/Tests/Ash.Tests.Architecture
- src/backend/Tests/Ash.Tests.Integration (empty)
- .editorconfig, .gitignore

## Steps
1. Create projects and references exactly as the layout says. Module projects reference only SharedKernel, Infrastructure and `*.Contracts`.
2. Choose the EF Core MySQL provider: use Pomelo.EntityFrameworkCore.MySql if a version for EF Core 10 exists on NuGet; otherwise MySql.EntityFrameworkCore (Oracle). Write the choice and reason in `docs/decisions/001-mysql-provider.md`.
3. In each module project add folders `Domain/`, `Application/`, `Infrastructure/`, `Endpoints/` and a `<M>Module.cs` with `static IServiceCollection Add<M>Module(this IServiceCollection, IConfiguration)` and `static IEndpointRouteBuilder Map<M>Endpoints(...)` (empty bodies).
4. Architecture test with NetArchTest: no `Ash.Modules.X` type depends on `Ash.Modules.Y` (Y≠X) except `Ash.Modules.Y.Contracts`.

## Acceptance criteria
- [ ] `dotnet build` succeeds with zero warnings.
- [ ] `dotnet test` runs the architecture test and it passes.
- [ ] Adding a reference from Cases to Documents internals makes the architecture test fail (try it, then revert).

## Out of scope
- Any feature code, DbContexts, endpoints.

---
name: ash-backend-feature
description: Pattern for implementing a backend use case in the ASH case management .NET modular monolith — endpoint, handler, validation, case access check, history event, unit of work, integration test. Use when implementing or changing any backend task from docs/tasks (endpoints under /api, handlers in Ash.Modules.*).
---

# Implementing a backend use case (ASH)

Follow this shape for every use case. Do not invent a different structure.

## Files (inside `Ash.Modules.<Module>`)
```
Application/<UseCase>/<UseCase>Request.cs     record, what the client sends (public ids only)
Application/<UseCase>/<UseCase>Validator.cs   static validation helpers, Albanian messages (no FluentValidation)
Application/<UseCase>/<UseCase>Handler.cs     the logic, returns Result<T>
Application/<UseCase>/<UseCase>Response.cs    record, what the API returns (public ids only)
Endpoints/<Feature>Endpoints.cs               maps the route, no logic
Infrastructure/<Module>DbContext.cs           DbContext + entity classes next to it (there is no Domain/ folder)
```
Small admin lists may keep request/response records and validation in one `<Thing>AdminSupport.cs` static class
(see `Ash.Modules.Reference/Application/Admin/*`).

Register the handler as scoped in `<Module>Module.Add<Module>Module(IServiceCollection, IConfiguration)`; map routes in `<Module>Module.Map<Module>Endpoints(IEndpointRouteBuilder)`.

Real types (namespaces):
- `Ash.SharedKernel.Results`: `Result`, `Result<T>` (implicit from `T` and from `Error`; `IsSuccess`, `IsFailure`, `Value`, `Error`), `Error(Code, Message, ErrorType)` with `Error.Validation/NotFound/Conflict/Forbidden(code, message)`, `Error.ValidationFields(message, fields)` for field errors, and `Extensions` for extra ProblemDetails members (`error with { Extensions = … }`).
- `Ash.SharedKernel.Time`: `IClock.UtcNow`; `TiraneTime.Today(clock)` / `TiraneTime.DateOf(utc)` for Albanian calendar dates.
- `Ash.SharedKernel.Identity`: `ICurrentUser` (`UserId`, `PublicId`, `OfficeId`, `InstitutionId`, `Roles`, `HasPermission(string)`).
- `Ash.SharedKernel.Events`: `IEvent`, `IEventHandler<TEvent>`, `IEventPublisher.PublishAsync<TEvent>(event, ct)`. Event records live in the publishing module's `.Contracts` (e.g. `Ash.Modules.Cases.Contracts.Events.CaseRegistered`).
- `Ash.SharedKernel.Concurrency`: `RowVersionMismatchException` (mapped to 409 by `ApiExceptionHandler`).
- `Ash.SharedKernel.Paging`: `Cursor.Encode/TryDecode`, `KeysetPage<T>(Items, NextCursor)`.
- `Ash.Infrastructure.Persistence`: `IUnitOfWork` (`OnCommitted(Action)`, `CreateCommand(sql)`), `ModuleDbContextBase` (module DbContexts derive from it).
- `Ash.AspNetCore.Authorization`: `.RequirePermission("code")` on an endpoint (system permission, 401/403).
- `Ash.AspNetCore.Http`: `Error.ToProblem()` → `IResult` (ProblemDetails with `code` and any `Extensions`), `ErrorType.ToStatusCode()`.
- `Ash.Modules.Cases.Contracts`: `ICaseAccessService` (`CanViewAsync`, `HasAsync`, `RequireAsync`), `IHistoryWriter` + `HistoryEventType`.

`.Contracts` projects reference `Ash.SharedKernel` only — never `Ash.Infrastructure` (architecture test).

## Validation
Plain static helpers, checked before trimming (a null field is a 400, never a 500):
```csharp
public static class AssignLawyersValidator
{
    public static Error? Validate(AssignLawyersRequest req)
    {
        var fields = new Dictionary<string, string[]>();
        if (req.PrimaryLawyerId is null) fields["primaryLawyerId"] = ["Zgjidhni avokatin primar."];
        // keys are camelCase request paths, e.g. "firstHearing.at", so the UI can map them to fields
        return fields.Count == 0 ? null : Error.ValidationFields("Të dhënat nuk janë të vlefshme.", fields);
    }
}
```
A single-field rule may return `Error.Validation("thing.code_invalid", "…")` instead.

## Handler template
```csharp
// usings: Ash.SharedKernel.Results, Ash.SharedKernel.Time, Ash.SharedKernel.Identity, Ash.SharedKernel.Events,
//         Ash.Modules.Cases.Contracts (ICaseAccessService, IHistoryWriter, HistoryEventType)
public sealed class AssignLawyersHandler(
    CasesDbContext db, ICaseAccessService access, IHistoryWriter history,
    IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<AssignLawyersResponse>> Handle(Guid casePublicId, AssignLawyersRequest req, CancellationToken ct)
    {
        var invalid = AssignLawyersValidator.Validate(req);
        if (invalid is not null) return invalid;

        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct)) return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        // system permission is checked on the endpoint; case permission here:
        // await access.RequireAsync((long)c.Id, "party.add", ct);
        if (c.RowVersion != req.RowVersion) return Error.Conflict("case.changed", "Të dhënat u ndryshuan nga një përdorues tjetër.");

        // ... change state explicitly, never by copying the request onto the entity blindly
        c.RowVersion++;
        await db.SaveChangesAsync(ct);
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.LawyerAssigned, "Avokati primar u caktua: …", old: null, @new: new { … });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new LawyerAssigned(c.PublicId, …), ct);   // LawyerAssigned : IEvent; handlers run in the same transaction
        return new AssignLawyersResponse(…);
    }
}
```
Rules the handler must respect:
- Look up by `public_id`, never accept internal ids from the client (courts and judges keep their numeric ids).
- No access → `NotFound` (404). Has view but lacks the action → `Forbidden` (403).
- Every state change writes ONE history event with old/new values of changed fields only.
- `row_version` check on `cases`, `case_hearings`, `case_decisions`; increment on change.
- Soft delete only (`deleted_at`/`ended_at`).
- Use `IClock.UtcNow`, never `DateTime.Now`. Calendar dates (DATE columns) are `DateOnly`, never converted to UTC.
- Do NOT begin/commit/roll back yourself: `UnitOfWorkMiddleware` opens the `IUnitOfWork` transaction for POST/PUT/PATCH/DELETE and commits when the response status is < 400 (rolls back otherwise). Call `SaveChangesAsync` on the module DbContext so changes are flushed inside that transaction.
- Raw SQL / stored procedures: `await using var cmd = unitOfWork.CreateCommand("create_dossier");` — bound to the request's connection and transaction (throws outside one). Never `new MySqlCommand(...)` on the connection.
- Side effects that must only happen after a successful commit (cache invalidation): `unitOfWork.OnCommitted(() => cache.Invalidate())`.
- A `RowVersionMismatchException` thrown from persistence is mapped to 409 by `ApiExceptionHandler`.

## Endpoint template
```csharp
// usings: Ash.AspNetCore.Authorization (RequirePermission), Ash.AspNetCore.Http (ToProblem)
group.MapPost("/{id:guid}/assignment", async (Guid id, AssignLawyersRequest req, AssignLawyersHandler h, CancellationToken ct) =>
        {
            var result = await h.Handle(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
        })
     .RequirePermission("case.assign_lawyer")
     .WithName("AssignLawyers").Produces<AssignLawyersResponse>().ProducesProblem(404).ProducesProblem(409);
```
Numeric route ids are constrained to the column type, e.g. `{id:int:range(1,255)}` for TINYINT UNSIGNED.

## Integration test (required for every write use case)
Real host + real MySQL (`AshApiFactory`, Testcontainers). Business tables are emptied before each test
(`IntegrationTest` → `MySqlFixture.ResetAsync`); offices, users and lookup rows a test creates are deleted in `finally`.
```csharp
public class AssignLawyersTests(MySqlFixture db) : IntegrationTest(db)
{
    [Fact]
    public async Task Kryetar_assigns_primary_lawyer_and_case_becomes_active()
    {
        await using var seed = await Seed.CreateAsync(Db.AppConnectionString);
        var caseId = await seed.RegisteredCaseAsync(…);
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient().AsUser("kryetar@ash.local");

        var res = await client.PostAsJsonAsync($"/api/cases/{casePublicId}/assignment", new { … }, TestContext.Current.CancellationToken);

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        // assert on the database with plain SQL (MySqlConnection to Db.AppConnectionString): status, history rows, …
    }
}
```
Also test: a user without access gets 404, a stale `rowVersion` gets 409, a missing field gets 400 with that field in `errors`.

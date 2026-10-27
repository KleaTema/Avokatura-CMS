---
name: ash-backend-feature
description: Pattern for implementing a backend use case in the ASH case management .NET modular monolith — endpoint, handler, validator, case access check, history event, unit of work, integration test. Use when implementing or changing any backend task from docs/tasks (endpoints under /api, handlers in Ash.Modules.*).
---

# Implementing a backend use case (ASH)

Follow this shape for every use case. Do not invent a different structure.

## Files (inside `Ash.Modules.<Module>`)
```
Application/<UseCase>/<UseCase>Request.cs     record, what the client sends (public ids only)
Application/<UseCase>/<UseCase>Validator.cs   FluentValidation, Albanian messages
Application/<UseCase>/<UseCase>Handler.cs     the logic, returns Result<T>
Application/<UseCase>/<UseCase>Response.cs    record, what the API returns (public ids only)
Endpoints/<Feature>Endpoints.cs               maps the route, no logic
```
Register the handler as scoped in `<Module>Module.Add<Module>Module(IServiceCollection, IConfiguration)`; map routes in `<Module>Module.Map<Module>Endpoints(IEndpointRouteBuilder)`.

Real types used below (namespaces):
- `Ash.SharedKernel.Results`: `Result`, `Result<T>` (implicit from `T` and from `Error`; `IsSuccess`, `IsFailure`, `Value`, `Error`), `Error(Code, Message, ErrorType)` with `Error.Validation/NotFound/Conflict/Forbidden(code, message)`.
- `Ash.SharedKernel.Time`: `IClock.UtcNow`.
- `Ash.SharedKernel.Identity`: `ICurrentUser` (`UserId`, `PublicId`, `OfficeId`, `InstitutionId`, `Roles`, `HasPermission(string)`).
- `Ash.SharedKernel.Concurrency`: `RowVersionMismatchException` (mapped to 409 by `ApiExceptionHandler`).
- `Ash.Infrastructure.Persistence`: `IUnitOfWork`, `ModuleDbContextBase` (module DbContexts derive from it).
- `Ash.Infrastructure.Events`: `IEvent`, `IEventHandler<TEvent>`, `IEventPublisher.PublishAsync<TEvent>(event, ct)`.
- `Ash.Api.Errors`: `Error.ToProblem()` -> `IResult` (ProblemDetails with `code` extension), `ErrorType.ToStatusCode()`.
- Not in code yet: `ICaseAccessService` (added in task 007), `IHistoryWriter` / `HistoryEventType` (added in task 009), `RequirePermission` (added in task 006).

## Handler template
```csharp
// usings: Ash.SharedKernel.Results, Ash.SharedKernel.Time, Ash.SharedKernel.Identity, Ash.Infrastructure.Events
// ICaseAccessService (added in task 007), IHistoryWriter + HistoryEventType (added in task 009)
public sealed class AssignLawyersHandler(
    CasesDbContext db, ICaseAccessService access, IHistoryWriter history,
    IEventPublisher events, ICurrentUser user, IClock clock)
{
    public async Task<Result<AssignLawyersResponse>> Handle(Guid casePublicId, AssignLawyersRequest req, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync(c.Id, ct)) return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        // system permission is checked on the endpoint; case permission here:
        // await access.RequireAsync(c.Id, "party.add", ct);
        if (c.RowVersion != req.RowVersion) return Error.Conflict("case.changed", "Të dhënat u ndryshuan nga një përdorues tjetër.");

        // ... change state through domain methods, never by setting fields from the request blindly
        c.RowVersion++;
        history.Add(c.DossierId, c.Id, HistoryEventType.LawyerAssigned, "Avokati primar u caktua: …", old: null, @new: new { … });
        await events.PublishAsync(new LawyerAssigned(c.Id, …), ct);   // LawyerAssigned : IEvent; handlers run in the same transaction
        return new AssignLawyersResponse(…);
    }
}
```
Rules the handler must respect:
- Look up by `public_id`, never accept internal ids from the client.
- No access → `NotFound` (404). Has view but lacks the action → `Forbidden` (403).
- Every state change writes ONE history event with old/new values of changed fields only.
- `row_version` check on `cases`, `case_hearings`, `case_decisions`; increment on change.
- Soft delete only (`deleted_at`/`ended_at`).
- Use `IClock.UtcNow`, never `DateTime.Now`.
- Do NOT begin/commit/roll back yourself: `UnitOfWorkMiddleware` opens the `IUnitOfWork` transaction for POST/PUT/PATCH/DELETE and commits when the response status is < 400 (rolls back otherwise). Call `SaveChangesAsync` on the module DbContext so changes are flushed inside that transaction.
- A `RowVersionMismatchException` thrown from persistence is mapped to 409 by `ApiExceptionHandler`.

## Endpoint template
```csharp
group.MapPost("/{id:guid}/assignment", async (Guid id, AssignLawyersRequest req, AssignLawyersHandler h, CancellationToken ct) =>
        {
            var result = await h.Handle(id, req, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();   // ToProblem: Ash.Api.Errors
        })
     .RequirePermission("case.assign_lawyer")   // added in task 006
     .WithName("AssignLawyers").Produces<AssignLawyersResponse>().ProducesProblem(404).ProducesProblem(409);
```

## Integration test (required for every write use case)
```csharp
[Fact]
public async Task Kryetar_assigns_primary_lawyer_and_case_becomes_active()
{
    var s = await Seed.Office().WithHead().WithLawyer().RegisteredCase().BuildAsync(Db);
    var res = await Client.AsUser(s.Head.Email).PostAsJsonAsync($"/api/cases/{s.Case.PublicId}/assignment", new { … });
    res.StatusCode.ShouldBe(HttpStatusCode.OK);
    (await Db.Cases.SingleAsync(x => x.Id == s.Case.Id)).LifecycleStatus.ShouldBe("ACTIVE");
    (await Db.HistoryOf(s.Case.Id)).ShouldContain(e => e.EventType == "LAWYER_ASSIGNED");
}
```
Also test: a user without access gets 404, a stale `rowVersion` gets 409.

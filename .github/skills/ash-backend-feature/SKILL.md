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
Register the handler as scoped in `<Module>Module.Add<Module>Module`.

## Handler template
```csharp
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
        await events.Publish(new LawyerAssigned(c.Id, …), ct);   // handlers run in the same transaction
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
- Do NOT call `SaveChanges` + commit yourself: the unit of work does it after the handler succeeds.

## Endpoint template
```csharp
group.MapPost("/{id:guid}/assignment", async (Guid id, AssignLawyersRequest req, AssignLawyersHandler h, CancellationToken ct)
        => (await h.Handle(id, req, ct)).ToHttpResult())
     .RequirePermission("case.assign_lawyer")
     .AddEndpointFilter<ValidationFilter<AssignLawyersRequest>>()
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

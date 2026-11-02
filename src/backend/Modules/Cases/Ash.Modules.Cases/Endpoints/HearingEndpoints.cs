using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Hearings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// Hearings (task 030). Case permissions (hearing.add, hearing.edit) through the effective permissions in the handlers:
/// no view → 404, CLOSED → 409, missing → 403. Hearings are addressed by their public id only.
/// </summary>
public static class HearingEndpoints
{
    public static IEndpointRouteBuilder MapCaseHearings(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cases/{id:guid}/hearings").RequireAuthorization();

        group.MapGet("", async (Guid id, ListHearingsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<IReadOnlyList<HearingView>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListHearings");

        group.MapGet("/new-defaults", async (Guid id, NewHearingDefaultsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<NewHearingDefaults>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetNewHearingDefaults");

        group.MapPost("", async (Guid id, AddHearingRequest req, AddHearingHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, req, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/cases/{id}/hearings/{result.Value.Hearing.PublicId}", result.Value)
                    : result.Error!.ToProblem();
            })
            .Produces<HearingSaveResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AddHearing");

        group.MapPut("/{hearingId:guid}", async (Guid id, Guid hearingId, UpdateHearingRequest req, UpdateHearingHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, hearingId, req, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<HearingSaveResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateHearing");

        group.MapPost("/{hearingId:guid}/complete",
                async (Guid id, Guid hearingId, CompleteHearingRequest req, CompleteHearingHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, hearingId, req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/cases/{id}/hearings/{result.Value.Hearing.PublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .Produces<CompleteHearingResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CompleteHearing");

        return endpoints;
    }
}

using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Handoff;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// "Përcjellja e çështjes" (task 053c). The handoff itself is created by POST /api/cases/{id}/decision (not final);
/// here: the office a court resolves to (the closing dialog), a case's open handoff and its correction, and the
/// receiving office's "Çështjet e ardhura".
/// </summary>
public static class HandoffEndpoints
{
    public static IEndpointRouteBuilder MapCaseHandoffs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cases/handoff-destination",
                async (int courtId, HandoffQueries queries, CancellationToken ct) => Results.Ok(await queries.DestinationAsync(courtId, ct)))
            .RequireAuthorization()
            .Produces<HandoffDestinationResponse>()
            .WithName("GetHandoffDestination");

        endpoints.MapGet("/api/cases/handoffs/incoming", async (HandoffQueries queries, CancellationToken ct) =>
                {
                    var result = await queries.IncomingAsync(ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<IncomingHandoffsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("ListIncomingHandoffs");

        var group = endpoints.MapGroup("/api/cases/{id:guid}/handoff").RequireAuthorization();

        group.MapGet("", async (Guid id, HandoffQueries queries, CancellationToken ct) =>
            {
                var result = await queries.ForCaseAsync(id, ct);
                return !result.IsSuccess ? result.Error!.ToProblem()
                    : result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
            })
            .Produces<CaseHandoffView>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("GetCaseHandoff");

        group.MapPut("", async (Guid id, ChangeHandoffRequest req, ChangeHandoffHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, req, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<CaseHandoffView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ChangeCaseHandoff");

        return endpoints;
    }
}

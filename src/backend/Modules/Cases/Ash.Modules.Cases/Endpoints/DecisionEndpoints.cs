using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.RecordDecision;
using Ash.Modules.Cases.Application.UpdateDecision;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// The decision of a level (tasks 036/037). Case permission decision.record through the effective permissions in the
/// handlers (no view → 404, wrong state → 409, missing → 403). The decision itself is read with GET /api/cases/{id}.
/// </summary>
public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapCaseDecision(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cases/{id:guid}/decision").RequireAuthorization();

        group.MapPost("", async (Guid id, DecisionRequest req, RecordDecisionHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, req, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<RecordDecisionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RecordDecision");

        group.MapPut("", async (Guid id, DecisionRequest req, UpdateDecisionHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(id, req, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<UpdateDecisionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateDecision");

        return endpoints;
    }
}

using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.SetCategory;
using Ash.Modules.Cases.Application.SetPriority;
using Ash.Modules.Cases.Application.SetSecurityMeasure;
using Ash.Modules.Cases.Application.SetSuspension;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// The case's working data (task 025): one small endpoint per action. All are CASE permissions, checked in the
/// handlers through the effective permissions (no view → 404, missing → 403, CLOSED → 409). Each takes rowVersion
/// and returns the new one with the changed fields, so the UI can chain saves.
/// </summary>
public static class CaseDataEndpoints
{
    public static IEndpointRouteBuilder MapCaseData(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPatch("/api/cases/{id:guid}/claim",
                async (Guid id, UpdateClaimRequest req, UpdateClaimHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<UpdateClaimResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateCaseClaim");

        endpoints.MapPatch("/api/cases/{id:guid}/category",
                async (Guid id, SetCategoryRequest req, SetCategoryHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<SetCategoryResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("SetCaseCategory");

        endpoints.MapPatch("/api/cases/{id:guid}/suspension",
                async (Guid id, SetSuspensionRequest req, SetSuspensionHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<SetSuspensionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("SetCaseSuspension");

        endpoints.MapPut("/api/cases/{id:guid}/security-measure",
                async (Guid id, SetSecurityMeasureRequest req, SetSecurityMeasureHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<SetSecurityMeasureResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("SetCaseSecurityMeasure");

        endpoints.MapPatch("/api/cases/{id:guid}/priority",
                async (Guid id, SetPriorityRequest req, SetPriorityHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<SetPriorityResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("SetCasePriority");

        return endpoints;
    }
}

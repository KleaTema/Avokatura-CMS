using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.TransferLawyer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// Assigning lawyers (task 023). The system permission is checked on the endpoint (403); the case itself in the
/// handlers through the effective permissions (no view → 404, the action missing → 403).
/// </summary>
public static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapCaseAssignment(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cases/{id:guid}/assignable-lawyers",
                async (Guid id, AssignableLawyersHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.assign_lawyer")
            .Produces<IReadOnlyList<AssignableLawyer>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListAssignableLawyers");

        // The lawyer filter of the case list: the lawyers of the user's office.
        endpoints.MapGet("/api/cases/office-lawyers",
                async (OfficeLawyersHandler handler, CancellationToken ct) => Results.Ok(await handler.Handle(ct)))
            .RequirePermission("case.view_office")
            .Produces<IReadOnlyList<OfficeLawyer>>()
            .WithName("ListOfficeLawyers");

        endpoints.MapPost("/api/cases/{id:guid}/assignment",
                async (Guid id, AssignLawyersRequest req, AssignLawyersHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.assign_lawyer")
            .Produces<AssignLawyersResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AssignLawyers");

        // Task 024: replace the primary and/or change the secondary of an ACTIVE case.
        endpoints.MapPost("/api/cases/{id:guid}/transfer",
                async (Guid id, TransferLawyerRequest req, TransferLawyerHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("case.transfer")
            .Produces<TransferLawyerResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("TransferLawyer");

        return endpoints;
    }
}

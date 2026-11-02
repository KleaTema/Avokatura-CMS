using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.AssignJurist;
using Ash.Modules.Cases.Application.DelegateCase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// Institutions on a case (task 027). The jurist endpoints need the SYSTEM permission institution.assign_jurist (403
/// without it); the handlers then check the case: no view → 404, the user's institution not linked → 403.
/// Delegation is a CASE permission (case.delegate, the primary lawyer), checked in the handlers.
/// </summary>
public static class InstitutionEndpoints
{
    public static IEndpointRouteBuilder MapCaseInstitutions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cases/{id:guid}/assignable-jurists",
                async (Guid id, AssignableJuristsHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("institution.assign_jurist")
            .Produces<IReadOnlyList<AssignableJurist>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListAssignableJurists");

        endpoints.MapPost("/api/cases/{id:guid}/jurist",
                async (Guid id, AssignJuristRequest req, AssignJuristHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission("institution.assign_jurist")
            .Produces<AssignJuristResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AssignJurist");

        endpoints.MapPost("/api/cases/{id:guid}/delegate",
                async (Guid id, DelegateCaseRequest req, DelegateCaseHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<DelegationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("DelegateCase");

        endpoints.MapPost("/api/cases/{id:guid}/undelegate",
                async (Guid id, UndelegateCaseRequest req, UndelegateCaseHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(id, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<DelegationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UndelegateCase");

        return endpoints;
    }
}

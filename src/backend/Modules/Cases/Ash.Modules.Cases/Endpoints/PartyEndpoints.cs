using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Application.RegisterCase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>
/// Parties of a case (task 017). Rights are case-level (PartyCaseGate), so no system permission here.
/// <para>
/// Removing an institution's last party: the UI asks the user BEFORE deleting — from GET it can see that no other
/// party has the same institution.publicId — and sends the answer as endInstitutionLink=true|false in the single
/// DELETE. One action, one transaction; there is no separate endpoint to end a link afterwards.
/// </para>
/// </summary>
public static class PartyEndpoints
{
    public static IEndpointRouteBuilder MapCaseParties(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cases/{caseId:guid}/parties").RequireAuthorization();

        group.MapGet("", async (Guid caseId, ListPartiesHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(caseId, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .Produces<IReadOnlyList<PartyResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListCaseParties");

        group.MapPost("", async (Guid caseId, PartyRequest req, AddPartyHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(caseId, req, ct);
                    return result.IsSuccess
                        ? Results.Created($"/api/cases/{caseId}/parties/{result.Value.PublicId}", result.Value)
                        : result.Error!.ToProblem();
                })
            .Produces<PartyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("AddCaseParty");

        group.MapPut("/{partyId:guid}", async (Guid caseId, Guid partyId, PartyRequest req, UpdatePartyHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(caseId, partyId, req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .Produces<PartyResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("UpdateCaseParty");

        // Soft delete. endInstitutionLink is the user's answer, asked before the call when this is the institution's
        // last party: true ends a MONITORING link (a DELEGATED link is never ended here: warning); false or omitted
        // keeps it. The response's lastInstitutionParty lets the UI confirm "lidhja me institucionin u ruajt".
        group.MapDelete("/{partyId:guid}", async (Guid caseId, Guid partyId, bool? endInstitutionLink, RemovePartyHandler handler, CancellationToken ct) =>
                {
                    var result = await handler.Handle(caseId, partyId, endInstitutionLink, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .Produces<RemovePartyResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RemoveCaseParty");

        return endpoints;
    }
}

using Ash.AspNetCore.Authorization;
using Ash.AspNetCore.Http;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.Assistants;
using Ash.Modules.Cases.Application.FilterOptions;
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

        // The office and lawyer filters of the case list and the agenda (task 031); empty lists without office/all view.
        endpoints.MapGet("/api/cases/filter-options",
                async (FilterOptionsHandler handler, CancellationToken ct) => Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .Produces<FilterOptionsResponse>()
            .WithName("GetCaseFilterOptions");

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

        // Task 053e: legal assistants are attached to LAWYERS by the Kryetar ("Ndihmësit ligjorë"); the per-case
        // endpoints of task 026 are gone. AssistantSync gives each assistant the cases of their lawyers.
        var links = endpoints.MapGroup("/api/assistant-links");

        links.MapGet("", async (AssistantLinkHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.ListAsync(ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(AssistantLinkHandlers.Permission)
            .Produces<AssistantLinksResponse>()
            .WithName("ListAssistantLinks");

        links.MapPost("", async (CreateAssistantLinkRequest req, AssistantLinkHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.CreateAsync(req, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequirePermission(AssistantLinkHandlers.Permission)
            .Produces<AssistantLinkView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateAssistantLink");

        links.MapDelete("/{linkPublicId:guid}", async (Guid linkPublicId, AssistantLinkHandlers handler, CancellationToken ct) =>
                {
                    var result = await handler.EndAsync(linkPublicId, ct);
                    return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
                })
            .RequirePermission(AssistantLinkHandlers.Permission)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("EndAssistantLink");

        return endpoints;
    }
}

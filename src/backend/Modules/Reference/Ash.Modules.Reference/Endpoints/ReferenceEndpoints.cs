using Ash.Modules.Reference.Application;
using Ash.Modules.Reference.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Reference.Endpoints;

public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReference(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reference").RequireAuthorization();

        group.MapGet("/court-levels", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListCourtLevelsAsync(ct)).Select(x => new CourtLevelResponse(x.Code, x.Name, x.AllowsDelegation, x.AppealKind)))
            .WithName("ListCourtLevels");

        group.MapGet("/courts", async (IReferenceData data, CancellationToken ct) =>
            {
                var levels = (await data.ListCourtLevelsAsync(ct)).ToDictionary(x => x.Id, x => x.Code);
                return (await data.ListCourtsAsync(ct)).Where(x => x.IsActive)
                    .Select(x => new CourtResponse(x.Id, x.Name, levels.GetValueOrDefault(x.CourtLevelId), x.City, x.IsForeign));
            })
            .WithName("ListCourts");

        group.MapGet("/judges", async (int? courtId, string? q, SearchJudgesHandler handler, CancellationToken ct) =>
                await handler.Handle(courtId, q, ct))
            .WithName("SearchJudges");

        group.MapGet("/party-roles", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListPartyRolesAsync(ct)).Where(x => x.IsActive).Select(x => new PartyRoleResponse(x.Code, x.Name, x.ReportGroup)))
            .WithName("ListPartyRoles");

        group.MapGet("/summons-types", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListSummonsTypesAsync(ct)).Where(x => x.IsActive).Select(x => new SummonsTypeResponse(x.Code, x.Name)))
            .WithName("ListSummonsTypes");

        group.MapGet("/hearing-types", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListHearingTypesAsync(ct)).Where(x => x.IsActive).Select(x => new HearingTypeResponse(x.Code, x.Name, x.IsOther)))
            .WithName("ListHearingTypes");

        group.MapGet("/document-types", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListDocumentTypesAsync(ct)).Where(x => x.IsActive).Select(x => new DocumentTypeResponse(x.Code, x.Name, x.IsOther)))
            .WithName("ListDocumentTypes");

        group.MapGet("/decision-results", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListDecisionResultsAsync(ct)).Where(x => x.IsActive)
                    .Select(x => new DecisionResultResponse(x.Code, x.Name, x.OutcomeGroup, x.SendsBackForRetrial, x.IsOther)))
            .WithName("ListDecisionResults");

        group.MapGet("/case-categories", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListCaseCategoriesAsync(ct)).Select(x => new CaseCategoryResponse(x.Code, x.Name, x.IsDelegation)))
            .WithName("ListCaseCategories");

        group.MapGet("/case-subcategories", async (IReferenceData data, CancellationToken ct) =>
            {
                var categories = (await data.ListCaseCategoriesAsync(ct)).ToDictionary(x => x.Id, x => x.Code);
                return (await data.ListCaseSubcategoriesAsync(ct))
                    .Select(x => new CaseSubcategoryResponse(x.Code, x.Name, categories.GetValueOrDefault(x.CategoryId)));
            })
            .WithName("ListCaseSubcategories");

        group.MapGet("/allowed-file-types", async (IReferenceData data, CancellationToken ct) =>
                (await data.ListAllowedFileTypesAsync(ct)).Where(x => x.IsActive)
                    .Select(x => new AllowedFileTypeResponse(x.Extension, x.ContentType, x.MaxSizeBytes, x.AppliesTo)))
            .WithName("ListAllowedFileTypes");

        return endpoints;
    }
}

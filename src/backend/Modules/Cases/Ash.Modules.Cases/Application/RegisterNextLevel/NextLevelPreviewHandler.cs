using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

public sealed record NextLevelPreviewParty(string Role, string Kind, string DisplayName, string? Identifier);

/// <summary>An institution link the new level will get: always MONITORING (client decision B.1; no delegation is copied).</summary>
public sealed record NextLevelPreviewLink(string Institution, string Mode);

/// <summary>
/// What continuing this level will copy: the object and the parties of a copied role (plaintiff, defendant — task
/// 053j), plus the institution links from those parties.
/// </summary>
public sealed record NextLevelPreviewResponse(
    string CaseCode,
    string CourtLevelName,
    string CourtName,
    string ObjectText,
    IReadOnlyList<NextLevelPreviewParty> Parties,
    IReadOnlyList<NextLevelPreviewLink> InstitutionLinks);

/// <summary>
/// GET /api/cases/{id}/next-level/preview (task 021): only for a level that can be continued (same gate as the
/// registration, otherwise its 404/409). The registration lookup scope, not case.view — Protokoll may see the
/// object of another office's case here, so every preview is audited as CASE_VIEW ("registration preview").
/// </summary>
internal sealed class NextLevelPreviewHandler(
    CasesDbContext db,
    NextLevelGate gate,
    IReferenceData reference,
    IInstitutionDirectory institutions,
    IAuditLog audit)
{
    public async Task<Result<NextLevelPreviewResponse>> Handle(Guid previousCasePublicId, CancellationToken ct)
    {
        var source = await gate.CheckAsync(previousCasePublicId, ct);
        if (source.IsFailure)
        {
            return source.Error!;
        }

        var previous = source.Value.Previous;
        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        // the same rows, in the same order, as RegisterNextLevelHandler copies them (only the copied roles, task 053j)
        var parties = await RegisterNextLevelHandler.CopiedPartiesAsync(db, previous.Id, roles, ct);
        // the links the new level gets: MONITORING for each institution party (client decision B.1)
        var institutionIds = RegisterNextLevelHandler.InstitutionIds(parties);
        var linked = (await institutions.GetByIdsAsync(institutionIds, ct)).ToDictionary(i => i.Id);

        var response = new NextLevelPreviewResponse(
            previous.CaseCode,
            (await reference.GetCourtLevel(previous.CourtLevelId, ct))?.Name ?? string.Empty,
            (await reference.GetCourt(previous.CourtId, ct))?.Name ?? string.Empty,
            previous.ObjectText,
            parties.Select(p => new NextLevelPreviewParty(
                    roles.GetValueOrDefault(p.PartyRoleId)?.Name ?? string.Empty, p.PartyKind, p.DisplayName, p.Identifier))
                .ToList(),
            institutionIds.Select(id => new NextLevelPreviewLink(
                    linked.TryGetValue(id, out var i) ? i.Name : string.Empty, CaseInstitution.Monitoring))
                .ToList());

        var caseId = (long)previous.Id;
        audit.Record(AuditActions.CaseView, "CASE", caseId, caseId, details: new { Purpose = "registration preview" });
        return response;
    }
}

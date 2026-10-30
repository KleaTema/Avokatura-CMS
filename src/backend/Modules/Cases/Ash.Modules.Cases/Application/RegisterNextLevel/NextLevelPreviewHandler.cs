using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

public sealed record NextLevelPreviewParty(string Role, string Kind, string DisplayName, string? Identifier);

/// <summary>An active institution link that will be copied, with its CURRENT mode (the new level may turn DELEGATED into MONITORING).</summary>
public sealed record NextLevelPreviewLink(string Institution, string Mode);

/// <summary>What continuing this level will copy: the object and the parties, plus the institution links.</summary>
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
        // the same rows, in the same order, as RegisterNextLevelHandler copies them
        var parties = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == previous.Id && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        var links = await db.CaseInstitutions.AsNoTracking()
            .Where(l => l.CaseId == previous.Id && l.EndedAt == null)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);
        var linked = (await institutions.GetByIdsAsync(links.Select(l => (long)l.InstitutionId).ToList(), ct))
            .ToDictionary(i => i.Id);

        var response = new NextLevelPreviewResponse(
            previous.CaseCode,
            (await reference.GetCourtLevel(previous.CourtLevelId, ct))?.Name ?? string.Empty,
            (await reference.GetCourt(previous.CourtId, ct))?.Name ?? string.Empty,
            previous.ObjectText,
            parties.Select(p => new NextLevelPreviewParty(
                    roles.GetValueOrDefault(p.PartyRoleId)?.Name ?? string.Empty, p.PartyKind, p.DisplayName, p.Identifier))
                .ToList(),
            links.Select(l => new NextLevelPreviewLink(
                    linked.TryGetValue(l.InstitutionId, out var i) ? i.Name : string.Empty, l.LinkMode))
                .ToList());

        var caseId = (long)previous.Id;
        audit.Record(AuditActions.CaseView, "CASE", caseId, caseId, details: new { Purpose = "registration preview" });
        return response;
    }
}

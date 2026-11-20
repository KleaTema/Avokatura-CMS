using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.RegisterNextLevel;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegistrationSearch;

/// <summary>The registration data of a case the Protokoll registered (read-only).</summary>
public sealed record RegistrationSummaryResponse(
    string CaseCode,
    string Status,
    string CourtLevelName,
    string CourtName,
    string? ActNumber,
    string ObjectText,
    IReadOnlyList<NextLevelPreviewParty> Parties);

/// <summary>
/// GET /api/cases/{id}/registration-summary (task 054d): what the Protokoll entered, for a case it registered that it can
/// no longer open (the Kryetar assigned it, or it was closed) — its own tabs list such cases. Registration level only:
/// never case.view (CaseAccessService is unchanged). Another user's case → 404. Audited as CASE_VIEW.
/// </summary>
internal sealed class RegistrationSummaryHandler(CasesDbContext db, IReferenceData reference, ICurrentUser user, IAuditLog audit)
{
    public async Task<Result<RegistrationSummaryResponse>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var userId = (uint)user.UserId;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId && x.RegisteredBy == userId, ct);
        if (c is null)
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        var parties = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == c.Id && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        audit.Record(AuditActions.CaseView, "CASE", (long)c.Id, (long)c.Id, details: new { Purpose = "registration summary" });
        return new RegistrationSummaryResponse(
            c.CaseCode,
            c.LifecycleStatus,
            (await reference.GetCourtLevel(c.CourtLevelId, ct))?.Name ?? string.Empty,
            (await reference.GetCourt(c.CourtId, ct))?.Name ?? string.Empty,
            c.ActNumber,
            c.ObjectText,
            parties.Select(p => new NextLevelPreviewParty(
                roles.GetValueOrDefault(p.PartyRoleId)?.Name ?? string.Empty, p.PartyKind, p.DisplayName, p.Identifier)).ToList());
    }
}

using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>Soft-deletes a party; for an institution's last party, see <see cref="PartyWriter.RemoveAsync"/>.</summary>
internal sealed class RemovePartyHandler(CasesDbContext db, PartyCaseGate gate, IReferenceData reference, PartyWriter writer)
{
    public async Task<Result<RemovePartyResponse>> Handle(
        Guid casePublicId, Guid partyPublicId, bool? endInstitutionLink, CancellationToken ct)
    {
        var c = await gate.ForChangeAsync(casePublicId, PartyCaseGate.Delete, ct);
        if (c.IsFailure)
        {
            return c.Error!;
        }

        var party = await db.CaseParties.SingleOrDefaultAsync(
            p => p.PublicId == partyPublicId && p.CaseId == c.Value.Id && p.DeletedAt == null, ct);
        if (party is null)
        {
            return PartyCaseGate.PartyNotFound();
        }

        var role = (await reference.ListPartyRolesAsync(ct)).Single(r => r.Id == party.PartyRoleId);
        var removal = await writer.RemoveAsync(c.Value, party, role, endInstitutionLink, ct);
        IReadOnlyList<PartyWarning> warnings = removal.Warning is { } w ? [new PartyWarning("institution.delegated", w)] : [];
        return new RemovePartyResponse(removal.LastInstitutionParty, removal.InstitutionLinkEnded, warnings);
    }
}

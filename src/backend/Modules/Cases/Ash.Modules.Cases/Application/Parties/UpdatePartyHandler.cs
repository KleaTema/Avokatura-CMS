using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>
/// Changes the role (any party), and the name and identifier of a NATURAL/LEGAL party. Kind and institution are
/// immutable (delete and add again); an institution's name is its snapshot and is not edited.
/// </summary>
internal sealed class UpdatePartyHandler(
    CasesDbContext db, PartyCaseGate gate, IReferenceData reference, IInstitutionDirectory institutions,
    PartyWriter writer, PartyViews views)
{
    public async Task<Result<PartyResponse>> Handle(Guid casePublicId, Guid partyPublicId, PartyRequest req, CancellationToken ct)
    {
        var c = await gate.ForChangeAsync(casePublicId, PartyCaseGate.Edit, ct);
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

        var errors = new FieldErrors();
        await CheckImmutableAsync(party, req, errors, ct);
        if (errors.Any)
        {
            return errors.ToError(PartyRules.ImmutableKindMessage);
        }

        string displayName;
        string? identifier;
        if (party.PartyKind == CaseParty.Institution)
        {
            if (string.IsNullOrWhiteSpace(req.PartyRoleCode))
            {
                errors.Add("partyRoleCode", "Roli i palës është i detyrueshëm.");
            }

            if (req.DisplayName is not null && req.DisplayName.Trim() != party.DisplayName)
            {
                errors.Add("displayName", "Emri i një institucioni nuk ndryshohet; ruhet emri i institucionit kur u shtua.");
            }

            if (req.Identifier is not null && PartyValidator.CleanIdentifier(party.PartyKind, req.Identifier) != party.Identifier)
            {
                errors.Add("identifier", "Identifikuesi i një institucioni nuk ndryshohet.");
            }

            displayName = party.DisplayName;
            identifier = party.Identifier;
        }
        else
        {
            // same format rules as on add, with the party's own (immutable) kind
            PartyValidator.ValidateFormat(req with { Kind = party.PartyKind, InstitutionPublicId = null }, string.Empty, errors);
            displayName = req.DisplayName?.Trim() ?? string.Empty;
            identifier = PartyValidator.CleanIdentifier(party.PartyKind, req.Identifier);
        }

        PartyRoleDto? newRole = null;
        if (!errors.Has("partyRoleCode"))
        {
            newRole = await reference.GetPartyRole(req.PartyRoleCode!.Trim(), ct);
            if (newRole is null || !newRole.IsActive)
            {
                errors.Add("partyRoleCode", "Roli i palës nuk ekziston ose nuk është aktiv.");
            }
        }

        if (!errors.Any && party.PartyKind != CaseParty.Institution)
        {
            await PartyRules.CheckDuplicatesAsync(db, c.Value.Id, newRole!.Id, party.PartyKind, null, displayName,
                party.Id, errors, ct);
        }

        if (errors.Any)
        {
            return errors.ToError("Të dhënat e palës nuk janë të vlefshme.");
        }

        var oldRole = (await reference.ListPartyRolesAsync(ct)).Single(r => r.Id == party.PartyRoleId);
        await writer.UpdateAsync(c.Value, party, oldRole, newRole!, displayName, identifier, ct);
        return await views.ToResponseAsync(party, ct);
    }

    /// <summary>Kind and institution may be sent back unchanged, never changed.</summary>
    private async Task CheckImmutableAsync(CaseParty party, PartyRequest req, FieldErrors errors, CancellationToken ct)
    {
        if (req.Kind is not null && req.Kind.Trim() != party.PartyKind)
        {
            errors.Add("kind", PartyRules.ImmutableKindMessage);
        }

        Guid? current = party.InstitutionId is { } id
            ? (await institutions.GetByIdsAsync([id], ct)).Single().PublicId
            : null;
        if (req.InstitutionPublicId is not null && req.InstitutionPublicId != current)
        {
            errors.Add("institutionPublicId", PartyRules.ImmutableKindMessage);
        }
    }
}

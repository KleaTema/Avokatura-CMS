using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>Turns a party request (format already checked) into a <see cref="NewParty"/>: active role, active institution.</summary>
internal sealed class PartyResolver(IReferenceData reference, IInstitutionDirectory institutions)
{
    /// <summary>Null when something is missing or not allowed; the reason is added to <paramref name="errors"/> (unless already there).</summary>
    public async Task<NewParty?> ResolveAsync(PartyRequest p, string kind, string prefix, FieldErrors errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.PartyRoleCode))
        {
            return null; // reported by PartyValidator
        }

        var role = await reference.GetPartyRole(p.PartyRoleCode.Trim(), ct);
        if (role is null || !role.IsActive)
        {
            errors.Add($"{prefix}partyRoleCode", "Roli i palës nuk ekziston ose nuk është aktiv.");
            return null;
        }

        InstitutionInfo? institution = null;
        if (kind == CaseParty.Institution)
        {
            if (p.InstitutionPublicId is not { } institutionId || errors.Has($"{prefix}institutionPublicId"))
            {
                return null;
            }

            institution = (await institutions.GetByPublicIdsAsync([institutionId], ct)).SingleOrDefault();
            if (institution is not { IsActive: true })
            {
                errors.Add($"{prefix}institutionPublicId", "Institucioni nuk ekziston ose nuk është aktiv.");
                return null;
            }
        }

        return new NewParty(role, kind, institution, p.DisplayName?.Trim() ?? string.Empty,
            PartyValidator.CleanIdentifier(kind, p.Identifier));
    }
}

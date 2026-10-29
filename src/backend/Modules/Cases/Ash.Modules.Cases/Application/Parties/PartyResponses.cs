using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;

namespace Ash.Modules.Cases.Application.Parties;

public sealed record PartyRoleRef(string Code, string Name);

public sealed record PartyInstitutionRef(Guid PublicId, string Name);

/// <summary>A party as the API shows it: public ids only.</summary>
public sealed record PartyResponse(
    Guid PublicId, PartyRoleRef Role, string Kind, string DisplayName, string? Identifier, PartyInstitutionRef? Institution);

public sealed record PartyWarning(string Code, string Message);

/// <summary>
/// LastInstitutionParty: the removed party was the institution's last one in the case. When the link was kept
/// (endInstitutionLink not true, or a DELEGATED link), InstitutionLinkEnded is false.
/// </summary>
public sealed record RemovePartyResponse(
    bool LastInstitutionParty, bool InstitutionLinkEnded, IReadOnlyList<PartyWarning> Warnings);

/// <summary>Builds <see cref="PartyResponse"/>s: role code/name from Reference, institution public id/name from Identity.</summary>
internal sealed class PartyViews(IReferenceData reference, IInstitutionDirectory institutions)
{
    /// <summary>Ordered by the role's sort order, then by creation.</summary>
    public async Task<IReadOnlyList<PartyResponse>> ToResponsesAsync(IReadOnlyCollection<CaseParty> parties, CancellationToken ct)
    {
        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        var institutionIds = parties.Where(p => p.InstitutionId != null).Select(p => (long)p.InstitutionId!.Value).Distinct().ToList();
        var known = (await institutions.GetByIdsAsync(institutionIds, ct)).ToDictionary(i => i.Id);

        return parties
            .OrderBy(p => roles.GetValueOrDefault(p.PartyRoleId)?.SortOrder ?? byte.MaxValue)
            .ThenBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Select(p =>
            {
                var role = roles.GetValueOrDefault(p.PartyRoleId);
                var institution = p.InstitutionId is { } id && known.TryGetValue(id, out var info)
                    ? new PartyInstitutionRef(info.PublicId, info.Name)
                    : null;
                return new PartyResponse(p.PublicId, new PartyRoleRef(role?.Code ?? string.Empty, role?.Name ?? string.Empty),
                    p.PartyKind, p.DisplayName, p.Identifier, institution);
            })
            .ToList();
    }

    public async Task<PartyResponse> ToResponseAsync(CaseParty party, CancellationToken ct) =>
        (await ToResponsesAsync([party], ct)).Single();
}

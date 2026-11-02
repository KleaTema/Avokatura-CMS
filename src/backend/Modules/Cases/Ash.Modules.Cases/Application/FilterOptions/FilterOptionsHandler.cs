using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Application.FilterOptions;

public sealed record FilterOffice(Guid PublicId, string Name);

/// <summary>A lawyer for a filter, with the office it belongs to (so the UI can narrow the list to a chosen office).</summary>
public sealed record FilterLawyer(Guid PublicId, string FullName, Guid OfficePublicId);

public sealed record FilterOptionsResponse(IReadOnlyList<FilterOffice> Offices, IReadOnlyList<FilterLawyer> Lawyers);

/// <summary>
/// GET /api/cases/filter-options: the office and lawyer filters of the case list and the agenda (task 031).
/// - case.view_all → every active office and every active STATE_LAWYER with an office;
/// - case.view_office → the user's own office and its lawyers (the same people as the assignment picker offers);
/// - anyone else → empty lists.
/// Users and offices come from Identity.Contracts (Cases does not read Identity tables).
/// </summary>
internal sealed class FilterOptionsHandler(IOfficeDirectory offices, IUserDirectory users, OfficeLawyers officeLawyers, ICurrentUser user)
{
    public async Task<FilterOptionsResponse> Handle(CancellationToken ct)
    {
        if (user.HasPermission(CaseScopeQuery.ViewAllPermission))
        {
            var active = await offices.ListActiveAsync(ct);
            var publicIdOf = active.ToDictionary(o => o.Id, o => o.PublicId);
            var lawyers = (await users.GetUsersByRoleAsync(OfficeLawyers.LawyerRole, ct))
                .Where(u => u.OfficeId is { } office && publicIdOf.ContainsKey(office))
                .Select(u => new FilterLawyer(u.PublicId, u.FullName, publicIdOf[u.OfficeId!.Value]));
            return Sorted(active.Select(o => new FilterOffice(o.PublicId, o.Name)), lawyers);
        }

        if (user.HasPermission(CaseScopeQuery.ViewOfficePermission) && user.OfficeId is { } officeId)
        {
            var own = (await offices.ListActiveAsync(ct)).SingleOrDefault(o => o.Id == officeId);
            if (own is null)
            {
                return Empty;
            }

            var lawyers = (await officeLawyers.ListAsync(officeId, user.HasPermission(OfficeLawyers.AssignPermission), ct))
                .Select(l => new FilterLawyer(l.PublicId, l.FullName, own.PublicId));
            return Sorted([new FilterOffice(own.PublicId, own.Name)], lawyers);
        }

        return Empty;
    }

    private static readonly FilterOptionsResponse Empty = new([], []);

    private static FilterOptionsResponse Sorted(IEnumerable<FilterOffice> offices, IEnumerable<FilterLawyer> lawyers) =>
        new(offices.OrderBy(o => o.Name, OfficeLawyers.NameOrder).ToList(),
            lawyers.OrderBy(l => l.FullName, OfficeLawyers.NameOrder).ToList());
}

using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListPartyRolesAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<PartyRoleAdminResponse>> Handle(CancellationToken ct)
    {
        return await db.PartyRoles.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .Select(x => new PartyRoleAdminResponse(x.Code, x.Name, x.ReportGroup, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
    }
}

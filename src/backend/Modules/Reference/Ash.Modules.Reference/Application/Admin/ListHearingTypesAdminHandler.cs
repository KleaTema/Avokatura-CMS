using Ash.Modules.Audit.Contracts;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListHearingTypesAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<HearingTypeAdminResponse>> Handle(CancellationToken ct)
    {
        return await db.HearingTypes.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .Select(x => new HearingTypeAdminResponse(x.Code, x.Name, x.IsOther, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
    }
}

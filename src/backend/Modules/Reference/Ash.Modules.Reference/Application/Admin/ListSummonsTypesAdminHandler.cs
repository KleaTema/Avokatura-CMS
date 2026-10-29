using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListSummonsTypesAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<SummonsTypeAdminResponse>> Handle(CancellationToken ct)
    {
        return await db.SummonsTypes.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new SummonsTypeAdminResponse(x.Code, x.Name, x.IsActive))
            .ToListAsync(ct);
    }
}

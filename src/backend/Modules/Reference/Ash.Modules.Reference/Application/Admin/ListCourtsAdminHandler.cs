using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

/// <summary>About 100 rows, so no paging; inactive courts are included.</summary>
public sealed class ListCourtsAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<CourtAdminResponse>> Handle(CancellationToken ct)
    {
        return await (from c in db.Courts.AsNoTracking()
                      join l in db.CourtLevels.AsNoTracking() on c.CourtLevelId equals l.Id
                      orderby l.SortOrder, c.Name
                      select new CourtAdminResponse(c.Id, c.Name, l.Code, c.City, c.IsForeign, c.IsActive))
            .ToListAsync(ct);
    }
}

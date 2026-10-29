using Ash.Modules.Reference.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

public sealed class ListDecisionResultsAdminHandler(ReferenceDbContext db)
{
    public async Task<IReadOnlyList<DecisionResultAdminResponse>> Handle(CancellationToken ct)
    {
        return await db.DecisionResults.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code)
            .Select(x => new DecisionResultAdminResponse(
                x.Code, x.Name, x.OutcomeGroup, x.SendsBackForRetrial, x.IsOther, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
    }
}

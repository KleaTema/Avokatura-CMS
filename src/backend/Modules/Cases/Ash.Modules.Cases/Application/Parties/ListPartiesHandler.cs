using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Parties;

/// <summary>The non-deleted parties of a case; anyone who can view the case may read them.</summary>
internal sealed class ListPartiesHandler(CasesDbContext db, PartyCaseGate gate, PartyViews views)
{
    public async Task<Result<IReadOnlyList<PartyResponse>>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await gate.ForReadAsync(casePublicId, ct);
        if (c.IsFailure)
        {
            return c.Error!;
        }

        var parties = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == c.Value.Id && p.DeletedAt == null)
            .ToListAsync(ct);
        return Result.Success(await views.ToResponsesAsync(parties, ct));
    }
}

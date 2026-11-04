using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application;

/// <summary><see cref="ICaseLookup"/>: code, court and team of a case for other modules — no access check.</summary>
internal sealed class CaseLookup(CasesDbContext db, IReferenceData reference) : ICaseLookup
{
    public Task<CaseSummary?> GetAsync(Guid casePublicId, CancellationToken ct = default) =>
        SummaryAsync(db.Cases.AsNoTracking().Where(c => c.PublicId == casePublicId), ct);

    public Task<CaseSummary?> GetAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        return SummaryAsync(db.Cases.AsNoTracking().Where(c => c.Id == id), ct);
    }

    public async Task<IReadOnlyList<CaseMember>> GetTeamAsync(long caseId, CancellationToken ct = default)
    {
        var id = (ulong)caseId;
        return await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == id && a.EndedAt == null)
            .OrderBy(a => a.Id)
            .Select(a => new CaseMember(a.UserId, a.CaseRole))
            .ToListAsync(ct);
    }

    private async Task<CaseSummary?> SummaryAsync(IQueryable<Case> cases, CancellationToken ct)
    {
        var c = await cases.Select(x => new { x.Id, x.PublicId, x.CaseCode, x.CourtId, x.OfficeId }).SingleOrDefaultAsync(ct);
        if (c is null)
        {
            return null;
        }

        var court = await reference.GetCourt(c.CourtId, ct);
        return new CaseSummary((long)c.Id, c.PublicId, c.CaseCode, court?.Name ?? string.Empty, c.OfficeId);
    }
}

using Ash.Modules.Cases.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Infrastructure.Access;

public sealed class CaseAssignmentCounts(CasesDbContext db) : ICaseAssignmentCounts
{
    public async Task<IReadOnlyDictionary<long, int>> CountCurrentAsync(
        IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        var ids = userIds.Select(x => (uint)x).ToList();
        var rows = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.EndedAt == null && ids.Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => (long)r.UserId, r => r.Count);
    }
}

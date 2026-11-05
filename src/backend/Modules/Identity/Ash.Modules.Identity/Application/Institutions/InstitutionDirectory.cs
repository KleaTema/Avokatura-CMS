using Ash.Modules.Identity.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

public sealed class InstitutionDirectory(IdentityDbContext db) : IInstitutionDirectory
{
    public async Task<IReadOnlyList<InstitutionInfo>> GetByPublicIdsAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct = default)
    {
        if (publicIds.Count == 0)
        {
            return [];
        }

        return await db.Institutions.AsNoTracking()
            .Where(i => publicIds.Contains(i.PublicId))
            .Select(i => new InstitutionInfo(i.Id, i.PublicId, i.Name, i.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InstitutionInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var internalIds = ids.Select(id => (uint)id).ToList();
        return await db.Institutions.AsNoTracking()
            .Where(i => internalIds.Contains(i.Id))
            .Select(i => new InstitutionInfo(i.Id, i.PublicId, i.Name, i.IsActive))
            .ToListAsync(ct);
    }

    // ftn_institutions_search (script 014)
    public async Task<IReadOnlyList<long>> SearchIdsAsync(string booleanWords, int limit, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(booleanWords)
            ? []
            : (await db.Database.SqlQueryRaw<uint>(
                    "SELECT id AS Value FROM institutions WHERE MATCH(name, short_name, nipt) AGAINST ({0} IN BOOLEAN MODE) LIMIT {1}",
                    booleanWords, limit)
                .ToListAsync(ct)).Select(id => (long)id).ToList();
}

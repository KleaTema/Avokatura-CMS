using Ash.Infrastructure.Persistence;
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

    // ftn_institutions_search (script 014) + the contains rule (TextSearch)
    public async Task<IReadOnlyList<long>> SearchIdsAsync(string text, int limit, CancellationToken ct = default)
    {
        var search = TextSearch.Parse(text);
        if (search.IsEmpty)
        {
            return [];
        }

        var sql = $"SELECT id AS Value FROM institutions WHERE {search.Predicate("inst", "name", "short_name", "nipt")} LIMIT {limit}";
        return (await db.Database.SqlQueryRaw<uint>(sql, search.Parameters("inst").ToArray<object>()).ToListAsync(ct))
            .Select(id => (long)id).ToList();
    }
}

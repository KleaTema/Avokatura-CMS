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
}

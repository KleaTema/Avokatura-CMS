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
}

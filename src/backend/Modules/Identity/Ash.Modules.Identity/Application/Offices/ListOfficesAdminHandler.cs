using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

/// <summary>All offices (a few dozen), inactive included, with parent, head and the number of active users.</summary>
public sealed class ListOfficesAdminHandler(IdentityDbContext db)
{
    public async Task<IReadOnlyList<OfficeAdminResponse>> Handle(CancellationToken ct)
    {
        var offices = await db.Offices.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
        var headIds = offices.Where(o => o.HeadUserId != null).Select(o => o.HeadUserId!.Value).ToList();
        var heads = await db.Users.AsNoTracking().Where(u => headIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new NamedRef(u.PublicId, u.FirstName + " " + u.LastName), ct);
        var activeUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.OfficeId != null)
            .GroupBy(u => u.OfficeId!.Value)
            .Select(g => new { OfficeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OfficeId, x => x.Count, ct);
        var byId = offices.ToDictionary(o => o.Id);

        return offices.Select(o => new OfficeAdminResponse(
            o.PublicId,
            o.Code,
            o.Name,
            o.OfficeType,
            o.ParentOfficeId is { } p && byId.TryGetValue(p, out var parent) ? new NamedRef(parent.PublicId, parent.Name) : null,
            o.HeadUserId is { } h ? heads.GetValueOrDefault(h) : null,
            o.IsActive,
            activeUsers.GetValueOrDefault(o.Id))).ToList();
    }
}

using Ash.Modules.Identity.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

public sealed class OfficeDirectory(IdentityDbContext db) : IOfficeDirectory
{
    public async Task<OfficeRouting?> GetRoutingAsync(long officeId, CancellationToken ct = default)
    {
        var id = (ushort)officeId;
        var office = await db.Offices.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        if (office is null)
        {
            return null;
        }

        var head = office.HeadUserId is { } headId
            ? await db.Users.AsNoTracking().Where(u => u.Id == headId)
                .Select(u => new OfficeHead(u.Id, u.PublicId, u.FirstName + " " + u.LastName, u.IsActive))
                .SingleOrDefaultAsync(ct)
            : null;
        return new OfficeRouting(office.Id, office.Name, office.IsActive, head);
    }
}

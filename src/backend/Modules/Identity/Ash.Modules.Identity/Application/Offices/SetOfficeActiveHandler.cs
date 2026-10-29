using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

/// <summary>Deactivation = is_active 0, never a delete; refused while the office still has active users.</summary>
public sealed class SetOfficeActiveHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<OfficeAdminResponse>> Handle(Guid publicId, bool active, CancellationToken ct)
    {
        var office = await db.Offices.SingleOrDefaultAsync(o => o.PublicId == publicId, ct);
        if (office is null)
        {
            return OfficeAdminSupport.NotFound();
        }

        if (!active)
        {
            var activeUsers = await db.Users.CountAsync(u => u.OfficeId == office.Id && u.IsActive, ct);
            if (activeUsers > 0)
            {
                return Error.Validation("office.has_active_users",
                    $"Zyra ka {activeUsers} përdorues aktivë. Transferojini ose çaktivizojini ata para se të çaktivizoni zyrën.");
            }
        }

        if (office.IsActive != active)
        {
            var oldValues = OfficeAdminSupport.Snapshot(office);
            office.IsActive = active;
            await db.SaveChangesAsync(ct);
            OfficeAdminSupport.AuditChange(audit, office, active ? "OFFICE_ACTIVATED" : "OFFICE_DEACTIVATED", oldValues,
                OfficeAdminSupport.Snapshot(office));
        }

        return await OfficeAdminSupport.ToResponseAsync(db, office, ct);
    }
}

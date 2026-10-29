using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

/// <summary>Deactivation = is_active 0, never a delete; refused while the institution still has active users.</summary>
public sealed class SetInstitutionActiveHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<InstitutionAdminResponse>> Handle(Guid publicId, bool active, CancellationToken ct)
    {
        var institution = await db.Institutions.SingleOrDefaultAsync(i => i.PublicId == publicId, ct);
        if (institution is null)
        {
            return InstitutionAdminSupport.NotFound();
        }

        if (!active)
        {
            var activeUsers = await db.Users.CountAsync(u => u.InstitutionId == institution.Id && u.IsActive, ct);
            if (activeUsers > 0)
            {
                return Error.Validation("institution.has_active_users",
                    $"Institucioni ka {activeUsers} përdorues aktivë. Çaktivizojini ata para se të çaktivizoni institucionin.");
            }
        }

        if (institution.IsActive != active)
        {
            var oldValues = InstitutionAdminSupport.Snapshot(institution);
            institution.IsActive = active;
            await db.SaveChangesAsync(ct);
            InstitutionAdminSupport.AuditChange(audit, institution, active ? "INSTITUTION_ACTIVATED" : "INSTITUTION_DEACTIVATED",
                oldValues, InstitutionAdminSupport.Snapshot(institution));
        }

        return await InstitutionAdminSupport.ToResponseAsync(db, institution, ct);
    }
}

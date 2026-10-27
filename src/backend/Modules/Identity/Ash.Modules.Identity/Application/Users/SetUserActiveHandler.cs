using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

/// <summary>Deactivation = is_active 0, never a delete. The response carries the number of current case assignments.</summary>
public sealed class SetUserActiveHandler(IdentityDbContext db, UserAdminSupport support)
{
    public async Task<Result<UserResponse>> Handle(Guid publicId, bool active, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId, ct);
        if (user is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        if (!active)
        {
            if (user.Id == support.Actor.UserId)
            {
                return Error.Validation("user.cannot_deactivate_self", "Nuk mund ta çaktivizoni veten.");
            }

            var admins = await support.LockActiveAdminsAsync(ct);
            if (admins.Count == 1 && admins[0] == user.Id)
            {
                return Error.Validation("user.last_admin", "Nuk mund të çaktivizohet administratori i fundit aktiv.");
            }
        }

        if (user.IsActive != active)
        {
            var oldActive = user.IsActive;
            user.IsActive = active;
            await db.SaveChangesAsync(ct);
            support.AuditChange(user.Id, active ? "USER_ACTIVATED" : "USER_DEACTIVATED",
                new { IsActive = oldActive }, new { IsActive = active });
            support.InvalidateAccess(user.Id);
        }

        return await support.ToResponseAsync(user, ct);
    }
}

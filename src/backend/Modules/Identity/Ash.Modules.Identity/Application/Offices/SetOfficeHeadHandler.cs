using Ash.Modules.Audit.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Offices;

/// <summary>
/// The head (Kryetari) must be an active user of this office with role OFFICE_HEAD, and head of no other office.
/// New cases of the office are routed to the head.
/// </summary>
public sealed class SetOfficeHeadHandler(IdentityDbContext db, IAuditLog audit)
{
    public async Task<Result<OfficeAdminResponse>> Handle(Guid publicId, SetOfficeHeadRequest req, CancellationToken ct)
    {
        var office = await db.Offices.SingleOrDefaultAsync(o => o.PublicId == publicId, ct);
        if (office is null)
        {
            return OfficeAdminSupport.NotFound();
        }

        uint? headId = null;
        // TODO(client-q-office-without-head): what happens at registration when the office has no head — default for task 016: registration refused with a clear message.
        if (req.UserPublicId is { } userPublicId)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.PublicId == userPublicId, ct);
            if (user is null || !user.IsActive)
            {
                return Error.Validation("office.head_invalid", "Kryetari duhet të jetë një përdorues aktiv.");
            }

            if (user.OfficeId != office.Id)
            {
                return Error.Validation("office.head_other_office", "Kryetari duhet të jetë përdorues i kësaj zyre.");
            }

            var isOfficeHead = await (from ur in db.UserRoles
                                      join r in db.Roles on ur.RoleId equals r.Id
                                      where ur.UserId == user.Id && r.Code == OfficeAdminSupport.HeadRole
                                      select ur).AnyAsync(ct);
            if (!isOfficeHead)
            {
                return Error.Validation("office.head_role_missing", "Kryetari duhet të ketë rolin OFFICE_HEAD.");
            }

            var otherOffice = await db.Offices.AsNoTracking()
                .Where(o => o.HeadUserId == user.Id && o.Id != office.Id)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct);
            if (otherOffice is not null)
            {
                return Error.Validation("office.head_elsewhere", $"Ky përdorues është tashmë Kryetar i zyrës {otherOffice}.");
            }

            headId = user.Id;
        }

        if (office.HeadUserId != headId)
        {
            var oldValues = OfficeAdminSupport.Snapshot(office);
            office.HeadUserId = headId;
            await db.SaveChangesAsync(ct);
            OfficeAdminSupport.AuditChange(audit, office, "OFFICE_HEAD_CHANGED", oldValues, OfficeAdminSupport.Snapshot(office));
        }

        return await OfficeAdminSupport.ToResponseAsync(db, office, ct);
    }
}

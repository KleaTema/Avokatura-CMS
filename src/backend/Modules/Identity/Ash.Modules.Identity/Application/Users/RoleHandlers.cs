using Ash.Modules.Identity.Application.Offices;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

public sealed record RoleRequest(string Role);

public sealed class GrantRoleHandler(IdentityDbContext db, UserAdminSupport support)
{
    public async Task<Result<UserResponse>> Handle(Guid publicId, string role, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId, ct);
        if (user is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        var roleId = await support.FindRoleIdAsync(role, ct);
        if (roleId is null)
        {
            return Error.Validation("user.role_unknown", $"Roli {role} nuk ekziston.");
        }

        var roleError = UserAdminSupport.ValidateRole(role, user.InstitutionId is not null);
        if (roleError is not null)
        {
            return roleError;
        }

        var before = await support.RoleCodesAsync(user.Id, ct);
        if (!before.Contains(role))
        {
            db.UserRoles.Add(new UserRoleRecord { UserId = user.Id, RoleId = roleId.Value, GrantedBy = (uint)support.Actor.UserId });
            await db.SaveChangesAsync(ct);
            var after = await support.RoleCodesAsync(user.Id, ct);
            support.AuditChange(user.Id, "ROLE_GRANTED", new { Roles = before }, new { Roles = after });
            support.InvalidateAccess(user.Id);
        }

        return await support.ToResponseAsync(user, ct);
    }
}

public sealed class RevokeRoleHandler(IdentityDbContext db, UserAdminSupport support)
{
    public async Task<Result<UserResponse>> Handle(Guid publicId, string role, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId, ct);
        if (user is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        var roleId = await support.FindRoleIdAsync(role, ct);
        if (roleId is null)
        {
            return Error.Validation("user.role_unknown", $"Roli {role} nuk ekziston.");
        }

        if (role == UserAdminSupport.AdminRole)
        {
            if (user.Id == support.Actor.UserId)
            {
                return Error.Validation("user.cannot_remove_own_admin", "Nuk mund ta hiqni vetes rolin ADMIN.");
            }

            var admins = await support.LockActiveAdminsAsync(ct);
            if (admins.Count == 1 && admins[0] == user.Id)
            {
                return Error.Validation("user.last_admin", "Nuk mund të hiqet roli i administratorit të fundit aktiv.");
            }
        }

        if (role == OfficeAdminSupport.HeadRole)
        {
            var headError = await support.HeadOfOfficeErrorAsync(user.Id, ct);
            if (headError is not null)
            {
                return headError;
            }
        }

        var before = await support.RoleCodesAsync(user.Id, ct);
        var link = await db.UserRoles.SingleOrDefaultAsync(x => x.UserId == user.Id && x.RoleId == roleId.Value, ct);
        if (link is not null)
        {
            db.UserRoles.Remove(link);
            await db.SaveChangesAsync(ct);
            var after = await support.RoleCodesAsync(user.Id, ct);
            support.AuditChange(user.Id, "ROLE_REVOKED", new { Roles = before }, new { Roles = after });
            support.InvalidateAccess(user.Id);
        }

        return await support.ToResponseAsync(user, ct);
    }
}

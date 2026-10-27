using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

public sealed record CreateUserRequest(
    string Email, string FirstName, string LastName, Guid? OfficeId, Guid? InstitutionId, IReadOnlyList<string>? Roles);

/// <summary>
/// TODO(client-q): the admin creates the user BEFORE the first login. external_subject stays NULL until the first
/// production login, when ResolveUserHandler links the account by (lower-case) email.
/// </summary>
public sealed class CreateUserHandler(IdentityDbContext db, UserAdminSupport support)
{
    public async Task<Result<UserResponse>> Handle(CreateUserRequest req, CancellationToken ct)
    {
        var email = UserAdminSupport.NormalizeEmail(req.Email);
        var invalid = UserAdminSupport.ValidateIdentity(email, req.FirstName, req.LastName);
        if (invalid is not null)
        {
            return invalid;
        }

        var org = await support.ResolveOrganizationAsync(req.OfficeId, req.InstitutionId, ct);
        if (org.IsFailure)
        {
            return org.Error!;
        }

        var roles = (req.Roles ?? []).Distinct().ToList();
        var roleIds = new List<byte>();
        foreach (var role in roles)
        {
            var roleError = UserAdminSupport.ValidateRole(role, org.Value.InstitutionId is not null);
            if (roleError is not null)
            {
                return roleError;
            }

            var roleId = await support.FindRoleIdAsync(role, ct);
            if (roleId is null)
            {
                return Error.Validation("user.role_unknown", $"Roli {role} nuk ekziston.");
            }

            roleIds.Add(roleId.Value);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Error.Conflict("user.email_exists", "Ekziston tashmë një përdorues me këtë email.");
        }

        var user = new UserRecord
        {
            PublicId = Guid.NewGuid(),
            ExternalSubject = null,
            Email = email,
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            OfficeId = org.Value.OfficeId,
            InstitutionId = org.Value.InstitutionId,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var actorId = (uint)support.Actor.UserId;
        foreach (var roleId in roleIds)
        {
            db.UserRoles.Add(new UserRoleRecord { UserId = user.Id, RoleId = roleId, GrantedBy = actorId });
        }

        await db.SaveChangesAsync(ct);

        support.AuditChange(user.Id, "USER_CREATED", null, new
        {
            user.Email, user.FirstName, user.LastName, Office = req.OfficeId, Institution = req.InstitutionId, Roles = roles
        });
        return await support.ToResponseAsync(user, ct);
    }
}

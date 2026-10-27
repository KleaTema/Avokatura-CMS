using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

public sealed record UpdateUserRequest(
    string Email, string FirstName, string LastName, Guid? OfficeId, Guid? InstitutionId);

public sealed class UpdateUserHandler(IdentityDbContext db, UserAdminSupport support)
{
    public async Task<Result<UserResponse>> Handle(Guid publicId, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.PublicId == publicId, ct);
        if (user is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

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

        // The kind of user (office / institution) decides which roles are valid, so it cannot change.
        if ((org.Value.InstitutionId is not null) != (user.InstitutionId is not null))
        {
            return Error.Validation("user.organization_type_locked",
                "Nuk mund të ndryshohet lloji i përdoruesit (zyrë / institucion). Krijoni një përdorues të ri.");
        }

        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id, ct))
        {
            return Error.Conflict("user.email_exists", "Ekziston tashmë një përdorues me këtë email.");
        }

        var oldValues = new
        {
            user.Email, user.FirstName, user.LastName, Office = user.OfficeId, Institution = user.InstitutionId
        };
        user.Email = email;
        user.FirstName = req.FirstName.Trim();
        user.LastName = req.LastName.Trim();
        user.OfficeId = org.Value.OfficeId;
        user.InstitutionId = org.Value.InstitutionId;
        await db.SaveChangesAsync(ct);

        support.AuditChange(user.Id, "USER_UPDATED", oldValues, new
        {
            user.Email, user.FirstName, user.LastName, Office = user.OfficeId, Institution = user.InstitutionId
        });
        support.InvalidateAccess(user.Id);
        return await support.ToResponseAsync(user, ct);
    }
}

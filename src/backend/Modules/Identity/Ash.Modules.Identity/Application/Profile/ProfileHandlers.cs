using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Profile;

public sealed record RoleRef(string Code, string Name);

/// <summary>
/// The own profile (tasks 048, 053g), read-only: name, position (Organika), role(s), e-mail, office / institution. The
/// name and e-mail come from the government identity provider, the position and unit from the Organika
/// import. Client decision E (12.11.2026): no phone and no notification preferences — every notification is mandatory.
/// </summary>
public sealed record ProfileResponse(
    Guid PublicId,
    string FirstName,
    string LastName,
    string Email,
    string? Position,
    string? OrgUnit,
    NamedRef? Office,
    NamedRef? Institution,
    IReadOnlyList<RoleRef> Roles,
    string? HeadOf,
    DateTime? LastLoginAt);

public sealed class ProfileHandlers(IdentityDbContext db, ICurrentUser user)
{
    public async Task<Result<ProfileResponse>> GetAsync(CancellationToken ct)
    {
        var id = (uint)user.UserId;
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.PublicId,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Position,
                u.OrgUnit,
                u.LastLoginAt,
                Office = db.Offices.Where(o => o.Id == u.OfficeId).Select(o => new NamedRef(o.PublicId, o.Name)).FirstOrDefault(),
                Institution = db.Institutions.Where(i => i.Id == u.InstitutionId).Select(i => new NamedRef(i.PublicId, i.Name)).FirstOrDefault(),
                HeadOf = db.Offices.Where(o => o.HeadUserId == u.Id).Select(o => o.Name).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ur.UserId == id
                           orderby r.Id
                           select new RoleRef(r.Code, r.Name)).ToListAsync(ct);
        return new ProfileResponse(row.PublicId, row.FirstName, row.LastName, row.Email, row.Position, row.OrgUnit,
            row.Office, row.Institution, roles, row.HeadOf, row.LastLoginAt);
    }
}

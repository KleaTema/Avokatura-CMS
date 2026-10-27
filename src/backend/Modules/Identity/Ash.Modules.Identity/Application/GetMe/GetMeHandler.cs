using Ash.Modules.Identity.Application.ResolveUser;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.GetMe;

public sealed class GetMeHandler(IdentityDbContext db, CurrentUser user)
{
    private static readonly string[] MenuOrder =
        ["dashboard", "cases", "chat", "notifications", "profile", "agenda", "reports", "admin"];

    public async Task<Result<GetMeResponse>> Handle(CancellationToken ct)
    {
        var publicId = user.PublicId;
        var row = await db.Users.AsNoTracking()
            .Where(u => u.PublicId == publicId)
            .Select(u => new
            {
                u.FirstName,
                u.LastName,
                u.Email,
                Office = db.Offices.Where(o => o.Id == u.OfficeId).Select(o => new NamedRef(o.PublicId, o.Name)).FirstOrDefault(),
                Institution = db.Institutions.Where(i => i.Id == u.InstitutionId).Select(i => new NamedRef(i.PublicId, i.Name)).FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Error.NotFound("user.not_found", "Përdoruesi nuk u gjet.");
        }

        var menus = MenuOrder.Where(m => user.HasPermission($"menu.{m}")).ToList();
        var permissions = user.Permissions.Order(StringComparer.Ordinal).ToList();
        var roles = user.Roles.Order(StringComparer.Ordinal).ToList();

        return new GetMeResponse(
            publicId, row.FirstName, row.LastName, row.Email, row.Office, row.Institution, roles, permissions, menus);
    }
}

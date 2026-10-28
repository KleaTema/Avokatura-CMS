using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

/// <summary>Scope is OFFICE or INSTITUTION: the kind of user the role may be granted to.</summary>
public sealed record RoleResponse(string Code, string Name, string Scope);

/// <summary>Roles from the roles table; the scope comes from <see cref="UserAdminSupport.ScopeOf"/>, the same rule grant/create validation uses.</summary>
public sealed class ListRolesHandler(IdentityDbContext db)
{
    public async Task<IReadOnlyList<RoleResponse>> Handle(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        return roles
            .Select(r => (Role: r, Scope: UserAdminSupport.ScopeOf(r.Code)))
            .Where(x => x.Scope is not null)
            .Select(x => new RoleResponse(x.Role.Code, x.Role.Name, x.Scope!))
            .ToList();
    }
}

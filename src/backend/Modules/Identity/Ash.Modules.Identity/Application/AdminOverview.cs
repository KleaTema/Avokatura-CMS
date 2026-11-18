using Ash.Modules.Identity.Application.Imports;
using Ash.Modules.Identity.Application.Users;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application;

/// <summary>
/// Task 053n — <see cref="IAdminOverview"/>: every number through the query of the page it opens — the users through
/// <see cref="ListUsersHandler.CountAsync"/> (the list's own filters), the imports through
/// <see cref="ImportPipeline.ListAsync"/> (the history the page filters in the browser).
/// </summary>
internal sealed class AdminOverview(
    IdentityDbContext db, ListUsersHandler users, ImportPipeline imports, OrganikaImport organika, UsersImport usersImport)
    : IAdminOverview
{
    public Task<int> CountUsersAsync(AdminUserCount which, DateOnly since, CancellationToken ct = default)
    {
        var all = new ListUsersQuery(null, null, null, null, null, null, null);
        var query = which switch
        {
            AdminUserCount.Active => all with { Active = true },
            AdminUserCount.Inactive => all with { Active = false },
            AdminUserCount.NoRole => all with { NoRole = true },
            AdminUserCount.NeverLoggedIn => all with { NeverLoggedIn = true },
            AdminUserCount.CreatedSince => all with { CreatedFrom = since },
            AdminUserCount.LoginBlocked => all with { Login = UserLoginFilter.Blocked },
            AdminUserCount.LoginNone => all with { Login = UserLoginFilter.None },
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        return users.CountAsync(query, ct);
    }

    public Task<int> CountOfficesWithoutHeadAsync(CancellationToken ct = default) =>
        db.Offices.AsNoTracking().CountAsync(o => o.IsActive && o.HeadUserId == null, ct);

    public async Task<IReadOnlySet<int>> ListCoveredCourtIdsAsync(CancellationToken ct = default)
    {
        // the same rule as OfficeDirectory.GetByCourtAsync (where a new case of the court goes)
        var ids = await (from oc in db.OfficeCourts.AsNoTracking()
                         join o in db.Offices.AsNoTracking() on oc.OfficeId equals o.Id
                         where oc.IsActive && o.IsActive
                         select (int)oc.CourtId).Distinct().ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<AdminImportError>> ListImportsWithErrorsAsync(CancellationToken ct = default)
    {
        var result = new List<AdminImportError>();
        foreach (IImportKind kind in new IImportKind[] { organika, usersImport })
        {
            result.AddRange((await imports.ListAsync(kind, ct))
                .Where(i => i.Summary.Errors > 0)
                .Select(i => new AdminImportError(i.PublicId, kind.Kind, i.FileName, i.UploadedAt, i.Summary.Errors)));
        }

        return result.OrderByDescending(i => i.UploadedAt).ToList();
    }
}

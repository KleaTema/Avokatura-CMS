using Ash.Modules.Identity.Contracts;
using Ash.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application;

public sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public async Task<UserSummary?> GetUser(Guid publicId, CancellationToken ct = default)
    {
        var rows = await Summaries().Where(x => x.User.PublicId == publicId).ToListAsync(ct);
        return rows.Select(ToSummary).SingleOrDefault();
    }

    public async Task<IReadOnlyList<UserSummary>> FindUsers(string? query, string? role, CancellationToken ct = default)
    {
        var users = Summaries();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            users = users.Where(x => x.User.FirstName.Contains(text) || x.User.LastName.Contains(text) || x.User.Email.Contains(text));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var userIds = await db.Database
                .SqlQuery<uint>($"""
                    SELECT
                    JOIN roles r ON r.id = ur.role_id WHERE r.code = {role}
                    """)
                .ToListAsync(ct);
            users = users.Where(x => userIds.Contains(x.User.Id));
        }

        var rows = await users.OrderBy(x => x.User.LastName).ThenBy(x => x.User.FirstName).Take(100).ToListAsync(ct);
        return rows.Select(ToSummary).ToList();
    }

    private IQueryable<UserWithOrg> Summaries() =>
        from u in db.Users.AsNoTracking()
        where u.IsActive
        join o in db.Offices.AsNoTracking() on u.OfficeId equals (ushort?)o.Id into offices
        from o in offices.DefaultIfEmpty()
        join i in db.Institutions.AsNoTracking() on u.InstitutionId equals (uint?)i.Id into institutions
        from i in institutions.DefaultIfEmpty()
        select new UserWithOrg(u, o, i);

    private static UserSummary ToSummary(UserWithOrg x) =>
        new(x.User.PublicId, x.User.FirstName, x.User.LastName, x.User.Email, x.Office?.PublicId, x.Institution?.PublicId);

    private sealed record UserWithOrg(UserRecord User, OfficeRecord? Office, InstitutionRecord? Institution);
}

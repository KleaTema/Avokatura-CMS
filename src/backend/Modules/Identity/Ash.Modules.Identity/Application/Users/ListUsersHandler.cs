using Ash.Modules.Cases.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Users;

public sealed record ListUsersQuery(
    string? Q, string? Role, Guid? OfficeId, Guid? InstitutionId, bool? Active, string? After, int? Limit);

/// <summary>Keyset paging by internal id (ascending); the cursor is opaque and ids never leave the server.</summary>
public sealed class ListUsersHandler(IdentityDbContext db, ICaseAssignmentCounts assignments)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<KeysetPage<UserResponse>> Handle(ListUsersQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            users = users.Where(u => u.FirstName.Contains(term) || u.LastName.Contains(term)
                                     || (u.FirstName + " " + u.LastName).Contains(term) || u.Email.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(q.Role))
        {
            var role = q.Role;
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Code == role)));
        }

        if (q.OfficeId is { } officeGuid)
        {
            users = users.Where(u => db.Offices.Any(o => o.Id == u.OfficeId && o.PublicId == officeGuid));
        }

        if (q.InstitutionId is { } institutionGuid)
        {
            users = users.Where(u => db.Institutions.Any(i => i.Id == u.InstitutionId && i.PublicId == institutionGuid));
        }

        if (q.Active is { } active)
        {
            users = users.Where(u => u.IsActive == active);
        }

        if (Cursor.TryDecode(q.After, out _, out var afterId))
        {
            var after = (uint)afterId;
            users = users.Where(u => u.Id > after);
        }

        var rows = await users.OrderBy(u => u.Id).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var ids = rows.Select(u => u.Id).ToList();
        var counts = await assignments.CountCurrentAsync(ids.Select(x => (long)x).ToList(), ct);
        var roles = await (from ur in db.UserRoles.AsNoTracking()
                           join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId)
                           orderby r.Code
                           select new { ur.UserId, r.Code }).ToListAsync(ct);
        var officeIds = rows.Where(u => u.OfficeId != null).Select(u => u.OfficeId!.Value).Distinct().ToList();
        var offices = await db.Offices.AsNoTracking().Where(o => officeIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => new NamedRef(o.PublicId, o.Name), ct);
        var institutionIds = rows.Where(u => u.InstitutionId != null).Select(u => u.InstitutionId!.Value).Distinct().ToList();
        var institutions = await db.Institutions.AsNoTracking().Where(i => institutionIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new NamedRef(i.PublicId, i.Name), ct);

        var items = rows.Select(u => new UserResponse(
            u.PublicId, u.Email, u.FirstName, u.LastName,
            u.OfficeId is { } o ? offices.GetValueOrDefault(o) : null,
            u.InstitutionId is { } i ? institutions.GetValueOrDefault(i) : null,
            roles.Where(r => r.UserId == u.Id).Select(r => r.Code).ToList(),
            u.IsActive, u.LastLoginAt, counts.GetValueOrDefault(u.Id))).ToList();

        var next = hasMore ? Cursor.Encode(string.Empty, rows[^1].Id) : null;
        return new KeysetPage<UserResponse>(items, next);
    }
}

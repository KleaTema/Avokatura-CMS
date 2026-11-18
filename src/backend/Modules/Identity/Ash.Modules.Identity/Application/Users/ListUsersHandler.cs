using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Identity.Application.Users;

public sealed record ListUsersQuery(
    string? Q, string? Role, Guid? OfficeId, Guid? InstitutionId, bool? Active, string? After, int? Limit,
    string? Position = null, DateOnly? LastLoginFrom = null, DateOnly? LastLoginTo = null)
{
    /// <summary>Task 053n (the admin dashboard's cards): true = no role at all, false = at least one.</summary>
    public bool? NoRole { get; init; }

    /// <summary>true = never signed in, false = signed in at least once.</summary>
    public bool? NeverLoggedIn { get; init; }

    /// <summary>Created on these Tirane days (users.created_at).</summary>
    public DateOnly? CreatedFrom { get; init; }

    public DateOnly? CreatedTo { get; init; }

    /// <summary>The sign-in accounts (task 051): LINKED (an account that may sign in), NONE (no account), BLOCKED (one blocked).</summary>
    public string? Login { get; init; }

    /// <summary>One user (a dashboard row "what changed"): found whatever the other filters, inactive included.</summary>
    public Guid? PublicId { get; init; }
}

/// <summary>The values of <see cref="ListUsersQuery.Login"/>.</summary>
public static class UserLoginFilter
{
    public const string Linked = "LINKED";
    public const string None = "NONE";
    public const string Blocked = "BLOCKED";
}

/// <summary>
/// Keyset paging by internal id (ascending); the cursor is opaque and ids never leave the server. ?q= matches anywhere
/// inside the first name, last name or email (ngram FULLTEXT ftn_users_search, script 014).
/// </summary>
public sealed class ListUsersHandler(IdentityDbContext db, ParallelReads parallel, ICaseAssignmentCounts assignments)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<KeysetPage<UserResponse>> Handle(ListUsersQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);

        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<IdentityDbContext, int>((other, token) => Filtered(other, q).CountAsync(token), ct);
        var users = Filtered(db, q);
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
            u.IsActive, u.LastLoginAt, counts.GetValueOrDefault(u.Id), u.Position)).ToList();

        var next = hasMore ? Cursor.Encode(string.Empty, rows[^1].Id) : null;
        return new KeysetPage<UserResponse>(items, next, await total);
    }

    /// <summary>
    /// How many users the list shows for these filters (task 053n): the admin dashboard's cards count with this, so a
    /// card and the list it opens show the same number.
    /// </summary>
    public Task<int> CountAsync(ListUsersQuery q, CancellationToken ct) => Filtered(db, q).CountAsync(ct);

    private static IQueryable<UserRecord> Filtered(IdentityDbContext db, ListUsersQuery q)
    {
        var users = db.Users.AsNoTracking();

        if (q.PublicId is { } publicId)
        {
            users = users.Where(u => u.PublicId == publicId);
        }

        if (q.NoRole is { } noRole)
        {
            users = noRole
                ? users.Where(u => !db.UserRoles.Any(ur => ur.UserId == u.Id))
                : users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id));
        }

        if (q.NeverLoggedIn is { } never)
        {
            users = never ? users.Where(u => u.LastLoginAt == null) : users.Where(u => u.LastLoginAt != null);
        }

        if (q.CreatedFrom is { } createdFrom)
        {
            var start = Ash.SharedKernel.Time.TiraneTime.StartOfDayUtc(createdFrom);
            users = users.Where(u => u.CreatedAt >= start);
        }

        if (q.CreatedTo is { } createdTo)
        {
            var end = Ash.SharedKernel.Time.TiraneTime.StartOfDayUtc(createdTo.AddDays(1));
            users = users.Where(u => u.CreatedAt < end);
        }

        switch (q.Login)
        {
            case UserLoginFilter.Linked:
                users = users.Where(u => db.UserIdentities.Any(i => i.UserId == u.Id && i.BlockedAt == null));
                break;
            case UserLoginFilter.None:
                users = users.Where(u => !db.UserIdentities.Any(i => i.UserId == u.Id));
                break;
            case UserLoginFilter.Blocked:
                users = users.Where(u => db.UserIdentities.Any(i => i.UserId == u.Id && i.BlockedAt != null));
                break;
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var words = NgramSearch.BooleanQuery(q.Q.Trim());
            var matches = db.Database.SqlQueryRaw<uint>(
                "SELECT id AS Value FROM users WHERE MATCH(first_name, last_name, email) AGAINST (@users_q IN BOOLEAN MODE)",
                new MySqlParameter("@users_q", words));
            users = words.Length == 0 ? users.Where(_ => false) : users.Where(u => matches.Contains(u.Id));
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

        // task 053m: the other columns (a small table: plain predicates)
        if (!string.IsNullOrWhiteSpace(q.Position))
        {
            var position = q.Position.Trim();
            users = users.Where(u => u.Position != null && u.Position.Contains(position));
        }

        if (q.LastLoginFrom is { } from)
        {
            var start = Ash.SharedKernel.Time.TiraneTime.StartOfDayUtc(from);
            users = users.Where(u => u.LastLoginAt >= start);
        }

        if (q.LastLoginTo is { } to)
        {
            var end = Ash.SharedKernel.Time.TiraneTime.StartOfDayUtc(to.AddDays(1));
            users = users.Where(u => u.LastLoginAt < end);
        }

        return users;
    }
}

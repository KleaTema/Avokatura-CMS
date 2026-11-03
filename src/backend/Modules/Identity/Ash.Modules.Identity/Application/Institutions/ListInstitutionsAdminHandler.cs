using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Identity.Application.Institutions;

/// <summary>
/// Keyset paging by id (ascending), no OFFSET; inactive institutions included unless ?active filters them. ?q= matches
/// anywhere inside the name, short name or NIPT (ngram FULLTEXT ftn_institutions_search, script 014).
/// </summary>
public sealed class ListInstitutionsAdminHandler(IdentityDbContext db, ParallelReads parallel)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 50;

    public async Task<KeysetPage<InstitutionAdminResponse>> Handle(ListInstitutionsAdminQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);

        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<IdentityDbContext, int>((other, token) => Filtered(other, q).CountAsync(token), ct);
        var institutions = Filtered(db, q);
        if (Cursor.TryDecode(q.After, out _, out var afterId))
        {
            var after = (uint)afterId;
            institutions = institutions.Where(i => i.Id > after);
        }

        var rows = await institutions.OrderBy(i => i.Id).Take(limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var parentIds = rows.Where(i => i.ParentInstitutionId != null).Select(i => i.ParentInstitutionId!.Value).Distinct().ToList();
        var parents = await db.Institutions.AsNoTracking().Where(i => parentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new NamedRef(i.PublicId, i.Name), ct);
        var ids = rows.Select(i => (uint?)i.Id).ToList();
        var activeUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && ids.Contains(u.InstitutionId))
            .GroupBy(u => u.InstitutionId!.Value)
            .Select(g => new { InstitutionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.InstitutionId, x => x.Count, ct);

        var items = rows.Select(i => new InstitutionAdminResponse(
            i.PublicId, i.Name, i.ShortName, i.InstitutionType,
            i.ParentInstitutionId is { } p ? parents.GetValueOrDefault(p) : null,
            i.Nipt, i.IsActive, activeUsers.GetValueOrDefault(i.Id))).ToList();

        var next = hasMore ? Cursor.Encode(string.Empty, rows[^1].Id) : null;
        return new KeysetPage<InstitutionAdminResponse>(items, next, await total);
    }

    /// <summary>Unknown type or parent: nothing matches (a filter value from an old link, not a 400).</summary>
    private static IQueryable<InstitutionRecord> Filtered(IdentityDbContext db, ListInstitutionsAdminQuery q)
    {
        var institutions = db.Institutions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var words = NgramSearch.BooleanQuery(q.Q.Trim());
            var matches = db.Database.SqlQueryRaw<uint>(
                "SELECT id AS Value FROM institutions WHERE MATCH(name, short_name, nipt) AGAINST (@institutions_q IN BOOLEAN MODE)",
                new MySqlParameter("@institutions_q", words));
            institutions = words.Length == 0 ? institutions.Where(_ => false) : institutions.Where(i => matches.Contains(i.Id));
        }

        if (!string.IsNullOrWhiteSpace(q.Type))
        {
            var type = q.Type.Trim().ToUpperInvariant();
            institutions = institutions.Where(i => i.InstitutionType == type);
        }

        if (q.Active is { } active)
        {
            institutions = institutions.Where(i => i.IsActive == active);
        }

        if (q.ParentId is { } parentId)
        {
            institutions = institutions.Where(i => db.Institutions.Any(p => p.Id == i.ParentInstitutionId && p.PublicId == parentId));
        }

        return institutions;
    }
}

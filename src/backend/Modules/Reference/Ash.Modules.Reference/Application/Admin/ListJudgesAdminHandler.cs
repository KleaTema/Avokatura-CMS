using Ash.Infrastructure.Persistence;
using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Reference.Application.Admin;

/// <summary>
/// Keyset paging by id (ascending), no OFFSET; inactive judges are included unless ?active filters them. ?q= matches
/// anywhere inside the first or last name (ngram FULLTEXT ftn_judges_name, script 014); ?courtId= the judge's court.
/// </summary>
public sealed class ListJudgesAdminHandler(ReferenceDbContext db, ParallelReads parallel)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<KeysetPage<JudgeAdminResponse>> Handle(ListJudgesAdminQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);

        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<ReferenceDbContext, int>((other, token) => Filtered(other, q).CountAsync(token), ct);
        var judges = Filtered(db, q);
        if (Cursor.TryDecode(q.After, out _, out var afterId))
        {
            var after = (uint)afterId;
            judges = judges.Where(x => x.Id > after);
        }

        var rows = await (from j in judges
                          join c in db.Courts.AsNoTracking() on j.CourtId equals (ushort?)c.Id into courts
                          from c in courts.DefaultIfEmpty()
                          orderby j.Id
                          select new JudgeAdminResponse(
                              j.Id, j.FirstName, j.LastName, j.CourtId, c != null ? c.Name : null, j.ExternalRef, j.IsActive))
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToList();
        var next = hasMore ? Cursor.Encode(string.Empty, items[^1].Id) : null;
        return new KeysetPage<JudgeAdminResponse>(items, next, await total);
    }

    private static IQueryable<JudgeRecord> Filtered(ReferenceDbContext db, ListJudgesAdminQuery q)
    {
        var judges = db.Judges.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var words = NgramSearch.BooleanQuery(q.Q.Trim());
            var matches = db.Database.SqlQueryRaw<uint>(
                "SELECT id AS Value FROM judges WHERE MATCH(first_name, last_name) AGAINST (@judges_q IN BOOLEAN MODE)",
                new MySqlParameter("@judges_q", words));
            judges = words.Length == 0 ? judges.Where(_ => false) : judges.Where(x => matches.Contains(x.Id));
        }

        if (q.Active is { } active)
        {
            judges = judges.Where(x => x.IsActive == active);
        }

        if (q.CourtId is { } courtId)
        {
            // ix_judges_court (court_id, is_active)
            judges = judges.Where(x => x.CourtId == courtId);
        }

        return judges;
    }
}

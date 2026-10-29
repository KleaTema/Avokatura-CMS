using Ash.Modules.Reference.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Reference.Application.Admin;

/// <summary>Keyset paging by id (ascending), no OFFSET; inactive judges are included unless ?active filters them.</summary>
public sealed class ListJudgesAdminHandler(ReferenceDbContext db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public async Task<KeysetPage<JudgeAdminResponse>> Handle(ListJudgesAdminQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);
        var judges = db.Judges.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            judges = judges.Where(x => x.FirstName.Contains(term) || x.LastName.Contains(term)
                                       || (x.FirstName + " " + x.LastName).Contains(term));
        }

        if (q.Active is { } active)
        {
            judges = judges.Where(x => x.IsActive == active);
        }

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
        return new KeysetPage<JudgeAdminResponse>(items, next);
    }
}

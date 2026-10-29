using Ash.Modules.Identity.Application.GetMe;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

/// <summary>Keyset paging by id (ascending), no OFFSET; inactive institutions included. ?q= matches name, short name or NIPT.</summary>
public sealed class ListInstitutionsAdminHandler(IdentityDbContext db)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 50;

    public async Task<KeysetPage<InstitutionAdminResponse>> Handle(ListInstitutionsAdminQuery q, CancellationToken ct)
    {
        var limit = Math.Clamp(q.Limit ?? DefaultLimit, 1, MaxLimit);
        var institutions = db.Institutions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var pattern = InstitutionAdminSupport.LikeContains(q.Q.Trim());
            institutions = institutions.Where(i => EF.Functions.Like(i.Name, pattern, InstitutionAdminSupport.LikeEscape)
                                                   || EF.Functions.Like(i.ShortName!, pattern, InstitutionAdminSupport.LikeEscape)
                                                   || EF.Functions.Like(i.Nipt!, pattern, InstitutionAdminSupport.LikeEscape));
        }

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
        return new KeysetPage<InstitutionAdminResponse>(items, next);
    }
}

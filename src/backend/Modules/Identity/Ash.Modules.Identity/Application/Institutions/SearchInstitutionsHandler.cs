using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Identity.Application.Institutions;

/// <summary>Public institution data only: no NIPT, no internal ids, no user or case counts.</summary>
public sealed record InstitutionSearchResult(
    Guid PublicId, string Name, string? ShortName, string InstitutionType, string? ParentName);

/// <summary>
/// Search used when picking a party. LIKE (not FULLTEXT, which misses word prefixes such as 'financ') on
/// name and short name; their utf8mb4_0900_ai_ci collation makes it case- and accent-insensitive.
/// </summary>
public sealed class SearchInstitutionsHandler(IdentityDbContext db)
{
    public const int MaxLimit = 20;
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;

    public async Task<Result<IReadOnlyList<InstitutionSearchResult>>> Handle(string? q, int? limit, CancellationToken ct)
    {
        var text = q?.Trim() ?? string.Empty;
        if (text.Length is < MinQueryLength or > MaxQueryLength)
        {
            return Error.Validation("institution.search_query_invalid",
                "Shkruani nga 2 deri në 100 karaktere për të kërkuar institucionin.");
        }

        var take = Math.Clamp(limit ?? MaxLimit, 1, MaxLimit);
        var pattern = InstitutionAdminSupport.LikeContains(text);

        var rows = await (from i in db.Institutions.AsNoTracking()
                          where i.IsActive
                                && (EF.Functions.Like(i.Name, pattern, InstitutionAdminSupport.LikeEscape) || EF.Functions.Like(i.ShortName!, pattern, InstitutionAdminSupport.LikeEscape))
                          join p in db.Institutions.AsNoTracking() on i.ParentInstitutionId equals (uint?)p.Id into parents
                          from p in parents.DefaultIfEmpty()
                          orderby i.Name
                          select new InstitutionSearchResult(i.PublicId, i.Name, i.ShortName, i.InstitutionType, p != null ? p.Name : null))
            .Take(take)
            .ToListAsync(ct);
        return rows;
    }
}

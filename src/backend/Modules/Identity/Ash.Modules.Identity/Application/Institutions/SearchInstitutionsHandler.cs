using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Identity.Application.Institutions;

/// <summary>Public institution data only: no NIPT, no internal ids, no user or case counts.</summary>
public sealed record InstitutionSearchResult(
    Guid PublicId, string Name, string? ShortName, string InstitutionType, string? ParentName);

/// <summary>
/// Search used when picking a party: anywhere inside the name, short name or NIPT ('financ', 'inan' find
/// 'Financave'), ngram FULLTEXT ftn_institutions_search (script 014); the utf8mb4_0900_ai_ci collation makes it case-
/// and accent-insensitive.
/// </summary>
public sealed class SearchInstitutionsHandler(IdentityDbContext db)
{
    public const int MaxLimit = 20;
    public const int MinQueryLength = TextSearch.MinTermLength;
    public const int MaxQueryLength = TextSearch.MaxLength;

    public async Task<Result<IReadOnlyList<InstitutionSearchResult>>> Handle(string? q, int? limit, CancellationToken ct)
    {
        var search = TextSearch.Parse(q);
        if (search.Text.Length > MaxQueryLength || search.IsEmpty)
        {
            return Error.Validation("institution.search_query_invalid",
                "Shkruani nga 2 deri në 100 karaktere për të kërkuar institucionin.");
        }

        var take = Math.Clamp(limit ?? MaxLimit, 1, MaxLimit);
        // live search (TextSearch): every term a substring, from 2 characters
        // fixed text (column and parameter names); the values are parameters
        var sql = $"SELECT id AS Value FROM institutions WHERE {search.Predicate("institution", "name", "short_name", "nipt")}";
        var matches = db.Database.SqlQueryRaw<uint>(sql, search.Parameters("institution").ToArray<object>());
        var rows = await (from i in db.Institutions.AsNoTracking()
                          where i.IsActive && matches.Contains(i.Id)
                          join p in db.Institutions.AsNoTracking() on i.ParentInstitutionId equals (uint?)p.Id into parents
                          from p in parents.DefaultIfEmpty()
                          orderby i.Name
                          select new InstitutionSearchResult(i.PublicId, i.Name, i.ShortName, i.InstitutionType, p != null ? p.Name : null))
            .Take(take)
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>
    /// Every ACTIVE institution, by name (a bounded list: the pickers load it once, cache it and filter in the browser,
    /// from the 2nd letter — task 054a). Public data only, as the search.
    /// </summary>
    public async Task<IReadOnlyList<InstitutionSearchResult>> ListActiveAsync(CancellationToken ct) =>
        await (from i in db.Institutions.AsNoTracking()
               where i.IsActive
               join p in db.Institutions.AsNoTracking() on i.ParentInstitutionId equals (uint?)p.Id into parents
               from p in parents.DefaultIfEmpty()
               orderby i.Name
               select new InstitutionSearchResult(i.PublicId, i.Name, i.ShortName, i.InstitutionType, p != null ? p.Name : null))
            .ToListAsync(ct);
}

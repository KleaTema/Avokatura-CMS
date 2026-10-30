using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Paging;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.ListCases;

public enum CaseListSort
{
    /// <summary>Open cases: next_hearing_at ASC, cases without a hearing last, then id.</summary>
    NextHearing,

    /// <summary>Registered tab: registered_at DESC, id DESC.</summary>
    RegisteredDesc,

    /// <summary>Closed cases: closed_at DESC, id DESC.</summary>
    ClosedDesc
}

/// <summary>A parsed, validated list request. Status null = open cases (REGISTERED + ACTIVE).</summary>
public sealed record CaseListFilter(
    CaseListTab Tab,
    string? Status,
    ushort? CourtId,
    byte? CategoryId,
    string? Priority,
    bool? Suspended,
    uint? LawyerUserId,
    DateTime? HearingFromUtc,
    DateTime? HearingToUtc,
    string? Q)
{
    /// <summary>The tab's open cases without filters (tab counts, default tab).</summary>
    public static CaseListFilter Open(CaseListTab tab) => new(tab, null, null, null, null, null, null, null, null, null);

    public CaseListSort Sort => Status == Case.Closed
        ? CaseListSort.ClosedDesc
        : Tab == CaseListTab.Registered ? CaseListSort.RegisteredDesc : CaseListSort.NextHearing;

    /// <summary>Binds a cursor to the request that produced it: same tab, filters and sort, or the cursor is refused.</summary>
    public string Hash()
    {
        var text = string.Join('|', CaseListTabs.Code(Tab), Status ?? "open", CourtId, CategoryId, Priority, Suspended,
            LawyerUserId, HearingFromUtc?.Ticks, HearingToUtc?.Ticks, Q, Sort);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}

/// <summary>Position after the last row of a page: the sort key (null = "no hearing") and the id.</summary>
public sealed record CaseListCursor(DateTime? SortKey, ulong Id)
{
    private const string NullMarker = "null";

    public string Encode(CaseListFilter filter) =>
        Cursor.Encode($"{filter.Hash()}~{(SortKey is { } key ? key.Ticks.ToString() : NullMarker)}", (long)Id);

    /// <summary>Null when the cursor is malformed or was made for another tab, filter set or sort.</summary>
    public static CaseListCursor? Decode(string cursor, CaseListFilter filter)
    {
        if (!Cursor.TryDecode(cursor, out var sortKey, out var id) || id <= 0)
        {
            return null;
        }

        var parts = sortKey.Split('~');
        if (parts.Length != 2 || parts[0] != filter.Hash())
        {
            return null;
        }

        if (parts[1] == NullMarker)
        {
            return new CaseListCursor(null, (ulong)id);
        }

        return long.TryParse(parts[1], out var ticks) && ticks > 0
            ? new CaseListCursor(new DateTime(ticks, DateTimeKind.Utc), (ulong)id)
            : null;
    }
}

/// <summary>
/// Builds the case-list query: the tab's own index-friendly predicate (no OR across access paths), the filters,
/// then the access scope ON TOP (defence in depth), then keyset order. Public so tests can EXPLAIN the real SQL.
/// </summary>
public sealed partial class CaseListQueryBuilder(CasesDbContext db, CaseAccessService access, ICurrentUser user)
{
    private static readonly string[] OpenStatuses = [Case.Registered, "ACTIVE"];

    /// <summary>Tab + filters + scope, without order or paging (also used for the tab counts).</summary>
    public IQueryable<Case> Filtered(CaseListFilter f)
    {
        var cases = TabPredicate(db.Cases.AsNoTracking(), f.Tab);

        cases = f.Status is null
            ? cases.Where(c => OpenStatuses.Contains(c.LifecycleStatus))
            : cases.Where(c => c.LifecycleStatus == f.Status);

        if (f.CourtId is { } courtId)
        {
            cases = cases.Where(c => c.CourtId == courtId);
        }

        if (f.CategoryId is { } categoryId)
        {
            cases = cases.Where(c => c.CategoryId == categoryId);
        }

        if (f.Priority is { } priority)
        {
            cases = cases.Where(c => c.Priority == priority);
        }

        if (f.Suspended is { } suspended)
        {
            cases = cases.Where(c => c.IsSuspended == suspended);
        }

        if (f.LawyerUserId is { } lawyer)
        {
            cases = cases.Where(c => db.CaseAssignments.Any(a => a.CaseId == c.Id && a.UserId == lawyer && a.EndedAt == null
                && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == "SECONDARY_LAWYER")));
        }

        if (f.HearingFromUtc is { } from)
        {
            cases = cases.Where(c => c.NextHearingAt >= from);
        }

        if (f.HearingToUtc is { } to)
        {
            cases = cases.Where(c => c.NextHearingAt < to);
        }

        if (f.Q is { } q)
        {
            var matches = SearchIds(q);
            cases = cases.Where(c => matches.Contains(c.Id));
        }

        return access.ApplyScope(cases);
    }

    /// <summary>One page in keyset order: <paramref name="take"/> rows after <paramref name="after"/>.</summary>
    public IQueryable<Case> Page(CaseListFilter f, CaseListCursor? after, int take)
    {
        var cases = Filtered(f);
        switch (f.Sort)
        {
            case CaseListSort.ClosedDesc:
                if (after is { SortKey: { } closed })
                {
                    var id = after.Id;
                    cases = cases.Where(c => c.ClosedAt < closed || (c.ClosedAt == closed && c.Id < id));
                }

                return cases.OrderByDescending(c => c.ClosedAt).ThenByDescending(c => c.Id).Take(take);

            case CaseListSort.RegisteredDesc:
                if (after is { SortKey: { } registered })
                {
                    var id = after.Id;
                    cases = cases.Where(c => c.RegisteredAt < registered || (c.RegisteredAt == registered && c.Id < id));
                }

                return cases.OrderByDescending(c => c.RegisteredAt).ThenByDescending(c => c.Id).Take(take);

            default:
                if (after is not null)
                {
                    var id = after.Id;
                    if (after.SortKey is { } hearing)
                    {
                        // after a dated row: later dates, the same date with a higher id, then every undated row
                        cases = cases.Where(c => c.NextHearingAt == null || c.NextHearingAt > hearing
                                                 || (c.NextHearingAt == hearing && c.Id > id));
                    }
                    else
                    {
                        cases = cases.Where(c => c.NextHearingAt == null && c.Id > id);
                    }
                }

                return cases.OrderBy(c => c.NextHearingAt == null).ThenBy(c => c.NextHearingAt).ThenBy(c => c.Id).Take(take);
        }
    }

    public static CaseListCursor CursorAfter(Case last, CaseListSort sort) => sort switch
    {
        CaseListSort.ClosedDesc => new CaseListCursor(last.ClosedAt, last.Id),
        CaseListSort.RegisteredDesc => new CaseListCursor(last.RegisteredAt, last.Id),
        _ => new CaseListCursor(last.NextHearingAt, last.Id)
    };

    private IQueryable<Case> TabPredicate(IQueryable<Case> cases, CaseListTab tab)
    {
        var userId = (uint)user.UserId;
        var officeId = (ushort)(user.OfficeId ?? 0);
        var institutionId = (uint)(user.InstitutionId ?? 0);
        return tab switch
        {
            // ix_ca_user_current (user_id, ended_at, case_id)
            CaseListTab.Mine => cases.Where(c => db.CaseAssignments.Any(a => a.UserId == userId && a.EndedAt == null && a.CaseId == c.Id)),
            // ix_cases_office_status (office_id, lifecycle_status, registered_at); registered cases are in the registrar's office
            CaseListTab.Registered => cases.Where(c => c.OfficeId == officeId && c.LifecycleStatus == Case.Registered && c.RegisteredBy == userId),
            CaseListTab.Unassigned => cases.Where(c => c.OfficeId == officeId && c.LifecycleStatus == Case.Registered),
            CaseListTab.Office => cases.Where(c => c.OfficeId == officeId),
            // ix_ci_institution (institution_id, ended_at, case_id)
            CaseListTab.Institution => cases.Where(c => db.CaseInstitutions.Any(i => i.InstitutionId == institutionId && i.EndedAt == null && i.CaseId == c.Id)),
            _ => cases
        };
    }

    /// <summary>
    /// Case ids matching q, as separate index-friendly queries joined by UNION (composed into the list query as a
    /// subquery, never materialized): unique code / case code prefix (when q looks like a code), act number prefix,
    /// FULLTEXT on the object and on party names (every word as a prefix).
    /// </summary>
    private IQueryable<ulong> SearchIds(string q)
    {
        var prefix = q.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%";
        var words = BooleanPrefixWords(q);
        var parts = new List<string>();
        if (CodeRegex().IsMatch(q))
        {
            parts.Add("SELECT c.id AS Value FROM cases c JOIN dossiers d ON d.id = c.dossier_id WHERE d.unique_code LIKE @list_prefix ESCAPE '!'");
            parts.Add("SELECT id AS Value FROM cases WHERE case_code LIKE @list_prefix ESCAPE '!'");
        }

        parts.Add("SELECT id AS Value FROM cases WHERE act_number LIKE @list_prefix ESCAPE '!'");
        if (words.Length > 0)
        {
            parts.Add("SELECT id AS Value FROM cases WHERE MATCH(object_text) AGAINST (@list_words IN BOOLEAN MODE)");
            parts.Add("SELECT case_id AS Value FROM case_parties WHERE deleted_at IS NULL AND MATCH(display_name) AGAINST (@list_words IN BOOLEAN MODE)");
        }

        return db.Database.SqlQueryRaw<ulong>(string.Join(" UNION ", parts),
            new MySqlParameter("@list_prefix", prefix), new MySqlParameter("@list_words", words));
    }

    /// <summary>"+word1* +word2*": every word required, as a prefix; only letters and digits (no boolean operators).</summary>
    private static string BooleanPrefixWords(string q)
    {
        var words = WordRegex().Matches(q).Select(m => m.Value).Where(w => w.Length >= 2);
        return string.Join(' ', words.Select(w => $"+{w}*"));
    }

    [GeneratedRegex(@"^\d{4}-\d")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();
}

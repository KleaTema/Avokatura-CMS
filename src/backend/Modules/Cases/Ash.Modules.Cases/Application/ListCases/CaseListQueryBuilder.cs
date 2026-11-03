using System.Security.Cryptography;
using System.Text;
using Ash.Infrastructure.Persistence;
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

/// <summary>
/// A parsed, validated list request. Status: <see cref="OpenStatus"/> (REGISTERED + ACTIVE), <see cref="AllStatuses"/>
/// or one lifecycle status; null counts as open.
/// </summary>
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
    string? Q,
    ushort? OfficeId = null)
{
    public const string OpenStatus = "OPEN";
    public const string AllStatuses = "ALL";

    /// <summary>The courts of the chosen court level (Shkalla e gjykimit); empty = the level has no court.</summary>
    public IReadOnlyList<ushort>? LevelCourtIds { get; init; }

    public uint? JudgeId { get; init; }

    /// <summary>An institution that is a (not deleted) party of the case.</summary>
    public uint? InstitutionId { get; init; }

    public DateTime? RegisteredFromUtc { get; init; }

    public DateTime? RegisteredToUtc { get; init; }

    /// <summary>The tab's open cases without filters (tab counts, default tab).</summary>
    public static CaseListFilter Open(CaseListTab tab) => new(tab, OpenStatus, null, null, null, null, null, null, null, null);

    public CaseListSort Sort => Status == Case.Closed
        ? CaseListSort.ClosedDesc
        : Tab == CaseListTab.Registered ? CaseListSort.RegisteredDesc : CaseListSort.NextHearing;

    /// <summary>Binds a cursor to the request that produced it: same tab, filters and sort, or the cursor is refused.</summary>
    public string Hash()
    {
        var text = string.Join('|', CaseListTabs.Code(Tab), Status ?? OpenStatus, CourtId, CategoryId, Priority, Suspended,
            LawyerUserId, HearingFromUtc?.Ticks, HearingToUtc?.Ticks, Q, Sort, OfficeId,
            LevelCourtIds is null ? null : string.Join(',', LevelCourtIds), JudgeId, InstitutionId,
            RegisteredFromUtc?.Ticks, RegisteredToUtc?.Ticks);
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
public sealed class CaseListQueryBuilder(CasesDbContext db, CaseAccessService access, ICurrentUser user)
{
    private static readonly string[] OpenStatuses = [Case.Registered, "ACTIVE"];

    /// <summary>Tab + filters + scope, without order or paging (also used for the tab counts).</summary>
    public IQueryable<Case> Filtered(CaseListFilter f)
    {
        var cases = TabPredicate(db.Cases.AsNoTracking(), f.Tab);

        cases = f.Status switch
        {
            null or CaseListFilter.OpenStatus => cases.Where(c => OpenStatuses.Contains(c.LifecycleStatus)),
            CaseListFilter.AllStatuses => cases,
            var status => cases.Where(c => c.LifecycleStatus == status)
        };

        if (f.CourtId is { } courtId)
        {
            cases = cases.Where(c => c.CourtId == courtId);
        }

        if (f.LevelCourtIds is { } levelCourts)
        {
            // ix_cases_court (court_id, lifecycle_status): an IN list of the level's courts
            cases = cases.Where(c => levelCourts.Contains(c.CourtId));
        }

        if (f.JudgeId is { } judgeId)
        {
            // ix_cases_judge (judge_id)
            cases = cases.Where(c => c.JudgeId == judgeId);
        }

        if (f.InstitutionId is { } institutionId)
        {
            // ix_cp_institution (institution_id): a semi-join on the parties
            cases = cases.Where(c => db.CaseParties.Any(p => p.CaseId == c.Id && p.InstitutionId == institutionId && p.DeletedAt == null));
        }

        if (f.RegisteredFromUtc is { } registeredFrom)
        {
            cases = cases.Where(c => c.RegisteredAt >= registeredFrom);
        }

        if (f.RegisteredToUtc is { } registeredTo)
        {
            cases = cases.Where(c => c.RegisteredAt < registeredTo);
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
                && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == CaseAssignmentRow.SecondaryLawyer)));
        }

        if (f.OfficeId is { } office)
        {
            // narrows only (the scope below still applies): the Monitor's office filter of the "all" tab
            cases = cases.Where(c => c.OfficeId == office);
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
    /// Case ids matching q anywhere inside a value (substring, script 014): separate index-friendly MATCH queries over
    /// the ngram FULLTEXT indexes, joined by UNION and composed into the list query as a subquery (never
    /// materialized) — case code / act number / summons number (ftn_cases_numbers), the object (ftn_cases_object),
    /// party names and NIPT (ftn_cp_name_identifier). Every word of q must be in the same source.
    /// </summary>
    private IQueryable<ulong> SearchIds(string q)
    {
        var words = NgramSearch.BooleanQuery(q);
        if (words.Length == 0)
        {
            return db.Database.SqlQueryRaw<ulong>("SELECT id AS Value FROM cases WHERE FALSE");
        }

        const string sql = """
            SELECT id AS Value FROM cases WHERE MATCH(case_code, act_number, summons_number) AGAINST (@list_words IN BOOLEAN MODE)
            UNION SELECT id AS Value FROM cases WHERE MATCH(object_text) AGAINST (@list_words IN BOOLEAN MODE)
            UNION SELECT case_id AS Value FROM case_parties WHERE deleted_at IS NULL AND MATCH(display_name, identifier) AGAINST (@list_words IN BOOLEAN MODE)
            """;
        return db.Database.SqlQueryRaw<ulong>(sql, new MySqlParameter("@list_words", words));
    }
}

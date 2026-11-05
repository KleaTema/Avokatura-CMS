using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Application.Search;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>
/// Query string of GET /api/cases. Status: OPEN (REGISTERED + ACTIVE), ALL, REGISTERED, ACTIVE or CLOSED; without
/// one, OPEN — or ALL as soon as q is given (a search finds closed cases too). hearingFrom/hearingTo and
/// registeredFrom/registeredTo are Tirane calendar dates; lawyerId a user, officeId an office, institutionId an
/// institution that is a party (public ids); courtLevel a court level code; judgeId a judge id. filter: one of the
/// dashboard's named predicates (task 046, <see cref="CaseListSpecial"/>): missingData30, missingData6m,
/// hearingOverdue, hearingThisWeek, decisionPending (status CLOSED unless given), noJurist, delegated (institution users).
/// </summary>
public sealed record ListCasesQuery(
    string? Tab,
    string? Status,
    int? CourtId,
    string? CategoryCode,
    string? Priority,
    bool? Suspended,
    DateOnly? HearingFrom,
    DateOnly? HearingTo,
    Guid? LawyerId,
    string? Q,
    string? After,
    int? Limit,
    Guid? OfficeId = null,
    string? CourtLevel = null,
    long? JudgeId = null,
    DateOnly? RegisteredFrom = null,
    DateOnly? RegisteredTo = null,
    Guid? InstitutionId = null,
    string? Filter = null);

/// <summary>A row of the case list. Public ids only; names from Reference (court) and Identity (lawyer).</summary>
public sealed record CaseListItem(
    Guid PublicId,
    string CaseCode,
    string ActNumber,
    string CourtName,
    string? PrimaryLawyerName,
    string Priority,
    string Status,
    DateTime? NextHearingAt,
    bool IsSuspended,
    DateTime RegisteredAt,
    DateTime? ClosedAt,
    /// <summary>The next hearing's time has passed and it was not completed yet (task 030): the UI marks it.</summary>
    bool NextHearingIsOverdue);

/// <summary>
/// The case list (task 019): one endpoint, a tab per kind of user, keyset paging. A tab the user may not use → 403;
/// a cursor made with other filters → 400. Lists are not audited.
/// </summary>
internal sealed class ListCasesHandler(
    CasesDbContext db,
    CaseListQueryBuilder builder,
    ParallelReads parallel,
    IReferenceData reference,
    IUserDirectory users,
    IOfficeDirectory offices,
    IInstitutionDirectory institutions,
    CaseListSpecialFactory specials,
    CaseSearch caseSearch,
    ICurrentUser user,
    IClock clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    private static readonly string[] Statuses =
        [CaseListFilter.OpenStatus, CaseListFilter.AllStatuses, Case.Registered, Case.Active, Case.Closed];

    public async Task<Result<KeysetPage<CaseListItem>>> Handle(ListCasesQuery query, CancellationToken ct)
    {
        var tabs = CaseListTabs.For(user);
        CaseListTab tab;
        if (query.Tab is null)
        {
            var unassigned = tabs.Contains(CaseListTab.Unassigned)
                ? await builder.Filtered(CaseListFilter.Open(CaseListTab.Unassigned)).CountAsync(ct)
                : 0;
            if (CaseListTabs.Default(tabs, unassigned) is not { } defaultTab)
            {
                return Forbidden();
            }

            tab = defaultTab;
        }
        else if (!CaseListTabs.TryParse(query.Tab, out tab))
        {
            return FieldError("tab", "Skeda nuk ekziston.");
        }
        else if (!tabs.Contains(tab))
        {
            return Forbidden();
        }

        var errors = new FieldErrors();
        var filter = await ParseFilterAsync(tab, query, errors, ct);
        CaseListCursor? after = null;
        if (filter is not null && query.After is { } cursor
            && (after = CaseListCursor.Decode(cursor, filter)) is null)
        {
            errors.Add("after", "Kursori nuk vlen për këtë kërkim; filloni nga faqja e parë.");
        }

        if (errors.Any || filter is null)
        {
            return errors.ToError("Kërkimi nuk është i vlefshëm.");
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        // the total (same filters, no cursor) on its own connection, beside the page query
        var total = parallel.RunAsync<CaseListQueryBuilder, int>((b, token) => b.Filtered(filter).CountAsync(token), ct);
        var rows = await builder.Page(filter, after, limit + 1).ToListAsync(ct);
        var hasMore = rows.Count > limit;
        rows = rows.Take(limit).ToList();

        var lawyers = await PrimaryLawyerNamesAsync(rows, ct);
        var items = new List<CaseListItem>(rows.Count);
        foreach (var c in rows)
        {
            var court = await reference.GetCourt(c.CourtId, ct); // cached lookup
            items.Add(new CaseListItem(c.PublicId, c.CaseCode, c.ActNumber, court?.Name ?? string.Empty,
                lawyers.GetValueOrDefault(c.Id), c.Priority, c.LifecycleStatus, c.NextHearingAt, c.IsSuspended,
                c.RegisteredAt, c.ClosedAt, c.NextHearingAt < clock.UtcNow));
        }

        var next = hasMore ? CaseListQueryBuilder.CursorAfter(rows[^1], filter.Sort).Encode(filter) : null;
        return new KeysetPage<CaseListItem>(items, next, await total);
    }

    private async Task<CaseListFilter?> ParseFilterAsync(CaseListTab tab, ListCasesQuery q, FieldErrors errors, CancellationToken ct)
    {
        string? status = null;
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            status = q.Status.Trim().ToUpperInvariant();
            if (!Statuses.Contains(status))
            {
                errors.Add("status", "Statusi duhet të jetë OPEN, ALL, REGISTERED, ACTIVE ose CLOSED.");
            }
        }

        string? priority = null;
        if (!string.IsNullOrWhiteSpace(q.Priority))
        {
            priority = q.Priority.Trim().ToUpperInvariant();
            if (!Case.Priorities.Contains(priority))
            {
                errors.Add("priority", "Prioriteti duhet të jetë NONE, LOW, MEDIUM ose HIGH.");
            }
        }

        ushort? courtId = null;
        if (q.CourtId is { } court)
        {
            if (court is <= 0 or > ushort.MaxValue)
            {
                errors.Add("courtId", "Gjykata nuk ekziston.");
            }
            else
            {
                courtId = (ushort)court;
            }
        }

        byte? categoryId = null;
        if (!string.IsNullOrWhiteSpace(q.CategoryCode))
        {
            var category = await reference.GetCaseCategory(q.CategoryCode.Trim(), ct);
            if (category is null)
            {
                errors.Add("categoryCode", "Kategoria nuk ekziston.");
            }
            else
            {
                categoryId = category.Id;
            }
        }

        uint? lawyerUserId = null;
        if (q.LawyerId is { } lawyerPublicId)
        {
            var ids = await users.GetIdsAsync([lawyerPublicId], ct);
            if (ids.TryGetValue(lawyerPublicId, out var id))
            {
                lawyerUserId = (uint)id;
            }
            else
            {
                errors.Add("lawyerId", "Avokati nuk u gjet.");
            }
        }

        ushort? officeId = null;
        if (q.OfficeId is { } officePublicId)
        {
            if (await offices.GetIdAsync(officePublicId, ct) is { } id)
            {
                officeId = (ushort)id;
            }
            else
            {
                errors.Add("officeId", "Zyra nuk u gjet.");
            }
        }

        IReadOnlyList<ushort>? levelCourtIds = null;
        if (!string.IsNullOrWhiteSpace(q.CourtLevel))
        {
            if (await reference.GetCourtLevelByCode(q.CourtLevel.Trim(), ct) is { } level)
            {
                // the courts of that level (cached), so the predicate stays on ix_cases_court
                levelCourtIds = (await reference.ListCourtsAsync(ct))
                    .Where(c => c.CourtLevelId == level.Id).Select(c => c.Id).Order().ToList();
            }
            else
            {
                errors.Add("courtLevel", "Shkalla e gjykimit nuk ekziston.");
            }
        }

        uint? judgeId = null;
        if (q.JudgeId is { } judge)
        {
            if (judge is > 0 and <= uint.MaxValue && await reference.GetJudge((uint)judge, ct) is not null)
            {
                judgeId = (uint)judge;
            }
            else
            {
                errors.Add("judgeId", "Gjyqtari nuk ekziston.");
            }
        }

        uint? institutionId = null;
        if (q.InstitutionId is { } institutionPublicId)
        {
            if ((await institutions.GetByPublicIdsAsync([institutionPublicId], ct)).FirstOrDefault() is { } institution)
            {
                institutionId = (uint)institution.Id;
            }
            else
            {
                errors.Add("institutionId", "Institucioni nuk u gjet.");
            }
        }

        if (q.HearingFrom is { } from && q.HearingTo is { } to && to < from)
        {
            errors.Add("hearingTo", "Data e fundit nuk mund të jetë para datës së fillimit.");
        }

        if (q.RegisteredFrom is { } registeredFrom && q.RegisteredTo is { } registeredTo && registeredTo < registeredFrom)
        {
            errors.Add("registeredTo", "Data e fundit nuk mund të jetë para datës së fillimit.");
        }

        string? search = null;
        if (q.Q is not null)
        {
            search = q.Q.Trim();
            if (search.Length is < 2 or > 100)
            {
                errors.Add("q", "Shkruani nga 2 deri në 100 karaktere për të kërkuar.");
            }
        }

        CaseListSpecialFilter? special = null;
        if (!string.IsNullOrWhiteSpace(q.Filter))
        {
            if (!CaseListSpecialFilter.TryParse(q.Filter, out var kind))
            {
                errors.Add("filter", "Filtri nuk ekziston.");
            }
            else if ((special = await specials.CreateAsync(kind, clock.UtcNow, ct)) is null)
            {
                errors.Add("filter", "Ky filtër është vetëm për përdoruesit e një institucioni.");
            }
        }

        if (errors.Any)
        {
            return null;
        }

        // the reasoned decision is awaited on CLOSED cases: that filter alone lists the closed ones
        if (special?.Kind == CaseListSpecial.DecisionPending)
        {
            status ??= Case.Closed;
        }

        // no status chosen: a search covers every status (closed cases must be findable), the plain list the open ones
        status ??= search is null ? CaseListFilter.OpenStatus : CaseListFilter.AllStatuses;

        // Tirane calendar dates → UTC half-open range [from 00:00, to+1 00:00)
        return new CaseListFilter(tab, status, courtId, categoryId, priority, q.Suspended, lawyerUserId,
            q.HearingFrom is { } f ? TiraneTime.StartOfDayUtc(f) : null,
            q.HearingTo is { } t ? TiraneTime.StartOfDayUtc(t.AddDays(1)) : null,
            search, officeId)
        {
            LevelCourtIds = levelCourtIds,
            JudgeId = judgeId,
            InstitutionId = institutionId,
            RegisteredFromUtc = q.RegisteredFrom is { } rf ? TiraneTime.StartOfDayUtc(rf) : null,
            RegisteredToUtc = q.RegisteredTo is { } rt ? TiraneTime.StartOfDayUtc(rt.AddDays(1)) : null,
            Special = special,
            Search = search is null ? null : await caseSearch.ResolveAsync(search, ct),
        };
    }

    /// <summary>The current primary lawyer's name per case, with ONE call to Identity for the whole page.</summary>
    private async Task<Dictionary<ulong, string>> PrimaryLawyerNamesAsync(List<Case> rows, CancellationToken ct)
    {
        var caseIds = rows.Select(c => c.Id).ToList();
        var primaries = await db.CaseAssignments.AsNoTracking()
            .Where(a => caseIds.Contains(a.CaseId) && a.CaseRole == CaseAssignmentRow.PrimaryLawyer && a.EndedAt == null)
            .Select(a => new { a.CaseId, a.UserId })
            .ToListAsync(ct);
        var names = await users.GetNamesAsync(primaries.Select(p => (long)p.UserId).Distinct().ToList(), ct);
        return primaries
            .Where(p => names.ContainsKey(p.UserId))
            .ToDictionary(p => p.CaseId, p => names[p.UserId]);
    }

    private static Error Forbidden() => Error.Forbidden("case.list_forbidden", "Nuk keni leje për këtë listë.");

    private static Error FieldError(string field, string message)
    {
        var errors = new FieldErrors();
        errors.Add(field, message);
        return errors.ToError("Kërkimi nuk është i vlefshëm.");
    }
}

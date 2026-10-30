using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Paging;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>Query string of GET /api/cases. hearingFrom/hearingTo are Tirane calendar dates; lawyerId is a user public id.</summary>
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
    int? Limit);

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
    DateTime? ClosedAt);

/// <summary>
/// The case list (task 019): one endpoint, a tab per kind of user, keyset paging. A tab the user may not use → 403;
/// a cursor made with other filters → 400. Lists are not audited.
/// </summary>
internal sealed class ListCasesHandler(
    CasesDbContext db,
    CaseListQueryBuilder builder,
    IReferenceData reference,
    IUserDirectory users,
    ICurrentUser user)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    private static readonly string[] Statuses = [Case.Registered, "ACTIVE", Case.Closed];
    private static readonly string[] Priorities = ["NONE", "LOW", "MEDIUM", "HIGH"];

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
                c.RegisteredAt, c.ClosedAt));
        }

        var next = hasMore ? CaseListQueryBuilder.CursorAfter(rows[^1], filter.Sort).Encode(filter) : null;
        return new KeysetPage<CaseListItem>(items, next);
    }

    private async Task<CaseListFilter?> ParseFilterAsync(CaseListTab tab, ListCasesQuery q, FieldErrors errors, CancellationToken ct)
    {
        string? status = null;
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            status = q.Status.Trim().ToUpperInvariant();
            if (!Statuses.Contains(status))
            {
                errors.Add("status", "Statusi duhet të jetë REGISTERED, ACTIVE ose CLOSED.");
            }
        }

        string? priority = null;
        if (!string.IsNullOrWhiteSpace(q.Priority))
        {
            priority = q.Priority.Trim().ToUpperInvariant();
            if (!Priorities.Contains(priority))
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

        if (q.HearingFrom is { } from && q.HearingTo is { } to && to < from)
        {
            errors.Add("hearingTo", "Data e fundit nuk mund të jetë para datës së fillimit.");
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

        if (errors.Any)
        {
            return null;
        }

        // Tirane calendar dates → UTC half-open range [from 00:00, to+1 00:00)
        return new CaseListFilter(tab, status, courtId, categoryId, priority, q.Suspended, lawyerUserId,
            q.HearingFrom is { } f ? TiraneTime.StartOfDayUtc(f) : null,
            q.HearingTo is { } t ? TiraneTime.StartOfDayUtc(t.AddDays(1)) : null,
            search);
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

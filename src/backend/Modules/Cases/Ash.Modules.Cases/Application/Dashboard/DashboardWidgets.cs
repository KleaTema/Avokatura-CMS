using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.ListCases;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Dashboard;

/// <summary>
/// The widgets of <see cref="DashboardHandler"/>. Every count is <see cref="CaseListQueryBuilder.Filtered"/> of the
/// filter its LinkPath encodes (the list's URL parameters: tab, status, filter, from, regFrom, regTo, lawyer, office),
/// so a card and the list it opens show the same number. Lists show at most <see cref="ListSize"/> rows.
/// </summary>
internal sealed class DashboardWidgets(
    CasesDbContext db,
    CaseListQueryBuilder builder,
    CaseListSpecialFactory specials,
    OfficeLawyers officeLawyers,
    IReferenceData reference,
    IUserDirectory users,
    IOfficeDirectory offices,
    IDocumentService documents,
    JobStatusReader jobs,
    PersistenceOptions persistence,
    ICurrentUser user)
{
    public const int ListSize = 8;

    // ---------------------------------------------------------------- links (the case list's URL parameters)

    private static string Link(CaseListTab tab, params (string Name, string? Value)[] parameters)
    {
        var query = new List<string> { $"tab={CaseListTabs.Code(tab)}" };
        query.AddRange(parameters.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));
        return $"/cases?{string.Join('&', query)}";
    }

    private static string CaseLink(Guid casePublicId, string tab) => $"/cases/{casePublicId}/{tab}";

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static CaseListFilter Filter(CaseListTab tab, string status) =>
        new(tab, status, null, null, null, null, null, null, null, null);

    // ---------------------------------------------------------------- shared shapes

    /// <summary>A count of the tab's list with one named filter (decisionPending: CLOSED, as the list defaults it).</summary>
    public async Task<DashboardWidget> SpecialCountAsync(
        string code, string title, CaseListTab tab, CaseListSpecial kind, DateTime now, CancellationToken ct)
    {
        var special = await specials.CreateAsync(kind, now, ct)
                      ?? throw new InvalidOperationException($"Filter {kind} needs the user's institution.");
        var closed = kind == CaseListSpecial.DecisionPending;
        var filter = Filter(tab, closed ? Case.Closed : CaseListFilter.OpenStatus) with { Special = special };
        var count = await builder.Filtered(filter).CountAsync(ct);
        return new DashboardWidget(code, DashboardWidget.Count, title, count,
            LinkPath: Link(tab, ("status", closed ? Case.Closed : null), ("filter", CaseListSpecialFilter.Code(kind))));
    }

    /// <summary>The SCHEDULED hearings this Tirane week of the tab's open cases with one (hearingThisWeek), soonest first.</summary>
    public async Task<DashboardWidget> HearingsThisWeekAsync(string code, string title, CaseListTab tab, DateTime now, CancellationToken ct)
    {
        var special = CaseListSpecialFilter.Create(CaseListSpecial.HearingThisWeek, now);
        var filtered = builder.Filtered(Filter(tab, CaseListFilter.OpenStatus) with { Special = special });
        var count = await filtered.CountAsync(ct);
        var (weekStart, weekEnd) = (special.FromUtc!.Value, special.ToUtc!.Value);
        var caseIds = filtered.Select(c => c.Id);
        var rows = await (
                from h in db.CaseHearings.AsNoTracking()
                join c in db.Cases.AsNoTracking() on h.CaseId equals c.Id
                where caseIds.Contains(h.CaseId) && h.Status == CaseHearing.Scheduled && h.ScheduledAt >= weekStart && h.ScheduledAt < weekEnd
                orderby h.ScheduledAt, h.Id
                select new { h.ScheduledAt, c.PublicId, c.CaseCode, c.CourtId, h.Status })
            .Take(ListSize)
            .ToListAsync(ct);

        var items = new List<DashboardItem>(rows.Count);
        foreach (var r in rows)
        {
            items.Add(new DashboardItem(r.CaseCode, At: r.ScheduledAt, CaseCode: r.CaseCode,
                CourtName: (await reference.GetCourt(r.CourtId, ct))?.Name, Status: r.Status, LinkPath: CaseLink(r.PublicId, "seancat")));
        }

        return new DashboardWidget(code, DashboardWidget.List, title, count, Items: items,
            LinkPath: Link(tab, ("filter", CaseListSpecialFilter.Code(CaseListSpecial.HearingThisWeek))));
    }

    private async Task<List<DashboardItem>> CaseItemsAsync(IEnumerable<Case> cases, Func<Case, DateTime?> at, string tab, CancellationToken ct)
    {
        var items = new List<DashboardItem>();
        foreach (var c in cases)
        {
            items.Add(new DashboardItem(c.CaseCode, At: at(c), CaseCode: c.CaseCode,
                CourtName: (await reference.GetCourt(c.CourtId, ct))?.Name, Status: c.LifecycleStatus, LinkPath: CaseLink(c.PublicId, tab)));
        }

        return items;
    }

    // ---------------------------------------------------------------- Protokoll

    public async Task<DashboardWidget> RegisteredTodayAsync(DateTime now, CancellationToken ct)
    {
        // the "registered" tab is the registrar's still-REGISTERED cases: the ones registered today that wait
        var today = TiraneTime.DateOf(now);
        var filter = CaseListFilter.Open(CaseListTab.Registered) with
        {
            RegisteredFromUtc = TiraneTime.StartOfDayUtc(today),
            RegisteredToUtc = TiraneTime.StartOfDayUtc(today.AddDays(1)),
        };
        var count = await builder.Filtered(filter).CountAsync(ct);
        return new DashboardWidget("registeredToday", DashboardWidget.Count, "Regjistruar sot", count,
            LinkPath: Link(CaseListTab.Registered, ("regFrom", Day(today)), ("regTo", Day(today))));
    }

    public async Task<DashboardWidget> RegisteredWaitingAsync(DateTime now, CancellationToken ct) =>
        new("registeredWaiting", DashboardWidget.Count, "Në pritje të Kryetarit",
            await builder.Filtered(CaseListFilter.Open(CaseListTab.Registered)).CountAsync(ct), LinkPath: Link(CaseListTab.Registered));

    public async Task<DashboardWidget> MyRegistrationsAsync(DateTime now, CancellationToken ct)
    {
        var filter = CaseListFilter.Open(CaseListTab.Registered);
        var count = await builder.Filtered(filter).CountAsync(ct);
        var rows = await builder.Page(filter, null, ListSize).ToListAsync(ct);
        return new DashboardWidget("myRegistrations", DashboardWidget.List, "Regjistrimet e mia të fundit", count,
            Items: await CaseItemsAsync(rows, c => c.RegisteredAt, "te-dhenat", ct), LinkPath: Link(CaseListTab.Registered));
    }

    // ---------------------------------------------------------------- Kryetar

    /// <summary>"Pa avokat" and, in Secondary, how many days the oldest one has waited (Tirane calendar days).</summary>
    public async Task<DashboardWidget> UnassignedAsync(DateTime now, CancellationToken ct)
    {
        var filtered = builder.Filtered(CaseListFilter.Open(CaseListTab.Unassigned));
        var count = await filtered.CountAsync(ct);
        long? oldestDays = null;
        if (count > 0 && await filtered.MinAsync(c => (DateTime?)c.RegisteredAt, ct) is { } oldest)
        {
            oldestDays = TiraneTime.DateOf(now).DayNumber - TiraneTime.DateOf(oldest).DayNumber;
        }

        return new DashboardWidget("unassigned", DashboardWidget.Count, "Pa avokat", count, oldestDays, LinkPath: Link(CaseListTab.Unassigned));
    }

    /// <summary>
    /// Per lawyer of the office (and anyone else holding an office case): open office cases as primary (Value) and as
    /// secondary (Value2). The bar opens the office list filtered by that lawyer — the same total.
    /// </summary>
    public async Task<DashboardWidget> WorkloadAsync(DateTime now, CancellationToken ct)
    {
        // the office's open cases (ix_cases_office_status) joined to their current assignments (ix_ca_case_current)
        var counts = await (
                from c in builder.Filtered(CaseListFilter.Open(CaseListTab.Office))
                join a in db.CaseAssignments.AsNoTracking() on c.Id equals a.CaseId
                where a.EndedAt == null
                      && (a.CaseRole == CaseAssignmentRow.PrimaryLawyer || a.CaseRole == CaseAssignmentRow.SecondaryLawyer)
                group a by new { a.UserId, a.CaseRole } into g
                select new { g.Key.UserId, g.Key.CaseRole, Count = g.Count() })
            .ToListAsync(ct);

        var lawyers = user.OfficeId is { } office ? await officeLawyers.ListAsync(office, includeSelf: false, ct) : [];
        var ids = counts.Select(x => (long)x.UserId).Concat(lawyers.Select(l => l.UserId)).Distinct().ToList();
        var refs = await users.GetRefsAsync(ids, ct);
        var items = ids
            .Where(refs.ContainsKey)
            .Select(id =>
            {
                long Of(string role) => counts.Where(x => x.UserId == id && x.CaseRole == role).Sum(x => (long)x.Count);
                return new DashboardItem(refs[id].Name, Value: Of(CaseAssignmentRow.PrimaryLawyer), Value2: Of(CaseAssignmentRow.SecondaryLawyer),
                    LinkPath: Link(CaseListTab.Office, ("lawyer", refs[id].PublicId.ToString())));
            })
            .OrderByDescending(i => i.Value + i.Value2)
            .ThenBy(i => i.Label, OfficeLawyers.NameOrder)
            .ToList();
        // Value: every lawyer's cases; the bars: the ListSize busiest (the office list shows them all)
        return new DashboardWidget("officeWorkload", DashboardWidget.Chart, "Ngarkesa e avokatëve", items.Sum(i => (i.Value ?? 0) + (i.Value2 ?? 0)),
            Items: items.Take(ListSize).ToList(), LinkPath: Link(CaseListTab.Office));
    }

    // ---------------------------------------------------------------- lawyer, assistant, jurist

    public async Task<DashboardWidget> MineOpenAsync(DateTime now, CancellationToken ct) =>
        new("mineOpen", DashboardWidget.Count, "Çështjet e mia të hapura",
            await builder.Filtered(CaseListFilter.Open(CaseListTab.Mine)).CountAsync(ct), LinkPath: Link(CaseListTab.Mine));

    /// <summary>
    /// The last history rows of the levels the user is on now (any status) — never other levels (task 020 rule).
    /// Newest by id: the table is append-only, so the id is the order of recording, and MySQL can walk the primary key
    /// backwards and stop after <see cref="ListSize"/> matches instead of sorting all of the user's history.
    /// </summary>
    public async Task<DashboardWidget> MineActivityAsync(DateTime now, CancellationToken ct)
    {
        var mine = builder.Filtered(Filter(CaseListTab.Mine, CaseListFilter.AllStatuses)).Select(c => (ulong?)c.Id);
        var rows = await (
                from e in db.CaseHistoryEvents.AsNoTracking()
                join c in db.Cases.AsNoTracking() on e.CaseId equals (ulong?)c.Id
                where mine.Contains(e.CaseId)
                orderby e.Id descending
                select new { e.Summary, e.OccurredAt, c.PublicId, c.CaseCode, c.CourtId })
            .Take(ListSize)
            .ToListAsync(ct);

        var items = new List<DashboardItem>(rows.Count);
        foreach (var r in rows)
        {
            items.Add(new DashboardItem(r.Summary, At: r.OccurredAt, CaseCode: r.CaseCode,
                CourtName: (await reference.GetCourt(r.CourtId, ct))?.Name, LinkPath: CaseLink(r.PublicId, "historiku")));
        }

        return new DashboardWidget("mineActivity", DashboardWidget.List, "Aktiviteti i fundit", Items: items, LinkPath: Link(CaseListTab.Mine));
    }

    // ---------------------------------------------------------------- institution user

    public async Task<DashboardWidget> InstitutionLinkedAsync(DateTime now, CancellationToken ct) =>
        new("institutionLinked", DashboardWidget.Count, "Çështjet e institucionit",
            await builder.Filtered(CaseListFilter.Open(CaseListTab.Institution)).CountAsync(ct), LinkPath: Link(CaseListTab.Institution));

    /// <summary>The institution's open cases with a hearing from today on, soonest first (the list's "from" filter).</summary>
    public async Task<DashboardWidget> InstitutionUpcomingAsync(DateTime now, CancellationToken ct)
    {
        var today = TiraneTime.DateOf(now);
        var filter = CaseListFilter.Open(CaseListTab.Institution) with { HearingFromUtc = TiraneTime.StartOfDayUtc(today) };
        var count = await builder.Filtered(filter).CountAsync(ct);
        var rows = await builder.Page(filter, null, ListSize).ToListAsync(ct);
        return new DashboardWidget("institutionUpcoming", DashboardWidget.List, "Seancat e ardhshme", count,
            Items: await CaseItemsAsync(rows, c => c.NextHearingAt, "seancat", ct),
            LinkPath: Link(CaseListTab.Institution, ("from", Day(today))));
    }

    // ---------------------------------------------------------------- Monitor

    /// <summary>Open (Value) and closed (Value2) cases per office; each bar opens that office's list.</summary>
    public async Task<DashboardWidget> ByOfficeAsync(DateTime now, CancellationToken ct)
    {
        // ix_cases_office_status (office_id, lifecycle_status, …)
        var counts = await builder.Filtered(Filter(CaseListTab.All, CaseListFilter.AllStatuses))
            .GroupBy(c => new { c.OfficeId, c.LifecycleStatus })
            .Select(g => new { g.Key.OfficeId, g.Key.LifecycleStatus, Count = g.Count() })
            .ToListAsync(ct);
        var active = await offices.ListActiveAsync(ct);
        var items = active
            .Select(o =>
            {
                var mine = counts.Where(x => x.OfficeId == o.Id).ToList();
                var closed = mine.Where(x => x.LifecycleStatus == Case.Closed).Sum(x => (long)x.Count);
                var open = mine.Sum(x => (long)x.Count) - closed;
                return new DashboardItem(o.Name, Value: open, Value2: closed,
                    LinkPath: Link(CaseListTab.All, ("office", o.PublicId.ToString())),
                    Link2Path: Link(CaseListTab.All, ("status", Case.Closed), ("office", o.PublicId.ToString())));
            })
            .Where(i => i.Value + i.Value2 > 0)
            .OrderByDescending(i => i.Value + i.Value2)
            .ThenBy(i => i.Label, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
        // Value: the open cases of every office; the bars: the ListSize largest offices
        return new DashboardWidget("allByOffice", DashboardWidget.Chart, "Çështjet sipas zyrës", items.Sum(i => i.Value ?? 0),
            Items: items.Take(ListSize).ToList(), LinkPath: Link(CaseListTab.All));
    }

    // ---------------------------------------------------------------- administrator

    public async Task<DashboardWidget> FailedScansAsync(DateTime now, CancellationToken ct) =>
        new("adminFailedScans", DashboardWidget.Count, "Dokumente të pakontrolluara", await documents.CountFailedScansAsync(ct));

    /// <summary>Every job (ash_ops.job_runs): Value = how many are overdue; a row per job with its last success or error.</summary>
    public async Task<DashboardWidget> JobsAsync(DateTime now, CancellationToken ct)
    {
        var statuses = await jobs.ReadAsync(now, ct);
        var items = statuses
            .Select(j => new DashboardItem(j.Name, j.LastError, j.LastSucceededAt, Value: j.Stale ? 1 : 0))
            .ToList();
        return new DashboardWidget("adminJobs", DashboardWidget.List, "Punët në sfond", statuses.Count(j => j.Stale), Items: items);
    }

    /// <summary>Per partitioned table: Detail OK, P_MAX_ROWS or NEXT_MONTH_MISSING (both: two rows); Value = problems.</summary>
    public async Task<DashboardWidget> PartitionsAsync(DateTime now, CancellationToken ct)
    {
        var items = new List<DashboardItem>();
        foreach (var table in await PartitionStatus.ReadAsync(persistence.ConnectionString, now, ct))
        {
            if (table.PMaxHasRows)
            {
                items.Add(new DashboardItem(table.Table, "P_MAX_ROWS", Value: 1));
            }

            if (!table.NextMonthReady)
            {
                items.Add(new DashboardItem(table.Table, "NEXT_MONTH_MISSING", Value: 1));
            }

            if (table.Ok)
            {
                items.Add(new DashboardItem(table.Table, "OK", Value: 0));
            }
        }

        return new DashboardWidget("adminPartitions", DashboardWidget.List, "Particionet e bazës", items.Count(i => i.Value == 1), Items: items);
    }
}

using System.Diagnostics;
using Ash.Modules.Cases.Application.ListCases;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Cases.Application.Dashboard;

/// <summary>
/// GET /api/dashboard (task 046). The widgets of every audience the caller belongs to — decided like the case-list
/// tabs (permissions and the kind of user, never a role name alone), a union without duplicates:
/// Protokoll (the "registered" tab), Kryetar ("unassigned"), lawyer / assistant / jurist ("mine"), institution user
/// ("institution"), Monitor ("all"), administrator (audit.view). TODO(client-q-dashboard): confirm the widgets per role.
/// Every count is CaseListQueryBuilder.Filtered(...) of the list its link opens — one source of truth.
/// Each widget runs on its own: an exception or a timeout gives that widget an Error, the rest still come back.
/// Office widgets are cached 60 s per office, global ones (Monitor, admin) 5 minutes; personal ones never.
/// </summary>
internal sealed class DashboardHandler(
    DashboardWidgets widgets, CasesDbContext db, ICurrentUser user, IMemoryCache cache, IClock clock, ILogger<DashboardHandler> logger)
{
    public const string AdminPermission = "audit.view";

    public static readonly TimeSpan WidgetTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan OfficeCacheLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan GlobalCacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The text a failed widget carries (the app shows its own, with "Provo përsëri").</summary>
    public const string FailedMessage = "Ky element nuk u ngarkua.";

    private enum CacheScope { None, Office, Global }

    private sealed record Definition(string Code, string Kind, string Title, CacheScope Scope, Func<DateTime, CancellationToken, Task<DashboardWidget>> Compute);

    public async Task<DashboardResponse> Handle(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tabs = CaseListTabs.For(user);
        var definitions = new List<Definition>();

        if (tabs.Contains(CaseListTab.Registered))
        {
            definitions.Add(new("registeredToday", DashboardWidget.Count, "Regjistruar sot", CacheScope.None, widgets.RegisteredTodayAsync));
            definitions.Add(new("registeredWaiting", DashboardWidget.Count, "Në pritje të Kryetarit", CacheScope.None, widgets.RegisteredWaitingAsync));
            definitions.Add(new("myRegistrations", DashboardWidget.List, "Regjistrimet e mia të fundit", CacheScope.None, widgets.MyRegistrationsAsync));
        }

        if (tabs.Contains(CaseListTab.Unassigned))
        {
            definitions.Add(new("unassigned", DashboardWidget.Count, "Pa avokat", CacheScope.Office, widgets.UnassignedAsync));
            definitions.Add(new("officeMissingData30", DashboardWidget.Count, "Pa të dhëna mbi 30 ditë", CacheScope.Office,
                (n, t) => widgets.SpecialCountAsync("officeMissingData30", "Pa të dhëna mbi 30 ditë", CaseListTab.Office, CaseListSpecial.MissingData30, n, t)));
            definitions.Add(new("officeHearingOverdue", DashboardWidget.Count, "Seanca të pa përditësuara", CacheScope.Office,
                (n, t) => widgets.SpecialCountAsync("officeHearingOverdue", "Seanca të pa përditësuara", CaseListTab.Office, CaseListSpecial.HearingOverdue, n, t)));
            definitions.Add(new("officeDecisionPending", DashboardWidget.Count, "Në pritje të vendimit të arsyetuar", CacheScope.Office,
                (n, t) => widgets.SpecialCountAsync("officeDecisionPending", "Në pritje të vendimit të arsyetuar", CaseListTab.Office, CaseListSpecial.DecisionPending, n, t)));
            definitions.Add(new("officeWorkload", DashboardWidget.Chart, "Ngarkesa e avokatëve", CacheScope.Office, widgets.WorkloadAsync));
            definitions.Add(new("officeHearingsThisWeek", DashboardWidget.List, "Seancat e zyrës këtë javë", CacheScope.Office,
                (n, t) => widgets.HearingsThisWeekAsync("officeHearingsThisWeek", "Seancat e zyrës këtë javë", CaseListTab.Office, n, t)));
        }

        if (tabs.Contains(CaseListTab.Mine))
        {
            definitions.Add(new("mineOpen", DashboardWidget.Count, "Çështjet e mia të hapura", CacheScope.None, widgets.MineOpenAsync));
            definitions.Add(new("mineHearingOverdue", DashboardWidget.Count, "Seanca për t'u përditësuar", CacheScope.None,
                (n, t) => widgets.SpecialCountAsync("mineHearingOverdue", "Seanca për t'u përditësuar", CaseListTab.Mine, CaseListSpecial.HearingOverdue, n, t)));
            definitions.Add(new("mineMissingData30", DashboardWidget.Count, "Pa të dhëna mbi 30 ditë", CacheScope.None,
                (n, t) => widgets.SpecialCountAsync("mineMissingData30", "Pa të dhëna mbi 30 ditë", CaseListTab.Mine, CaseListSpecial.MissingData30, n, t)));
            definitions.Add(new("mineDecisionPending", DashboardWidget.Count, "Në pritje të vendimit të arsyetuar", CacheScope.None,
                (n, t) => widgets.SpecialCountAsync("mineDecisionPending", "Në pritje të vendimit të arsyetuar", CaseListTab.Mine, CaseListSpecial.DecisionPending, n, t)));
            definitions.Add(new("mineHearingsThisWeek", DashboardWidget.List, "Seancat e mia këtë javë", CacheScope.None,
                (n, t) => widgets.HearingsThisWeekAsync("mineHearingsThisWeek", "Seancat e mia këtë javë", CaseListTab.Mine, n, t)));
            definitions.Add(new("mineActivity", DashboardWidget.List, "Aktiviteti i fundit", CacheScope.None, widgets.MineActivityAsync));
        }

        if (tabs.Contains(CaseListTab.Institution))
        {
            definitions.Add(new("institutionLinked", DashboardWidget.Count, "Çështjet e institucionit", CacheScope.None, widgets.InstitutionLinkedAsync));
            definitions.Add(new("institutionDelegated", DashboardWidget.Count, "Të deleguara", CacheScope.None,
                (n, t) => widgets.SpecialCountAsync("institutionDelegated", "Të deleguara", CaseListTab.Institution, CaseListSpecial.Delegated, n, t)));
            definitions.Add(new("institutionNoJurist", DashboardWidget.Count, "Pa juristin tonë", CacheScope.None,
                (n, t) => widgets.SpecialCountAsync("institutionNoJurist", "Pa juristin tonë", CaseListTab.Institution, CaseListSpecial.NoJurist, n, t)));
            definitions.Add(new("institutionUpcoming", DashboardWidget.List, "Seancat e ardhshme", CacheScope.None, widgets.InstitutionUpcomingAsync));
        }

        if (tabs.Contains(CaseListTab.All))
        {
            definitions.Add(new("allHearingsThisWeek", DashboardWidget.Count, "Seanca këtë javë", CacheScope.Global,
                (n, t) => widgets.SpecialCountAsync("allHearingsThisWeek", "Seanca këtë javë", CaseListTab.All, CaseListSpecial.HearingThisWeek, n, t)));
            definitions.Add(new("allMissingData6m", DashboardWidget.Count, "Pa të dhëna mbi 6 muaj", CacheScope.Global,
                (n, t) => widgets.SpecialCountAsync("allMissingData6m", "Pa të dhëna mbi 6 muaj", CaseListTab.All, CaseListSpecial.MissingData6M, n, t)));
            definitions.Add(new("allDecisionPending", DashboardWidget.Count, "Në pritje të vendimit të arsyetuar", CacheScope.Global,
                (n, t) => widgets.SpecialCountAsync("allDecisionPending", "Në pritje të vendimit të arsyetuar", CaseListTab.All, CaseListSpecial.DecisionPending, n, t)));
            definitions.Add(new("allByOffice", DashboardWidget.Chart, "Çështjet sipas zyrës", CacheScope.Global, widgets.ByOfficeAsync));
        }

        if (user.HasPermission(AdminPermission))
        {
            definitions.Add(new("adminFailedScans", DashboardWidget.Count, "Dokumente të pakontrolluara", CacheScope.Global, widgets.FailedScansAsync));
            definitions.Add(new("adminJobs", DashboardWidget.List, "Punët në sfond", CacheScope.Global, widgets.JobsAsync));
            definitions.Add(new("adminPartitions", DashboardWidget.List, "Particionet e bazës", CacheScope.Global, widgets.PartitionsAsync));
        }

        var result = new List<DashboardWidget>(definitions.Count);
        await using (await WidgetLimitsAsync(ct))
        {
            foreach (var definition in definitions.DistinctBy(d => d.Code))
            {
                result.Add(await RunAsync(definition, now, ct));
            }
        }

        return new DashboardResponse(result, now);
    }

    /// <summary>
    /// The widget limit is enforced by MySQL, not by a CancellationToken: MySql.Data does not interrupt a statement that
    /// waits (for a table lock, a slow plan) when the token fires — the request would hang (found by the failing-widget
    /// test). For this request's connection (every module's DbContext shares it): a SELECT may run
    /// <see cref="WidgetTimeout"/> at most (max_execution_time, error 3024) and wait as long for a metadata lock
    /// (lock_wait_timeout, error 1205); either error fails that widget only. The connection is pooled, so the
    /// previous values are put back before it is released.
    /// </summary>
    private async Task<IAsyncDisposable> WidgetLimitsAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        long previousMs, previousLockSeconds;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT @@SESSION.max_execution_time, @@SESSION.lock_wait_timeout";
            await using var reader = await read.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            (previousMs, previousLockSeconds) = (Convert.ToInt64(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)));
        }

        await SetAsync(connection, (long)WidgetTimeout.TotalMilliseconds, (long)WidgetTimeout.TotalSeconds, ct);
        return new Restore(async () =>
        {
            await SetAsync(connection, previousMs, previousLockSeconds, CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        });
    }

    private static async Task SetAsync(System.Data.Common.DbConnection connection, long maxExecutionMs, long lockWaitSeconds, CancellationToken ct)
    {
        await using var set = connection.CreateCommand();
        set.CommandText = $"SET SESSION max_execution_time = {maxExecutionMs}, lock_wait_timeout = {lockWaitSeconds}";
        await set.ExecuteNonQueryAsync(ct);
    }

    private sealed class Restore(Func<Task> restore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await restore();
    }

    private async Task<DashboardWidget> RunAsync(Definition d, DateTime now, CancellationToken ct)
    {
        var key = d.Scope switch
        {
            CacheScope.Office => $"dashboard:office:{user.OfficeId}:{d.Code}",
            CacheScope.Global => $"dashboard:all:{d.Code}",
            _ => null,
        };
        if (key is not null && cache.TryGetValue(key, out DashboardWidget? cached) && cached is not null)
        {
            return cached;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(WidgetTimeout);
            var widget = await d.Compute(now, timeout.Token);
            if (key is not null)
            {
                cache.Set(key, widget, d.Scope == CacheScope.Office ? OfficeCacheLifetime : GlobalCacheLifetime);
            }

            logger.LogDebug("Dashboard widget {Code} in {Elapsed} ms.", d.Code, watch.ElapsedMilliseconds);
            return widget;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // never cached: the next load tries again
            logger.LogError(ex, "Dashboard widget {Code} failed after {Elapsed} ms.", d.Code, watch.ElapsedMilliseconds);
            return new DashboardWidget(d.Code, d.Kind, d.Title, Error: FailedMessage);
        }
    }
}

using System.Diagnostics;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Application.ListCases;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Cases.Application.Dashboard;

/// <summary>
/// GET /api/dashboard (task 046). The widgets of every audience the caller belongs to — decided like the case-list
/// tabs (permissions and the kind of user, never a role name alone), a union without duplicates:
/// Protokoll (the "registered" tab), Kryetar ("unassigned"), lawyer / assistant / jurist ("mine"), institution user
/// ("institution"), Monitor ("all"), administrator (audit.view). TODO(client-q-dashboard): confirm the widgets per role.
/// Every count is CaseListQueryBuilder.Filtered(...) of the list its link opens — one source of truth.
/// <para>
/// The widgets are independent, so they run in parallel (at most <see cref="Parallelism"/> at a time), each in its own
/// DI scope — its own connection — with the caller as current user (like ParallelReads). Each runs on its own: an
/// exception, or a query MySQL stopped at the read time limit (QueryTimeLimitInterceptor), gives that widget an Error;
/// the rest still come back. Office widgets are cached 60 s per office, global ones (Monitor, admin) 5 minutes,
/// personal ones 30 s per user.
/// </para>
/// </summary>
internal sealed class DashboardHandler(
    IServiceScopeFactory scopes, ICurrentUser user, IMemoryCache cache, IClock clock, ILogger<DashboardHandler> logger)
{
    public const string AdminPermission = "audit.view";

    /// <summary>Connections one dashboard load may use at once (each widget borrows one from the pool).</summary>
    public const int Parallelism = 4;

    public static readonly TimeSpan OfficeCacheLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan GlobalCacheLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PersonalCacheLifetime = TimeSpan.FromSeconds(30);

    /// <summary>The text a failed widget carries (the app shows its own, with "Provo përsëri").</summary>
    public const string FailedMessage = "Ky element nuk u ngarkua.";

    private enum CacheScope { Personal, Office, Global }

    private delegate Task<DashboardWidget> Compute(DashboardWidgets widgets, DateTime now, CancellationToken ct);

    private sealed record Definition(string Code, string Kind, string Title, CacheScope Scope, Compute Compute);

    public async Task<DashboardResponse> Handle(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tabs = CaseListTabs.For(user);
        var definitions = new List<Definition>();

        Definition Special(string code, string title, CacheScope scope, CaseListTab tab, CaseListSpecial kind) =>
            new(code, DashboardWidget.Count, title, scope, (w, n, t) => w.SpecialCountAsync(code, title, tab, kind, n, t));

        if (tabs.Contains(CaseListTab.Registered))
        {
            definitions.Add(new("registeredToday", DashboardWidget.Count, "Regjistruar sot", CacheScope.Personal, (w, n, t) => w.RegisteredTodayAsync(n, t)));
            definitions.Add(new("registeredWaiting", DashboardWidget.Count, "Në pritje të Kryetarit", CacheScope.Personal, (w, n, t) => w.RegisteredWaitingAsync(n, t)));
            definitions.Add(new("myRegistrations", DashboardWidget.List, "Regjistrimet e mia të fundit", CacheScope.Personal, (w, n, t) => w.MyRegistrationsAsync(n, t)));
        }

        if (tabs.Contains(CaseListTab.Unassigned))
        {
            definitions.Add(new("unassigned", DashboardWidget.Count, "Pa avokat", CacheScope.Office, (w, n, t) => w.UnassignedAsync(n, t)));
            definitions.Add(Special("officeMissingData30", "Pa të dhëna mbi 30 ditë", CacheScope.Office, CaseListTab.Office, CaseListSpecial.MissingData30));
            definitions.Add(Special("officeHearingOverdue", "Seanca të pa përditësuara", CacheScope.Office, CaseListTab.Office, CaseListSpecial.HearingOverdue));
            definitions.Add(Special("officeDecisionPending", "Në pritje të vendimit të arsyetuar", CacheScope.Office, CaseListTab.Office, CaseListSpecial.DecisionPending));
            definitions.Add(new("officeWorkload", DashboardWidget.Chart, "Ngarkesa e avokatëve", CacheScope.Office, (w, n, t) => w.WorkloadAsync(n, t)));
            definitions.Add(new("officeHearingsThisWeek", DashboardWidget.List, "Seancat e zyrës këtë javë", CacheScope.Office,
                (w, n, t) => w.HearingsThisWeekAsync("officeHearingsThisWeek", "Seancat e zyrës këtë javë", CaseListTab.Office, n, t)));
        }

        if (tabs.Contains(CaseListTab.Mine))
        {
            definitions.Add(new("mineOpen", DashboardWidget.Count, "Çështjet e mia të hapura", CacheScope.Personal, (w, n, t) => w.MineOpenAsync(n, t)));
            definitions.Add(Special("mineHearingOverdue", "Seanca për t'u përditësuar", CacheScope.Personal, CaseListTab.Mine, CaseListSpecial.HearingOverdue));
            definitions.Add(Special("mineMissingData30", "Pa të dhëna mbi 30 ditë", CacheScope.Personal, CaseListTab.Mine, CaseListSpecial.MissingData30));
            definitions.Add(Special("mineDecisionPending", "Në pritje të vendimit të arsyetuar", CacheScope.Personal, CaseListTab.Mine, CaseListSpecial.DecisionPending));
            definitions.Add(new("mineHearingsThisWeek", DashboardWidget.List, "Seancat e mia këtë javë", CacheScope.Personal,
                (w, n, t) => w.HearingsThisWeekAsync("mineHearingsThisWeek", "Seancat e mia këtë javë", CaseListTab.Mine, n, t)));
            definitions.Add(new("mineActivity", DashboardWidget.List, "Aktiviteti i fundit", CacheScope.Personal, (w, n, t) => w.MineActivityAsync(n, t)));
        }

        if (tabs.Contains(CaseListTab.Institution))
        {
            definitions.Add(new("institutionLinked", DashboardWidget.Count, "Çështjet e institucionit", CacheScope.Personal, (w, n, t) => w.InstitutionLinkedAsync(n, t)));
            definitions.Add(Special("institutionDelegated", "Të deleguara", CacheScope.Personal, CaseListTab.Institution, CaseListSpecial.Delegated));
            definitions.Add(Special("institutionNoJurist", "Pa juristin tonë", CacheScope.Personal, CaseListTab.Institution, CaseListSpecial.NoJurist));
            definitions.Add(new("institutionUpcoming", DashboardWidget.List, "Seancat e ardhshme", CacheScope.Personal, (w, n, t) => w.InstitutionUpcomingAsync(n, t)));
        }

        if (tabs.Contains(CaseListTab.All))
        {
            definitions.Add(Special("allHearingsThisWeek", "Seanca këtë javë", CacheScope.Global, CaseListTab.All, CaseListSpecial.HearingThisWeek));
            definitions.Add(Special("allMissingData6m", "Pa të dhëna mbi 6 muaj", CacheScope.Global, CaseListTab.All, CaseListSpecial.MissingData6M));
            definitions.Add(Special("allDecisionPending", "Në pritje të vendimit të arsyetuar", CacheScope.Global, CaseListTab.All, CaseListSpecial.DecisionPending));
            definitions.Add(new("allByOffice", DashboardWidget.Chart, "Çështjet sipas zyrës", CacheScope.Global, (w, n, t) => w.ByOfficeAsync(n, t)));
        }

        if (user.HasPermission(AdminPermission))
        {
            definitions.Add(new("adminFailedScans", DashboardWidget.Count, "Dokumente të pakontrolluara", CacheScope.Global, (w, n, t) => w.FailedScansAsync(n, t)));
            definitions.Add(new("adminJobs", DashboardWidget.List, "Punët në sfond", CacheScope.Global, (w, n, t) => w.JobsAsync(n, t)));
            definitions.Add(new("adminPartitions", DashboardWidget.List, "Particionet e bazës", CacheScope.Global, (w, n, t) => w.PartitionsAsync(n, t)));
        }

        using var gate = new SemaphoreSlim(Parallelism);
        var tasks = definitions.DistinctBy(d => d.Code).Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await RunAsync(d, now, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        return new DashboardResponse(await Task.WhenAll(tasks), now);
    }

    private async Task<DashboardWidget> RunAsync(Definition d, DateTime now, CancellationToken ct)
    {
        var key = d.Scope switch
        {
            CacheScope.Office => $"dashboard:office:{user.OfficeId}:{d.Code}",
            CacheScope.Global => $"dashboard:all:{d.Code}",
            _ => $"dashboard:user:{user.UserId}:{d.Code}",
        };
        if (cache.TryGetValue(key, out DashboardWidget? cached) && cached is not null)
        {
            return cached;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            // its own scope and connection, the caller as current user (Task.Run: MySql.Data blocks the thread while
            // a statement runs, so the widgets would otherwise still go one by one)
            var widget = await Task.Run(async () =>
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<InheritedCurrentUser>().User = user;
                return await d.Compute(scope.ServiceProvider.GetRequiredService<DashboardWidgets>(), now, ct);
            }, ct);
            cache.Set(key, widget, d.Scope switch
            {
                CacheScope.Office => OfficeCacheLifetime,
                CacheScope.Global => GlobalCacheLifetime,
                _ => PersonalCacheLifetime,
            });
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

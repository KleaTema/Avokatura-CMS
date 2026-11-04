using System.Globalization;
using System.Text.Json;
using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.History;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Cases.Application.Jobs;

/// <summary>
/// Hourly (task 041), two reminders, each sent once (ash_core.sent_reminders, marked in the same transaction as the
/// notifications):
/// - HEARING_TOMORROW: a SCHEDULED hearing on tomorrow's Tirane date, from 08:00 Tirane today, to the current team
///   (HearingTomorrow → Notifications). Key "hearing:&lt;id&gt;:&lt;date&gt;:tomorrow": a rescheduled hearing has a new
///   date, so a new key and a new reminder. TODO(client-q4): the day before at 08:00 is the default.
/// - NEXT_LEVEL_PENDING_3D: a NEXT_LEVEL_SUMMONS_PENDING notice (task 018) whose hearing is at most 3 days away while
///   the level is still not CLOSED — to the current primary lawyer (if any) and the office head. Key
///   "pending:&lt;historyId&gt;:3d".
/// Cases that are CLOSED or of an archived dossier get neither.
/// </summary>
internal sealed class HearingReminderJob(
    IServiceScopeFactory scopes, CasesDbContext db, IUserDirectory users, IOfficeDirectory offices, ILogger<HearingReminderJob> logger)
    : IScheduledJob
{
    public const string JobName = "hearing-reminders";

    /// <summary>From this Tirane hour on, tomorrow's hearings are announced.</summary>
    public const int TomorrowFromHour = 8;

    public static readonly TimeSpan PendingWindow = TimeSpan.FromDays(3);

    /// <summary>Notices older than this are not read again (their hearing is long past).</summary>
    private static readonly TimeSpan PendingLookBack = TimeSpan.FromDays(366);

    public string Name => JobName;

    public JobSchedule Schedule => JobSchedule.Hourly;

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var failures = await TomorrowAsync(nowUtc, ct) + await PendingAsync(nowUtc, ct);
        if (failures > 0)
        {
            throw new InvalidOperationException($"{failures} hearing reminder(s) failed; see the log.");
        }
    }

    private async Task<int> TomorrowAsync(DateTime nowUtc, CancellationToken ct)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TiraneTime.Zone);
        if (localNow.Hour < TomorrowFromHour)
        {
            return 0;
        }

        var tomorrow = TiraneTime.DateOf(nowUtc).AddDays(1);
        var startUtc = TiraneTime.StartOfDayUtc(tomorrow);
        var endUtc = TiraneTime.StartOfDayUtc(tomorrow.AddDays(1));
        var hearings = await (
                from h in db.CaseHearings.AsNoTracking()
                join c in db.Cases.AsNoTracking() on h.CaseId equals c.Id
                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                where h.Status == CaseHearing.Scheduled && h.ScheduledAt >= startUtc && h.ScheduledAt < endUtc
                      && c.LifecycleStatus != Case.Closed && d.ArchivedAt == null
                orderby h.ScheduledAt, h.Id
                select new { h.Id, h.PublicId, h.ScheduledAt, CasePublicId = c.PublicId })
            .ToListAsync(ct);

        var failures = 0;
        foreach (var h in hearings)
        {
            var key = $"hearing:{h.Id}:{tomorrow:yyyy-MM-dd}:tomorrow";
            failures += await OnceAsync(key, nowUtc, events => events.PublishAsync(new HearingTomorrow(h.CasePublicId, h.PublicId, h.ScheduledAt), ct), ct);
        }

        return failures;
    }

    private async Task<int> PendingAsync(DateTime nowUtc, CancellationToken ct)
    {
        var type = HistoryEventCodes.ToCode(HistoryEventType.NextLevelSummonsPending);
        var since = nowUtc - PendingLookBack;
        var notices = await (
                from e in db.CaseHistoryEvents.AsNoTracking()
                join c in db.Cases.AsNoTracking() on e.CaseId equals (ulong?)c.Id
                join d in db.Dossiers.AsNoTracking() on c.DossierId equals d.Id
                where e.EventType == type && e.OccurredAt >= since && c.LifecycleStatus != Case.Closed && d.ArchivedAt == null
                orderby e.Id
                select new { e.Id, e.Details, CaseId = c.Id, c.PublicId, c.OfficeId })
            .ToListAsync(ct);

        var failures = 0;
        foreach (var n in notices)
        {
            if (Parse(n.Details) is not { } summons || summons.HearingAt <= nowUtc || summons.HearingAt - nowUtc > PendingWindow)
            {
                continue;
            }

            var lawyerId = await db.CaseAssignments.AsNoTracking()
                .Where(a => a.CaseId == n.CaseId && a.CaseRole == CaseAssignmentRow.PrimaryLawyer && a.EndedAt == null)
                .Select(a => (uint?)a.UserId)
                .FirstOrDefaultAsync(ct);
            var lawyer = lawyerId is { } id && (await users.GetPublicIdsAsync([id], ct)).TryGetValue(id, out var p) ? p : (Guid?)null;
            var head = (await offices.GetRoutingAsync(n.OfficeId, ct))?.Head is { IsActive: true } h ? h.PublicId : (Guid?)null;
            failures += await OnceAsync($"pending:{n.Id}:3d", nowUtc,
                events => events.PublishAsync(new NextLevelSummonsDueSoon(n.PublicId, summons.Number, summons.HearingAt, lawyer, head), ct), ct);
        }

        return failures;
    }

    /// <summary>One reminder in its own transaction: the key and the notifications, or neither. 1 = it failed.</summary>
    private async Task<int> OnceAsync(string key, DateTime nowUtc, Func<IEventPublisher, Task> publish, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginAsync(ct);
        try
        {
            if (await scope.ServiceProvider.GetRequiredService<SentReminders>().TryMarkAsync(key, nowUtc, ct))
            {
                await publish(scope.ServiceProvider.GetRequiredService<IEventPublisher>());
            }

            await unitOfWork.CommitAsync(ct);
            return 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            logger.LogError(ex, "Reminder {Key} failed; the next run tries again.", key);
            return 1;
        }
    }

    private sealed record Summons(string Number, DateTime HearingAt);

    /// <summary>The summons number and hearing time of a NEXT_LEVEL_SUMMONS_PENDING row ({"new":{…}}, camelCase).</summary>
    private static Summons? Parse(string? details)
    {
        if (string.IsNullOrEmpty(details))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(details);
            if (!json.RootElement.TryGetProperty("new", out var data)
                || !data.TryGetProperty("summonsNumber", out var number)
                || !data.TryGetProperty("hearingAt", out var at)
                || !DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var hearingAt))
            {
                return null;
            }

            return new Summons(number.GetString() ?? string.Empty, DateTime.SpecifyKind(hearingAt, DateTimeKind.Utc));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

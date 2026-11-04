using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// The reminders of the background jobs (task 041). Each event is published once (the job deduplicates it in the same
/// transaction), so a handler only picks the recipients and writes the text. No actor: the sender skips nobody.
/// </summary>
internal sealed class ReminderNotifications(
    INotificationSender sender, ICaseLookup cases, IUserDirectory users, ILogger<ReminderNotifications> logger) :
    IEventHandler<DataEntryReminderDue>,
    IEventHandler<HearingTomorrow>,
    IEventHandler<NextLevelSummonsDueSoon>
{
    public const string MonitorRole = "MONITOR";

    /// <summary>The primary lawyer and the office head; after 6 months also every active Monitor.</summary>
    public async Task HandleAsync(DataEntryReminderDue e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        var sixMonths = e.ReminderType == DataEntryReminderDue.SixMonths;
        var monitors = sixMonths ? (await users.GetUsersByRoleAsync(MonitorRole, ct)).Select(u => u.PublicId) : [];
        var recipients = new List<Guid?> { e.PrimaryLawyerPublicId, e.HeadUserPublicId };
        recipients.AddRange(monitors.Select(m => (Guid?)m));
        await sender.SendAsync(NotificationTexts.DataMissing(c, sixMonths), NotificationRecipients.Of([.. recipients]), ct);
    }

    /// <summary>The current team: lawyers, assistants, the institution's jurist.</summary>
    public async Task HandleAsync(HearingTomorrow e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        var team = await cases.GetTeamAsync(c.Id, ct);
        await sender.SendAsync(NotificationTexts.HearingTomorrow(c, e.ScheduledAt),
            NotificationRecipients.OfIds(team.Select(m => m.UserId).Distinct()), ct);
    }

    public async Task HandleAsync(NextLevelSummonsDueSoon e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.NextLevelPending(c, e.SummonsNumber, e.HearingAt),
                NotificationRecipients.Of(e.PrimaryLawyerPublicId, e.HeadUserPublicId), ct);
        }
    }

    private async Task<CaseSummary?> CaseAsync(Guid casePublicId, CancellationToken ct)
    {
        var c = await cases.GetAsync(casePublicId, ct);
        if (c is null)
        {
            logger.LogWarning("Case {CaseId} of a reminder was not found; no notification.", casePublicId);
        }

        return c;
    }
}

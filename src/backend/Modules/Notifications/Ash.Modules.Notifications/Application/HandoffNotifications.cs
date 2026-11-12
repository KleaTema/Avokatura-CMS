using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// Task 053c: every active Protokoll (role PROTOCOL) of the receiving office gets a mandatory notification AND an
/// e-mail when a case is handed off to the office, and when it is withdrawn (corrected elsewhere, or final). Runs in
/// the publishing request's transaction: the notification rows and the outbox rows commit with the handoff, and the
/// mails leave only after the commit.
/// </summary>
internal sealed class HandoffNotifications(
    INotificationSender sender,
    IEmailOutbox email,
    ICaseLookup cases,
    IUserDirectory users,
    ILogger<HandoffNotifications> logger) :
    IEventHandler<CaseHandedOff>,
    IEventHandler<CaseHandoffWithdrawn>
{
    public const string ProtocolRole = "PROTOCOL";

    public async Task HandleAsync(CaseHandedOff e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await TellAsync(e.ToOfficeId, NotificationTexts.CaseHandedOff(c, e.CourtLevelName, e.CourtName), ct);
        }
    }

    public async Task HandleAsync(CaseHandoffWithdrawn e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await TellAsync(e.OfficeId, NotificationTexts.HandoffWithdrawn(c, e.Reason), ct);
        }
    }

    private async Task TellAsync(long officeId, NotificationMessage message, CancellationToken ct)
    {
        var protocol = await users.GetOfficeContactsByRoleAsync(officeId, ProtocolRole, ct);
        if (protocol.Count == 0)
        {
            logger.LogWarning("Office {OfficeId} has no active Protokoll: nobody was told about {Type}.", officeId, message.Type);
            return;
        }

        await sender.SendAsync(message, NotificationRecipients.OfIds(protocol.Select(p => p.Id)), ct);
        await email.EnqueueAsync(
            protocol.Select(p => NotificationTexts.Email(p.Email, message.Title, message.Body ?? string.Empty)).ToList(), ct);
    }

    private async Task<CaseSummary?> CaseAsync(Guid casePublicId, CancellationToken ct)
    {
        var c = await cases.GetAsync(casePublicId, ct);
        if (c is null)
        {
            logger.LogWarning("Case {CaseId} of a handoff event was not found; no notification.", casePublicId);
        }

        return c;
    }
}

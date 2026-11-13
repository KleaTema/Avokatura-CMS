using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Events;

namespace Ash.Modules.Notifications.Application;

/// <summary>Task 053d: the Kryetar is told (mandatory) when a supervision is given to them and when it ends.</summary>
internal sealed class SupervisionNotifications(INotificationSender sender, ICaseLookup cases) :
    IEventHandler<SupervisionStarted>,
    IEventHandler<SupervisionEnded>
{
    public async Task HandleAsync(SupervisionStarted e, CancellationToken ct = default)
    {
        var c = e.CasePublicId is { } id ? await cases.GetAsync(id, ct) : null;
        await sender.SendAsync(NotificationTexts.SupervisionAssigned(e.LawyerName, c), NotificationRecipients.Of(e.HeadUserPublicId), ct);
    }

    public async Task HandleAsync(SupervisionEnded e, CancellationToken ct = default)
    {
        var c = e.CasePublicId is { } id ? await cases.GetAsync(id, ct) : null;
        await sender.SendAsync(NotificationTexts.SupervisionEnded(e.LawyerName, c), NotificationRecipients.Of(e.HeadUserPublicId), ct);
    }
}

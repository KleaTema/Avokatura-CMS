using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Documents.Contracts.Events;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Events;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// The scan job's verdicts (task 033), in the job's transaction. A rejected or unscannable upload is stored for the
/// uploader and the active administrators; every verdict (CLEAN too) is pushed to the uploader as "document.status"
/// after the commit, so an open documents tab stops polling — that push is not stored.
/// </summary>
internal sealed class DocumentEventNotifications(
    INotificationSender sender,
    ICaseLookup cases,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    NotificationPush push) :
    IEventHandler<DocumentInfected>,
    IEventHandler<DocumentScanFailed>,
    IEventHandler<DocumentScanned>
{
    public const string AdminRole = "ADMIN";
    public const string Infected = "INFECTED";
    public const string Failed = "FAILED";

    public async Task HandleAsync(DocumentInfected e, CancellationToken ct = default)
    {
        if (await cases.GetAsync(e.CaseId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.DocumentInfected(c), await RecipientsAsync(e.UploadedByUserId, ct), ct);
        }

        await PushStatusAsync(e.UploadedByUserId, e.DocumentPublicId, Infected, ct);
    }

    public async Task HandleAsync(DocumentScanFailed e, CancellationToken ct = default)
    {
        if (await cases.GetAsync(e.CaseId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.DocumentScanFailed(c), await RecipientsAsync(e.UploadedByUserId, ct), ct);
        }

        await PushStatusAsync(e.UploadedByUserId, e.DocumentPublicId, Failed, ct);
    }

    public Task HandleAsync(DocumentScanned e, CancellationToken ct = default) =>
        PushStatusAsync(e.UploadedByUserId, e.DocumentPublicId, e.ScanStatus, ct);

    private async Task<NotificationRecipients> RecipientsAsync(long uploadedBy, CancellationToken ct)
    {
        var admins = await users.GetUsersByRoleAsync(AdminRole, ct);
        return NotificationRecipients.OfIds(admins.Select(a => a.Id).Append(uploadedBy).Distinct());
    }

    private async Task PushStatusAsync(long uploadedBy, Guid documentPublicId, string scanStatus, CancellationToken ct)
    {
        var publicIds = await users.GetPublicIdsAsync([uploadedBy], ct);
        if (publicIds.TryGetValue(uploadedBy, out var uploader))
        {
            unitOfWork.OnCommitted(() => push.DocumentStatus(uploader, documentPublicId, scanStatus));
        }
    }
}

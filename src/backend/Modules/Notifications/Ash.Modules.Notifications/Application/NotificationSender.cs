using Ash.Infrastructure.Persistence;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.Modules.Notifications.Infrastructure;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// <see cref="INotificationSender"/>: rows in the caller's transaction, the push after its commit.
/// TODO(client-q-email): in-app only (stored + SignalR); no e-mail until the client confirms it is wanted (and AKSHI
/// gives an SMTP relay) — see docs/client-questions.md.
/// </summary>
internal sealed class NotificationSender(
    NotificationsDbContext db,
    IUserDirectory users,
    CurrentActor actor,
    IUnitOfWork unitOfWork,
    NotificationPush push,
    IClock clock) : INotificationSender
{
    public async Task<int> SendAsync(NotificationMessage message, NotificationRecipients recipients, CancellationToken ct = default)
    {
        var states = await users.GetStatesAsync(recipients.UserIds, recipients.UserPublicIds, ct);
        var actorId = actor.UserId;
        var targets = states.Where(u => u.IsActive && u.Id != actorId).DistinctBy(u => u.Id).ToList();
        if (targets.Count == 0)
        {
            return 0;
        }

        // milliseconds, as DATETIME(3) keeps them: the pushed createdAt and a later GET show the same instant
        var now = clock.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        var rows = targets.Select(u => new NotificationRow
            {
                PublicId = Guid.NewGuid(),
                UserId = (uint)u.Id,
                NotificationType = message.Type,
                CaseId = message.CaseId is { } caseId ? (ulong)caseId : null,
                Title = message.Title,
                Body = message.Body,
                LinkPath = message.LinkPath,
                CreatedAt = now,
            })
            .ToList();
        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync(ct);

        var pushed = rows.Zip(targets, (row, user) => new PushTarget(user.Id, user.PublicId,
                new NotificationPushed(row.PublicId, row.Title, row.LinkPath, row.CreatedAt)))
            .ToList();
        unitOfWork.OnCommitted(() => push.Created(pushed));
        return rows.Count;
    }
}

/// <summary>
/// The user whose action caused the event, or none: a background job (the scan queue, the reminders of task 041) has
/// no current user — ICurrentUser throws there — and then nobody is skipped as the actor.
/// </summary>
internal sealed class CurrentActor(ICurrentUser currentUser)
{
    public long? UserId
    {
        get
        {
            try
            {
                return currentUser.UserId;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}

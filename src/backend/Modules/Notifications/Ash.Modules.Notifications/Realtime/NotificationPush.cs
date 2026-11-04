using Ash.Modules.Notifications.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Realtime;

/// <summary>A stored notification to push: the recipient (internal id for the count, public id = SignalR user).</summary>
public sealed record PushTarget(long UserId, Guid UserPublicId, NotificationPushed Notification);

/// <summary>
/// Sends to the users' open sessions. Called ONLY from IUnitOfWork.OnCommitted (or after a write without a
/// transaction), so a push never announces a row that could still roll back. Fire-and-forget: a push failure (no
/// connection, network) is logged and never fails the request — the list and the count are the truth, the push is a
/// hint. Singleton; the unread count is read in its own scope, after the commit.
/// </summary>
public sealed class NotificationPush(
    IHubContext<NotificationsHub> hub, IServiceScopeFactory scopes, ILogger<NotificationPush> logger)
{
    public void Created(IReadOnlyList<PushTarget> targets) => Run(async () =>
    {
        foreach (var target in targets)
        {
            await hub.Clients.User(target.UserPublicId.ToString())
                .SendAsync(NotificationsHub.NewMethod, target.Notification);
        }

        foreach (var user in targets.DistinctBy(t => t.UserId))
        {
            await SendCountAsync(user.UserId, user.UserPublicId);
        }
    });

    /// <summary>After read / read-all: every open tab of the user shows the same count.</summary>
    public void CountChanged(long userId, Guid userPublicId) => Run(() => SendCountAsync(userId, userPublicId));

    public void DocumentStatus(Guid userPublicId, Guid documentPublicId, string scanStatus) => Run(() =>
        hub.Clients.User(userPublicId.ToString())
            .SendAsync(NotificationsHub.DocumentStatusMethod, new DocumentStatusPushed(documentPublicId, scanStatus)));

    private async Task SendCountAsync(long userId, Guid userPublicId)
    {
        int unread;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var id = (uint)userId;
            unread = await db.Notifications.AsNoTracking().CountAsync(n => n.UserId == id && n.ReadAt == null);
        }

        await hub.Clients.User(userPublicId.ToString()).SendAsync(NotificationsHub.CountMethod, new UnreadCountPushed(unread));
    }

    private void Run(Func<Task> push) => _ = Task.Run(async () =>
    {
        try
        {
            await push();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A SignalR push failed; the client sees the change on its next load.");
        }
    });
}

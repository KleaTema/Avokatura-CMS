using System.Security.Claims;
using Ash.SharedKernel.Identity;
using Microsoft.AspNetCore.SignalR;

namespace Ash.Modules.Notifications.Realtime;

/// <summary>
/// /hubs/notifications (task 040): server → client only, one group per user (Clients.User). The connection is
/// authenticated like any request (bearer token, or in Development the dev user); for the WebSocket handshake the
/// browser cannot send headers, so the host reads them from the query string — for /hubs/* only, never logged.
/// TODO(akshi-q-signalr-backplane): with more than one API instance a push reaches only the users connected to the
/// instance that sends it; a backplane (Redis) or sticky sessions are needed — see docs/client-questions.md.
/// </summary>
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";

    /// <summary>{ publicId, title, linkPath, createdAt } — a notification was stored for the user.</summary>
    public const string NewMethod = "notification.new";

    /// <summary>{ unread } — the user's unread count after a change (new, read, read-all).</summary>
    public const string CountMethod = "notification.count";

    /// <summary>{ documentPublicId, scanStatus } — the scan of a document the user uploaded finished (not stored).</summary>
    public const string DocumentStatusMethod = "document.status";
}

/// <summary>
/// SignalR's user id = the resolved user's public id (the claim CurrentUserMiddleware adds), so Clients.User(publicId)
/// reaches every open tab of that user and nobody else. No claim (unknown or inactive user) = no connection: the
/// middleware answers 401 before the hub.
/// </summary>
public sealed class PublicIdUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(AshClaims.UserPublicId)?.Value;
}

public sealed record NotificationPushed(Guid PublicId, string Title, string? LinkPath, DateTime CreatedAt);

public sealed record UnreadCountPushed(int Unread);

public sealed record DocumentStatusPushed(Guid DocumentPublicId, string ScanStatus);

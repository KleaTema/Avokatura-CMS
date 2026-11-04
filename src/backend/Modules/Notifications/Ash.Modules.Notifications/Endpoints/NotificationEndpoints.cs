using Ash.AspNetCore.Http;
using Ash.Modules.Notifications.Application;
using Ash.Modules.Notifications.Realtime;
using Ash.SharedKernel.Paging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Notifications.Endpoints;

/// <summary>
/// The current user's notifications (task 040). Any authenticated user: there is no permission, the rows are the
/// user's own. The SignalR hub at <see cref="NotificationsHub.Path"/> pushes new ones and the unread count.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/notifications",
                async (bool? unreadOnly, string? after, int? limit, NotificationQueries queries, CancellationToken ct) =>
                {
                    var result = await queries.ListAsync(unreadOnly ?? false, after, limit, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<KeysetPage<NotificationItem>>()
            .ProducesValidationProblem()
            .WithName("ListNotifications");

        endpoints.MapGet("/api/notifications/unread-count",
                async (NotificationQueries queries, CancellationToken ct) => Results.Ok(await queries.UnreadCountAsync(ct)))
            .RequireAuthorization()
            .Produces<UnreadCountResponse>()
            .WithName("GetUnreadNotificationCount");

        endpoints.MapPost("/api/notifications/{id:guid}/read",
                async (Guid id, NotificationQueries queries, CancellationToken ct) =>
                {
                    var result = await queries.MarkReadAsync(id, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
                })
            .RequireAuthorization()
            .Produces<UnreadCountResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("MarkNotificationRead");

        endpoints.MapPost("/api/notifications/read-all",
                async (NotificationQueries queries, CancellationToken ct) => Results.Ok(await queries.MarkAllReadAsync(ct)))
            .RequireAuthorization()
            .Produces<ReadAllResponse>()
            .WithName("MarkAllNotificationsRead");

        endpoints.MapHub<NotificationsHub>(NotificationsHub.Path).RequireAuthorization();
        return endpoints;
    }
}

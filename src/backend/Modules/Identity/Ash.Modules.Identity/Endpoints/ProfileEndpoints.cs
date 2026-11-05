using Ash.AspNetCore.Http;
using Ash.Modules.Identity.Application.Profile;
using Ash.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Identity.Endpoints;

/// <summary>Task 048: the own profile. Every signed-in user; only their own data, never another user's.</summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfile(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/profile").RequireAuthorization();

        group.MapGet("", async (ProfileHandlers handler, CancellationToken ct) =>
                await handler.GetAsync(ct) is { IsSuccess: true } r ? Results.Ok(r.Value) : Results.NotFound())
            .Produces<ProfileResponse>()
            .WithName("GetProfile");

        group.MapPut("", async (UpdateProfileRequest req, ProfileHandlers handler, CancellationToken ct) =>
            {
                var result = await handler.UpdateAsync(req, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<ProfileResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("UpdateProfile");

        group.MapPut("/notifications", async (Dictionary<string, bool> choices, ProfileHandlers handler, CancellationToken ct) =>
            {
                var result = await handler.SetNotificationsAsync(choices, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            })
            .Produces<IReadOnlyList<NotificationPreference>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithName("UpdateProfileNotifications");

        return endpoints;
    }
}

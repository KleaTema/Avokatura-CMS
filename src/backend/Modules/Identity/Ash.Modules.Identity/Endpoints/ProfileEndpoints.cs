using Ash.Modules.Identity.Application.Profile;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Identity.Endpoints;

/// <summary>
/// Tasks 048 / 053g: the own profile, read-only. Every signed-in user; only their own data, never another user's.
/// The phone and the notification switches are gone (client decision E): no PUT here any more.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfile(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/identity/profile").RequireAuthorization();

        group.MapGet("", async (ProfileHandlers handler, CancellationToken ct) =>
                await handler.GetAsync(ct) is { IsSuccess: true } r ? Results.Ok(r.Value) : Results.NotFound())
            .Produces<ProfileResponse>()
            .WithName("GetProfile");

        return endpoints;
    }
}

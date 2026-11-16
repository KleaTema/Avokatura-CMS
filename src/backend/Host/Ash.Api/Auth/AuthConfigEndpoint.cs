using Ash.Modules.Identity.Application.Auth;

namespace Ash.Api.Auth;

/// <summary>One provider as the login page needs it (public values only — no secret exists in an SPA flow).</summary>
public sealed record AuthProviderView(string Name, string Kind, string DisplayName, string Authority, string ClientId, IReadOnlyList<string> Scopes);

/// <summary>GET /api/auth/config: "dev" (the dev login, Development only) or "oidc" with the enabled providers.</summary>
public sealed record AuthConfigResponse(string Mode, IReadOnlyList<AuthProviderView> Providers);

/// <summary>
/// Task 051: the login page asks which way to sign in — one button per enabled provider (with a single provider it
/// redirects at once). Anonymous by necessity (it is read before the login); on the allow-list of the endpoint tests.
/// </summary>
public static class AuthConfigEndpoint
{
    public const string Path = "/api/auth/config";

    public static IEndpointRouteBuilder MapAuthConfig(this IEndpointRouteBuilder endpoints, bool devHeader)
    {
        endpoints.MapGet(Path, (AuthProvidersOptions providers) => Results.Ok(new AuthConfigResponse(
                devHeader ? "dev" : "oidc",
                providers.Enabled
                    .Select(p => new AuthProviderView(p.Name, p.Kind, p.DisplayName, p.Authority ?? string.Empty, p.ClientId, p.Scopes))
                    .ToList())))
            .AllowAnonymous()
            .Produces<AuthConfigResponse>()
            .WithName("GetAuthConfig");
        return endpoints;
    }
}

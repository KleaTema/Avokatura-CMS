using Ash.Modules.Identity.Application.Auth;

namespace Ash.Api.Auth;

/// <summary>
/// One provider as the login page needs it — PUBLIC values only (no secret exists in an SPA flow; the API's own
/// Audience / ValidIssuers / tenants are not sent). KnownAuthorities: the hosts MSAL must trust (B2C) and that the
/// frontend's CSP must allow (task 052 compares them with /config.json). Policy: the B2C user flow (null for Entra ID).
/// </summary>
public sealed record AuthProviderView(
    string Name, string Kind, string DisplayName, string Authority, string? Policy, IReadOnlyList<string> KnownAuthorities,
    string ClientId, IReadOnlyList<string> Scopes)
{
    public static AuthProviderView Of(AuthProviderOptions p)
    {
        var authority = p.Authority ?? string.Empty;
        var hosts = new[] { authority, p.MetadataAddress }
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .Select(u => new Uri(u!).Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // B2C: https://<tenant>.b2clogin.com/<tenant>.onmicrosoft.com/<policy>/v2.0 → the policy segment
        var policy = p.Kind == AuthProviderKinds.B2C && Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            ? uri.Segments.Select(s => s.Trim('/')).FirstOrDefault(s => s.StartsWith("B2C_", StringComparison.OrdinalIgnoreCase))
            : null;
        return new AuthProviderView(p.Name, p.Kind, p.DisplayName, authority, policy, hosts, p.ClientId, p.Scopes);
    }
}

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
                    .Select(AuthProviderView.Of)
                    .ToList())))
            .AllowAnonymous()
            .Produces<AuthConfigResponse>()
            .WithName("GetAuthConfig");
        return endpoints;
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ash.Api.Auth;

/// <summary>
/// Development only: authenticates as the user whose email is in the X-Dev-User header — or, for a SignalR hub
/// (/hubs/*) only, in the dev_user query parameter: a browser WebSocket cannot send headers.
/// </summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevHeader";
    public const string HeaderName = "X-Dev-User";
    public const string QueryName = "dev_user";
    public const string EmailClaim = "ash_dev_email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers[HeaderName].ToString().Trim();
        if (string.IsNullOrEmpty(email) && HubQuery.IsHubRequest(Request))
        {
            email = Request.Query[QueryName].ToString().Trim();
        }

        if (string.IsNullOrEmpty(email))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(EmailClaim, email)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Credentials in the query string are accepted ONLY on the SignalR hubs (the WebSocket handshake cannot carry an
/// Authorization header) — anywhere else they are ignored. Such URLs must never be logged: see LogRedaction.
/// </summary>
public static class HubQuery
{
    public const string HubPrefix = "/hubs";
    public const string AccessTokenName = "access_token";

    public static bool IsHubRequest(HttpRequest request) => request.Path.StartsWithSegments(HubPrefix);
}

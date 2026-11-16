using System.Security.Claims;
using Ash.Modules.Identity.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ash.Api.Auth;

/// <summary>
/// Task 051 — authentication, provider-neutral:
/// <list type="bullet">
/// <item>one JwtBearer scheme per enabled Auth:Providers[] entry (B2C and / or Entra ID; issuer, audience, lifetime and
/// signature from the provider's metadata keys with automatic rollover; clock skew 2 minutes; Entra ID: the token's
/// tenant must be in AllowedTenantIds);</item>
/// <item>a policy scheme ("Ash") that picks the scheme by the token's "iss" — an unknown issuer → 401;</item>
/// <item>the X-Dev-User header only in Development and only with DevFeatures:HeaderAuth (DevFeatureGuard).</item>
/// </list>
/// Outside Development the providers must be complete (<see cref="AuthProvidersOptions.Validate"/>) — fail fast.
/// </summary>
public static class AuthExtensions
{
    public const string PolicySchemeName = "Ash";
    public const string RejectSchemeName = "Reject";
    /// <summary>The provider that validated the token (its Name), added on validation.</summary>
    public const string ProviderClaim = "ash_provider";
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public static IServiceCollection AddAshAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var providers = new AuthProvidersOptions { Providers = configuration.GetSection(AuthProvidersOptions.Section).Get<List<AuthProviderOptions>>() ?? [] };
        var devHeader = DevFeatures.HeaderAuthEnabled(configuration, environment);
        if (!environment.IsDevelopment())
        {
            var errors = providers.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Authentication is not configured: " + string.Join(" ", errors));
            }
        }

        services.AddSingleton(providers);
        var builder = services.AddAuthentication(PolicySchemeName);
        builder.AddScheme<AuthenticationSchemeOptions, RejectAuthHandler>(RejectSchemeName, null);
        if (devHeader)
        {
            builder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
        }

        foreach (var provider in providers.Enabled)
        {
            builder.AddJwtBearer(provider.SchemeName, o => ConfigureProvider(o, provider));
        }

        builder.AddPolicyScheme(PolicySchemeName, "Ash", o => o.ForwardDefaultSelector = context =>
        {
            if (devHeader && DevAuthHandler.HasCredentials(context.Request))
            {
                return DevAuthHandler.SchemeName;
            }

            var token = TokenOf(context.Request);
            if (token is null)
            {
                return RejectSchemeName;
            }

            var issuer = IssuerOf(token);
            return providers.Enabled.FirstOrDefault(p => issuer is not null && p.ValidIssuers.Contains(issuer, StringComparer.Ordinal))
                ?.SchemeName ?? RejectSchemeName;
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermissionDeniedResultHandler>();

        return services;
    }

    private static void ConfigureProvider(JwtBearerOptions o, AuthProviderOptions provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.Authority))
        {
            o.Authority = provider.Authority;
        }

        if (!string.IsNullOrWhiteSpace(provider.MetadataAddress))
        {
            o.MetadataAddress = provider.MetadataAddress;
        }

        o.Audience = provider.Audience;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = provider.ValidIssuers,
            ValidateAudience = true,
            ValidAudiences = [provider.Audience],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = ClockSkew,
        };
        o.Events = new JwtBearerEvents
        {
            // SignalR sends the token as ?access_token= for the WebSocket handshake: read it for /hubs/* only
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) && HubQuery.IsHubRequest(context.Request)
                    && context.Request.Query[HubQuery.AccessTokenName].ToString() is { Length: > 0 } token)
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // Entra ID: only our tenants (no multi-tenant acceptance; B2B guests of our tenant carry our tid)
                if (provider.Kind == AuthProviderKinds.EntraId
                    && !provider.AllowedTenantIds.Contains(context.Principal?.FindFirst("tid")?.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    context.Fail("The token's tenant is not allowed.");
                    return Task.CompletedTask;
                }

                context.Principal?.AddIdentity(new ClaimsIdentity([new Claim(ProviderClaim, provider.Name)]));
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>The bearer token: the Authorization header, or (hubs only) ?access_token=.</summary>
    private static string? TokenOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        return HubQuery.IsHubRequest(request) && request.Query[HubQuery.AccessTokenName].ToString() is { Length: > 0 } token
            ? token
            : null;
    }

    /// <summary>The token's "iss", read WITHOUT validation — only to choose the scheme that then validates it.</summary>
    private static string? IssuerOf(string token)
    {
        try
        {
            return new JsonWebToken(token).Issuer;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Authenticates nobody: a request without a usable credential (unknown issuer, no token) ends as 401.</summary>
public sealed class RejectAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}

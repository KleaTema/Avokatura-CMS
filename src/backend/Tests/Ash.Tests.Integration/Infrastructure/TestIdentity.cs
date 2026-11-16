using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Task 051: two test identity providers ("b2c", "entra") with a locally generated signing key — no real tenant. The
/// API host gets them as Auth:Providers (environment variables) and each JwtBearer scheme gets a fixed configuration
/// with the test key, so nothing is fetched from the network. <see cref="Token"/> signs a token as either would.
/// </summary>
public static class TestIdentity
{
    public const string B2CName = "b2c";
    public const string EntraName = "entra";
    public const string B2CIssuer = "https://ashtest.b2clogin.com/b2c-tenant-id/v2.0/";
    public const string EntraIssuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
    public const string EntraTenant = "11111111-1111-1111-1111-111111111111";
    public const string B2CAudience = "api://ash-b2c";
    public const string EntraAudience = "api://ash-entra";

    /// <summary>The test signing key (generated once per run; never a real key).</summary>
    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "ash-test-key" };

    /// <summary>A key nobody trusts (a token signed with it must be refused).</summary>
    public static readonly RsaSecurityKey ForeignKey = new(RSA.Create(2048)) { KeyId = "foreign-key" };

    /// <summary>Which test providers are enabled (both by default).</summary>
    public sealed record Providers(bool B2C = true, bool Entra = true)
    {
        public static readonly Providers Both = new();
        public static readonly Providers None = new(false, false);
    }

    /// <summary>Auth:Providers as environment variables (the host reads them before overrides apply).</summary>
    public static void Configure(Providers providers)
    {
        Set(0, B2CName, "B2C", providers.B2C, "https://ashtest.b2clogin.com/ashtest.onmicrosoft.com/B2C_1_signin/v2.0",
            "spa-b2c", B2CAudience, B2CIssuer, null, "Hyr me email dhe fjalëkalim");
        Set(1, EntraName, "EntraId", providers.Entra, $"https://login.microsoftonline.com/{EntraTenant}/v2.0",
            "spa-entra", EntraAudience, EntraIssuer, EntraTenant, "Hyr me llogarinë e institucionit (Microsoft)");
    }

    private static void Set(int i, string name, string kind, bool enabled, string authority, string clientId, string audience,
        string issuer, string? tenant, string displayName)
    {
        var p = $"Auth__Providers__{i}__";
        Environment.SetEnvironmentVariable(p + "Name", name);
        Environment.SetEnvironmentVariable(p + "Kind", kind);
        Environment.SetEnvironmentVariable(p + "Enabled", enabled ? "true" : "false");
        Environment.SetEnvironmentVariable(p + "Authority", authority);
        Environment.SetEnvironmentVariable(p + "ClientId", clientId);
        Environment.SetEnvironmentVariable(p + "Audience", audience);
        Environment.SetEnvironmentVariable(p + "ValidIssuers__0", issuer);
        Environment.SetEnvironmentVariable(p + "AllowedTenantIds__0", tenant);
        Environment.SetEnvironmentVariable(p + "Scopes__0", $"{audience}/access");
        Environment.SetEnvironmentVariable(p + "DisplayName", displayName);
    }

    /// <summary>Every provider scheme validates with the test key instead of fetching the provider's metadata.</summary>
    public static void UseTestKeys(IServiceCollection services) =>
        services.PostConfigureAll<JwtBearerOptions>(o =>
        {
            var configuration = new OpenIdConnectConfiguration();
            configuration.SigningKeys.Add(Key);
            o.Configuration = configuration;
            // JwtBearer's own post-configuration already built a manager from the Authority: replace it, or it fetches
            o.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });

    /// <summary>A signed token as the provider would issue it; every part can be spoiled for the negative tests.</summary>
    public static string Token(
        string issuer, string audience, IEnumerable<Claim> claims, DateTime? expires = null, SecurityKey? key = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now.AddMinutes(-5),
            Expires = expires ?? now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>A B2C local account: oid + "emails" (an array).</summary>
    public static string B2C(string oid, string email, DateTime? expires = null) =>
        Token(B2CIssuer, B2CAudience, [new("oid", oid), new("sub", oid), new("emails", $"[\"{email}\"]", JsonClaimValueTypes.JsonArray)], expires);

    /// <summary>An Entra ID member of our tenant: oid, tid, the optional "email" claim.</summary>
    public static string Entra(string oid, string email, string tenant = EntraTenant) =>
        Token(EntraIssuer, EntraAudience, [new("oid", oid), new("sub", oid), new("tid", tenant), new("email", email)]);
}

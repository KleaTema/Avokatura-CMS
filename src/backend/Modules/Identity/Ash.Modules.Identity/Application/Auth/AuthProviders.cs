using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ash.Modules.Identity.Application.Auth;

/// <summary>The identity providers AKSHI may choose (task 051): Azure AD B2C and / or Microsoft Entra ID.</summary>
public static class AuthProviderKinds
{
    public const string B2C = "B2C";
    public const string EntraId = "EntraId";
}

/// <summary>
/// Which claims carry the stable subject and the e-mail (the first present wins). Defaults per kind
/// (<see cref="IdentityClaimMap.DefaultFor"/>); a provider's configuration may override either list.
/// </summary>
public sealed class ClaimMapOptions
{
    public List<string> Subject { get; set; } = [];
    public List<string> Email { get; set; } = [];
}

/// <summary>
/// One sign-in provider (Auth:Providers[]). Switching later (e.g. to Entra External ID) is configuration only.
/// <list type="bullet">
/// <item>Authority / MetadataAddress: the OpenID metadata (B2C: the user flow's policy URL).</item>
/// <item>ClientId: the SPA's application id (the frontend's MSAL); Audience: the API's (the token's aud).</item>
/// <item>ValidIssuers: the token's iss values — also how the API picks this provider for a token.</item>
/// <item>AllowedTenantIds (EntraId): only tokens of these tenants (tid) — never multi-tenant.</item>
/// <item>Scopes: what the SPA asks for (the API's exposed scope); DisplayName: the login button (Albanian).</item>
/// </list>
/// </summary>
public sealed class AuthProviderOptions
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Authority { get; set; }
    public string? MetadataAddress { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public List<string> ValidIssuers { get; set; } = [];
    public List<string> AllowedTenantIds { get; set; } = [];
    public List<string> Scopes { get; set; } = [];
    public string DisplayName { get; set; } = string.Empty;
    public ClaimMapOptions? ClaimMap { get; set; }

    /// <summary>The ASP.NET authentication scheme of this provider.</summary>
    public string SchemeName => $"Jwt:{Name}";
}

/// <summary>Auth:Providers — read once at startup; outside Development it must be complete (<see cref="Validate"/>).</summary>
public sealed class AuthProvidersOptions
{
    public const string Section = "Auth:Providers";

    public List<AuthProviderOptions> Providers { get; set; } = [];

    public IReadOnlyList<AuthProviderOptions> Enabled => Providers.Where(p => p.Enabled).ToList();

    /// <summary>
    /// Every problem of the configuration, in words: none enabled, a missing field, an unknown kind, an EntraId without
    /// tenants, a duplicate name or issuer. Empty = valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var enabled = Enabled;
        if (enabled.Count == 0)
        {
            errors.Add("Auth:Providers: at least one enabled provider is required outside Development.");
        }

        foreach (var p in enabled)
        {
            var at = $"Auth:Providers[{(string.IsNullOrWhiteSpace(p.Name) ? "?" : p.Name)}]";
            if (string.IsNullOrWhiteSpace(p.Name)) errors.Add($"{at}: Name is required.");
            if (p.Kind is not (AuthProviderKinds.B2C or AuthProviderKinds.EntraId)) errors.Add($"{at}: Kind must be B2C or EntraId.");
            if (string.IsNullOrWhiteSpace(p.Authority) && string.IsNullOrWhiteSpace(p.MetadataAddress))
                errors.Add($"{at}: Authority or MetadataAddress is required.");
            if (string.IsNullOrWhiteSpace(p.ClientId)) errors.Add($"{at}: ClientId is required.");
            if (string.IsNullOrWhiteSpace(p.Audience)) errors.Add($"{at}: Audience is required.");
            if (p.ValidIssuers.Count == 0) errors.Add($"{at}: ValidIssuers is required.");
            if (p.Scopes.Count == 0) errors.Add($"{at}: Scopes is required.");
            if (string.IsNullOrWhiteSpace(p.DisplayName)) errors.Add($"{at}: DisplayName is required.");
            if (p.Kind == AuthProviderKinds.EntraId && p.AllowedTenantIds.Count == 0)
                errors.Add($"{at}: AllowedTenantIds is required for EntraId (no multi-tenant acceptance).");
        }

        foreach (var name in enabled.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Auth:Providers: the name {name.Key} is used twice.");
        }

        foreach (var issuer in enabled.SelectMany(p => p.ValidIssuers).GroupBy(i => i, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Auth:Providers: the issuer {issuer.Key} belongs to two providers.");
        }

        return errors;
    }
}

/// <summary>What the claim map found in a token: the stable subject and the e-mail (lower case).</summary>
public sealed record MappedIdentity(string? Subject, string? Email, bool EmailFromUsername);

/// <summary>
/// Task 051: the subject and the e-mail of a validated token, per provider kind.
/// <list type="bullet">
/// <item>Subject: "oid" (the object id, the same for every app of the tenant), fallback "sub".</item>
/// <item>B2C e-mail: "emails" (a JSON array; local and federated accounts), then "email".</item>
/// <item>EntraId e-mail: "email" (the optional claim AKSHI configures); "preferred_username" / "upn" only as a last
/// resort and only when they look like an e-mail — the caller logs a warning then (EmailFromUsername).</item>
/// </list>
/// </summary>
public static partial class IdentityClaimMap
{
    public const string UsernameFallbackClaim = "preferred_username";

    public static ClaimMapOptions DefaultFor(string kind) => kind == AuthProviderKinds.B2C
        ? new ClaimMapOptions { Subject = ["oid", "sub"], Email = ["emails", "email"] }
        : new ClaimMapOptions { Subject = ["oid", "sub"], Email = ["email", UsernameFallbackClaim, "upn"] };

    public static MappedIdentity Map(IEnumerable<Claim> claims, AuthProviderOptions provider)
    {
        var all = claims.ToList();
        var defaults = DefaultFor(provider.Kind);
        var subjects = provider.ClaimMap?.Subject is { Count: > 0 } s ? s : defaults.Subject;
        var emails = provider.ClaimMap?.Email is { Count: > 0 } e ? e : defaults.Email;

        var subject = subjects.Select(type => all.FirstOrDefault(c => c.Type == type)?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        foreach (var type in emails)
        {
            foreach (var claim in all.Where(c => c.Type == type))
            {
                foreach (var value in Values(claim.Value))
                {
                    if (!LooksLikeEmail(value))
                    {
                        continue;
                    }

                    var fromUsername = type is UsernameFallbackClaim or "upn";
                    return new MappedIdentity(subject, value.Trim().ToLowerInvariant(), fromUsername);
                }
            }
        }

        return new MappedIdentity(subject, null, false);
    }

    /// <summary>A claim's value, or the items of a JSON array (B2C's "emails" may arrive as '["a@b"]').</summary>
    private static IEnumerable<string> Values(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('['))
        {
            return [trimmed];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
        }
        catch (JsonException)
        {
            return [trimmed];
        }
    }

    public static bool LooksLikeEmail(string value) => EmailPattern().IsMatch(value.Trim());

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

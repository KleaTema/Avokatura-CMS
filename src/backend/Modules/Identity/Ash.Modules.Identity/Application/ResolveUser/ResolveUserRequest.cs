namespace Ash.Modules.Identity.Application.ResolveUser;

/// <summary>Exactly one of the two is set: Subject (production, OIDC sub claim) or Email (Development only).</summary>
public sealed record ResolveUserRequest(string? Subject, string? Email);

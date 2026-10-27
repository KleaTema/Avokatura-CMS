namespace Ash.Modules.Identity.Application.ResolveUser;

/// <summary>
/// Subject (production, OIDC sub claim) and/or Email. Production sends both: the user is found by subject first, then by email
/// (first-login linking). Development sends only the email (X-Dev-User header).
/// </summary>
public sealed record ResolveUserRequest(string? Subject, string? Email);

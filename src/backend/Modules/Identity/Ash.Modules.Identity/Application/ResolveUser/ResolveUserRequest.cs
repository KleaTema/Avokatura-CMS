namespace Ash.Modules.Identity.Application.ResolveUser;

/// <summary>
/// Production (task 051): Provider (Auth:Providers[].Name) + Subject (the mapped oid / sub) + Email (the mapped e-mail,
/// for the first login only). Development: only the Email of the X-Dev-User header (Provider null).
/// </summary>
public sealed record ResolveUserRequest(string? Provider, string? Subject, string? Email);

namespace Ash.Modules.Identity.Application.GetMe;

public sealed record GetMeResponse(
    Guid PublicId,
    string FirstName,
    string LastName,
    string Email,
    NamedRef? Office,
    NamedRef? Institution,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Menus);

public sealed record NamedRef(Guid PublicId, string Name);

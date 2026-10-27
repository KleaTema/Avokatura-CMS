namespace Ash.Modules.Identity.Application.ResolveUser;

public sealed record ResolveUserResponse(
    long UserId,
    Guid PublicId,
    string DisplayName,
    long? OfficeId,
    long? InstitutionId,
    IReadOnlyCollection<string> Roles,
    IReadOnlySet<string> Permissions);

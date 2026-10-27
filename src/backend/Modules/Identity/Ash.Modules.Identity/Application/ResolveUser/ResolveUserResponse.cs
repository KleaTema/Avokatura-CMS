namespace Ash.Modules.Identity.Application.ResolveUser;

public sealed record ResolveUserResponse(
    long UserId,
    Guid PublicId,
    long? OfficeId,
    long? InstitutionId,
    IReadOnlyCollection<string> Roles,
    IReadOnlySet<string> Permissions);

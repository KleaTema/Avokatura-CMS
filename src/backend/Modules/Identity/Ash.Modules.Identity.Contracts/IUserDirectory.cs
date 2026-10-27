namespace Ash.Modules.Identity.Contracts;

public sealed record UserSummary(
    Guid PublicId,
    string FirstName,
    string LastName,
    string Email,
    Guid? OfficePublicId,
    Guid? InstitutionPublicId);

public interface IUserDirectory
{
    Task<UserSummary?> GetUser(Guid publicId, CancellationToken ct = default);

    /// <summary>Active users matching name/email text and, optionally, a role code.</summary>
    Task<IReadOnlyList<UserSummary>> FindUsers(string? query, string? role, CancellationToken ct = default);
}

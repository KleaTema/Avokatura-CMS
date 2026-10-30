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

    /// <summary>Public ids of users known by internal id (other modules store internal ids in foreign keys).</summary>
    Task<IReadOnlyDictionary<long, Guid>> GetPublicIdsAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);

    /// <summary>Internal ids of users known by public id (filters that arrive with a public id).</summary>
    Task<IReadOnlyDictionary<Guid, long>> GetIdsAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct = default);

    /// <summary>"First Last" per internal user id, in one call (e.g. the primary lawyers of a list page).</summary>
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);

    /// <summary>Active users matching name/email text and, optionally, a role code.</summary>
    Task<IReadOnlyList<UserSummary>> FindUsers(string? query, string? role, CancellationToken ct = default);
}

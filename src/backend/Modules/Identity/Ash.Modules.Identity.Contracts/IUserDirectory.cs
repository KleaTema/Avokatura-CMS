namespace Ash.Modules.Identity.Contracts;

public sealed record UserSummary(
    Guid PublicId,
    string FirstName,
    string LastName,
    string Email,
    Guid? OfficePublicId,
    Guid? InstitutionPublicId);

/// <summary>
/// A user as another module shows them: public id and "First Last". InstitutionId is internal (only for matching
/// foreign keys, e.g. a jurist to the institution link), never sent out.
/// </summary>
public sealed record UserRef(Guid PublicId, string Name, long? InstitutionId);

public interface IUserDirectory
{
    Task<UserSummary?> GetUser(Guid publicId, CancellationToken ct = default);

    /// <summary>Public ids of users known by internal id (other modules store internal ids in foreign keys).</summary>
    Task<IReadOnlyDictionary<long, Guid>> GetPublicIdsAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);

    /// <summary>Internal ids of users known by public id (filters that arrive with a public id).</summary>
    Task<IReadOnlyDictionary<Guid, long>> GetIdsAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct = default);

    /// <summary>"First Last" per internal user id, in one call (e.g. the primary lawyers of a list page).</summary>
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);

    /// <summary>Public id + name (+ institution) per internal user id, inactive users included, in one call.</summary>
    Task<IReadOnlyDictionary<long, UserRef>> GetRefsAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default);

    /// <summary>Active users matching name/email text and, optionally, a role code.</summary>
    Task<IReadOnlyList<UserSummary>> FindUsers(string? query, string? role, CancellationToken ct = default);
}

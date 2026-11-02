namespace Ash.Modules.Identity.Contracts;

public sealed record OfficeHead(long UserId, Guid PublicId, string FullName, bool IsActive);

/// <summary>Where new cases of an office go: the office's state and its head (Kryetari), if any.</summary>
public sealed record OfficeRouting(long OfficeId, string Name, bool IsActive, OfficeHead? Head);

public interface IOfficeDirectory
{
    /// <summary>Null when the office does not exist.</summary>
    Task<OfficeRouting?> GetRoutingAsync(long officeId, CancellationToken ct = default);

    /// <summary>The internal id of an office known by public id (filters that arrive with a public id); null when unknown.</summary>
    Task<long?> GetIdAsync(Guid officePublicId, CancellationToken ct = default);
}

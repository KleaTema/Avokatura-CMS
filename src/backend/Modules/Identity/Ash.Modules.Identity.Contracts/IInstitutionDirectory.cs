namespace Ash.Modules.Identity.Contracts;

/// <summary>An institution as other modules see it. The id is internal: use it only for foreign keys, never send it out.</summary>
public sealed record InstitutionInfo(long Id, Guid PublicId, string Name, bool IsActive);

public interface IInstitutionDirectory
{
    /// <summary>The institutions with these public ids (inactive ones included); unknown ids are simply missing.</summary>
    Task<IReadOnlyList<InstitutionInfo>> GetByPublicIdsAsync(IReadOnlyCollection<Guid> publicIds, CancellationToken ct = default);

    /// <summary>By internal id (as stored in other modules' foreign keys); unknown ids are simply missing.</summary>
    Task<IReadOnlyList<InstitutionInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);
}

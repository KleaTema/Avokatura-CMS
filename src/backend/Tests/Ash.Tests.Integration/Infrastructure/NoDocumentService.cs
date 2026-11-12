using Ash.Modules.Documents.Contracts;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// IDocumentService for test service collections built without the Documents module: CaseAccessService asks the
/// archive rule (task 053a) for the decision document, and there is none — so no level is archivable.
/// </summary>
public sealed class NoDocumentService : IDocumentService
{
    public Task<DocumentInfo?> GetAsync(Guid publicId, CancellationToken ct = default) => Task.FromResult<DocumentInfo?>(null);
    public Task<DocumentInfo?> GetByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<DocumentInfo?>(null);
    public Task<IReadOnlyDictionary<long, int>> CountsByCaseAsync(IReadOnlyCollection<long> caseIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, int>>(new Dictionary<long, int>());
    public Task<bool> IsCleanAsync(Guid publicId, CancellationToken ct = default) => Task.FromResult(false);
    public Task<int> CountFailedScansAsync(CancellationToken ct = default) => Task.FromResult(0);
}

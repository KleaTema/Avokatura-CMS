namespace Ash.Modules.Documents.Contracts;

/// <summary>
/// A document as other modules see it (task 034). Id is internal — only for foreign keys such as
/// case_decisions.decision_document_id — and never sent out.
/// </summary>
public sealed record DocumentInfo(
    long Id, Guid PublicId, long DossierId, long? CaseId, string Title, string DocumentTypeCode, string ScanStatus, bool IsDeleted);

/// <summary>What other modules may ask about documents (the decision and security-measure tasks link one; the case page counts).</summary>
public interface IDocumentService
{
    /// <summary>The document by public id (deleted ones too: IsDeleted), or null when it does not exist.</summary>
    Task<DocumentInfo?> GetAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>By internal id (as stored in case_decisions.decision_document_id), deleted ones too; null when none.</summary>
    Task<DocumentInfo?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Documents uploaded to each level (not deleted), by case id; a level without documents is missing.</summary>
    Task<IReadOnlyDictionary<long, int>> CountsByCaseAsync(IReadOnlyCollection<long> caseIds, CancellationToken ct = default);

    /// <summary>True only for an existing, not deleted, CLEAN document (only those may be linked or downloaded).</summary>
    Task<bool> IsCleanAsync(Guid publicId, CancellationToken ct = default);
}

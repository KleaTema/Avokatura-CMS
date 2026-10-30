namespace Ash.Modules.Documents.Contracts;

/// <summary>Document numbers other modules show (e.g. the case page). The case id is internal (a foreign key), never sent out.</summary>
public interface IDocumentCounts
{
    /// <summary>Non-deleted documents uploaded to this level (documents.case_id).</summary>
    Task<int> CountForCaseAsync(long caseId, CancellationToken ct = default);
}

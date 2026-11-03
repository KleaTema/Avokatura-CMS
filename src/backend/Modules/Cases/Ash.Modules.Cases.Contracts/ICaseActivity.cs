namespace Ash.Modules.Cases.Contracts;

/// <summary>A case as other modules see it: internal ids only for their foreign keys, never sent out.</summary>
public sealed record CaseRef(long Id, long DossierId, string CaseCode, bool IsClosed);

/// <summary>
/// What other modules (Documents first, task 033) may do with a case without touching Cases tables: find it for the
/// current user, write its history and mark the first data entry — all in the current unit of work (transaction).
/// The case's actions are checked with <see cref="ICaseAccessService.GetEffectivePermissionsAsync"/>.
/// </summary>
public interface ICaseActivity
{
    /// <summary>The case by public id, or null when it does not exist or the current user cannot view it (→ 404).</summary>
    Task<CaseRef?> FindViewableAsync(Guid casePublicId, CancellationToken ct = default);

    /// <summary>
    /// The case by internal id, without an access check: for background jobs that act on rows the module already owns
    /// (e.g. the scan job writing DOCUMENT_REJECTED). Null when it does not exist.
    /// </summary>
    Task<CaseRef?> FindAsync(long caseId, CancellationToken ct = default);

    /// <summary>
    /// One history row, saved at once in the current transaction (rolled back with it). Without a current user (a
    /// background job) the actor is the system. Same rules as <see cref="IHistoryWriter.Add"/>: no file contents, no
    /// storage keys, no secrets.
    /// </summary>
    Task AddHistoryAsync(long dossierId, long? caseId, HistoryEventType type, string summary, object? @new = null, CancellationToken ct = default);

    /// <summary>
    /// Sets cases.first_data_entry_at the first time a member of the team writes data (stops the 30-day reminder);
    /// nothing for anyone else or when already set. In the current transaction.
    /// </summary>
    Task MarkFirstDataEntryAsync(long caseId, CancellationToken ct = default);
}

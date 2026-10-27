namespace Ash.Modules.Cases.Contracts;

/// <summary>Writes the business history (Historiku) of a case through the current unit of work.</summary>
public interface IHistoryWriter
{
    /// <summary>
    /// Adds a history row to the Cases DbContext; it is stored when the handler calls SaveChangesAsync and
    /// is committed or rolled back with the request transaction.
    /// <paramref name="summary"/> is Albanian text, at most 500 characters (longer text is rejected with ArgumentException).
    /// <paramref name="old"/> and <paramref name="new"/> are objects with the fields that may have changed; only changed fields are stored.
    /// Pass <c>old: null</c> for creations. Never put file contents or secrets in them.
    /// </summary>
    void Add(long dossierId, long? caseId, HistoryEventType type, string summary, object? old = null, object? @new = null);
}

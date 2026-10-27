namespace Ash.Modules.Cases.Contracts;

/// <summary>Read-only view of how many cases a user currently works on (used by the user administration).</summary>
public interface ICaseAssignmentCounts
{
    /// <summary>Number of current (not ended) case assignments per user id; users without any are missing from the result.</summary>
    Task<IReadOnlyDictionary<long, int>> CountCurrentAsync(
        IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);
}

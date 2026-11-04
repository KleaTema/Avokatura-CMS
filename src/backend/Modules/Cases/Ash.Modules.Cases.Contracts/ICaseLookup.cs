namespace Ash.Modules.Cases.Contracts;

/// <summary>
/// A case as a notification needs it (task 040): ids, code and court — never its object or other content. The internal
/// id only for foreign keys (notifications.case_id).
/// </summary>
public sealed record CaseSummary(long Id, Guid PublicId, string CaseCode, string CourtName, long OfficeId);

/// <summary>A current assignment: the user's internal id and the case role (PRIMARY_LAWYER, SECONDARY_LAWYER, …).</summary>
public sealed record CaseMember(long UserId, string Role);

/// <summary>
/// Read-only facts about a case for modules that react to its events (Notifications). NO access check: callers act for
/// the system (e.g. "tell the team"), never to show the case to the current user — that is ICaseAccessService's job.
/// </summary>
public interface ICaseLookup
{
    Task<CaseSummary?> GetAsync(Guid casePublicId, CancellationToken ct = default);

    Task<CaseSummary?> GetAsync(long caseId, CancellationToken ct = default);

    /// <summary>The case's current assignments (lawyers, assistants, institution jurists), not ended ones.</summary>
    Task<IReadOnlyList<CaseMember>> GetTeamAsync(long caseId, CancellationToken ct = default);
}

namespace Ash.Modules.Cases.Application.RecordDecision;

/// <summary>
/// The decision of a level, as POST (record + close, task 036) and PUT (complete later, task 037) receive it — the
/// whole decision each time. Money and m² as decimal STRINGS ("1250000.50"), one currency for all (default ALL).
/// RowVersion: the CASE's on POST, the DECISION's on PUT. CancelOpenHearings: POST only — the case's SCHEDULED
/// hearings are cancelled with the closing (otherwise a 409 lists them).
/// </summary>
public sealed record DecisionRequest(
    string? ResultCode,
    string? ResultOtherText,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    string? Dispositive,
    string? Currency,
    string? WonAmount,
    string? WonAreaM2,
    string? LostAmount,
    string? LostAreaM2,
    string? PreventedDamageAmount,
    string? PreventedDamageAreaM2,
    string? CausedDamageAmount,
    string? CausedDamageAreaM2,
    string? CourtCostsAmount,
    bool? DecisionReceived,
    Guid? DecisionDocumentPublicId,
    bool? IsAppealed,
    bool? IsRecursed,
    bool? IsFinal,
    bool? CancelOpenHearings,
    uint? RowVersion);

/// <summary>The linked decision document (a VENDIM of the dossier), shown by its title.</summary>
public sealed record DecisionDocumentView(Guid PublicId, string Title);

/// <summary>
/// What the UI may offer next: RETRIAL_EXPECTED (the result sends the case back for retrial — "Vazhdo si rigjykim",
/// task 018) and ARCHIVE_SUGGESTED (the decision is final — archive the dossier, task 038).
/// </summary>
public static class DecisionSuggestions
{
    public const string RetrialExpected = "RETRIAL_EXPECTED";
    public const string ArchiveSuggested = "ARCHIVE_SUGGESTED";
}

/// <summary>POST /decision: the case is CLOSED now; both row versions for the next save.</summary>
public sealed record RecordDecisionResponse(
    uint CaseRowVersion,
    DateTime ClosedAt,
    int CancelledHearings,
    GetCase.CaseDecisionSummary Decision,
    IReadOnlyList<string> Suggestions);

/// <summary>PUT /decision: the decision as saved (its new rowVersion inside).</summary>
public sealed record UpdateDecisionResponse(GetCase.CaseDecisionSummary Decision, IReadOnlyList<string> Suggestions);

/// <summary>A SCHEDULED hearing that blocks the closing (409 case.open_hearings lists them).</summary>
public sealed record OpenHearingView(Guid PublicId, DateTime ScheduledAt, string HearingType);

using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Application.SetSecurityMeasure;

namespace Ash.Modules.Cases.Application.GetCase;

/// <summary>
/// The case page (task 020). Public ids and names only, never internal ids. <see cref="Permissions"/> is what the user
/// may do in this case now (the UI hides the other buttons; the API checks the same list).
/// </summary>
public sealed record GetCaseResponse(
    CaseHeader Header,
    CaseData Data,
    IReadOnlyList<CaseTeamMember> Team,
    IReadOnlyList<CaseInstitutionView> Institutions,
    IReadOnlyList<PartyResponse> Parties,
    int HearingsCount,
    int DocumentsCount,
    CaseDecisionSummary? Decision,
    IReadOnlyList<CaseLevelView> Levels,
    IReadOnlyList<string> Permissions);

/// <summary>DossierPublicId: for POST /api/dossiers/{id}/archive; IsArchived: the dossier is read-only (task 038).</summary>
public sealed record CaseHeader(
    Guid PublicId, string CaseCode, int LevelSeq, string Status, string Priority, bool IsSuspended, uint RowVersion,
    Guid DossierPublicId = default, bool IsArchived = false);

public sealed record CodeName(string Code, string Name);

public sealed record UserView(Guid UserPublicId, string Name);

/// <summary>Amount and m² as decimal strings with two decimals ("1250000.50"), never JSON numbers.</summary>
public sealed record ClaimView(string? Amount, string? Currency, string? AreaM2);

public sealed record CaseData(
    string SummonsNumber,
    CodeName? SummonsType,
    string ActNumber,
    DateOnly? ActDate,
    DateOnly ReceivedDate,
    CodeName? CourtLevel,
    string CourtName,
    string? JudgeName,
    string ObjectText,
    CodeName? Category,
    CodeName? Subcategory,
    ClaimView? Claim,
    bool HasSecurityMeasure,
    /// <summary>The measure's decision while it is active; null when there is none or it was lifted.</summary>
    SecurityMeasureView? SecurityMeasure,
    /// <summary>Since when the case is suspended (null when it is not).</summary>
    DateTime? SuspendedAt,
    DateTime? NextHearingAt,
    /// <summary>The next hearing's time has passed and it was not completed yet (task 030): the UI marks it.</summary>
    bool NextHearingIsOverdue,
    DateTime RegisteredAt,
    UserView? RegisteredBy,
    DateTime? ClosedAt,
    /// <summary>
    /// court_levels.appeal_kind of the level: ANKIM → the decision offers "Ankimuar", REKURS → "Rekursuar", null →
    /// neither ("Formë e prerë" on every level).
    /// </summary>
    string? AppealKind = null);

/// <summary>A current assignment: PRIMARY_LAWYER | SECONDARY_LAWYER | LEGAL_ASSISTANT | INSTITUTION_JURIST.</summary>
public sealed record CaseTeamMember(string Role, Guid UserPublicId, string Name, DateTime AssignedAt);

/// <summary>An active institution link (MONITORING | DELEGATED) with the institution's jurists currently on the case.</summary>
public sealed record CaseInstitutionView(Guid InstitutionPublicId, string Name, string Mode, IReadOnlyList<string> JuristNames);

/// <summary>
/// The whole decision of the level (tasks 036/037): money and m² as decimal strings with two decimals, the linked
/// decision document by public id and title, the decision's own RowVersion (PUT /decision sends it back).
/// </summary>
public sealed record CaseDecisionSummary(
    CodeName Result,
    string? ResultOtherText,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    bool DecisionReceived,
    bool IsAppealed,
    bool IsRecursed,
    bool IsFinal,
    string? Dispositive,
    string Currency,
    string? WonAmount,
    string? WonAreaM2,
    string? LostAmount,
    string? LostAreaM2,
    string? PreventedDamageAmount,
    string? PreventedDamageAreaM2,
    string? CausedDamageAmount,
    string? CausedDamageAreaM2,
    string? CourtCostsAmount,
    DateTime? DecisionReceivedAt,
    RecordDecision.DecisionDocumentView? Document,
    /// <summary>The result sends the case back for retrial (decision_results.sends_back_for_retrial).</summary>
    bool SendsBackForRetrial,
    DateTime RecordedAt,
    uint RowVersion);

/// <summary>A level X/n of the dossier. PublicId only when the user can open it.</summary>
public sealed record CaseLevelView(string CaseCode, int LevelSeq, string CourtLevelName, string Status, Guid? PublicId, bool CanOpen);

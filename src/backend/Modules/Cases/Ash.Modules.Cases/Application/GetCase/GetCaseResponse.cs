using Ash.Modules.Cases.Application.Parties;

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

public sealed record CaseHeader(
    Guid PublicId, string CaseCode, int LevelSeq, string Status, string Priority, bool IsSuspended, uint RowVersion);

public sealed record CodeName(string Code, string Name);

public sealed record UserView(Guid UserPublicId, string Name);

public sealed record ClaimView(decimal? Amount, string? Currency, decimal? AreaM2);

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
    DateTime? NextHearingAt,
    DateTime RegisteredAt,
    UserView? RegisteredBy,
    DateTime? ClosedAt);

/// <summary>A current assignment: PRIMARY_LAWYER | SECONDARY_LAWYER | LEGAL_ASSISTANT | INSTITUTION_JURIST.</summary>
public sealed record CaseTeamMember(string Role, Guid UserPublicId, string Name, DateTime AssignedAt);

/// <summary>An active institution link (MONITORING | DELEGATED) with the institution's jurists currently on the case.</summary>
public sealed record CaseInstitutionView(Guid InstitutionPublicId, string Name, string Mode, IReadOnlyList<string> JuristNames);

public sealed record CaseDecisionSummary(
    CodeName Result,
    string? ResultOtherText,
    string? DecisionNumber,
    DateOnly? DecisionDate,
    bool DecisionReceived,
    bool IsAppealed,
    bool IsRecursed,
    bool IsFinal);

/// <summary>A level X/n of the dossier. PublicId only when the user can open it.</summary>
public sealed record CaseLevelView(string CaseCode, int LevelSeq, string CourtLevelName, string Status, Guid? PublicId, bool CanOpen);

namespace Ash.Modules.Reference.Application;

/// <summary>Lookup lists are identified by code; courts and judges (no code) by numeric id.</summary>
public sealed record CourtLevelResponse(string Code, string Name, bool AllowsDelegation, string? AppealKind);

public sealed record CourtResponse(int Id, string Name, string? CourtLevelCode, string? City, bool IsForeign);

public sealed record JudgeResponse(long Id, string FirstName, string LastName, int? CourtId);

public sealed record PartyRoleResponse(string Code, string Name, string ReportGroup);

public sealed record SummonsTypeResponse(string Code, string Name);

public sealed record HearingTypeResponse(string Code, string Name, bool IsOther);

public sealed record DocumentTypeResponse(string Code, string Name, bool IsOther);

public sealed record DecisionResultResponse(string Code, string Name, string OutcomeGroup, bool SendsBackForRetrial, bool IsOther);

public sealed record CaseCategoryResponse(string Code, string Name, bool IsDelegation);

public sealed record CaseSubcategoryResponse(string Code, string Name, string? CategoryCode);

public sealed record AllowedFileTypeResponse(string Extension, string ContentType, ulong MaxSizeBytes, string AppliesTo);

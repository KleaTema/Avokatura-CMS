namespace Ash.Modules.Reference.Contracts;

public sealed record CourtLevelDto(byte Id, string Code, string Name, byte SortOrder, bool AllowsDelegation, string? AppealKind);

public sealed record CourtDto(ushort Id, string Name, byte CourtLevelId, string? City, bool IsForeign, bool IsActive);

public sealed record JudgeDto(uint Id, string FirstName, string LastName, ushort? CourtId, bool IsActive);

/// <summary>CopyToNextLevel (task 053j): the party is copied when the case continues at the next level.</summary>
public sealed record PartyRoleDto(byte Id, string Code, string Name, string ReportGroup, byte SortOrder, bool IsActive, bool CopyToNextLevel = false);

public sealed record SummonsTypeDto(byte Id, string Code, string Name, bool IsActive);

public sealed record HearingTypeDto(byte Id, string Code, string Name, bool IsOther, byte SortOrder, bool IsActive);

public sealed record DocumentTypeDto(byte Id, string Code, string Name, bool IsOther, byte SortOrder, bool IsActive);

public sealed record DecisionResultDto(
    byte Id, string Code, string Name, string OutcomeGroup, bool SendsBackForRetrial, bool IsOther, byte SortOrder, bool IsActive);

public sealed record CaseCategoryDto(byte Id, string Code, string Name, bool IsDelegation, byte SortOrder);

public sealed record CaseSubcategoryDto(byte Id, byte CategoryId, string Code, string Name, byte SortOrder);

public sealed record AllowedFileTypeDto(
    byte Id, string Extension, string ContentType, string MagicHex, ulong MaxSizeBytes, string AppliesTo, bool IsActive);

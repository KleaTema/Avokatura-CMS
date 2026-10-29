namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>
/// What Protokoll sends from the summons (fletëthirrje). Codes for lookups; numeric ids only for courts and
/// judges (as in task 012). Everything is nullable so a missing field becomes a field error, not a binding error.
/// The parties are saved in the same transaction (at least one); later changes go through task 017's endpoints.
/// </summary>
public sealed record RegisterCaseRequest(
    string? SummonsNumber,
    string? SummonsTypeCode,
    string? ActNumber,
    DateOnly? ActDate,
    DateOnly? ReceivedDate,
    string? CourtLevelCode,
    int? CourtId,
    long? JudgeId,
    string? ObjectText,
    FirstHearingRequest? FirstHearing,
    IReadOnlyList<PartyRequest>? Parties,
    bool ConfirmPossibleDuplicate = false);

/// <summary>
/// Kind is NATURAL, LEGAL or INSTITUTION. InstitutionPublicId is required for INSTITUTION and not allowed otherwise;
/// for an institution DisplayName is ignored (the institution's current name is stored). For LEGAL the identifier is a NIPT.
/// </summary>
public sealed record PartyRequest(
    string? PartyRoleCode, string? Kind, Guid? InstitutionPublicId, string? DisplayName, string? Identifier);

/// <summary><see cref="At"/> is ISO 8601 with an offset or Z, e.g. 2026-11-05T09:30:00+01:00; it is stored in UTC.</summary>
public sealed record FirstHearingRequest(string? At, string? HearingTypeCode, string? OtherTypeText);

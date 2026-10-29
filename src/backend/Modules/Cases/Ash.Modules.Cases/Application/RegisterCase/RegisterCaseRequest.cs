namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>
/// What Protokoll sends from the summons (fletëthirrje). Codes for lookups; numeric ids only for courts and
/// judges (as in task 012). Everything is nullable so a missing field becomes a field error, not a binding error.
/// Parties are added afterwards by task 017's endpoint, once the case exists.
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
    bool ConfirmPossibleDuplicate = false);

/// <summary><see cref="At"/> is ISO 8601 with an offset or Z, e.g. 2026-11-05T09:30:00+01:00; it is stored in UTC.</summary>
public sealed record FirstHearingRequest(string? At, string? HearingTypeCode, string? OtherTypeText);

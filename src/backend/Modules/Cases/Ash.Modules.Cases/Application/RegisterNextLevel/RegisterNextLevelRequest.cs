using Ash.Modules.Cases.Application.RegisterCase;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

/// <summary>
/// The new summons for an existing dispute (appeal, recourse, retrial). Same fields as a registration (task 016)
/// except the object text and the parties, which are copied from the previous level.
/// </summary>
public sealed record RegisterNextLevelRequest(
    string? SummonsNumber,
    string? SummonsTypeCode,
    string? ActNumber,
    DateOnly? ActDate,
    DateOnly? ReceivedDate,
    string? CourtLevelCode,
    int? CourtId,
    long? JudgeId,
    FirstHearingRequest? FirstHearing,
    bool ConfirmPossibleDuplicate = false)
{
    /// <summary>As a registration form, with the copied object text, so the 016 validation applies unchanged.</summary>
    internal RegisterCaseRequest AsRegistration(string objectText) =>
        new(SummonsNumber, SummonsTypeCode, ActNumber, ActDate, ReceivedDate, CourtLevelCode, CourtId, JudgeId,
            objectText, FirstHearing, Parties: null, ConfirmPossibleDuplicate);
}

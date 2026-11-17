using Ash.Modules.Cases.Application.RegisterCase;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

/// <summary>
/// The new summons for an existing dispute (appeal, recourse, retrial). Same fields as a registration (task 016)
/// except the object text, which is copied from the previous level, as are the parties whose role is copied
/// (party_roles.copy_to_next_level: the plaintiff and the defendant, task 053j). Parties: MORE parties the Protokoll
/// adds in the form (optional). OfficePublicId: "Zyra që e ndjek", as in the registration.
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
    bool ConfirmPossibleDuplicate = false,
    IReadOnlyList<PartyRequest>? Parties = null,
    Guid? OfficePublicId = null)
{
    /// <summary>As a registration form, with the copied object text, so the 016 validation applies unchanged.</summary>
    internal RegisterCaseRequest AsRegistration(string objectText) =>
        new(SummonsNumber, SummonsTypeCode, ActNumber, ActDate, ReceivedDate, CourtLevelCode, CourtId, JudgeId,
            objectText, FirstHearing, Parties, ConfirmPossibleDuplicate, OfficePublicId);
}

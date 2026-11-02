using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.RegisterCase;

namespace Ash.Modules.Cases.Application.Hearings;

/// <summary>
/// A hearing as the API shows it: public id only, the type by code and name, times in UTC, the names of who created
/// and last changed it, its own row version, and whether a SCHEDULED hearing's time has passed (isOverdue).
/// </summary>
public sealed record HearingView(
    Guid PublicId,
    CodeName Type,
    string? OtherTypeText,
    DateTime ScheduledAt,
    string Status,
    string? Description,
    string? CreatedBy,
    string? UpdatedBy,
    uint RowVersion,
    bool IsOverdue);

/// <summary>The saved hearing and warnings to show (the save went through).</summary>
public sealed record HearingSaveResponse(HearingView Hearing, IReadOnlyList<RegistrationWarning> Warnings);

/// <summary>The completed hearing, the next one when it was scheduled in the same step, and warnings.</summary>
public sealed record CompleteHearingResponse(HearingView Hearing, HearingView? Next, IReadOnlyList<RegistrationWarning> Warnings);

/// <summary>To pre-fill the new hearing form: the previous hearing's type and description (nulls when none).</summary>
public sealed record NewHearingDefaults(CodeName? Type, string? OtherTypeText, string? Description);

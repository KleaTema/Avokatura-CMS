using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// The Kryetar changed the lawyers of an ACTIVE case: the primary and/or the secondary, as they were before and are
/// after (an unchanged primary has Old = New; a secondary is null when there is none). Handlers run in the same
/// transaction.
/// Notifications (task 040) tell the old and the new lawyer of each role that changed.
/// </summary>
public sealed record LawyerTransferred(
    Guid CasePublicId,
    Guid OldPrimaryUserPublicId,
    Guid NewPrimaryUserPublicId,
    Guid? OldSecondaryUserPublicId,
    Guid? NewSecondaryUserPublicId) : IEvent;

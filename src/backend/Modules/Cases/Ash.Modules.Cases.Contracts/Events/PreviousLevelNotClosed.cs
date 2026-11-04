using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A next-level summons arrived for a level that is not closed in the system: the lawyer must record the decision
/// first (client answer Q1b: Protokoll waits for the lawyer and never edits the previous level).
/// Recipients: the current primary lawyer (if any) and the office head. Published only from
/// POST /api/cases/{id}/notify-not-closed, inside that request's committed transaction.
/// Notifications (task 040) tell the lawyer and the Kryetar, with the new summons' hearing date.
/// TODO(task-041): reminder to lawyer + Kryetar if the level is still not closed 3 days before HearingAt.
/// </summary>
public sealed record PreviousLevelNotClosed(
    Guid PreviousCasePublicId,
    string SummonsNumber,
    DateTime HearingAt,
    Guid? PrimaryLawyerPublicId,
    Guid? HeadUserPublicId) : IEvent;

using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A hearing was scheduled (added, or created as the next one when completing a hearing). Times in UTC. Handlers run in
/// the same transaction. TODO(task-041): reminders for the team before the hearing.
/// </summary>
public sealed record HearingScheduled(Guid CasePublicId, Guid HearingPublicId, DateTime ScheduledAt) : IEvent;

/// <summary>A SCHEDULED hearing moved to another time. TODO(task-041): move its reminders.</summary>
public sealed record HearingRescheduled(Guid CasePublicId, Guid HearingPublicId, DateTime OldScheduledAt, DateTime NewScheduledAt) : IEvent;

/// <summary>
/// A hearing was HELD, POSTPONED or CANCELLED; <see cref="NextHearingPublicId"/> when the next one was scheduled in the
/// same step. TODO(task-041): drop the reminders of a hearing that will not take place.
/// </summary>
public sealed record HearingCompleted(Guid CasePublicId, Guid HearingPublicId, string Outcome, Guid? NextHearingPublicId) : IEvent;

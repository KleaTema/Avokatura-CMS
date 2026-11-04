using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A hearing was scheduled (added, or created as the next one when completing a hearing). Times in UTC. Handlers run in
/// the same transaction. The team's reminder the day before: HearingReminderJob (task 041), from the hearing row.
/// </summary>
public sealed record HearingScheduled(Guid CasePublicId, Guid HearingPublicId, DateTime ScheduledAt) : IEvent;

/// <summary>A SCHEDULED hearing moved to another time. Its reminder follows by itself: the job reads the current date (a new date is a new reminder key, task 041).</summary>
public sealed record HearingRescheduled(Guid CasePublicId, Guid HearingPublicId, DateTime OldScheduledAt, DateTime NewScheduledAt) : IEvent;

/// <summary>
/// A hearing was HELD, POSTPONED or CANCELLED; <see cref="NextHearingPublicId"/> when the next one was scheduled in the
/// same step. A hearing no longer SCHEDULED gets no reminder (the job only reads SCHEDULED ones, task 041).
/// </summary>
public sealed record HearingCompleted(Guid CasePublicId, Guid HearingPublicId, string Outcome, Guid? NextHearingPublicId) : IEvent;

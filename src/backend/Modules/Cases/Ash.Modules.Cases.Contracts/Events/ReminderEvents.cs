using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// Task 041, DataReminderJob: an ACTIVE case still has no data entry 30 days (ReminderType DATA_MISSING_30D) or 6 months
/// (DATA_MISSING_6M) after its CURRENT primary assignment. Published once per assignment and type, in the transaction
/// that inserts the case_reminders row. Recipients chosen by the job: the primary lawyer and the office head;
/// Notifications adds the active Monitors for 6 months.
/// </summary>
public sealed record DataEntryReminderDue(
    Guid CasePublicId, string ReminderType, DateTime AssignedAt, Guid? PrimaryLawyerPublicId, Guid? HeadUserPublicId) : IEvent
{
    public const string ThirtyDays = "DATA_MISSING_30D";
    public const string SixMonths = "DATA_MISSING_6M";
}

/// <summary>
/// Task 041, HearingReminderJob: a SCHEDULED hearing is tomorrow (Tirane date); sent once after 08:00 Tirane the day
/// before (a new date after a reschedule is a new reminder). Recipients: the case's current team.
/// </summary>
public sealed record HearingTomorrow(Guid CasePublicId, Guid HearingPublicId, DateTime ScheduledAt) : IEvent;

/// <summary>
/// Task 041, HearingReminderJob: the next-level summons of a NEXT_LEVEL_SUMMONS_PENDING notice (task 018) has its
/// hearing within 3 days and this level is still not CLOSED. Recipients chosen by the job: the current primary lawyer
/// (if any) and the office head.
/// </summary>
public sealed record NextLevelSummonsDueSoon(
    Guid CasePublicId, string SummonsNumber, DateTime HearingAt, Guid? PrimaryLawyerPublicId, Guid? HeadUserPublicId) : IEvent;

namespace Ash.Modules.Notifications.Contracts;

/// <summary>
/// The one list of notifications.notification_type codes (VARCHAR(40)). Their Albanian texts live in one place too:
/// NotificationTexts in the Notifications module. Task 041 adds the reminder types here.
/// </summary>
public static class NotificationTypes
{
    /// <summary>A new case waits for the office head to assign its lawyers.</summary>
    public const string CaseRegistered = "CASE_REGISTERED";

    /// <summary>The user became the primary or the secondary lawyer of a case (assignment or transfer).</summary>
    public const string LawyerAssigned = "LAWYER_ASSIGNED";

    /// <summary>The user is no longer the primary or the secondary lawyer of a case (transfer).</summary>
    public const string LawyerRemoved = "LAWYER_REMOVED";

    /// <summary>A case linked to the user's institution got its lawyers.</summary>
    public const string InstitutionCaseAssigned = "INSTITUTION_CASE_ASSIGNED";

    public const string AssistantAssigned = "ASSISTANT_ASSIGNED";
    public const string AssistantRemoved = "ASSISTANT_REMOVED";

    public const string JuristAssigned = "JURIST_ASSIGNED";

    /// <summary>The institution assigned another jurist in place of the user.</summary>
    public const string JuristReplaced = "JURIST_REPLACED";

    /// <summary>The user's institution was linked to a case (monitoring or delegated).</summary>
    public const string InstitutionLinked = "INSTITUTION_LINKED";

    public const string CaseDelegated = "CASE_DELEGATED";
    public const string CaseUndelegated = "CASE_UNDELEGATED";

    /// <summary>A next-level summons arrived while this level is not closed in the system (task 018).</summary>
    public const string PreviousLevelNotClosed = "PREVIOUS_LEVEL_NOT_CLOSED";

    public const string CaseClosed = "CASE_CLOSED";

    public const string DocumentInfected = "DOCUMENT_INFECTED";
    public const string DocumentScanFailed = "DOCUMENT_SCAN_FAILED";

    // task 041 — the reminders of the background jobs

    /// <summary>No data entered 30 days after the primary assignment (same code as case_reminders.reminder_type).</summary>
    public const string DataMissing30D = "DATA_MISSING_30D";

    /// <summary>No data entered 6 months after the primary assignment; the Monitors are told too.</summary>
    public const string DataMissing6M = "DATA_MISSING_6M";

    public const string HearingTomorrow = "HEARING_TOMORROW";

    /// <summary>A pending next-level summons has its hearing within 3 days and the level is still not closed.</summary>
    public const string NextLevelPending3D = "NEXT_LEVEL_PENDING_3D";

    /// <summary>To the administrators: a partitioned table's p_max holds rows, or next month's partition is missing.</summary>
    public const string PartitionAlert = "PARTITION_ALERT";

    public static readonly IReadOnlyList<string> All =
    [
        CaseRegistered, LawyerAssigned, LawyerRemoved, InstitutionCaseAssigned, AssistantAssigned, AssistantRemoved,
        JuristAssigned, JuristReplaced, InstitutionLinked, CaseDelegated, CaseUndelegated, PreviousLevelNotClosed,
        CaseClosed, DocumentInfected, DocumentScanFailed, DataMissing30D, DataMissing6M, HearingTomorrow,
        NextLevelPending3D, PartitionAlert,
    ];
}

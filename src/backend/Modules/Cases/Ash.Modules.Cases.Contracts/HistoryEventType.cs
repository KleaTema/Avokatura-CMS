namespace Ash.Modules.Cases.Contracts;

/// <summary>
/// The list of record of case_history_events.event_type (stored codes: HistoryEventCodes). 004_core_documents_history.sql
/// lists the original codes and says to keep the list here; codes are only ever added, never removed or renamed.
/// </summary>
public enum HistoryEventType
{
    DossierCreated,
    CaseRegistered,
    CaseCreatedFromPrevious,
    /// <summary>NEXT_LEVEL_SUMMONS_PENDING (task 018): a next-level summons arrived while this level is not closed.</summary>
    NextLevelSummonsPending,
    SentToHead,
    LawyerAssigned,
    LawyerTransferred,
    SecondaryAssigned,
    /// <summary>SECONDARY_REMOVED (task 024): the secondary lawyer left the case (removed, or moved up to primary).</summary>
    SecondaryRemoved,
    AssistantAdded,
    AssistantRemoved,
    PriorityChanged,
    SuspensionChanged,
    CategoryChanged,
    Delegated,
    /// <summary>UNDELEGATED (task 027): the delegation ended; the institution monitors the case again.</summary>
    Undelegated,
    InstitutionLinked,
    /// <summary>INSTITUTION_UNLINKED (task 017): a MONITORING link ended when the institution's last party was removed.</summary>
    InstitutionUnlinked,
    JuristAssigned,
    JudgeChanged,
    ObjectChanged,
    PartyAdded,
    /// <summary>PARTY_UPDATED (task 017): role, name or identifier of a party changed.</summary>
    PartyUpdated,
    PartyRemoved,
    HearingAdded,
    HearingUpdated,
    /// <summary>HEARING_COMPLETED (task 030): a hearing was HELD, POSTPONED or CANCELLED (with the next one, if set).</summary>
    HearingCompleted,
    DocumentUploaded,
    DocumentRemoved,
    SecurityMeasureSet,
    /// <summary>SECURITY_MEASURE_LIFTED (task 025): the security measure was lifted (the row is kept).</summary>
    SecurityMeasureLifted,
    /// <summary>CLAIM_CHANGED (task 025): claim amount / currency / m² changed.</summary>
    ClaimChanged,
    DecisionRecorded,
    DecisionUpdated,
    CaseClosed,
    DossierArchived,
}

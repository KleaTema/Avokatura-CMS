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
    AssistantAdded,
    AssistantRemoved,
    PriorityChanged,
    SuspensionChanged,
    CategoryChanged,
    Delegated,
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
    DocumentUploaded,
    DocumentRemoved,
    SecurityMeasureSet,
    DecisionRecorded,
    DecisionUpdated,
    CaseClosed,
    DossierArchived,
}

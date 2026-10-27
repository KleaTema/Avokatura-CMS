namespace Ash.Modules.Cases.Contracts;

/// <summary>Mirrors the event_type list in 004_core_documents_history.sql.</summary>
public enum HistoryEventType
{
    DossierCreated,
    CaseRegistered,
    CaseCreatedFromPrevious,
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
    JuristAssigned,
    JudgeChanged,
    ObjectChanged,
    PartyAdded,
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
